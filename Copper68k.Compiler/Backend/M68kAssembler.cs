using System.Buffers.Binary;
using System.Text;

namespace Copper68k.Compiler.Backend;

internal enum M68kCondition : byte
{
	True = 0,
	False = 1,
	Higher = 2,
	LowerOrSame = 3,
	CarryClear = 4,
	CarrySet = 5,
	NotEqual = 6,
	Equal = 7,
	OverflowClear = 8,
	OverflowSet = 9,
	Plus = 10,
	Minus = 11,
	GreaterOrEqual = 12,
	LessThan = 13,
	GreaterThan = 14,
	LessOrEqual = 15
}

internal sealed class M68kAssembler
{
	private readonly List<byte> _bytes = new();
	private readonly Dictionary<string, int> _labels = new(StringComparer.Ordinal);
	private readonly List<BranchFixup> _branches = new();
	private readonly List<AddressFixup> _addresses = new();

	public int Offset => _bytes.Count;

	public IReadOnlyCollection<string> ExternalTargets =>
		_addresses
			.Where(static fixup => fixup.External)
			.Select(static fixup => fixup.Target)
			.Distinct(StringComparer.Ordinal)
			.ToArray();

	public void Mark(string label)
	{
		if (!_labels.TryAdd(label, Offset))
		{
			throw new InvalidOperationException($"Duplicate assembler label '{label}'.");
		}
	}

	public void AlignWord()
	{
		if ((Offset & 1) != 0)
		{
			EmitByte(0);
		}
	}

	public void EmitByte(byte value) => _bytes.Add(value);

	public void EmitWord(ushort value)
	{
		_bytes.Add((byte)(value >> 8));
		_bytes.Add((byte)value);
	}

	public void EmitLong(uint value)
	{
		_bytes.Add((byte)(value >> 24));
		_bytes.Add((byte)(value >> 16));
		_bytes.Add((byte)(value >> 8));
		_bytes.Add((byte)value);
	}

	public void EmitBranch(M68kCondition condition, string target)
	{
		var opcodeOffset = Offset;
		EmitWord((ushort)(0x6000 | ((int)condition << 8)));
		EmitWord(0);
		_branches.Add(new BranchFixup(opcodeOffset, target));
	}

	public void EmitDbra(int dataRegister, string target)
	{
		ValidateDataRegister(dataRegister);
		var opcodeOffset = Offset;
		EmitWord((ushort)(0x51C8 | dataRegister));
		EmitWord(0);
		_branches.Add(new BranchFixup(opcodeOffset, target));
	}

	public void EmitJsr(string target, bool external)
	{
		EmitWord(0x4EB9);
		EmitAddress(target, external);
	}

	public void EmitJmp(string target, bool external)
	{
		EmitWord(0x4EF9);
		EmitAddress(target, external);
	}

	public void EmitAddress(string target, bool external = false)
	{
		var addressOffset = Offset;
		EmitLong(0);
		_addresses.Add(new AddressFixup(addressOffset, target, external));
	}

	public LinkedCode Link(
		uint origin,
		IReadOnlyDictionary<string, uint> imports)
	{
		var code = _bytes.ToArray();
		foreach (var branch in _branches)
		{
			if (!_labels.TryGetValue(branch.Target, out var targetOffset))
			{
				throw new InvalidOperationException($"Undefined assembler label '{branch.Target}'.");
			}

			var displacement = targetOffset - (branch.OpcodeOffset + 2);
			if (displacement < short.MinValue || displacement > short.MaxValue)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.ImageOverflow,
					$"Branch to '{branch.Target}' exceeds the signed 16-bit displacement range.");
			}

			BinaryPrimitives.WriteInt16BigEndian(
				code.AsSpan(branch.OpcodeOffset + 2, 2),
				(short)displacement);
		}

		var relocations = new List<M68kRelocation>();
		foreach (var address in _addresses)
		{
			uint value;
			if (address.External)
			{
				if (!imports.TryGetValue(address.Target, out value))
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.UnresolvedImport,
						$"No absolute address was supplied for import '{address.Target}'.");
				}
			}
			else
			{
				if (!_labels.TryGetValue(address.Target, out var targetOffset))
				{
					throw new InvalidOperationException($"Undefined assembler label '{address.Target}'.");
				}

				value = checked(origin + (uint)targetOffset);
				relocations.Add(new M68kRelocation(address.Offset, address.Target));
			}

			BinaryPrimitives.WriteUInt32BigEndian(code.AsSpan(address.Offset, 4), value);
		}

		return new LinkedCode(
			code,
			new Dictionary<string, int>(_labels, StringComparer.Ordinal),
			relocations);
	}

	public string RenderAssembly(M68kCpuTarget cpu)
	{
		var output = new StringBuilder();
		output.AppendLine(cpu switch
		{
			M68kCpuTarget.M68000 => "\tmc68000",
			M68kCpuTarget.M68020 => "\tmc68020",
			M68kCpuTarget.M68040 => "\tmc68040",
			_ => throw new ArgumentOutOfRangeException(nameof(cpu))
		});
		output.AppendLine("\tsection\tcode,code");

		foreach (var target in _addresses
			.Where(static fixup => fixup.External)
			.Select(static fixup => fixup.Target)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(static target => target, StringComparer.Ordinal))
		{
			output.AppendLine($"\txref\t{AssemblySymbol(target)}");
		}
		output.AppendLine();

		var labelsByOffset = _labels
			.GroupBy(static item => item.Value)
			.ToDictionary(
				static group => group.Key,
				static group => group.Select(item => item.Key).OrderBy(item => item, StringComparer.Ordinal).ToArray());
		var addressesByOffset = _addresses.ToDictionary(static fixup => fixup.Offset);
		var branchesByOffset = _branches.ToDictionary(static fixup => fixup.OpcodeOffset);
		var code = true;
		for (var offset = 0; offset < _bytes.Count;)
		{
			if (labelsByOffset.TryGetValue(offset, out var labels))
			{
				foreach (var label in labels)
				{
					output.AppendLine($"{AssemblySymbol(label)}:");
					if (label.StartsWith("runtime:", StringComparison.Ordinal) ||
						label.StartsWith("platform-base:", StringComparison.Ordinal) ||
						label.StartsWith("static:", StringComparison.Ordinal) ||
						label.StartsWith("type:", StringComparison.Ordinal) ||
						label.StartsWith("array:", StringComparison.Ordinal) ||
						label.StartsWith("string:", StringComparison.Ordinal))
					{
						code = false;
					}
				}
			}

			if (code && TryRenderInstruction(
				offset,
				addressesByOffset,
				branchesByOffset,
				out var instruction,
				out var instructionLength))
			{
				output.AppendLine($"\t{instruction}");
				offset += instructionLength;
			}
			else if (addressesByOffset.TryGetValue(offset, out var address))
			{
				output.AppendLine($"\tdc.l\t{AssemblySymbol(address.Target)}");
				offset += 4;
			}
			else if (offset + 1 < _bytes.Count)
			{
				var word = (ushort)((_bytes[offset] << 8) | _bytes[offset + 1]);
				output.AppendLine($"\tdc.w\t${word:X4}");
				offset += 2;
			}
			else
			{
				output.AppendLine($"\tdc.b\t${_bytes[offset]:X2}");
				offset++;
			}
		}

		if (labelsByOffset.TryGetValue(_bytes.Count, out var endLabels))
		{
			foreach (var label in endLabels)
			{
				output.AppendLine($"{AssemblySymbol(label)}:");
			}
		}

		return output.ToString();
	}

	private bool TryRenderInstruction(
		int offset,
		IReadOnlyDictionary<int, AddressFixup> addresses,
		IReadOnlyDictionary<int, BranchFixup> branches,
		out string instruction,
		out int length)
	{
		instruction = string.Empty;
		length = 2;
		if (offset + 1 >= _bytes.Count)
		{
			return false;
		}

		var opcode = (ushort)((_bytes[offset] << 8) | _bytes[offset + 1]);
		if (branches.TryGetValue(offset, out var branch))
		{
			var condition = (M68kCondition)((opcode >> 8) & 0x0F);
			instruction = condition == M68kCondition.True
				? $"bra.w\t{AssemblySymbol(branch.Target)}"
				: $"{ConditionMnemonic(condition)}.w\t{AssemblySymbol(branch.Target)}";
			length = 4;
			return true;
		}

		if (opcode == 0x4EB9 && addresses.TryGetValue(offset + 2, out var call))
		{
			instruction = $"jsr\t{AssemblySymbol(call.Target)}";
			length = 6;
			return true;
		}

		if (opcode == 0x4EF9 && addresses.TryGetValue(offset + 2, out var jump))
		{
			instruction = $"jmp\t{AssemblySymbol(jump.Target)}";
			length = 6;
			return true;
		}

		if (addresses.TryGetValue(offset + 2, out var addressOperand))
		{
			instruction = opcode switch
			{
				0x2F3C => $"move.l\t#{AssemblySymbol(addressOperand.Target)},-(a7)",
				0x2F39 => $"move.l\t{AssemblySymbol(addressOperand.Target)},-(a7)",
				0x23DF => $"move.l\t(a7)+,{AssemblySymbol(addressOperand.Target)}",
				0x4879 => $"pea\t{AssemblySymbol(addressOperand.Target)}",
				0x2C79 => $"movea.l\t{AssemblySymbol(addressOperand.Target)},a6",
				_ => string.Empty
			};
			if (instruction.Length != 0)
			{
				length = 6;
				return true;
			}
		}

		instruction = opcode switch
		{
			0x4E71 => "nop",
			0x4E75 => "rts",
			0x4AFC => "illegal",
			0x2017 => "move.l\t(a7),d0",
			0x201F => "move.l\t(a7)+,d0",
			0x221F => "move.l\t(a7)+,d1",
			0x241F => "move.l\t(a7)+,d2",
			0x508F => "addq.l\t#8,a7",
			0x588F => "addq.l\t#4,a7",
			0x5381 => "subq.l\t#1,d1",
			0xD080 => "add.l\td0,d0",
			0xD081 => "add.l\td1,d0",
			0xD480 => "add.l\td0,d2",
			0xB081 => "cmp.l\td1,d0",
			0x4480 => "neg.l\td0",
			0x4680 => "not.l\td0",
			0x4880 => "ext.w\td0",
			0x48C0 => "ext.l\td0",
			0x4A80 => "tst.l\td0",
			0x4A81 => "tst.l\td1",
			0x2F00 => "move.l\td0,-(a7)",
			0x2F08 => "move.l\ta0,-(a7)",
			0x2A78 => "movea.l\t$0004.w,a5",
			0x2C4D => "movea.l\ta5,a6",
			_ when (opcode & 0xFFF8) == 0x2F00 =>
				$"move.l\td{opcode & 7},-(a7)",
			_ when (opcode & 0xFFF8) == 0x2F08 =>
				$"move.l\ta{opcode & 7},-(a7)",
			_ when (opcode & 0xF1FF) == 0x201F =>
				$"move.l\t(a7)+,d{(opcode >> 9) & 7}",
			_ when (opcode & 0xF1FF) == 0x205F =>
				$"movea.l\t(a7)+,a{(opcode >> 9) & 7}",
			_ when (opcode & 0xF100) == 0x7000 =>
				$"moveq\t#{unchecked((sbyte)opcode)},d{(opcode >> 9) & 7}",
			_ when (opcode & 0xF1FF) == 0x5097 =>
				$"addq.l\t#{QuickCount(opcode)},(a7)",
			_ when (opcode & 0xF1FF) == 0x5197 =>
				$"subq.l\t#{QuickCount(opcode)},(a7)",
			_ when (opcode & 0xF1FF) == 0x508F =>
				$"addq.l\t#{QuickCount(opcode)},a7",
			_ when (opcode & 0xF1FF) == 0x518F =>
				$"subq.l\t#{QuickCount(opcode)},a7",
			_ => string.Empty
		};
		if (instruction.Length != 0)
		{
			return true;
		}

		if (opcode == 0x4FEF && TryReadWord(offset + 2, out var leaDisplacement))
		{
			instruction = $"lea\t{unchecked((short)leaDisplacement)}(a7),a7";
			length = 4;
			return true;
		}
		if (TryReadWord(offset + 2, out var displacement))
		{
			instruction = opcode switch
			{
				0x2C78 => $"movea.l\t${displacement:X4}.w,a6",
				0x4EAE => $"jsr\t{unchecked((short)displacement)}(a6)",
				0x42AF => $"clr.l\t{unchecked((short)displacement)}(a7)",
				0x2F5F => $"move.l\t(a7)+,{unchecked((short)displacement)}(a7)",
				0x2F2F => $"move.l\t{unchecked((short)displacement)}(a7),-(a7)",
				_ => string.Empty
			};
			if (instruction.Length != 0)
			{
				length = 4;
				return true;
			}
		}
		if (opcode == 0x2F3C && TryReadLong(offset + 2, out var immediate))
		{
			instruction = $"move.l\t#${immediate:X8},-(a7)";
			length = 6;
			return true;
		}
		if ((opcode & 0xF1FF) == 0x203C &&
			TryReadLong(offset + 2, out immediate))
		{
			instruction = $"move.l\t#${immediate:X8},d{(opcode >> 9) & 7}";
			length = 6;
			return true;
		}
		if ((opcode & 0xF1FF) == 0x207C &&
			TryReadLong(offset + 2, out immediate))
		{
			instruction = $"movea.l\t#${immediate:X8},a{(opcode >> 9) & 7}";
			length = 6;
			return true;
		}
		if (opcode == 0x0C80 && TryReadLong(offset + 2, out immediate))
		{
			instruction = $"cmpi.l\t#${immediate:X8},d0";
			length = 6;
			return true;
		}

		return false;
	}

	private bool TryReadWord(int offset, out ushort value)
	{
		if (offset + 1 >= _bytes.Count)
		{
			value = 0;
			return false;
		}
		value = (ushort)((_bytes[offset] << 8) | _bytes[offset + 1]);
		return true;
	}

	private bool TryReadLong(int offset, out uint value)
	{
		if (offset + 3 >= _bytes.Count)
		{
			value = 0;
			return false;
		}
		value = ((uint)_bytes[offset] << 24) |
			((uint)_bytes[offset + 1] << 16) |
			((uint)_bytes[offset + 2] << 8) |
			_bytes[offset + 3];
		return true;
	}

	private static string ConditionMnemonic(M68kCondition condition) => condition switch
	{
		M68kCondition.Higher => "bhi",
		M68kCondition.LowerOrSame => "bls",
		M68kCondition.CarryClear => "bcc",
		M68kCondition.CarrySet => "bcs",
		M68kCondition.NotEqual => "bne",
		M68kCondition.Equal => "beq",
		M68kCondition.OverflowClear => "bvc",
		M68kCondition.OverflowSet => "bvs",
		M68kCondition.Plus => "bpl",
		M68kCondition.Minus => "bmi",
		M68kCondition.GreaterOrEqual => "bge",
		M68kCondition.LessThan => "blt",
		M68kCondition.GreaterThan => "bgt",
		M68kCondition.LessOrEqual => "ble",
		_ => throw new ArgumentOutOfRangeException(nameof(condition))
	};

	private static int QuickCount(ushort opcode)
	{
		var count = (opcode >> 9) & 7;
		return count == 0 ? 8 : count;
	}

	private static string AssemblySymbol(string value)
	{
		var result = new StringBuilder("C68K_");
		foreach (var character in value)
		{
			if (char.IsAsciiLetterOrDigit(character) || character == '_')
			{
				result.Append(character);
			}
			else
			{
				result.Append('_');
				result.Append(((int)character).ToString("X4"));
			}
		}
		return result.ToString();
	}

	private static void ValidateDataRegister(int register)
	{
		if ((uint)register > 7)
		{
			throw new ArgumentOutOfRangeException(nameof(register));
		}
	}

	private readonly record struct BranchFixup(int OpcodeOffset, string Target);

	private readonly record struct AddressFixup(int Offset, string Target, bool External);
}

internal sealed record LinkedCode(
	byte[] Bytes,
	IReadOnlyDictionary<string, int> Labels,
	IReadOnlyList<M68kRelocation> Relocations);
