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

internal enum M68kFpuFormat : byte
{
	LongInteger = 0,
	Single = 1,
	Double = 5
}

internal enum M68kFpuOperation : byte
{
	Move = 0x00,
	SquareRoot = 0x04,
	Absolute = 0x18,
	Negate = 0x1A,
	Divide = 0x20,
	Add = 0x22,
	Multiply = 0x23,
	Subtract = 0x28,
	Compare = 0x38,
	Test = 0x3A
}

internal sealed class M68kAssembler
{
	private readonly M68kAssemblyBuffer _buffer = new();
	private List<byte> _bytes => _buffer.Bytes;
	private Dictionary<string, int> _labels => _buffer.Labels;
	private List<BranchFixup> _branches => _buffer.Branches;
	private List<AddressFixup> _addresses => _buffer.Addresses;
	private List<PcRelativeFixup> _pcRelative => _buffer.PcRelative;
	private readonly HashSet<string> _longAlignmentLabels = new(StringComparer.Ordinal);

	internal IReadOnlySet<int> AddressFixupOffsets =>
		_addresses.Select(static address => address.Offset).ToHashSet();

	private static readonly OpcodeRenderRule[] SimpleInstructionRules =
	[
		new(0xFFFF, 0x4E71, static _ => "nop"),
		new(0xFFFF, 0x4E75, static _ => "rts"),
		new(0xFFFF, 0x4AFC, static _ => "illegal"),
		new(0xFFFF, 0x508F, static _ => "addq.l\t#8,a7"),
		new(0xFFFF, 0x588F, static _ => "addq.l\t#4,a7"),
		new(0xFFFF, 0x5381, static _ => "subq.l\t#1,d1"),
		new(0xFFFF, 0x4680, static _ => "not.l\td0"),
		new(0xFFF8, 0x4A00, static opcode => "tst.b\td" + (opcode & 7)),
		new(0xFFF8, 0x4A40, static opcode => "tst.w\td" + (opcode & 7)),
		new(0xFFF8, 0x4A80, static opcode => "tst.l\td" + (opcode & 7)),
		new(0xFFFF, 0x42A7, static _ => "clr.l\t-(a7)"),
		new(0xFFFF, 0x4297, static _ => "clr.l\t(a7)"),
		new(0xFFF8, 0x4880, static opcode => "ext.w\td" + (opcode & 7)),
		new(0xFFF8, 0x48C0, static opcode => "ext.l\td" + (opcode & 7)),
		new(0xFFF8, 0x49C0, static opcode => "extb.l\td" + (opcode & 7)),
		new(0xFFF8, 0x4840, static opcode => "swap\td" + (opcode & 7)),
		new(0xFFF8, 0x4240, static opcode => "clr.w\td" + (opcode & 7)),
		new(0xFFF8, 0x4280, static opcode => "clr.l\td" + (opcode & 7)),
		new(0xFFF8, 0x4850, static opcode => "pea\t(a" + (opcode & 7) + ")"),
		new(0xFFF8, 0x4E90, static opcode => "jsr\t(a" + (opcode & 7) + ")"),
		new(0xFFF8, 0x4ED0, static opcode => "jmp\t(a" + (opcode & 7) + ")"),
		new(0xF100, 0x7000, static opcode =>
			"moveq\t#" + unchecked((sbyte)(opcode & 0xFF)) + ",d" + ((opcode >> 9) & 7)),
		new(0xF1F8, 0xD180, static opcode =>
			"addx.l\td" + (opcode & 7) + ",d" + ((opcode >> 9) & 7)),
		new(0xF1F8, 0xD1C0, static opcode =>
			"adda.l\td" + (opcode & 7) + ",a" + ((opcode >> 9) & 7)),
		new(0xF1F8, 0x91C8, static opcode =>
			"suba.l\ta" + (opcode & 7) + ",a" + ((opcode >> 9) & 7)),
		new(0xF1F8, 0xD080, static opcode =>
			"add.l\td" + (opcode & 7) + ",d" + ((opcode >> 9) & 7)),
		new(0xF1F8, 0xD040, static opcode =>
			"add.w\td" + (opcode & 7) + ",d" + ((opcode >> 9) & 7)),
		new(0xF1F8, 0xB080, static opcode =>
			"cmp.l\td" + (opcode & 7) + ",d" + ((opcode >> 9) & 7)),
		new(0xF1F8, 0xB090, static opcode =>
			"cmp.l\t(a" + (opcode & 7) + "),d" + ((opcode >> 9) & 7)),
		new(0xF1F8, 0xB088, static opcode =>
			"cmp.l\ta" + (opcode & 7) + ",d" + ((opcode >> 9) & 7)),
		new(0xF1F8, 0xC140, static opcode =>
			"exg\td" + ((opcode >> 9) & 7) + ",d" + (opcode & 7)),
		new(0xF1F8, 0xC188, static opcode =>
			"exg\td" + ((opcode >> 9) & 7) + ",a" + (opcode & 7)),
		new(0xF0F8, 0x50C0, static opcode =>
			SetConditionMnemonic((M68kCondition)((opcode >> 8) & 0x0F)) + "\td" + (opcode & 7)),
		new(0xFFF8, 0x4298, static opcode => "clr.l\t(a" + (opcode & 7) + ")+"),
		new(0xFFF8, 0x4400, static opcode => "neg.b\td" + (opcode & 7)),
		new(0xFFF8, 0x4440, static opcode => "neg.w\td" + (opcode & 7)),
		new(0xFFF8, 0x4480, static opcode => "neg.l\td" + (opcode & 7)),
		new(0xFFF8, 0x4640, static opcode => "not.w\td" + (opcode & 7)),
		new(0xF1FF, 0x5097, static opcode => "addq.l\t#" + QuickCount(opcode) + ",(a7)"),
		new(0xF1FF, 0x5197, static opcode => "subq.l\t#" + QuickCount(opcode) + ",(a7)"),
		new(0xF1F8, 0x5000, static opcode => "addq.b\t#" + QuickCount(opcode) + ",d" + (opcode & 7)),
		new(0xF1F8, 0x5100, static opcode => "subq.b\t#" + QuickCount(opcode) + ",d" + (opcode & 7)),
		new(0xF1F8, 0x5080, static opcode => "addq.l\t#" + QuickCount(opcode) + ",d" + (opcode & 7)),
		new(0xF1F8, 0x5180, static opcode => "subq.l\t#" + QuickCount(opcode) + ",d" + (opcode & 7)),
		new(0xF1F8, 0x5040, static opcode => "addq.w\t#" + QuickCount(opcode) + ",d" + (opcode & 7)),
		new(0xF1F8, 0x5140, static opcode => "subq.w\t#" + QuickCount(opcode) + ",d" + (opcode & 7)),
		new(0xF1F8, 0x5088, static opcode => "addq.l\t#" + QuickCount(opcode) + ",a" + (opcode & 7)),
		new(0xF1F8, 0x5188, static opcode => "subq.l\t#" + QuickCount(opcode) + ",a" + (opcode & 7))
	];

	private static readonly ImmediateRenderRule[] ImmediateInstructionRules =
	[
		new(0xFFF8, 0x0280, 4, static (opcode, value) =>
			"andi.l\t#$" + value.ToString("X8") + ",d" + (opcode & 7)),
		new(0xFFF8, 0x0240, 2, static (opcode, value) =>
			"andi.w\t#$" + value.ToString("X4") + ",d" + (opcode & 7)),
		new(0xFFF8, 0x0200, 2, static (opcode, value) =>
			"andi.b\t#$" + (value & 0xFF).ToString("X2") + ",d" + (opcode & 7)),
		new(0xFFF8, 0x0080, 4, static (opcode, value) =>
			"ori.l\t#$" + value.ToString("X8") + ",d" + (opcode & 7)),
		new(0xFFF8, 0x0040, 2, static (opcode, value) =>
			"ori.w\t#$" + value.ToString("X4") + ",d" + (opcode & 7)),
		new(0xFFF8, 0x0A80, 4, static (opcode, value) =>
			"eori.l\t#$" + value.ToString("X8") + ",d" + (opcode & 7)),
		new(0xFFF8, 0x0A40, 2, static (opcode, value) =>
			"eori.w\t#$" + value.ToString("X4") + ",d" + (opcode & 7)),
		new(0xFFF8, 0x0680, 4, static (opcode, value) =>
			"addi.l\t#$" + value.ToString("X8") + ",d" + (opcode & 7)),
		new(0xFFF8, 0x0640, 2, static (opcode, value) =>
			"addi.w\t#$" + value.ToString("X4") + ",d" + (opcode & 7)),
		new(0xF1FF, 0xD0FC, 2, static (opcode, value) =>
			"adda.w\t#" + value + ",a" + ((opcode >> 9) & 7)),
		new(0xF1FF, 0xD1FC, 4, static (opcode, value) =>
			"adda.l\t#" + value + ",a" + ((opcode >> 9) & 7)),
		new(0xF1FF, 0x90FC, 2, static (opcode, value) =>
			"suba.w\t#" + unchecked((short)(ushort)value) + ",a" + ((opcode >> 9) & 7)),
		new(0xF1FF, 0x91FC, 4, static (opcode, value) =>
			"suba.l\t#$" + value.ToString("X8") + ",a" + ((opcode >> 9) & 7)),
		new(0xF1FF, 0xB0FC, 2, static (opcode, value) =>
			"cmpa.w\t#" + unchecked((short)(ushort)value) + ",a" + ((opcode >> 9) & 7)),
		new(0xF1FF, 0xB1FC, 4, static (opcode, value) =>
			"cmpa.l\t#$" + value.ToString("X8") + ",a" + ((opcode >> 9) & 7)),
		new(0xFFF8, 0x0C80, 4, static (opcode, value) =>
			"cmpi.l\t#$" + value.ToString("X8") + ",d" + (opcode & 7)),
		new(0xFFF8, 0x0C40, 2, static (opcode, value) =>
			"cmpi.w\t#" + unchecked((short)(ushort)value) + ",d" + (opcode & 7))
	];

	public int Offset => _bytes.Count;

	public IReadOnlyCollection<string> ExternalTargets =>
		_addresses
			.Where(static fixup => fixup.External)
			.Select(static fixup => fixup.Target)
			.Distinct(StringComparer.Ordinal)
			.ToArray();

	public bool ReferencesTargetPrefix(string prefix) =>
		_branches.Any(fixup => fixup.Target.StartsWith(prefix, StringComparison.Ordinal)) ||
		_addresses.Any(fixup => fixup.Target.StartsWith(prefix, StringComparison.Ordinal)) ||
		_pcRelative.Any(fixup => fixup.Target.StartsWith(prefix, StringComparison.Ordinal));

	public bool ReferencesTarget(string target) =>
		_branches.Any(fixup => StringComparer.Ordinal.Equals(fixup.Target, target)) ||
		_addresses.Any(fixup => StringComparer.Ordinal.Equals(fixup.Target, target)) ||
		_pcRelative.Any(fixup => StringComparer.Ordinal.Equals(fixup.Target, target));

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

	public void RequestLongAlignment(string label) =>
		_longAlignmentLabels.Add(label);

	public void ApplyRequestedAlignments()
	{
		foreach (var requestedLabel in _longAlignmentLabels
			.OrderBy(label => _labels[label])
			.ToArray())
		{
			var offset = _labels[requestedLabel];
			if ((offset & 3) == 0)
			{
				continue;
			}
			_buffer.InsertBytes(offset, 2);
			_buffer.WriteWord(offset, 0x4E71); // NOP before aligned label
			foreach (var label in _labels.Keys
				.Where(label => _labels[label] == offset)
				.ToArray())
			{
				_labels[label] += 2;
			}
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

	public void EmitFpuDataRegisterToRegister(
		int dataRegister,
		int destinationFpuRegister,
		M68kFpuFormat format)
	{
		ValidateDataRegister(dataRegister);
		ValidateFpuRegister(destinationFpuRegister);
		EmitWord((ushort)(0xF200 | dataRegister));
		EmitWord((ushort)(0x4000 | ((int)format << 10) |
			(destinationFpuRegister << 7)));
	}

	public void EmitFpuRegisterToDataRegister(
		int sourceFpuRegister,
		int dataRegister,
		M68kFpuFormat format)
	{
		ValidateFpuRegister(sourceFpuRegister);
		ValidateDataRegister(dataRegister);
		EmitWord((ushort)(0xF200 | dataRegister));
		EmitWord((ushort)(0x6000 | ((int)format << 10) |
			(sourceFpuRegister << 7)));
	}

	public void EmitFpuStackToRegister(
		int destinationFpuRegister,
		M68kFpuFormat format)
	{
		ValidateFpuRegister(destinationFpuRegister);
		EmitWord(0xF217); // (A7)
		EmitWord((ushort)(0x4000 | ((int)format << 10) |
			(destinationFpuRegister << 7)));
	}

	public void EmitFpuStackDisplacementToRegister(
		int destinationFpuRegister,
		M68kFpuFormat format,
		short displacement)
	{
		ValidateFpuRegister(destinationFpuRegister);
		EmitWord(0xF22F); // d16(A7)
		EmitWord((ushort)(0x4000 | ((int)format << 10) |
			(destinationFpuRegister << 7)));
		EmitWord(unchecked((ushort)displacement));
	}

	public void EmitFpuRegisterToStack(
		int sourceFpuRegister,
		M68kFpuFormat format)
	{
		ValidateFpuRegister(sourceFpuRegister);
		EmitWord(0xF217); // (A7)
		EmitWord((ushort)(0x6000 | ((int)format << 10) |
			(sourceFpuRegister << 7)));
	}

	public void EmitFpuRegisterToStackDisplacement(
		int sourceFpuRegister,
		M68kFpuFormat format,
		short displacement)
	{
		ValidateFpuRegister(sourceFpuRegister);
		EmitWord(0xF22F); // d16(A7)
		EmitWord((ushort)(0x6000 | ((int)format << 10) |
			(sourceFpuRegister << 7)));
		EmitWord(unchecked((ushort)displacement));
	}

	public void EmitFpuRegisterOperation(
		int sourceFpuRegister,
		int destinationFpuRegister,
		M68kFpuOperation operation)
	{
		ValidateFpuRegister(sourceFpuRegister);
		ValidateFpuRegister(destinationFpuRegister);
		EmitWord(0xF200);
		EmitWord((ushort)((sourceFpuRegister << 10) |
			(destinationFpuRegister << 7) | (int)operation));
	}

	public void EmitFpuUnaryOperation(
		int destinationFpuRegister,
		M68kFpuOperation operation) =>
		EmitFpuRegisterOperation(destinationFpuRegister, destinationFpuRegister, operation);

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

	internal void MarkDataStart() => _buffer.MarkDataStart();

	internal void MarkAnalysisAnchor(string name)
	{
		if (!_buffer.AnalysisAnchors.TryAdd(name, Offset))
		{
			throw new InvalidOperationException($"Duplicate analysis anchor '{name}'.");
		}
	}

	internal IReadOnlyDictionary<string, int> Labels => _buffer.Labels;

	internal IReadOnlyDictionary<string, int> AnalysisAnchors =>
		_buffer.AnalysisAnchors;

	public void OptimizeForM68000() => OptimizeForCpu(M68kCpuTarget.M68000);

	public void OptimizeForCpu(
		M68kCpuTarget cpu,
		M68kClrPolicy clrPolicy = M68kClrPolicy.Auto,
		IReadOnlyList<M68kLoopLayout>? sizeFirstLoops = null) =>
		new M68kOptimizerPipeline(
			this,
			_buffer,
			cpu,
			clrPolicy,
			sizeFirstLoops).Run();

	internal void RelaxBranches()
	{
		// Each shortening shifts every later label and fixup, so keep iterating
		// until no branch or local jump can enable another relaxation.
		while (TryRelaxShortBranch() || TryRelaxLocalAbsoluteControlTransfer())
		{
		}
	}

	internal void RelaxFinalLayout()
	{
		// Every shortening can move a later hot-loop header off its requested
		// boundary. Re-apply padding after each individual relaxation so branch
		// range decisions always see the aligned layout rather than a transiently
		// smaller one.
		ApplyRequestedAlignments();
		while (true)
		{
			if (TryRelaxShortBranch() ||
				TryRelaxLocalAbsoluteControlTransfer() ||
				TryRemoveBranchToNextInstruction() ||
				TryRelaxLocalAbsoluteLoad())
			{
				ApplyRequestedAlignments();
				continue;
			}
			return;
		}
	}

	private bool TryRemoveBranchToNextInstruction()
	{
		for (var index = 0; index < _branches.Count; index++)
		{
			var branch = _branches[index];
			var opcode = _buffer.ReadWord(branch.OpcodeOffset);
			if ((opcode & 0xF000) != 0x6000 ||
				(opcode & 0xFF00) == 0x6100)
			{
				// BSR has the observable side effect of pushing a return address,
				// even when its target is the immediately following instruction.
				// DBcc fixups are not ordinary branches either.
				continue;
			}
			var length = IsWordBranchOpcode(opcode) ? 4 : 2;
			if (!_labels.TryGetValue(branch.Target, out var targetOffset) ||
				targetOffset != branch.OpcodeOffset + length)
			{
				continue;
			}

			_branches.RemoveAt(index);
			_buffer.RemoveBytes(branch.OpcodeOffset, length);
			return true;
		}

		return false;
	}

	private bool TryRelaxLocalAbsoluteLoad()
	{
		for (var index = 0; index < _addresses.Count; index++)
		{
			var address = _addresses[index];
			var opcodeOffset = address.Offset - 2;
			if (address.External ||
				opcodeOffset < 0 ||
				_buffer.DataStartOffset is { } dataStartOffset && opcodeOffset >= dataStartOffset ||
				!_labels.TryGetValue(address.Target, out var targetOffset) ||
				!TryGetPcRelativeAddressingOpcode(
					_buffer.ReadWord(opcodeOffset),
					out var pcRelativeOpcode))
			{
				continue;
			}

			// The absolute long form is six bytes.  After removing its final
			// word, labels after the instruction move two bytes closer.
			var relaxedTargetOffset = GetTargetOffsetAfterTwoByteRelaxation(
				targetOffset,
				opcodeOffset + 6);
			var displacement = relaxedTargetOffset - (opcodeOffset + 2);
			if (displacement < short.MinValue || displacement > short.MaxValue)
			{
				continue;
			}
			_buffer.WriteWord(opcodeOffset, pcRelativeOpcode);
			_addresses.RemoveAt(index);
			_buffer.RemoveBytes(opcodeOffset + 4, 2);
			_pcRelative.Add(new PcRelativeFixup(opcodeOffset + 2, address.Target));
			return true;
		}

		return false;
	}

	private static bool TryGetPcRelativeAddressingOpcode(
		ushort opcode,
		out ushort pcRelativeOpcode)
	{
		if ((opcode & 0xF1FF) == 0x2039)
		{
			pcRelativeOpcode = (ushort)((opcode & 0xFFC0) | 0x003A);
			return true;
		}

		if ((opcode & 0xF1FF) == 0x2079)
		{
			pcRelativeOpcode = (ushort)((opcode & 0xFFC0) | 0x003A);
			return true;
		}

		if ((opcode & 0xF1FF) == 0x41F9)
		{
			pcRelativeOpcode = (ushort)((opcode & 0xFFC0) | 0x003A);
			return true;
		}

		pcRelativeOpcode = opcode switch
		{
			0x2F39 => 0x2F3A, // MOVE.L abs.l,-(A7)
			0x4879 => 0x487A, // PEA abs.l
			_ => 0
		};
		return pcRelativeOpcode != 0;
	}

	private bool TryRelaxShortBranch()
	{
		for (var index = 0; index < _branches.Count; index++)
		{
			var branch = _branches[index];
			var opcode = _buffer.ReadWord(branch.OpcodeOffset);
			if (!branch.CanRelaxToShort ||
				!IsWordBranchOpcode(opcode) ||
				!_labels.TryGetValue(branch.Target, out var targetOffset) ||
				GetAlignmentRegion(branch.OpcodeOffset) != GetAlignmentRegion(targetOffset))
			{
				continue;
			}

			var relaxedTargetOffset = GetTargetOffsetAfterTwoByteRelaxation(
				targetOffset,
				branch.OpcodeOffset + 4);
			var displacement = relaxedTargetOffset - (branch.OpcodeOffset + 2);
			if (displacement is < sbyte.MinValue or > sbyte.MaxValue || displacement == 0)
			{
				continue;
			}
			_buffer.WriteWord(
				branch.OpcodeOffset,
				(opcode & 0xFF00) | unchecked((byte)displacement));
			_buffer.RemoveBytes(branch.OpcodeOffset + 2, 2);
			return true;
		}

		return false;
	}

	private int GetAlignmentRegion(int offset) =>
		_longAlignmentLabels.Count(label => _labels[label] <= offset);

	private bool TryRelaxLocalAbsoluteControlTransfer()
	{
		for (var index = 0; index < _addresses.Count; index++)
		{
			var address = _addresses[index];
			var opcodeOffset = address.Offset - 2;
			var opcode = opcodeOffset < 0
				? (ushort)0
				: _buffer.ReadWord(opcodeOffset);
			if (address.External ||
				opcodeOffset < 0 ||
				_buffer.DataStartOffset is { } dataStartOffset && opcodeOffset >= dataStartOffset ||
				opcode is not 0x4EF9 and not 0x4EB9 ||
				!_labels.TryGetValue(address.Target, out var targetOffset) ||
				(targetOffset > opcodeOffset && targetOffset < opcodeOffset + 6))
			{
				continue;
			}

			var relaxedTargetOffset = GetTargetOffsetAfterTwoByteRelaxation(
				targetOffset,
				opcodeOffset + 6);
			var displacement = relaxedTargetOffset - (opcodeOffset + 2);
			if (displacement < short.MinValue || displacement > short.MaxValue)
			{
				continue;
			}
			_buffer.WriteWord(
				opcodeOffset,
				opcode == 0x4EB9 ? 0x6100 : 0x6000); // JSR/JMP -> BSR.W/BRA.W
			_buffer.WriteWord(opcodeOffset + 2, 0);
			_addresses.RemoveAt(index);
			_buffer.RemoveBytes(opcodeOffset + 4, 2);
			_branches.Add(new BranchFixup(opcodeOffset, address.Target, CanRelaxToShort: false));
			return true;
		}

		return false;
	}

	private int GetTargetOffsetAfterTwoByteRelaxation(
		int targetOffset,
		int instructionEndOffset)
	{
		if (targetOffset < instructionEndOffset)
		{
			return targetOffset;
		}

		// Removing two bytes before an aligned loop header makes that header
		// misaligned. Re-applying its padding absorbs the size reduction, so a
		// forward target at or beyond the first such header does not move.
		var alignmentAbsorbsReduction = _longAlignmentLabels.Any(label =>
		{
			var labelOffset = _labels[label];
			return labelOffset >= instructionEndOffset && labelOffset <= targetOffset;
		});
		return alignmentAbsorbsReduction ? targetOffset : targetOffset - 2;
	}

	private static bool IsWordBranchOpcode(ushort opcode) =>
		(opcode & 0xF000) == 0x6000 && (opcode & 0x00FF) == 0;

	private static bool IsShortBranchOpcode(ushort opcode) =>
		(opcode & 0xF000) == 0x6000 && (opcode & 0x00FF) != 0;

	internal IReadOnlyList<M68kEmittedInstruction> GetInstructionStream(
		int startOffset = 0)
	{
		var displayLabels = new Dictionary<string, string>(StringComparer.Ordinal);
		var addresses = _addresses.ToDictionary(static fixup => fixup.Offset);
		var branches = _branches.ToDictionary(static fixup => fixup.OpcodeOffset);
		var pcRelative = _pcRelative.ToDictionary(static fixup => fixup.DisplacementOffset);
		var result = new List<M68kEmittedInstruction>();

		for (var offset = startOffset; offset < _bytes.Count;)
		{
			var decoded = TryRenderInstruction(
				offset,
				displayLabels,
				addresses,
				branches,
				pcRelative,
				out _,
				out var length);
			if (!decoded)
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
			var isNonReturning = opcode == 0x4AFC; // ILLEGAL
			if (branches.TryGetValue(offset, out var branch))
			{
				kind = (opcode & 0xFFF8) == 0x51C8
					? M68kInstructionKind.Dbcc
					: (opcode & 0xFF00) == 0x6100
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
				isNonReturning = !call.External &&
					string.Equals(call.Target, "__c68k_exception_raise", StringComparison.Ordinal);
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
				decoded,
				kind,
				targetOffset,
				externalTarget,
				isNonReturning));
			offset += Math.Max(2, length);
		}

		return result;
	}

	public LinkedCode Link(
		uint origin,
		IReadOnlyDictionary<string, uint> imports)
	{
		RelaxFinalLayout();
		var code = _bytes.ToArray();
		foreach (var branch in _branches)
		{
			if (!_labels.TryGetValue(branch.Target, out var targetOffset))
			{
				throw new InvalidOperationException($"Undefined assembler label '{branch.Target}'.");
			}

			var displacement = targetOffset - (branch.OpcodeOffset + 2);
			if (IsShortBranchOpcode(_buffer.ReadWord(branch.OpcodeOffset)))
			{
				if (displacement is < sbyte.MinValue or > sbyte.MaxValue || displacement == 0)
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.ImageOverflow,
						$"Short branch to '{branch.Target}' exceeds the signed 8-bit displacement range.");
				}

				code[branch.OpcodeOffset + 1] = unchecked((byte)displacement);
				continue;
			}

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
			new Dictionary<string, int>(
				_buffer.AnalysisAnchors,
				StringComparer.Ordinal),
			relocations);
	}

	public string RenderAssembly(M68kCpuTarget cpu)
	{
		RelaxFinalLayout();
		var output = new StringBuilder();
		output.AppendLine(cpu switch
		{
			M68kCpuTarget.M68000 => "\tmc68000",
			M68kCpuTarget.M68020 => "\tmc68020",
			M68kCpuTarget.M68040 => "\tmc68040",
			M68kCpuTarget.M68060 => "\tmc68060",
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
		if (offset + 1 >= _bytes.Count)
		{
			instruction = string.Empty;
			length = 2;
			return false;
		}

		var context = new InstructionRenderContext(
			offset,
			(ushort)((_bytes[offset] << 8) | _bytes[offset + 1]),
			displayLabels,
			addresses,
			branches,
			pcRelative);
		if (TryRenderControlFlow(context, out var rendered) ||
			TryRenderFpu(context, out rendered) ||
			TryRenderFixupOperand(context, out rendered) ||
			TryRenderMove(context, out rendered) ||
			TryRenderMovem(context, out rendered) ||
			TryRenderDataRegisterArithmetic(context, out rendered) ||
			TryRenderMemoryDestinationArithmetic(context, out rendered) ||
			TryRenderSimpleInstruction(context.Opcode, out rendered) ||
			TryRenderImmediateInstruction(context.Offset, context.Opcode, out rendered) ||
			TryRenderStackAdjustment(context, out rendered) ||
			TryRenderLea(context, out rendered) ||
			TryRenderDisplacementInstruction(context.Offset, context.Opcode, out rendered) ||
			TryRenderGeneratedArithmetic(context, out rendered))
		{
			instruction = rendered.Text;
			length = rendered.Length;
			return true;
		}

		instruction = string.Empty;
		length = 2;
		return false;
	}

	private bool TryRenderFpu(
		InstructionRenderContext context,
		out RenderedInstruction rendered)
	{
		if ((context.Opcode & 0xFFC0) != 0xF200 ||
			context.Offset + 3 >= _bytes.Count)
		{
			rendered = default;
			return false;
		}

		var extension = (ushort)((_bytes[context.Offset + 2] << 8) |
			_bytes[context.Offset + 3]);
		var mode = (context.Opcode >> 3) & 7;
		var register = context.Opcode & 7;
		var transfer = extension & 0x6000;
		if (transfer is 0x4000 or 0x6000)
		{
			var format = (extension >> 10) & 7;
			var suffix = format switch { 1 => "s", 5 => "d", _ => "l" };
			var fp = (extension >> 7) & 7;
			var ea = mode == 0
				? $"d{register}"
				: mode == 2 && register == 7
					? "(a7)"
					: mode == 5 && register == 7 && context.Offset + 5 < _bytes.Count
						? $"{unchecked((short)((_bytes[context.Offset + 4] << 8) | _bytes[context.Offset + 5]))}(a7)"
						: null;
			if (ea is null)
			{
				rendered = default;
				return false;
			}
			rendered = new RenderedInstruction(
				transfer == 0x4000
					? $"fmove.{suffix}\t{ea},fp{fp}"
					: $"fmove.{suffix}\tfp{fp},{ea}",
				mode == 5 ? 6 : 4);
			return true;
		}

		var source = (extension >> 10) & 7;
		var destination = (extension >> 7) & 7;
		var mnemonic = (extension & 0x7F) switch
		{
			0x00 => "fmove.x",
			0x04 => "fsqrt.x",
			0x18 => "fabs.x",
			0x1A => "fneg.x",
			0x20 => "fdiv.x",
			0x22 => "fadd.x",
			0x23 => "fmul.x",
			0x28 => "fsub.x",
			0x38 => "fcmp.x",
			0x3A => "ftst.x",
			_ => null
		};
		if (mnemonic is null)
		{
			rendered = default;
			return false;
		}
		rendered = new RenderedInstruction(
			$"{mnemonic}\tfp{source},fp{destination}",
			4);
		return true;
	}

	private bool TryRenderControlFlow(
		in InstructionRenderContext context,
		out RenderedInstruction instruction)
	{
		if (context.Branches.TryGetValue(context.Offset, out var branch))
		{
			var target = AssemblySymbol(DisplayLabel(branch.Target, context.DisplayLabels));
			if ((context.Opcode & 0xFFF8) == 0x51C8)
			{
				instruction = new($"dbra\td{context.Opcode & 7},{target}", 4);
				return true;
			}

			if ((context.Opcode & 0xFF00) == 0x6100)
			{
				instruction = new($"bsr.{(IsShortBranchOpcode(context.Opcode) ? 's' : 'w')}\t{target}",
					IsShortBranchOpcode(context.Opcode) ? 2 : 4);
				return true;
			}

			var condition = (M68kCondition)((context.Opcode >> 8) & 0x0F);
			var suffix = IsShortBranchOpcode(context.Opcode) ? 's' : 'w';
			instruction = new(condition == M68kCondition.True
				? $"bra.{suffix}\t{target}"
				: $"{ConditionMnemonic(condition)}.{suffix}\t{target}",
				IsShortBranchOpcode(context.Opcode) ? 2 : 4);
			return true;
		}

		if (context.Opcode == 0x4EB9 && context.Addresses.TryGetValue(context.Offset + 2, out var call))
		{
			instruction = new($"jsr\t{AssemblySymbol(DisplayLabel(call.Target, context.DisplayLabels))}", 6);
			return true;
		}

		if (context.Opcode == 0x4EF9 && context.Addresses.TryGetValue(context.Offset + 2, out var jump))
		{
			instruction = new($"jmp\t{AssemblySymbol(DisplayLabel(jump.Target, context.DisplayLabels))}", 6);
			return true;
		}

		if (context.Opcode == 0x4878 && TryReadWord(context.Offset + 2, out var absoluteWord))
		{
			instruction = new($"pea\t${absoluteWord:X4}.w", 4);
			return true;
		}

		instruction = default;
		return false;
	}

	private bool TryRenderFixupOperand(
		in InstructionRenderContext context,
		out RenderedInstruction instruction)
	{
		var opcode = context.Opcode;
		var offset = context.Offset;
		var labels = context.DisplayLabels;
		if ((opcode & 0xF1FF) == 0x203A &&
			context.PcRelative.TryGetValue(offset + 2, out var pcRelativeOperand))
		{
			instruction = new(
				$"move.l\t{AssemblySymbol(DisplayLabel(pcRelativeOperand.Target, context.DisplayLabels))}(pc),d{(opcode >> 9) & 7}",
				4);
			return true;
		}
		if ((opcode & 0xF1FF) == 0x207A &&
			context.PcRelative.TryGetValue(offset + 2, out pcRelativeOperand))
		{
			instruction = new(
				$"movea.l\t{AssemblySymbol(DisplayLabel(pcRelativeOperand.Target, context.DisplayLabels))}(pc),a{(opcode >> 9) & 7}", 4);
			return true;
		}
		if ((opcode & 0xF1FF) == 0x41FA &&
			context.PcRelative.TryGetValue(offset + 2, out pcRelativeOperand))
		{
			instruction = new(
				$"lea\t{AssemblySymbol(DisplayLabel(pcRelativeOperand.Target, context.DisplayLabels))}(pc),a{(opcode >> 9) & 7}", 4);
			return true;
		}
		if (opcode == 0x2F3A &&
			context.PcRelative.TryGetValue(offset + 2, out pcRelativeOperand))
		{
			instruction = new(
				$"move.l\t{AssemblySymbol(DisplayLabel(pcRelativeOperand.Target, context.DisplayLabels))}(pc),-(a7)",
				4);
			return true;
		}
		if (opcode == 0x487A &&
			context.PcRelative.TryGetValue(offset + 2, out pcRelativeOperand))
		{
			instruction = new(
				$"pea\t{AssemblySymbol(DisplayLabel(pcRelativeOperand.Target, context.DisplayLabels))}(pc)",
				4);
			return true;
		}
		if (LongMemoryArithmeticMnemonic(opcode) is { } pcRelativeMnemonic &&
			(opcode & 0x003F) == 0x003A &&
			context.PcRelative.TryGetValue(offset + 2, out pcRelativeOperand))
		{
			instruction = new(
				$"{pcRelativeMnemonic}\t{AssemblySymbol(DisplayLabel(pcRelativeOperand.Target, context.DisplayLabels))}(pc),d{(opcode >> 9) & 7}",
				4);
			return true;
		}

		if (opcode == 0x23EF && TryReadWord(offset + 2, out var frameSourceDisplacement) &&
			context.Addresses.TryGetValue(offset + 4, out var frameToAddress))
		{
			instruction = new($"move.l\t{unchecked((short)frameSourceDisplacement)}(a7),{Symbol(frameToAddress)}", 8);
			return true;
		}
		if (opcode == 0x23F8 && TryReadWord(offset + 2, out var absoluteWordSource) &&
			context.Addresses.TryGetValue(offset + 4, out var wordMemoryToAddress))
		{
			instruction = new($"move.l\t${absoluteWordSource:X4}.w,{Symbol(wordMemoryToAddress)}", 8);
			return true;
		}
		if (opcode == 0x23F9 && TryReadLong(offset + 2, out var absoluteLongSource) &&
			context.Addresses.TryGetValue(offset + 6, out var longMemoryToAddress))
		{
			instruction = new($"move.l\t${absoluteLongSource:X8},{Symbol(longMemoryToAddress)}", 10);
			return true;
		}
		if (opcode == 0x23FC && TryReadLong(offset + 2, out var absoluteLongImmediate) &&
			context.Addresses.TryGetValue(offset + 6, out var immediateToLongMemory))
		{
			instruction = new($"move.l\t#${absoluteLongImmediate:X8},{Symbol(immediateToLongMemory)}", 10);
			return true;
		}

		if (context.Addresses.TryGetValue(offset + 2, out var addressOperand))
		{
			if (TryGetLongMemoryDestinationArithmetic(
					opcode,
					out var destinationArithmeticMnemonic,
					out var destinationArithmeticSource) &&
				(opcode & 0x003F) == 0x0039)
			{
				instruction = new(
					$"{destinationArithmeticMnemonic}\t{destinationArithmeticSource},{Symbol(addressOperand)}",
					6);
				return true;
			}

			if (LongMemoryArithmeticMnemonic(opcode) is { } addressArithmeticMnemonic &&
				(opcode & 0x003F) == 0x0039)
			{
				instruction = new(
					$"{addressArithmeticMnemonic}\t{Symbol(addressOperand)},d{(opcode >> 9) & 7}",
					6);
				return true;
			}

			if ((opcode & 0xF1FF) == 0x217C && TryReadWord(offset + 6, out var displacement))
			{
				instruction = new($"move.l\t#{Symbol(addressOperand)},{unchecked((short)displacement)}(a{(opcode >> 9) & 7})", 8);
				return true;
			}

			var target = Symbol(addressOperand);
			var text = opcode switch
			{
				0x2EBC => $"move.l\t#{target},(a7)",
				_ when (opcode & 0xF1FF) == 0x20BC =>
					$"move.l\t#{target},(a{(opcode >> 9) & 7})",
				0x2F3C => $"move.l\t#{target},-(a7)",
				0x2F39 => $"move.l\t{target},-(a7)",
				0x23DF => $"move.l\t(a7)+,{target}",
				0x42B9 => $"clr.l\t{target}",
				0x4879 => $"pea\t{target}",
				0x2C79 => $"movea.l\t{target},a6",
				_ when (opcode & 0xF1FF) == 0x2039 => $"move.l\t{target},d{(opcode >> 9) & 7}",
				_ when (opcode & 0xF1FF) == 0x2079 => $"movea.l\t{target},a{(opcode >> 9) & 7}",
				_ when (opcode & 0xFFF8) == 0x23C0 => $"move.l\td{opcode & 7},{target}",
				_ when (opcode & 0xFFF8) == 0x23C8 => $"move.l\ta{opcode & 7},{target}",
				_ when (opcode & 0xF1FF) == 0x203C => $"move.l\t#{target},d{(opcode >> 9) & 7}",
				_ when (opcode & 0xF1FF) == 0x207C => $"movea.l\t#{target},a{(opcode >> 9) & 7}",
				_ when (opcode & 0xF1FF) == 0xB0BC => $"cmp.l\t#{target},d{(opcode >> 9) & 7}",
				_ => null
			};
			if (text is not null)
			{
				instruction = new(text, 6);
				return true;
			}
		}

		if ((opcode & 0xF1FF) == 0x217C && TryReadLong(offset + 2, out var immediate) &&
			TryReadWord(offset + 6, out var immediateDisplacement))
		{
			instruction = new($"move.l\t#${immediate:X8},{unchecked((short)immediateDisplacement)}(a{(opcode >> 9) & 7})", 8);
			return true;
		}

		instruction = default;
		return false;

		string Symbol(AddressFixup fixup) =>
			AssemblySymbol(DisplayLabel(fixup.Target, labels));
	}

	private bool TryRenderMove(in InstructionRenderContext context, out RenderedInstruction instruction)
	{
		if (TryRenderMove(context.Offset, context.Opcode, out var text, out var length))
		{
			instruction = new(text, length);
			return true;
		}
		instruction = default;
		return false;
	}

	private bool TryRenderMovem(in InstructionRenderContext context, out RenderedInstruction instruction)
	{
		var isStore = (context.Opcode & 0xFFC0) == 0x48C0;
		var isLoad = (context.Opcode & 0xFFC0) == 0x4CC0;
		if ((isStore || isLoad) &&
			TryReadWord(context.Offset + 2, out var registerMask) &&
			TryDecodeEffectiveAddress(
				context.Offset + 4,
				(context.Opcode >> 3) & 7,
				context.Opcode & 7,
				4,
				out var memory) &&
			memory.Kind == M68kOperandKind.Memory)
		{
			var predecrement = isStore && ((context.Opcode >> 3) & 7) == 4;
			var registers = MovemRegisterList(registerMask, predecrement);
			instruction = new(
				isStore
					? $"movem.l\t{registers},{memory.Text}"
					: $"movem.l\t{memory.Text},{registers}",
				4 + memory.ExtensionBytes);
			return true;
		}

		instruction = default;
		return false;
	}

	private static bool TryRenderSimpleInstruction(ushort opcode, out RenderedInstruction instruction)
	{
		string text;
		if (TryRenderSimpleInstruction(opcode, out text))
		{
			instruction = new(text, 2);
			return true;
		}
		instruction = default;
		return false;
	}

	private bool TryRenderImmediateInstruction(int offset, ushort opcode, out RenderedInstruction instruction)
	{
		if (TryRenderImmediateInstruction(offset, opcode, out var text, out var length))
		{
			instruction = new(text, length);
			return true;
		}
		instruction = default;
		return false;
	}

	private bool TryRenderStackAdjustment(in InstructionRenderContext context, out RenderedInstruction instruction)
	{
		if ((context.Opcode & 0xF1FF) == 0x50AF &&
			TryReadWord(context.Offset + 2, out var addDisplacement))
		{
			instruction = new($"addq.l\t#{QuickCount(context.Opcode)},{unchecked((short)addDisplacement)}(a7)", 4);
			return true;
		}
		if ((context.Opcode & 0xF1FF) == 0x51AF &&
			TryReadWord(context.Offset + 2, out var subDisplacement))
		{
			instruction = new($"subq.l\t#{QuickCount(context.Opcode)},{unchecked((short)subDisplacement)}(a7)", 4);
			return true;
		}

		instruction = default;
		return false;
	}

	private bool TryRenderLea(in InstructionRenderContext context, out RenderedInstruction instruction)
	{
		if ((context.Opcode & 0xF1C0) == 0x41C0 &&
			TryDecodeEffectiveAddress(
				context.Offset + 2,
				(context.Opcode >> 3) & 7,
				context.Opcode & 7,
				4,
				out var source) &&
			source.Kind == M68kOperandKind.Memory)
		{
			instruction = new(
				$"lea\t{source.Text},a{(context.Opcode >> 9) & 7}",
				2 + source.ExtensionBytes);
			return true;
		}

		instruction = default;
		return false;
	}

	private bool TryRenderDataRegisterArithmetic(
		in InstructionRenderContext context,
		out RenderedInstruction instruction)
	{
		var mnemonic = LongMemoryArithmeticMnemonic(context.Opcode);
		if (mnemonic is null ||
			!TryDecodeEffectiveAddress(
				context.Offset + 2,
				(context.Opcode >> 3) & 7,
				context.Opcode & 7,
				4,
				out var source) ||
			source.Kind == M68kOperandKind.Immediate)
		{
			instruction = default;
			return false;
		}

		instruction = new(
			$"{mnemonic}\t{source.Text},d{(context.Opcode >> 9) & 7}",
			2 + source.ExtensionBytes);
		return true;
	}

	private static string? LongMemoryArithmeticMnemonic(ushort opcode) =>
		(opcode & 0xF1C0) switch
		{
			0xD080 => "add.l",
			0x9080 => "sub.l",
			0xB080 => "cmp.l",
			0xC080 => "and.l",
			_ => null
		};

	private bool TryRenderMemoryDestinationArithmetic(
		in InstructionRenderContext context,
		out RenderedInstruction instruction)
	{
		if (!TryGetLongMemoryDestinationArithmetic(
				context.Opcode,
				out var mnemonic,
				out var source) ||
			!TryDecodeEffectiveAddress(
				context.Offset + 2,
				(context.Opcode >> 3) & 7,
				context.Opcode & 7,
				4,
				out var destination) ||
			destination.Kind != M68kOperandKind.Memory)
		{
			instruction = default;
			return false;
		}

		instruction = new(
			$"{mnemonic}\t{source},{destination.Text}",
			2 + destination.ExtensionBytes);
		return true;
	}

	private static bool TryGetLongMemoryDestinationArithmetic(
		ushort opcode,
		out string mnemonic,
		out string source)
	{
		var operation = opcode & 0xF1C0;
		mnemonic = operation switch
		{
			0xD180 => "add.l",
			0x9180 => "sub.l",
			0x5080 => "addq.l",
			0x5180 => "subq.l",
			_ => string.Empty
		};
		if (mnemonic.Length == 0)
		{
			source = string.Empty;
			return false;
		}

		source = operation is 0x5080 or 0x5180
			? $"#{QuickCount(opcode)}"
			: $"d{(opcode >> 9) & 7}";
		return true;
	}

	private bool TryRenderDisplacementInstruction(int offset, ushort opcode, out RenderedInstruction instruction)
	{
		if (TryRenderDisplacementInstruction(offset, opcode, out var text, out var length))
		{
			instruction = new(text, length);
			return true;
		}
		instruction = default;
		return false;
	}

	private bool TryRenderGeneratedArithmetic(
		in InstructionRenderContext context,
		out RenderedInstruction instruction)
	{
		var opcode = context.Opcode;
		if ((opcode & 0xF1FF) == 0xD12F &&
			TryReadWord(context.Offset + 2, out var stackDisplacement))
		{
			instruction = new(
				$"add.b\td{(opcode >> 9) & 7},{unchecked((short)stackDisplacement)}(a7)",
				4);
			return true;
		}

		var binary = opcode & 0xF1F8;
		if (binary is
			0xD000 or 0xD040 or 0xD080 or
			0x9000 or 0x9040 or 0x9080 or
			0x8000 or 0x8040 or 0x8080 or
				0xC000 or 0xC040 or 0xC080 or
				0xB100 or 0xB140 or 0xB180)
		{
			var mnemonic = binary switch
			{
				0xD000 => "add.b",
				0xD040 => "add.w",
				0xD080 => "add.l",
				0x9000 => "sub.b",
				0x9040 => "sub.w",
				0x9080 => "sub.l",
				0x8000 => "or.b",
				0x8040 => "or.w",
				0x8080 => "or.l",
				0xC000 => "and.b",
				0xC040 => "and.w",
				0xC080 => "and.l",
				0xB100 => "eor.b",
				0xB140 => "eor.w",
				_ => "eor.l"
			};
			instruction = binary is 0xB100 or 0xB140 or 0xB180
				? new($"{mnemonic}\td{(opcode >> 9) & 7},d{opcode & 7}", 2)
				: new($"{mnemonic}\td{opcode & 7},d{(opcode >> 9) & 7}", 2);
			return true;
		}
		if (binary is 0xB000 or 0xB040 or 0xB080)
		{
			var mnemonic = binary switch
			{
				0xB000 => "cmp.b",
				0xB040 => "cmp.w",
				_ => "cmp.l"
			};
			instruction = new($"{mnemonic}\td{opcode & 7},d{(opcode >> 9) & 7}", 2);
			return true;
		}
		if (binary is 0xC0C0 or 0xC1C0)
		{
			instruction = new($"{(binary == 0xC1C0 ? "muls" : "mulu")}.w\td{opcode & 7},d{(opcode >> 9) & 7}", 2);
			return true;
		}

		if (TryRenderBitOperation(context, out instruction) ||
			TryRenderShift(context, out instruction) ||
			TryRenderLongMultiplyOrDivide(context, out instruction))
		{
			return true;
		}

		instruction = default;
		return false;
	}

	private bool TryRenderBitOperation(in InstructionRenderContext context, out RenderedInstruction instruction)
	{
		var opcode = context.Opcode;
		var immediateOperation = opcode & 0xFFF8;
		if ((immediateOperation is 0x0800 or 0x0840 or 0x0880 or 0x08C0) &&
			TryReadWord(context.Offset + 2, out var bit))
		{
			instruction = new($"{BitMnemonic((ushort)immediateOperation)}\t#{bit},d{opcode & 7}", 4);
			return true;
		}

		var registerOperation = opcode & 0xF1F8;
		if (registerOperation is 0x0100 or 0x0140 or 0x0180 or 0x01C0)
		{
			instruction = new($"{BitMnemonic((ushort)registerOperation)}\td{(opcode >> 9) & 7},d{opcode & 7}", 2);
			return true;
		}

		instruction = default;
		return false;
	}

	private static bool TryRenderShift(in InstructionRenderContext context, out RenderedInstruction instruction)
	{
		var masked = context.Opcode & 0xF1F8;
		var mnemonic = masked switch
		{
			0xE000 => "asr.b",
			0xE008 => "lsr.b",
			0xE108 => "lsl.b",
			0xE040 => "asr.w",
			0xE048 => "lsr.w",
			0xE148 => "lsl.w",
			0xE080 => "asr.l",
			0xE088 => "lsr.l",
			0xE188 => "lsl.l",
			0xE158 => "rol.w",
			0xE098 => "ror.l",
			_ => null
		};
		if (mnemonic is not null)
		{
			instruction = new($"{mnemonic}\t#{QuickCount(context.Opcode)},d{context.Opcode & 7}", 2);
			return true;
		}

		instruction = default;
		return false;
	}

	private bool TryRenderLongMultiplyOrDivide(in InstructionRenderContext context, out RenderedInstruction instruction)
	{
		if (context.Opcode == 0x4C01 && TryReadWord(context.Offset + 2, out var multiplyExtension) &&
			multiplyExtension == 0x0800)
		{
			instruction = new("muls.l\td1,d0", 4);
			return true;
		}
		if (context.Opcode == 0x4C41 && TryReadWord(context.Offset + 2, out var divideExtension) &&
			(divideExtension & 0xF7FF) == 0x0002)
		{
			instruction = new((divideExtension & 0x0800) != 0
				? "divs.l\td1,d2:d0"
				: "divu.l\td1,d2:d0", 4);
			return true;
		}

		instruction = default;
		return false;
	}

	private static string BitMnemonic(ushort operation) => operation switch
	{
		0x0800 or 0x0100 => "btst",
		0x0840 or 0x0140 => "bchg",
		0x0880 or 0x0180 => "bclr",
		0x08C0 or 0x01C0 => "bset",
		_ => throw new ArgumentOutOfRangeException(nameof(operation))
	};

	private static bool TryRenderSimpleInstruction(
		ushort opcode,
		out string instruction)
	{
		foreach (var rule in SimpleInstructionRules)
		{
			if ((opcode & rule.Mask) == rule.Value)
			{
				instruction = rule.Render(opcode);
				return true;
			}
		}

		instruction = string.Empty;
		return false;
	}

	private bool TryRenderImmediateInstruction(
		int offset,
		ushort opcode,
		out string instruction,
		out int length)
	{
		foreach (var rule in ImmediateInstructionRules)
		{
			if ((opcode & rule.Mask) != rule.Value)
			{
				continue;
			}

			uint value;
			if (rule.ImmediateBytes == 2)
			{
				if (!TryReadWord(offset + 2, out var word))
				{
					break;
				}
				value = word;
			}
			else if (!TryReadLong(offset + 2, out value))
			{
				break;
			}

			instruction = rule.Render(opcode, value);
			length = 2 + rule.ImmediateBytes;
			return true;
		}

		instruction = string.Empty;
		length = 2;
		return false;
	}

	private bool TryRenderDisplacementInstruction(
		int offset,
		ushort opcode,
		out string instruction,
		out int length)
	{
		instruction = string.Empty;
		length = 2;
		if (!TryReadWord(offset + 2, out var displacement))
		{
			return false;
		}

		var value = unchecked((short)displacement);
		instruction = opcode switch
		{
			0x4EAE => $"jsr\t{value}(a6)",
			_ when (opcode & 0xFFF8) == 0x4EA8 => $"jsr\t{value}(a{opcode & 7})",
			_ when (opcode & 0xFFF8) == 0x4EE8 => $"jmp\t{value}(a{opcode & 7})",
			_ when (opcode & 0xFFF8) == 0x42A8 =>
				$"clr.l\t{value}(a{opcode & 7})",
			0x486F => $"pea\t{value}(a7)",
			_ when (opcode & 0xF1FF) == 0x41EF =>
				$"lea\t{value}(a7),a{(opcode >> 9) & 7}",
			_ when (opcode & 0xF1FF) == 0xB0AF =>
				$"cmp.l\t{value}(a7),d{(opcode >> 9) & 7}",
			_ => string.Empty
		};
		if (instruction.Length == 0)
		{
			return false;
		}

		length = 4;
		return true;
	}

	private bool TryRenderMove(
		int offset,
		ushort opcode,
		out string instruction,
		out int length)
	{
		if (!TryDecodeMove(offset, opcode, out var decoded))
		{
			instruction = string.Empty;
			length = 2;
			return false;
		}

		instruction = decoded.Mnemonic + "\t" +
			decoded.Source.Text + "," + decoded.Destination.Text;
		length = decoded.Length;
		return true;
	}

	private bool TryDecodeMove(
		int offset,
		ushort opcode,
		out DecodedMove instruction)
	{
		instruction = default;
		var sizeCode = (opcode >> 12) & 0xF;
		if (sizeCode is not (1 or 2 or 3))
		{
			return false;
		}

		var sizeBytes = sizeCode == 1 ? 1 : sizeCode == 2 ? 4 : 2;
		var sourceMode = (opcode >> 3) & 7;
		var sourceRegister = opcode & 7;
		var destinationMode = (opcode >> 6) & 7;
		var destinationRegister = (opcode >> 9) & 7;
		var movea = destinationMode == 1;
		if (movea && sizeBytes == 1)
		{
			return false;
		}

		if (!TryDecodeEffectiveAddress(
			offset + 2,
			sourceMode,
			sourceRegister,
			sizeBytes,
			out var source))
		{
			return false;
		}

		if (!movea && (destinationMode == 1 ||
			(destinationMode == 7 && destinationRegister == 4)))
		{
			return false;
		}

		if (!TryDecodeEffectiveAddress(
			offset + 2 + source.ExtensionBytes,
			destinationMode,
			destinationRegister,
			sizeBytes,
			out var destination))
		{
			return false;
		}

		var mnemonic = movea
			? "movea." + (sizeBytes == 2 ? "w" : "l")
			: "move." + (sizeBytes == 1 ? "b" : sizeBytes == 2 ? "w" : "l");
		instruction = new DecodedMove(
			mnemonic,
			source,
			destination,
			2 + source.ExtensionBytes + destination.ExtensionBytes);
		return true;
	}

	private bool TryDecodeEffectiveAddress(
		int offset,
		int mode,
		int register,
		int sizeBytes,
		out M68kOperand operand)
	{
		operand = default;
		switch (mode)
		{
			case 0:
				operand = new M68kOperand(
					M68kOperandKind.DataRegister,
					"d" + register,
					0);
				return true;
			case 1:
				operand = new M68kOperand(
					M68kOperandKind.AddressRegister,
					"a" + register,
					0);
				return true;
			case 2:
				operand = new M68kOperand(
					M68kOperandKind.Memory,
					"(a" + register + ")",
					0);
				return true;
			case 3:
				operand = new M68kOperand(
					M68kOperandKind.Memory,
					"(a" + register + ")+",
					0);
				return true;
			case 4:
				operand = new M68kOperand(
					M68kOperandKind.Memory,
					"-(a" + register + ")",
					0);
				return true;
			case 5:
				if (!TryReadWord(offset, out var displacement))
				{
					return false;
				}
				operand = new M68kOperand(
					M68kOperandKind.Memory,
					FormatDisplacement(unchecked((short)displacement), "(a" + register + ")"),
					2);
				return true;
			case 6:
				if (!TryReadWord(offset, out var indexExtension))
				{
					return false;
				}
				var indexScale = 1 << ((indexExtension >> 9) & 3);
				operand = new M68kOperand(
					M68kOperandKind.Memory,
					FormatDisplacement(
						unchecked((sbyte)indexExtension),
						"(a" + register + "," +
						((indexExtension & 0x8000) != 0 ? "a" : "d") +
						((indexExtension >> 12) & 7) + "." +
						((indexExtension & 0x0800) != 0 ? "l" : "w") +
						(indexScale == 1 ? "" : "*" + indexScale) + ")"),
					2);
				return true;
			case 7:
				switch (register)
				{
					case 0:
						if (!TryReadWord(offset, out var absoluteWord))
						{
							return false;
						}
						operand = new M68kOperand(
							M68kOperandKind.Memory,
							"$" + absoluteWord.ToString("X4") + ".w",
							2);
						return true;
					case 1:
						if (!TryReadLong(offset, out var absoluteLong))
						{
							return false;
						}
						operand = new M68kOperand(
							M68kOperandKind.Memory,
							"$" + absoluteLong.ToString("X8"),
							4);
						return true;
					case 2:
						if (!TryReadWord(offset, out var pcDisplacement))
						{
							return false;
						}
						operand = new M68kOperand(
							M68kOperandKind.Memory,
							FormatDisplacement(unchecked((short)pcDisplacement), "(pc)"),
							2);
						return true;
					case 3:
						if (!TryReadWord(offset, out var pcIndexExtension))
						{
							return false;
						}
						operand = new M68kOperand(
							M68kOperandKind.Memory,
							FormatDisplacement(
								unchecked((sbyte)pcIndexExtension),
								"(pc," +
								((pcIndexExtension & 0x8000) != 0 ? "a" : "d") +
								((pcIndexExtension >> 12) & 7) + "." +
								((pcIndexExtension & 0x0800) != 0 ? "l" : "w") + ")"),
							2);
						return true;
					case 4:
						if (sizeBytes == 4)
						{
							if (!TryReadLong(offset, out var immediateLong))
							{
								return false;
							}
							operand = new M68kOperand(
								M68kOperandKind.Immediate,
								"#$" + immediateLong.ToString("X8"),
								4);
							return true;
						}

						if (!TryReadWord(offset, out var immediateWord))
						{
							return false;
						}
						operand = new M68kOperand(
							M68kOperandKind.Immediate,
							"#$" + (sizeBytes == 1
								? (immediateWord & 0xFF).ToString("X2")
								: immediateWord.ToString("X4")),
							2);
						return true;
				}
				break;
		}

		return false;
	}

	private static string FormatDisplacement(int displacement, string suffix) =>
		displacement == 0 ? suffix : displacement + suffix;

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
			var displayLabel = labels.FirstOrDefault(label =>
				referencedLabels.Contains(label) && !IsIlLabel(label)) ??
				labels.FirstOrDefault(static label => !IsIlLabel(label)) ??
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

	private static void ValidateFpuRegister(int register)
	{
		if ((uint)register > 7)
		{
			throw new ArgumentOutOfRangeException(nameof(register));
		}
	}

	private enum M68kOperandKind : byte
	{
		DataRegister,
		AddressRegister,
		Memory,
		Immediate
	}

	private readonly record struct M68kOperand(
		M68kOperandKind Kind,
		string Text,
		int ExtensionBytes);

	private readonly record struct DecodedMove(
		string Mnemonic,
		M68kOperand Source,
		M68kOperand Destination,
		int Length);

	private readonly record struct InstructionRenderContext(
		int Offset,
		ushort Opcode,
		IReadOnlyDictionary<string, string> DisplayLabels,
		IReadOnlyDictionary<int, AddressFixup> Addresses,
		IReadOnlyDictionary<int, BranchFixup> Branches,
		IReadOnlyDictionary<int, PcRelativeFixup> PcRelative);

	private readonly record struct RenderedInstruction(string Text, int Length);

	private readonly record struct OpcodeRenderRule(
		ushort Mask,
		ushort Value,
		Func<ushort, string> Render);

	private readonly record struct ImmediateRenderRule(
		ushort Mask,
		ushort Value,
		int ImmediateBytes,
		Func<ushort, uint, string> Render);
}

internal sealed record LinkedCode(
	byte[] Bytes,
	IReadOnlyDictionary<string, int> Labels,
	IReadOnlyDictionary<string, int> AnalysisAnchors,
	IReadOnlyList<M68kRelocation> Relocations);
