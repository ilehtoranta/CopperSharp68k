/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers.Binary;
using System.Text;

namespace CopperSharp.Compiler.Backend;

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
	private readonly M68kAssemblyBuffer _buffer = new();
	private List<byte> _bytes => _buffer.Bytes;
	private Dictionary<string, int> _labels => _buffer.Labels;
	private List<BranchFixup> _branches => _buffer.Branches;
	private List<AddressFixup> _addresses => _buffer.Addresses;
	private List<PcRelativeFixup> _pcRelative => _buffer.PcRelative;

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

	public void EmitBsr(string target)
	{
		var opcodeOffset = Offset;
		EmitWord(0x6100);
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

	public void EmitPcRelativeWord(string target)
	{
		var displacementOffset = Offset;
		EmitWord(0);
		_pcRelative.Add(new PcRelativeFixup(displacementOffset, target));
	}

	public void OptimizeForM68000() => new M68kOptimizerPipeline(this, _buffer).Run();

	internal IReadOnlyList<M68kEmittedInstruction> GetInstructionStream()
	{
		var displayLabels = new Dictionary<string, string>(StringComparer.Ordinal);
		var addresses = _addresses.ToDictionary(static fixup => fixup.Offset);
		var branches = _branches.ToDictionary(static fixup => fixup.OpcodeOffset);
		var pcRelative = _pcRelative.ToDictionary(static fixup => fixup.DisplacementOffset);
		var result = new List<M68kEmittedInstruction>();

		for (var offset = 0; offset < _bytes.Count;)
		{
			if (!TryRenderInstruction(
				offset,
				displayLabels,
				addresses,
				branches,
				pcRelative,
				out _,
				out var length))
			{
				length = 2;
			}

			var opcode = _buffer.ReadWord(offset);
			var extensionWord = length >= 4 && offset + 3 < _bytes.Count
				? _buffer.ReadWord(offset + 2)
				: (ushort)0;
			var extensionLong = length >= 6 && offset + 5 < _bytes.Count
				? _buffer.ReadLong(offset + 2)
				: 0;
			var kind = M68kInstructionKind.Normal;
			int? targetOffset = null;
			var externalTarget = false;
			if (branches.TryGetValue(offset, out var branch))
			{
				kind = (opcode & 0xFFF8) == 0x51C8
					? M68kInstructionKind.Dbcc
					: opcode == 0x6100
						? M68kInstructionKind.Call
						: (opcode & 0xFF00) == 0x6000 && ((opcode >> 8) & 0x0F) == 0
							? M68kInstructionKind.UnconditionalBranch
							: M68kInstructionKind.ConditionalBranch;
				targetOffset = _labels.TryGetValue(branch.Target, out var branchTarget)
					? branchTarget
					: null;
			}
			else if (opcode == 0x4EB9 && addresses.TryGetValue(offset + 2, out var call))
			{
				kind = M68kInstructionKind.Call;
				externalTarget = call.External;
				targetOffset = call.External || !_labels.TryGetValue(call.Target, out var callTarget)
					? null
					: callTarget;
			}
			else if (opcode == 0x4EF9 && addresses.TryGetValue(offset + 2, out var jump))
			{
				kind = M68kInstructionKind.UnconditionalBranch;
				externalTarget = jump.External;
				targetOffset = jump.External || !_labels.TryGetValue(jump.Target, out var jumpTarget)
					? null
					: jumpTarget;
			}
			else if ((opcode & 0xFFC0) == 0x4E80)
			{
				kind = M68kInstructionKind.Call;
			}
			else if ((opcode & 0xFFC0) == 0x4EC0)
			{
				kind = M68kInstructionKind.UnconditionalBranch;
			}
			else if (opcode == 0x4E75)
			{
				kind = M68kInstructionKind.Return;
			}

			result.Add(new M68kEmittedInstruction(
				offset,
				length,
				opcode,
				extensionWord,
				extensionLong,
				kind,
				targetOffset,
				externalTarget));
			offset += Math.Max(2, length);
		}

		return result;
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

		foreach (var fixup in _pcRelative)
		{
			if (!_labels.TryGetValue(fixup.Target, out var targetOffset))
			{
				throw new InvalidOperationException($"Undefined assembler label '{fixup.Target}'.");
			}

			var displacement = targetOffset - fixup.DisplacementOffset;
			if (displacement < short.MinValue || displacement > short.MaxValue)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.ImageOverflow,
					$"PC-relative reference to '{fixup.Target}' exceeds the signed 16-bit displacement range.");
			}

			BinaryPrimitives.WriteInt16BigEndian(
				code.AsSpan(fixup.DisplacementOffset, 2),
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

		var referencedLabels = new HashSet<string>(
			_branches.Select(static fixup => fixup.Target)
				.Concat(_pcRelative.Select(static fixup => fixup.Target))
				.Concat(_addresses.Where(static fixup => !fixup.External).Select(static fixup => fixup.Target)),
			StringComparer.Ordinal);
		var displayLabels = BuildDisplayLabelMap(referencedLabels);
		var labelsByOffset = _labels
			.Where(item => ShouldRenderLabel(item.Key, referencedLabels, displayLabels))
			.GroupBy(static item => item.Value)
			.ToDictionary(
				static group => group.Key,
				static group => group.Select(item => item.Key).OrderBy(item => item, StringComparer.Ordinal).ToArray());
		var addressesByOffset = _addresses.ToDictionary(static fixup => fixup.Offset);
		var branchesByOffset = _branches.ToDictionary(static fixup => fixup.OpcodeOffset);
		var pcRelativeByOffset = _pcRelative.ToDictionary(static fixup => fixup.DisplacementOffset);
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
						label.StartsWith("string:", StringComparison.Ordinal) ||
						label.StartsWith("cstring:", StringComparison.Ordinal))
					{
						code = false;
					}
				}
			}

			if (code && TryRenderInstruction(
				offset,
				displayLabels,
				addressesByOffset,
				branchesByOffset,
				pcRelativeByOffset,
				out var instruction,
				out var instructionLength))
			{
				output.AppendLine($"\t{instruction}");
				offset += instructionLength;
			}
			else if (addressesByOffset.TryGetValue(offset, out var address))
			{
				output.AppendLine($"\tdc.l\t{AssemblySymbol(DisplayLabel(address.Target, displayLabels))}");
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
		IReadOnlyDictionary<string, string> displayLabels,
		IReadOnlyDictionary<int, AddressFixup> addresses,
		IReadOnlyDictionary<int, BranchFixup> branches,
		IReadOnlyDictionary<int, PcRelativeFixup> pcRelative,
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
			if ((opcode & 0xFFF8) == 0x51C8)
			{
				instruction = $"dbra\td{opcode & 0x0007},{AssemblySymbol(DisplayLabel(branch.Target, displayLabels))}";
				length = 4;
				return true;
			}

			if (opcode == 0x6100)
			{
				instruction = $"bsr.w\t{AssemblySymbol(DisplayLabel(branch.Target, displayLabels))}";
				length = 4;
				return true;
			}

			var condition = (M68kCondition)((opcode >> 8) & 0x0F);
			instruction = condition == M68kCondition.True
				? $"bra.w\t{AssemblySymbol(DisplayLabel(branch.Target, displayLabels))}"
				: $"{ConditionMnemonic(condition)}.w\t{AssemblySymbol(DisplayLabel(branch.Target, displayLabels))}";
			length = 4;
			return true;
		}

		if (opcode == 0x4EB9 && addresses.TryGetValue(offset + 2, out var call))
		{
			instruction = $"jsr\t{AssemblySymbol(DisplayLabel(call.Target, displayLabels))}";
			length = 6;
			return true;
		}

		if (opcode == 0x4EF9 && addresses.TryGetValue(offset + 2, out var jump))
		{
			instruction = $"jmp\t{AssemblySymbol(DisplayLabel(jump.Target, displayLabels))}";
			length = 6;
			return true;
		}

		if ((opcode & 0xF1FF) == 0x207A &&
			pcRelative.TryGetValue(offset + 2, out var pcRelativeOperand))
		{
			instruction = $"movea.l\t{AssemblySymbol(DisplayLabel(pcRelativeOperand.Target, displayLabels))}(pc),a{(opcode >> 9) & 7}";
			length = 4;
			return true;
		}

		if (opcode == 0x23EF &&
			TryReadWord(offset + 2, out var frameSourceDisplacement) &&
			addresses.TryGetValue(offset + 4, out var frameToAddressOperand))
		{
			instruction = $"move.l\t{unchecked((short)frameSourceDisplacement)}(a7),{AssemblySymbol(DisplayLabel(frameToAddressOperand.Target, displayLabels))}";
			length = 8;
			return true;
		}
		if (opcode == 0x23F8 &&
			TryReadWord(offset + 2, out var absoluteWordSource) &&
			addresses.TryGetValue(offset + 4, out var wordMemoryToAddressOperand))
		{
			instruction = $"move.l\t${absoluteWordSource:X4}.w,{AssemblySymbol(DisplayLabel(wordMemoryToAddressOperand.Target, displayLabels))}";
			length = 8;
			return true;
		}
		if (opcode == 0x23F9 &&
			TryReadLong(offset + 2, out var absoluteLongSource) &&
			addresses.TryGetValue(offset + 6, out var longMemoryToAddressOperand))
		{
			instruction = $"move.l\t${absoluteLongSource:X8},{AssemblySymbol(DisplayLabel(longMemoryToAddressOperand.Target, displayLabels))}";
			length = 10;
			return true;
		}
		if (opcode == 0x23FC &&
			TryReadLong(offset + 2, out var absoluteLongImmediate) &&
			addresses.TryGetValue(offset + 6, out var immediateToLongMemoryOperand))
		{
			instruction = $"move.l\t#${absoluteLongImmediate:X8},{AssemblySymbol(DisplayLabel(immediateToLongMemoryOperand.Target, displayLabels))}";
			length = 10;
			return true;
		}

		if (addresses.TryGetValue(offset + 2, out var addressOperand))
		{
			if (opcode == 0x2F7C &&
				TryReadWord(offset + 6, out var immediateAddressDisplacement))
			{
				instruction = $"move.l\t#{AssemblySymbol(DisplayLabel(addressOperand.Target, displayLabels))},{unchecked((short)immediateAddressDisplacement)}(a7)";
				length = 8;
				return true;
			}

			var targetSymbol = AssemblySymbol(DisplayLabel(addressOperand.Target, displayLabels));
			instruction = opcode switch
			{
				0x2EBC => $"move.l\t#{targetSymbol},(a7)",
				0x2F3C => $"move.l\t#{targetSymbol},-(a7)",
				0x2F39 => $"move.l\t{targetSymbol},-(a7)",
				0x23DF => $"move.l\t(a7)+,{targetSymbol}",
				0x42B9 => $"clr.l\t{targetSymbol}",
				0x4879 => $"pea\t{targetSymbol}",
				0x2C79 => $"movea.l\t{targetSymbol},a6",
				_ when (opcode & 0xF1FF) == 0x2039 =>
					$"move.l\t{targetSymbol},d{(opcode >> 9) & 7}",
				_ when (opcode & 0xFFF8) == 0x23C0 =>
					$"move.l\td{opcode & 7},{targetSymbol}",
				_ when (opcode & 0xFFF8) == 0x23C8 =>
					$"move.l\ta{opcode & 7},{targetSymbol}",
				_ when (opcode & 0xF1FF) == 0x203C =>
					$"move.l\t#{targetSymbol},d{(opcode >> 9) & 7}",
				_ when (opcode & 0xF1FF) == 0x207C =>
					$"movea.l\t#{targetSymbol},a{(opcode >> 9) & 7}",
				_ => string.Empty
			};
			if (instruction.Length != 0)
			{
				length = 6;
				return true;
			}
		}

		if (opcode == 0x2038 &&
			TryReadWord(offset + 2, out var d0AbsoluteWord))
		{
			instruction = $"move.l\t${d0AbsoluteWord:X4}.w,d0";
			length = 4;
			return true;
		}

		if ((opcode & 0xF1F8) == 0x3028 &&
			TryReadWord(offset + 2, out var wordSourceDisplacement))
		{
			instruction =
				$"move.w\t{unchecked((short)wordSourceDisplacement)}(a{opcode & 7}),d{(opcode >> 9) & 7}";
			length = 4;
			return true;
		}

		if ((opcode & 0xFFF8) == 0x0280 &&
			TryReadLong(offset + 2, out var andImmediate))
		{
			instruction = $"andi.l\t#$" + andImmediate.ToString("X8") + $",d{opcode & 7}";
			length = 6;
			return true;
		}

		if ((opcode & 0xFFF8) == 0x0240 &&
			TryReadWord(offset + 2, out var andWordImmediate))
		{
			instruction = $"andi.w\t#$" + andWordImmediate.ToString("X4") + $",d{opcode & 7}";
			length = 4;
			return true;
		}

		if ((opcode & 0xFFF8) == 0x0080 &&
			TryReadLong(offset + 2, out var orImmediate))
		{
			instruction = $"ori.l\t#$" + orImmediate.ToString("X8") + $",d{opcode & 7}";
			length = 6;
			return true;
		}

		if ((opcode & 0xFFF8) == 0x0040 &&
			TryReadWord(offset + 2, out var orWordImmediate))
		{
			instruction = $"ori.w\t#$" + orWordImmediate.ToString("X4") + $",d{opcode & 7}";
			length = 4;
			return true;
		}

		if ((opcode & 0xFFF8) == 0x0A80 &&
			TryReadLong(offset + 2, out var xorImmediate))
		{
			instruction = $"eori.l\t#$" + xorImmediate.ToString("X8") + $",d{opcode & 7}";
			length = 6;
			return true;
		}

		if ((opcode & 0xFFF8) == 0x0A40 &&
			TryReadWord(offset + 2, out var xorWordImmediate))
		{
			instruction = $"eori.w\t#$" + xorWordImmediate.ToString("X4") + $",d{opcode & 7}";
			length = 4;
			return true;
		}

		if ((opcode & 0xFFF8) == 0x0680 &&
			TryReadLong(offset + 2, out var addLongImmediate))
		{
			instruction = $"addi.l\t#$" + addLongImmediate.ToString("X8") + $",d{opcode & 7}";
			length = 6;
			return true;
		}

		if ((opcode & 0xFFF8) == 0x0640 &&
			TryReadWord(offset + 2, out var addWordImmediate))
		{
			instruction = $"addi.w\t#$" + addWordImmediate.ToString("X4") + $",d{opcode & 7}";
			length = 4;
			return true;
		}

		if ((opcode & 0xFFF8) == 0x0200 &&
			TryReadWord(offset + 2, out var andByteImmediate))
		{
			instruction = $"andi.b\t#$" + (andByteImmediate & 0xFF).ToString("X2") + $",d{opcode & 7}";
			length = 4;
			return true;
		}

		if (opcode == 0x48E7 &&
			TryReadWord(offset + 2, out var saveMask))
		{
			instruction = $"movem.l\t{MovemRegisterList(saveMask, predecrement: true)},-(a7)";
			length = 4;
			return true;
		}

		if (opcode == 0x4CDF &&
			TryReadWord(offset + 2, out var restoreMask))
		{
			instruction = $"movem.l\t(a7)+,{MovemRegisterList(restoreMask, predecrement: false)}";
			length = 4;
			return true;
		}

		if (opcode == 0x48EF &&
			TryReadWord(offset + 2, out var storeMask) &&
			TryReadWord(offset + 4, out var movemDisplacement))
		{
			instruction =
				$"movem.l\t{MovemRegisterList(storeMask, predecrement: false)},{unchecked((short)movemDisplacement)}(a7)";
			length = 6;
			return true;
		}

		if (opcode == 0x48D7 &&
			TryReadWord(offset + 2, out var indirectStoreMask))
		{
			instruction = $"movem.l\t{MovemRegisterList(indirectStoreMask, predecrement: false)},(a7)";
			length = 4;
			return true;
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
			0x4680 => "not.l\td0",
			0x4880 => "ext.w\td0",
			0x48C0 => "ext.l\td0",
			0x4A80 => "tst.l\td0",
			0x4A81 => "tst.l\td1",
			0x42A7 => "clr.l\t-(a7)",
			0x2F00 => "move.l\td0,-(a7)",
			0x2F08 => "move.l\ta0,-(a7)",
			_ when (opcode & 0xFFF8) == 0x4850 =>
				$"pea\t(a{opcode & 7})",
			0x2F10 => "move.l\t(a0),-(a7)",
			0x2F17 => "move.l\t(a7),-(a7)",
			0x2E97 => "move.l\t(a7),(a7)",
			0x2E9F => "move.l\t(a7)+,(a7)",
			0x4297 => "clr.l\t(a7)",
			0x4857 => "pea\t(a7)",
			0x2080 => "move.l\td0,(a0)",
			0x204F => "movea.l\ta7,a0",
			0x224F => "movea.l\ta7,a1",
			0x2878 => "movea.l\t$0004.w,a4",
			0x2C4C => "movea.l\ta4,a6",
			0x2A78 => "movea.l\t$0004.w,a5",
			0x2C4D => "movea.l\ta5,a6",
			_ when (opcode & 0xFF00) == 0x7000 =>
				$"moveq\t#{unchecked((sbyte)(opcode & 0xFF))},d{(opcode >> 9) & 7}",
			_ when (opcode & 0xF1C0) == 0xD1C0 =>
				$"adda.l\td{opcode & 7},a{(opcode >> 9) & 7}",
			_ when (opcode & 0xF1F8) == 0xD080 =>
				$"add.l\td{opcode & 7},d{(opcode >> 9) & 7}",
			_ when (opcode & 0xF1F8) == 0xD040 =>
				$"add.w\td{opcode & 7},d{(opcode >> 9) & 7}",
			_ when (opcode & 0xF0F8) == 0x50C0 =>
				$"{SetConditionMnemonic((M68kCondition)((opcode >> 8) & 0x0F))}\td{opcode & 7}",
			_ when (opcode & 0xFFF8) == 0x4298 =>
				$"clr.l\t(a{opcode & 7})+",
			_ when (opcode & 0xF1F8) == 0x2010 =>
				$"move.l\t(a{opcode & 7}),d{(opcode >> 9) & 7}",
			_ when (opcode & 0xF1F8) == 0x2050 =>
				$"movea.l\t(a{opcode & 7}),a{(opcode >> 9) & 7}",
			_ when (opcode & 0xF1F8) == 0x2000 =>
				$"move.l\td{opcode & 7},d{(opcode >> 9) & 7}",
			_ when (opcode & 0xF1F8) == 0xB080 =>
				$"cmp.l\td{opcode & 7},d{(opcode >> 9) & 7}",
			_ when (opcode & 0xF1F8) == 0xB090 =>
				$"cmp.l\t(a{opcode & 7}),d{(opcode >> 9) & 7}",
			_ when (opcode & 0xFFF8) == 0x4400 =>
				$"neg.b\td{opcode & 7}",
			_ when (opcode & 0xFFF8) == 0x4480 =>
				$"neg.l\td{opcode & 7}",
			_ when (opcode & 0xF1F8) == 0x2008 =>
				$"move.l\ta{opcode & 7},d{(opcode >> 9) & 7}",
			_ when (opcode & 0xF1F8) == 0xB088 =>
				$"cmp.l\ta{opcode & 7},d{(opcode >> 9) & 7}",
			_ when (opcode & 0xF1F8) == 0x2040 =>
				$"movea.l\td{opcode & 7},a{(opcode >> 9) & 7}",
			_ when (opcode & 0xF1F8) == 0x2048 =>
				$"movea.l\ta{opcode & 7},a{(opcode >> 9) & 7}",
			_ when (opcode & 0xFFF8) == 0x2F00 =>
				$"move.l\td{opcode & 7},-(a7)",
			_ when (opcode & 0xFFF8) == 0x2F08 =>
				$"move.l\ta{opcode & 7},-(a7)",
			_ when (opcode & 0xFFF8) == 0x2E80 =>
				$"move.l\td{opcode & 7},(a7)",
			_ when (opcode & 0xFFF8) == 0x2E88 =>
				$"move.l\ta{opcode & 7},(a7)",
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
			_ when (opcode & 0xF1F8) == 0x5080 =>
				$"addq.l\t#{QuickCount(opcode)},d{opcode & 7}",
			_ when (opcode & 0xF1F8) == 0x5180 =>
				$"subq.l\t#{QuickCount(opcode)},d{opcode & 7}",
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

		if ((opcode & 0xF1FF) == 0x50AF &&
			TryReadWord(offset + 2, out var addQuickDisplacement))
		{
			instruction = $"addq.l\t#{QuickCount(opcode)},{unchecked((short)addQuickDisplacement)}(a7)";
			length = 4;
			return true;
		}

		if ((opcode & 0xF1FF) == 0x51AF &&
			TryReadWord(offset + 2, out var subQuickDisplacement))
		{
			instruction = $"subq.l\t#{QuickCount(opcode)},{unchecked((short)subQuickDisplacement)}(a7)";
			length = 4;
			return true;
		}

		if ((opcode & 0xF1FF) == 0xD0FC && TryReadWord(offset + 2, out var addWord))
		{
			instruction = $"adda.w\t#{addWord},a{(opcode >> 9) & 7}";
			length = 4;
			return true;
		}

		if ((opcode & 0xF1FF) == 0xD1FC && TryReadLong(offset + 2, out var addLong))
		{
			instruction = $"adda.l\t#{addLong},a{(opcode >> 9) & 7}";
			length = 6;
			return true;
		}

		if (opcode == 0x4FEF && TryReadWord(offset + 2, out var leaDisplacement))
		{
			instruction = $"lea\t{unchecked((short)leaDisplacement)}(a7),a7";
			length = 4;
			return true;
		}
		if (opcode == 0x2F6F &&
			TryReadWord(offset + 2, out var sourceDisplacement) &&
			TryReadWord(offset + 4, out var destinationDisplacement))
		{
			if (sourceDisplacement == 0 && destinationDisplacement == 0)
			{
				instruction = "move.l\t(a7),(a7)";
				length = 6;
				return true;
			}

			instruction = $"move.l\t{unchecked((short)sourceDisplacement)}(a7),{unchecked((short)destinationDisplacement)}(a7)";
			length = 6;
			return true;
		}
		if (opcode == 0x2F57 &&
			TryReadWord(offset + 2, out destinationDisplacement))
		{
			if (destinationDisplacement == 0)
			{
				instruction = "move.l\t(a7),(a7)";
				length = 4;
				return true;
			}

			instruction = $"move.l\t(a7),{unchecked((short)destinationDisplacement)}(a7)";
			length = 4;
			return true;
		}
		if (opcode == 0x2EAF &&
			TryReadWord(offset + 2, out sourceDisplacement))
		{
			if (sourceDisplacement == 0)
			{
				instruction = "move.l\t(a7),(a7)";
				length = 4;
				return true;
			}

			instruction = $"move.l\t{unchecked((short)sourceDisplacement)}(a7),(a7)";
			length = 4;
			return true;
		}
		if ((opcode & 0xFFF8) == 0x2F50 &&
			TryReadWord(offset + 2, out destinationDisplacement))
		{
			if (destinationDisplacement == 0)
			{
				instruction = $"move.l\t(a{opcode & 7}),(a7)";
				length = 4;
				return true;
			}

			instruction = $"move.l\t(a{opcode & 7}),{unchecked((short)destinationDisplacement)}(a7)";
			length = 4;
			return true;
		}
		if (opcode == 0x2EBC &&
			TryReadLong(offset + 2, out var indirectImmediate))
		{
			instruction = $"move.l\t#${indirectImmediate:X8},(a7)";
			length = 6;
			return true;
		}
		if (opcode == 0x2F7C &&
			TryReadLong(offset + 2, out var frameImmediate) &&
			TryReadWord(offset + 6, out var frameImmediateDisplacement))
		{
			instruction = $"move.l\t#${frameImmediate:X8},{unchecked((short)frameImmediateDisplacement)}(a7)";
			length = 8;
			return true;
		}
		if (TryReadWord(offset + 2, out var displacement))
		{
			if (displacement == 0)
			{
				instruction = opcode switch
				{
					_ when (opcode & 0xF1F8) == 0x2028 =>
						$"move.l\t(a{opcode & 7}),d{(opcode >> 9) & 7}",
					_ when (opcode & 0xF1F8) == 0x2068 =>
						$"movea.l\t(a{opcode & 7}),a{(opcode >> 9) & 7}",
					_ when (opcode & 0xF1F8) == 0x41E8 =>
						$"movea.l\ta{opcode & 7},a{(opcode >> 9) & 7}",
					_ when (opcode & 0xF1FF) == 0x202F =>
						$"move.l\t(a7),d{(opcode >> 9) & 7}",
					_ when (opcode & 0xF1FF) == 0x206F =>
						$"movea.l\t(a7),a{(opcode >> 9) & 7}",
					_ when (opcode & 0xF1FF) == 0x41EF =>
						$"movea.l\ta7,a{(opcode >> 9) & 7}",
					_ when (opcode & 0xFFF8) == 0x2F40 =>
						$"move.l\td{opcode & 7},(a7)",
					_ when (opcode & 0xFFF8) == 0x2F48 =>
						$"move.l\ta{opcode & 7},(a7)",
					0x42AF => "clr.l\t(a7)",
					0x486F => "pea\t(a7)",
					_ => string.Empty
				};
				if (instruction.Length != 0)
				{
					length = 4;
					return true;
				}
			}

			instruction = opcode switch
			{
				0x2C78 => $"movea.l\t${displacement:X4}.w,a6",
				0x4EAE => $"jsr\t{unchecked((short)displacement)}(a6)",
				_ when (opcode & 0xFFF8) == 0x4EA8 => $"jsr\t{unchecked((short)displacement)}(a{opcode & 7})",
				_ when (opcode & 0xFFF8) == 0x4EE8 => $"jmp\t{unchecked((short)displacement)}(a{opcode & 7})",
				0x42AF => $"clr.l\t{unchecked((short)displacement)}(a7)",
				0x486F => $"pea\t{unchecked((short)displacement)}(a7)",
				0x2F5F => $"move.l\t(a7)+,{unchecked((short)displacement)}(a7)",
				0x2F2F => $"move.l\t{unchecked((short)displacement)}(a7),-(a7)",
				_ when (opcode & 0xF1FF) == 0x41EF =>
					$"lea\t{unchecked((short)displacement)}(a7),a{(opcode >> 9) & 7}",
				_ when (opcode & 0xF1FF) == 0x202F =>
					$"move.l\t{unchecked((short)displacement)}(a7),d{(opcode >> 9) & 7}",
				_ when (opcode & 0xF1FF) == 0xB0AF =>
					$"cmp.l\t{unchecked((short)displacement)}(a7),d{(opcode >> 9) & 7}",
				_ when (opcode & 0xF1FF) == 0x206F =>
					$"movea.l\t{unchecked((short)displacement)}(a7),a{(opcode >> 9) & 7}",
				_ when (opcode & 0xFFF8) == 0x2F40 =>
					$"move.l\td{opcode & 7},{unchecked((short)displacement)}(a7)",
				_ when (opcode & 0xFFF8) == 0x2F48 =>
					$"move.l\ta{opcode & 7},{unchecked((short)displacement)}(a7)",
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
		if ((opcode & 0xFFF8) == 0x0C80 && TryReadLong(offset + 2, out immediate))
		{
			instruction = $"cmpi.l\t#${immediate:X8},d{opcode & 7}";
			length = 6;
			return true;
		}
		if ((opcode & 0xFFF8) == 0x0C40 &&
			TryReadWord(offset + 2, out var wordImmediate))
		{
			instruction = $"cmpi.w\t#{unchecked((short)wordImmediate)},d{opcode & 7}";
			length = 4;
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

	private static string SetConditionMnemonic(M68kCondition condition) => condition switch
	{
		M68kCondition.True => "st",
		M68kCondition.False => "sf",
		M68kCondition.Higher => "shi",
		M68kCondition.LowerOrSame => "sls",
		M68kCondition.CarryClear => "scc",
		M68kCondition.CarrySet => "scs",
		M68kCondition.NotEqual => "sne",
		M68kCondition.Equal => "seq",
		M68kCondition.OverflowClear => "svc",
		M68kCondition.OverflowSet => "svs",
		M68kCondition.Plus => "spl",
		M68kCondition.Minus => "smi",
		M68kCondition.GreaterOrEqual => "sge",
		M68kCondition.LessThan => "slt",
		M68kCondition.GreaterThan => "sgt",
		M68kCondition.LessOrEqual => "sle",
		_ => throw new ArgumentOutOfRangeException(nameof(condition))
	};

	private static int QuickCount(ushort opcode)
	{
		var count = (opcode >> 9) & 7;
		return count == 0 ? 8 : count;
	}

	private static string MovemRegisterList(ushort encodedMask, bool predecrement)
	{
		var mask = predecrement ? ReverseBits(encodedMask) : encodedMask;
		var parts = new List<string>(4);
		AppendMovemRanges(parts, mask, startBit: 0, prefix: 'd');
		AppendMovemRanges(parts, mask, startBit: 8, prefix: 'a');
		return string.Join("/", parts);
	}

	private static ushort ReverseBits(ushort value)
	{
		var result = 0;
		for (var bit = 0; bit < 16; bit++)
		{
			if ((value & (1 << bit)) != 0)
			{
				result |= 1 << (15 - bit);
			}
		}

		return (ushort)result;
	}

	private static void AppendMovemRanges(List<string> parts, ushort mask, int startBit, char prefix)
	{
		var register = 0;
		while (register < 8)
		{
			if ((mask & (1 << (startBit + register))) == 0)
			{
				register++;
				continue;
			}

			var first = register;
			while (register + 1 < 8 &&
				(mask & (1 << (startBit + register + 1))) != 0)
			{
				register++;
			}

			parts.Add(
				first == register
					? $"{prefix}{first}"
					: $"{prefix}{first}-{prefix}{register}");
			register++;
		}
	}

	private static string AssemblySymbol(string value)
	{
		if (value.Length != 0 &&
			value[0] == '_' &&
			value.All(static character => char.IsAsciiLetterOrDigit(character) || character == '_'))
		{
			return value;
		}

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

	private Dictionary<string, string> BuildDisplayLabelMap(IReadOnlySet<string> referencedLabels)
	{
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var group in _labels.GroupBy(static item => item.Value))
		{
			var labels = group
				.Select(static item => item.Key)
				.OrderBy(static item => item, StringComparer.Ordinal)
				.ToArray();
			var displayLabel = labels.FirstOrDefault(static label => !IsIlLabel(label)) ??
				labels.FirstOrDefault(label => referencedLabels.Contains(label));
			if (displayLabel is null)
			{
				continue;
			}

			foreach (var label in labels)
			{
				result[label] = displayLabel;
			}
		}

		return result;
	}

	private static string DisplayLabel(
		string label,
		IReadOnlyDictionary<string, string> displayLabels) =>
		displayLabels.TryGetValue(label, out var displayLabel)
			? displayLabel
			: label;

	private static bool ShouldRenderLabel(
		string label,
		IReadOnlySet<string> referencedLabels,
		IReadOnlyDictionary<string, string> displayLabels) =>
		!IsIlLabel(label) ||
		referencedLabels.Contains(label) &&
		displayLabels.TryGetValue(label, out var displayLabel) &&
		displayLabel == label;

	private static bool IsIlLabel(string label) =>
		label.StartsWith("method:", StringComparison.Ordinal) &&
		label.Contains(":IL_", StringComparison.Ordinal);

	private static void ValidateDataRegister(int register)
	{
		if ((uint)register > 7)
		{
			throw new ArgumentOutOfRangeException(nameof(register));
		}
	}

}

internal sealed record LinkedCode(
	byte[] Bytes,
	IReadOnlyDictionary<string, int> Labels,
	IReadOnlyList<M68kRelocation> Relocations);
