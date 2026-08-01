/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal enum M68kInstructionKind : byte
{
	Normal,
	ConditionalBranch,
	UnconditionalBranch,
	Dbcc,
	Call,
	Return
}

internal readonly record struct M68kEmittedInstruction(
	int Offset,
	int Length,
	ushort Opcode,
	ushort ExtensionWord,
	uint ExtensionLong,
	bool IsDecoded,
	M68kInstructionKind Kind,
	int? TargetOffset,
	bool ExternalTarget,
	bool IsNonReturning);

[Flags]
internal enum M68kConditionCodeSet : byte
{
	None = 0,
	Extend = 1,
	Negative = 2,
	Zero = 4,
	Overflow = 8,
	Carry = 16,
	All = Extend | Negative | Zero | Overflow | Carry
}

[Flags]
internal enum M68kMemorySet : byte
{
	None = 0,
	Stack = 1,
	Known = 2,
	Indirect = 4,
	Unknown = 8,
	All = Stack | Known | Indirect | Unknown
}

internal readonly record struct M68kInstructionEffects(
	ushort UsesData,
	ushort DefinesData,
	ushort UsesAddress,
	ushort DefinesAddress,
	M68kConditionCodeSet ReadsConditions,
	M68kConditionCodeSet WritesConditions,
	M68kMemorySet ReadsMemory,
	M68kMemorySet WritesMemory,
	int? StackDelta,
	bool IsBarrier,
	bool CanRemoveWhenOutputsDead);

internal readonly record struct M68kInstructionDataflowFacts(
	M68kInstructionEffects Effects,
	ushort LiveDataBefore,
	ushort LiveDataAfter,
	ushort LiveAddressBefore,
	ushort LiveAddressAfter,
	M68kConditionCodeSet LiveConditionsBefore,
	M68kConditionCodeSet LiveConditionsAfter,
	M68kMemorySet LiveMemoryBefore,
	M68kMemorySet LiveMemoryAfter,
	int? StackDeltaBefore,
	int? StackDeltaAfter)
{
	public bool ConditionsAreDeadAfter => LiveConditionsAfter == M68kConditionCodeSet.None;
}

internal sealed class M68kInstructionDataflow
{
	private const ushort AllRegisters = 0x00FF;

	private readonly IReadOnlyList<M68kEmittedInstruction> _instructions;
	private readonly IReadOnlyDictionary<int, M68kInstructionDataflowFacts> _facts;
	private readonly M68kValueRangeAnalysis _values;
	private readonly M68kConditionProvenanceAnalysis _conditions;

	private M68kInstructionDataflow(
		IReadOnlyList<M68kEmittedInstruction> instructions,
		IReadOnlyDictionary<int, M68kInstructionDataflowFacts> facts,
		M68kValueRangeAnalysis values,
		M68kConditionProvenanceAnalysis conditions)
	{
		_instructions = instructions;
		_facts = facts;
		_values = values;
		_conditions = conditions;
	}

	internal IReadOnlyList<M68kEmittedInstruction> Instructions => _instructions;

	internal bool TryGetFacts(int offset, out M68kInstructionDataflowFacts facts) =>
		_facts.TryGetValue(offset, out facts);

	internal static M68kInstructionEffects GetEffects(
		M68kEmittedInstruction instruction) =>
		Classify(instruction);

	internal M68kValueRange GetDataValueBefore(int offset, int register) =>
		_values.GetDataValueBefore(offset, register);

	internal M68kAddressAlias GetAddressAliasBefore(int offset, int register) =>
		_values.GetAddressAliasBefore(offset, register);

	internal bool IsConditionInstructionRedundant(
		M68kEmittedInstruction instruction,
		M68kConditionCodeSet required) =>
		_conditions.IsRedundant(instruction, required);

	internal bool TryGetKnownZeroTest(
		M68kEmittedInstruction instruction,
		out bool nonZero) =>
		_conditions.TryGetKnownZeroTest(instruction, out nonZero);

	internal static M68kInstructionDataflow Analyze(M68kAssembler assembler)
	{
		var instructions = assembler.GetInstructionStream();
		if (instructions.Count == 0)
		{
			return new M68kInstructionDataflow(
				instructions,
				new Dictionary<int, M68kInstructionDataflowFacts>(),
				M68kValueRangeAnalysis.Empty,
				M68kConditionProvenanceAnalysis.Empty);
		}

		var indexByOffset = instructions
			.Select((instruction, index) => (instruction.Offset, index))
			.ToDictionary(static item => item.Offset, static item => item.index);
		var effects = instructions
			.Select(Classify)
			.ToArray();
		var successors = BuildSuccessors(instructions, indexByOffset);
		var predecessors = BuildPredecessors(successors);
		var liveDataBefore = new ushort[instructions.Count];
		var liveDataAfter = new ushort[instructions.Count];
		var liveAddressBefore = new ushort[instructions.Count];
		var liveAddressAfter = new ushort[instructions.Count];
		var liveConditionsBefore = new M68kConditionCodeSet[instructions.Count];
		var liveConditionsAfter = new M68kConditionCodeSet[instructions.Count];
		var liveMemoryBefore = new M68kMemorySet[instructions.Count];
		var liveMemoryAfter = new M68kMemorySet[instructions.Count];

		bool changed;
		do
		{
			changed = false;
			for (var index = instructions.Count - 1; index >= 0; index--)
			{
				var afterData = Union(successors[index], liveDataBefore);
				var afterAddress = Union(successors[index], liveAddressBefore);
				var afterConditions = Union(successors[index], liveConditionsBefore);
				var afterMemory = Union(successors[index], liveMemoryBefore);
				var beforeData = (ushort)(effects[index].UsesData |
					(afterData & (ushort)~effects[index].DefinesData));
				var beforeAddress = (ushort)(effects[index].UsesAddress |
					(afterAddress & (ushort)~effects[index].DefinesAddress));
				var beforeConditions = effects[index].ReadsConditions |
					(afterConditions & ~effects[index].WritesConditions);
				var beforeMemory = TransferMemory(
					afterMemory,
					effects[index].ReadsMemory,
					effects[index].WritesMemory);

				changed |= Set(ref liveDataAfter[index], afterData);
				changed |= Set(ref liveAddressAfter[index], afterAddress);
				changed |= Set(ref liveConditionsAfter[index], afterConditions);
				changed |= Set(ref liveMemoryAfter[index], afterMemory);
				changed |= Set(ref liveDataBefore[index], beforeData);
				changed |= Set(ref liveAddressBefore[index], beforeAddress);
				changed |= Set(ref liveConditionsBefore[index], beforeConditions);
				changed |= Set(ref liveMemoryBefore[index], beforeMemory);
			}
		}
		while (changed);

		var stackBefore = new int?[instructions.Count];
		var stackAfter = new int?[instructions.Count];
		var stackSeen = new bool[instructions.Count];
		foreach (var index in Enumerable.Range(0, instructions.Count)
			.Where(index => predecessors[index].Count == 0))
		{
			stackSeen[index] = true;
			stackBefore[index] = 0;
		}

		bool stackChanged;
		do
		{
			stackChanged = false;
			for (var index = 0; index < instructions.Count; index++)
			{
				if (!stackSeen[index])
				{
					continue;
				}

				var before = stackBefore[index];
				var after = before.HasValue && effects[index].StackDelta.HasValue
					? (int?)(before.Value + effects[index].StackDelta.GetValueOrDefault())
					: null;
				stackChanged |= Set(ref stackAfter[index], after);
				foreach (var successor in successors[index])
				{
					stackChanged |= JoinStack(
						ref stackSeen[successor],
						ref stackBefore[successor],
						after);
				}
			}
		}
		while (stackChanged);

		var result = new Dictionary<int, M68kInstructionDataflowFacts>();
		for (var index = 0; index < instructions.Count; index++)
		{
			result[instructions[index].Offset] = new M68kInstructionDataflowFacts(
				effects[index],
				liveDataBefore[index],
				liveDataAfter[index],
				liveAddressBefore[index],
				liveAddressAfter[index],
				liveConditionsBefore[index],
				liveConditionsAfter[index],
				liveMemoryBefore[index],
				liveMemoryAfter[index],
				stackBefore[index],
				stackAfter[index]);
		}

		return new M68kInstructionDataflow(
			instructions,
			result,
			M68kValueRangeAnalysis.Analyze(instructions, successors, predecessors, effects),
			M68kConditionProvenanceAnalysis.Analyze(
				instructions,
				successors,
				predecessors,
				effects));
	}

	private static IReadOnlyList<int>[] BuildSuccessors(
		IReadOnlyList<M68kEmittedInstruction> instructions,
		IReadOnlyDictionary<int, int> indexByOffset)
	{
		var result = new IReadOnlyList<int>[instructions.Count];
		for (var index = 0; index < instructions.Count; index++)
		{
			var instruction = instructions[index];
			var successors = new List<int>();
			var next = index + 1 < instructions.Count ? index + 1 : -1;
			switch (instruction.Kind)
			{
				case M68kInstructionKind.ConditionalBranch:
				case M68kInstructionKind.Dbcc:
					AddTarget(successors, instruction.TargetOffset, indexByOffset);
					AddNext(successors, next);
					break;
				case M68kInstructionKind.UnconditionalBranch:
					AddTarget(successors, instruction.TargetOffset, indexByOffset);
					break;
				case M68kInstructionKind.Return:
					break;
				default:
					if (!instruction.IsNonReturning)
					{
						AddNext(successors, next);
					}
					break;
			}
			result[index] = successors;
		}

		return result;
	}

	private static List<int>[] BuildPredecessors(IReadOnlyList<int>[] successors)
	{
		var result = Enumerable.Range(0, successors.Length)
			.Select(static _ => new List<int>())
			.ToArray();
		for (var index = 0; index < successors.Length; index++)
		{
			foreach (var successor in successors[index])
			{
				result[successor].Add(index);
			}
		}

		return result;
	}

	private static void AddTarget(
		List<int> successors,
		int? targetOffset,
		IReadOnlyDictionary<int, int> indexByOffset)
	{
		if (targetOffset.HasValue && indexByOffset.TryGetValue(targetOffset.Value, out var target))
		{
			successors.Add(target);
		}
	}

	private static void AddNext(List<int> successors, int next)
	{
		if (next >= 0)
		{
			successors.Add(next);
		}
	}

	private static M68kInstructionEffects Classify(M68kEmittedInstruction instruction)
	{
		if (!instruction.IsDecoded)
		{
			return BarrierEffects;
		}

		if (instruction.Kind == M68kInstructionKind.Call)
		{
			return new M68kInstructionEffects(
				AllRegisters,
				0,
				AllRegisters,
				0,
				M68kConditionCodeSet.All,
				M68kConditionCodeSet.All,
				M68kMemorySet.All,
				M68kMemorySet.All,
				0,
				true,
				false);
		}

		if (instruction.Kind == M68kInstructionKind.ConditionalBranch)
		{
			return new M68kInstructionEffects(
				0,
				0,
				0,
				0,
				ConditionCodes((instruction.Opcode >> 8) & 0x0F),
				M68kConditionCodeSet.None,
				M68kMemorySet.None,
				M68kMemorySet.None,
				null,
				false,
				false);
		}

		if (instruction.Kind == M68kInstructionKind.UnconditionalBranch)
		{
			if ((instruction.Opcode & 0xFFF8) == 0x4EE8)
			{
				// A base-relative JMP emitted by the tail-call fold transfers to an
				// external Amiga vector.  It does not return locally, but it still
				// consumes the complete call ABI; model that use so preceding argument
				// and library-base setup remains live.
				return new M68kInstructionEffects(
					AllRegisters,
					0,
					AllRegisters,
					0,
					M68kConditionCodeSet.All,
					M68kConditionCodeSet.None,
					M68kMemorySet.All,
					M68kMemorySet.None,
					0,
					true,
					false);
			}

			return EmptyEffects;
		}

		if (instruction.Kind == M68kInstructionKind.Dbcc)
		{
			var register = (ushort)(1 << (instruction.Opcode & 7));
			return new M68kInstructionEffects(
				register,
				register,
				0,
				0,
				ConditionCodes((instruction.Opcode >> 8) & 0x0F),
				M68kConditionCodeSet.None,
				M68kMemorySet.None,
				M68kMemorySet.None,
				null,
				false,
				false);
		}

		if (instruction.Kind == M68kInstructionKind.Return)
		{
			return new M68kInstructionEffects(
				0x00FF, // D0-D1 return values and preserved D2-D7
				0,
				0x00FD, // A0 reference return, preserved A2-A6, and A7
				0x0080,
				M68kConditionCodeSet.None,
				M68kConditionCodeSet.None,
				M68kMemorySet.Stack,
				M68kMemorySet.None,
				4,
				true,
				false);
		}

		var builder = new EffectBuilder();
		builder.ClassifyNormal(instruction.Opcode, instruction.ExtensionWord);
		return builder.Build();
	}

	private static readonly M68kInstructionEffects EmptyEffects = new(
		0,
		0,
		0,
		0,
		M68kConditionCodeSet.None,
		M68kConditionCodeSet.None,
		M68kMemorySet.None,
		M68kMemorySet.None,
		0,
		false,
		false);

	private static readonly M68kInstructionEffects BarrierEffects = new(
		AllRegisters,
		0,
		AllRegisters,
		0,
		M68kConditionCodeSet.All,
		M68kConditionCodeSet.All,
		M68kMemorySet.All,
		M68kMemorySet.All,
		null,
		true,
		false);

	private static M68kConditionCodeSet ConditionCodes(int condition) => condition switch
	{
		2 or 3 => M68kConditionCodeSet.Zero | M68kConditionCodeSet.Carry,
		4 or 5 => M68kConditionCodeSet.Carry,
		6 or 7 => M68kConditionCodeSet.Zero,
		8 or 9 => M68kConditionCodeSet.Overflow,
		10 or 11 => M68kConditionCodeSet.Negative,
		12 or 13 => M68kConditionCodeSet.Negative | M68kConditionCodeSet.Overflow,
		14 or 15 => M68kConditionCodeSet.Negative |
			M68kConditionCodeSet.Zero |
			M68kConditionCodeSet.Overflow,
		_ => M68kConditionCodeSet.None
	};

	private static M68kMemorySet TransferMemory(
		M68kMemorySet after,
		M68kMemorySet reads,
		M68kMemorySet writes)
	{
		if ((reads & M68kMemorySet.Unknown) != 0)
		{
			return M68kMemorySet.All;
		}

		var live = (writes & M68kMemorySet.Unknown) != 0
			? M68kMemorySet.None
			: (M68kMemorySet)(after & ~writes);
		return (M68kMemorySet)(live | reads);
	}

	private static bool Set(ref ushort target, ushort value)
	{
		if (target == value)
		{
			return false;
		}
		target = value;
		return true;
	}

	private static bool Set(ref M68kConditionCodeSet target, M68kConditionCodeSet value)
	{
		if (target == value)
		{
			return false;
		}
		target = value;
		return true;
	}

	private static bool Set(ref M68kMemorySet target, M68kMemorySet value)
	{
		if (target == value)
		{
			return false;
		}
		target = value;
		return true;
	}

	private static bool Set(ref int? target, int? value)
	{
		if (target == value)
		{
			return false;
		}
		target = value;
		return true;
	}

	private static ushort Union(IReadOnlyList<int> successors, ushort[] values)
	{
		var result = (ushort)0;
		foreach (var successor in successors)
		{
			result |= values[successor];
		}
		return result;
	}

	private static M68kConditionCodeSet Union(
		IReadOnlyList<int> successors,
		M68kConditionCodeSet[] values)
	{
		var result = M68kConditionCodeSet.None;
		foreach (var successor in successors)
		{
			result |= values[successor];
		}
		return result;
	}

	private static M68kMemorySet Union(
		IReadOnlyList<int> successors,
		M68kMemorySet[] values)
	{
		var result = M68kMemorySet.None;
		foreach (var successor in successors)
		{
			result |= values[successor];
		}
		return result;
	}

	private static bool JoinStack(ref bool seen, ref int? target, int? value)
	{
		if (!seen)
		{
			seen = true;
			target = value;
			return true;
		}

		if (target is null && value is null)
		{
			return false;
		}

		if (target == value)
		{
			return false;
		}

		if (target is null)
		{
			return false;
		}

		target = null;
		return true;
	}

	private sealed class EffectBuilder
	{
		private ushort _usesData;
		private ushort _definesData;
		private ushort _usesAddress;
		private ushort _definesAddress;
		private M68kConditionCodeSet _readsConditions;
		private M68kConditionCodeSet _writesConditions;
		private M68kMemorySet _readsMemory;
		private M68kMemorySet _writesMemory;
		private int? _stackDelta = 0;
		private bool _barrier;
		private bool _canRemoveWhenOutputsDead;
		private ushort _extensionWord;

		internal void ClassifyNormal(ushort opcode, ushort extensionWord)
		{
			_extensionWord = extensionWord;
			if ((opcode & 0xF100) == 0x7000)
			{
				DefineData((opcode >> 9) & 7);
				WriteMoveConditions();
				_canRemoveWhenOutputsDead = true;
				return;
			}

			if ((opcode & 0xF1FF) == 0x41EF || opcode == 0x4FEF ||
				(opcode & 0xF1C0) == 0x41C0)
			{
				AddEffectiveAddress(opcode, EffectiveAddressAccess.AddressOnly);
				var destination = (opcode >> 9) & 7;
				DefineAddress(destination);
				if (destination == 7)
				{
					var sourceMode = (opcode >> 3) & 7;
					var sourceRegister = opcode & 7;
					if (sourceRegister == 7 && sourceMode is 2 or 5)
					{
						AdjustStack(sourceMode == 5 ? unchecked((short)extensionWord) : 0);
					}
					else
					{
						_stackDelta = null;
					}
				}
				_canRemoveWhenOutputsDead = true;
				return;
			}

			if ((opcode & 0xFFC0) is 0x48C0 or 0x4CC0)
			{
				ClassifyMovem(opcode, extensionWord);
				return;
			}

			if ((opcode & 0xFFF8) == 0x4850)
			{
				AddEffectiveAddress(opcode, EffectiveAddressAccess.AddressOnly);
				AdjustStack(-4, writesMemory: M68kMemorySet.Stack);
				return;
			}

			if ((opcode & 0xF000) == 0x2000)
			{
				ClassifyMove(opcode);
				_canRemoveWhenOutputsDead = true;
				return;
			}

			if ((opcode & 0xF000) == 0x3000)
			{
				ClassifyMove(opcode);
				_canRemoveWhenOutputsDead = true;
				return;
			}

			if ((opcode & 0xF1F8) == 0xC140)
			{
				UseData((opcode >> 9) & 7);
				DefineData((opcode >> 9) & 7);
				UseData(opcode & 7);
				DefineData(opcode & 7);
				_canRemoveWhenOutputsDead = true;
				return;
			}

			if ((opcode & 0xFFF8) is 0x4A80 or 0x4A40 or 0x4A00)
			{
				AddEffectiveAddress(opcode, EffectiveAddressAccess.Read);
				WriteArithmeticConditions();
				_canRemoveWhenOutputsDead = true;
				return;
			}

			if ((opcode & 0xFFC0) is 0x4280 or 0x4240 or 0x4200)
			{
				AddEffectiveAddress(opcode, EffectiveAddressAccess.ReadWrite);
				WriteArithmeticConditions();
				_canRemoveWhenOutputsDead = true;
				return;
			}

			var unaryOpcode = opcode & 0xFFC0;
			if (unaryOpcode is 0x4480 or 0x4440 or 0x4400 or
				0x4680 or 0x4640 or 0x4600)
			{
				AddEffectiveAddress(opcode, EffectiveAddressAccess.ReadWrite);
				WriteArithmeticConditions();
				_canRemoveWhenOutputsDead = true;
				return;
			}

			if ((opcode & 0xFFC0) is 0x4880 or 0x48C0 or 0x49C0)
			{
				UseData(opcode & 7);
				DefineData(opcode & 7);
				WriteArithmeticConditions();
				_canRemoveWhenOutputsDead = true;
				return;
			}

			if ((opcode & 0xF1FF) is 0xD1C0 or 0x91C0 ||
				(opcode & 0xF1F8) == 0x91C8)
			{
				AddEffectiveAddress(opcode, EffectiveAddressAccess.Read);
				UseAddress((opcode >> 9) & 7);
				DefineAddress((opcode >> 9) & 7);
				_canRemoveWhenOutputsDead = true;
				return;
			}

			if ((opcode & 0xF000) == 0x5000)
			{
				ClassifyQuickOrSet(opcode);
				_canRemoveWhenOutputsDead = true;
				return;
			}

			if (((opcode & 0xF100) == 0x0100 ||
				 (opcode & 0xFF00) == 0x0800) &&
				((opcode >> 3) & 7) != 1)
			{
				ClassifyBitOperation(opcode);
				return;
			}

			var logicalImmediate = opcode & 0x0F00;
			if ((opcode & 0xF000) == 0 &&
				logicalImmediate is 0x0000 or 0x0200 or 0x0A00)
			{
				AddEffectiveAddress(opcode, EffectiveAddressAccess.ReadWrite);
				WriteMoveConditions();
				_canRemoveWhenOutputsDead = true;
				return;
			}

			if ((opcode & 0xF000) == 0 &&
				(opcode & 0x0F00) is 0x0400 or 0x0600)
			{
				AddEffectiveAddress(opcode, EffectiveAddressAccess.ReadWrite);
				WriteArithmeticConditions();
				_canRemoveWhenOutputsDead = true;
				return;
			}

			if ((opcode & 0xF000) == 0x0000 && (opcode & 0x0C00) == 0x0C00)
			{
				AddEffectiveAddress(opcode, EffectiveAddressAccess.Read);
				WriteArithmeticConditions();
				_canRemoveWhenOutputsDead = true;
				return;
			}

			if ((opcode & 0xF000) is 0x8000 or 0x9000 or 0xB000 or 0xC000 or 0xD000)
			{
				ClassifyArithmetic(opcode);
				var operation = opcode & 0xF000;
				var operationMode = (opcode >> 6) & 7;
				_canRemoveWhenOutputsDead =
					operation is 0x9000 or 0xB000 or 0xD000 ||
					operationMode is not 3 and not 7;
				return;
			}

			if ((opcode & 0xF000) == 0xE000)
			{
				ClassifyShift(opcode);
				_canRemoveWhenOutputsDead = true;
				return;
			}

			_barrier = true;
			_usesData = AllRegisters;
			_definesData = 0;
			_usesAddress = AllRegisters;
			_definesAddress = 0;
			_readsConditions = M68kConditionCodeSet.All;
			_writesConditions = M68kConditionCodeSet.All;
			_readsMemory = M68kMemorySet.All;
			_writesMemory = M68kMemorySet.All;
			_stackDelta = null;
		}

		internal M68kInstructionEffects Build() => new(
			_usesData,
			_definesData,
			_usesAddress,
			_definesAddress,
			_readsConditions,
			_writesConditions,
			_readsMemory,
			_writesMemory,
			_stackDelta,
			_barrier,
			_canRemoveWhenOutputsDead);

		private void ClassifyMove(ushort opcode)
		{
			var destinationMode = (opcode >> 6) & 7;
			var destination = (opcode >> 9) & 7;
			AddEffectiveAddress(opcode, EffectiveAddressAccess.Read);
			if (destinationMode == 1)
			{
				DefineAddress(destination);
				if (destination == 7)
				{
					_stackDelta = null;
				}
				return;
			}

			if (destinationMode == 0)
			{
				if ((opcode & 0xF000) != 0x2000)
				{
					UseData(destination);
				}
				DefineData(destination);
			}
			else
			{
				AddEffectiveAddress((ushort)(destination | (destinationMode << 3)), EffectiveAddressAccess.Write);
			}
			WriteMoveConditions();
		}

		private void ClassifyMovem(ushort opcode, ushort mask)
		{
			var isStore = (opcode & 0xFFC0) == 0x48C0;
			var mode = (opcode >> 3) & 7;
			var addressRegister = opcode & 7;
			var predecrement = isStore && mode == 4;
			var postincrement = !isStore && mode == 3;
			if (predecrement)
			{
				UseAddress(addressRegister);
				DefineAddress(addressRegister);
				for (var register = 0; register < 8; register++)
				{
					if ((mask & (1 << register)) != 0)
					{
						UseData(register);
					}
					if ((mask & (1 << (register + 8))) != 0)
					{
						UseAddress(register);
					}
				}
				AdjustStack(-4 * BitCount(mask), writesMemory: M68kMemorySet.Stack);
				return;
			}

			if (postincrement)
			{
				UseAddress(addressRegister);
				DefineAddress(addressRegister);
				for (var register = 0; register < 8; register++)
				{
					if ((mask & (1 << register)) != 0)
					{
						DefineData(register);
					}
					if ((mask & (1 << (register + 8))) != 0)
					{
						DefineAddress(register);
					}
				}
				AdjustStack(4 * BitCount(mask), readsMemory: M68kMemorySet.Stack);
				return;
			}

			AddEffectiveAddress(
				opcode,
				isStore ? EffectiveAddressAccess.Write : EffectiveAddressAccess.Read);
			for (var registerIndex = 0; registerIndex < 8; registerIndex++)
			{
				if ((mask & (1 << registerIndex)) != 0)
				{
					if (isStore)
					{
						UseData(registerIndex);
					}
					else
					{
						DefineData(registerIndex);
					}
				}
				if ((mask & (1 << (registerIndex + 8))) != 0)
				{
					if (isStore)
					{
						UseAddress(registerIndex);
					}
					else
					{
						DefineAddress(registerIndex);
					}
				}
			}
		}

		private void ClassifyQuickOrSet(ushort opcode)
		{
			if ((opcode & 0xF0C0) == 0x50C0)
			{
				_readsConditions = ConditionCodes((opcode >> 8) & 0x0F);
				if (((opcode >> 3) & 7) == 0)
				{
					UseData(opcode & 7);
				}
				AddEffectiveAddress(opcode, EffectiveAddressAccess.Write);
				return;
			}

			var destination = opcode & 7;
			var destinationMode = (opcode >> 3) & 7;
			if (destinationMode == 1)
			{
				UseAddress(destination);
				DefineAddress(destination);
				if (destination == 7)
				{
					var amount = QuickCount(opcode);
					AdjustStack((opcode & 0x0100) == 0 ? amount : -amount);
				}
				return;
			}

			AddEffectiveAddress((ushort)(destination | (destinationMode << 3)), EffectiveAddressAccess.ReadWrite);
			WriteArithmeticConditions();
		}

		private void ClassifyArithmetic(ushort opcode)
		{
			var operationMode = (opcode >> 6) & 7;
			if (operationMode is >= 4 and <= 6 &&
				(opcode & 0xF130) is 0xD100 or 0x9100)
			{
				_readsConditions |= M68kConditionCodeSet.Extend |
					M68kConditionCodeSet.Zero;
			}

			if ((opcode & 0xF000) == 0xB000)
			{
				if (operationMode is 3 or 7)
				{
					AddEffectiveAddress(opcode, EffectiveAddressAccess.Read);
					UseAddress((opcode >> 9) & 7);
				}
				else if (operationMode <= 2)
				{
					AddEffectiveAddress(opcode, EffectiveAddressAccess.Read);
					UseData((opcode >> 9) & 7);
				}
				else
				{
					UseData((opcode >> 9) & 7);
					AddEffectiveAddress((ushort)(((opcode >> 9) & 7) | (operationMode << 3)), EffectiveAddressAccess.ReadWrite);
				}
				WriteArithmeticConditions();
				return;
			}

			if (operationMode is 3 or 7)
			{
				AddEffectiveAddress(opcode, EffectiveAddressAccess.Read);
				var destination = (opcode >> 9) & 7;
				UseAddress(destination);
				DefineAddress(destination);
				if (destination == 7)
				{
					_stackDelta = null;
				}
				return;
			}

			if (operationMode <= 2)
			{
				AddEffectiveAddress(opcode, EffectiveAddressAccess.Read);
				UseData((opcode >> 9) & 7);
				DefineData((opcode >> 9) & 7);
			}
			else
			{
				UseData((opcode >> 9) & 7);
				AddEffectiveAddress(opcode, EffectiveAddressAccess.ReadWrite);
			}
			WriteArithmeticConditions();
		}

		private void ClassifyBitOperation(ushort opcode)
		{
			if ((opcode & 0xF100) == 0x0100)
			{
				UseData((opcode >> 9) & 7);
			}
			var operation = (opcode >> 6) & 3;
			AddEffectiveAddress(
				opcode,
				operation == 0
					? EffectiveAddressAccess.Read
					: EffectiveAddressAccess.ReadWrite);
			_writesConditions = M68kConditionCodeSet.Zero;
			_canRemoveWhenOutputsDead = operation == 0;
		}

		private void ClassifyShift(ushort opcode)
		{
			if ((opcode & 0x20) != 0)
			{
				UseData((opcode >> 9) & 7);
			}

			if ((opcode & 0xC0) == 0xC0)
			{
				AddEffectiveAddress(opcode, EffectiveAddressAccess.ReadWrite);
			}
			else
			{
				UseData(opcode & 7);
				DefineData(opcode & 7);
			}
			WriteArithmeticConditions();
		}

		private void AddEffectiveAddress(ushort opcode, EffectiveAddressAccess access)
		{
			var mode = (opcode >> 3) & 7;
			var register = opcode & 7;
			switch (mode)
			{
				case 0:
					if (access != EffectiveAddressAccess.Write)
					{
						UseData(register);
					}
					if (access != EffectiveAddressAccess.Read)
					{
						DefineData(register);
					}
					break;
				case 1:
					UseAddress(register);
					if (access != EffectiveAddressAccess.Read)
					{
						DefineAddress(register);
					}
					break;
				case 2:
				case 3:
				case 4:
					UseAddress(register);
					if (mode is 3 or 4)
					{
						DefineAddress(register);
					}
					if (access != EffectiveAddressAccess.AddressOnly)
					{
						AddMemory(register == 7 ? M68kMemorySet.Stack : M68kMemorySet.Indirect, access);
						if (register == 7 && mode is 3 or 4)
						{
							var size = EffectiveAddressSize(opcode);
							AdjustStack(mode == 3 ? size : -size);
						}
					}
					break;
				case 5:
					UseAddress(register);
					if (access != EffectiveAddressAccess.AddressOnly)
					{
						AddMemory(register == 7 ? M68kMemorySet.Stack : M68kMemorySet.Indirect, access);
					}
					break;
				case 6:
					UseAddress(register);
					var indexRegister = (_extensionWord >> 12) & 7;
					if ((_extensionWord & 0x8000) != 0)
					{
						UseAddress(indexRegister);
					}
					else
					{
						UseData(indexRegister);
					}
					if (access != EffectiveAddressAccess.AddressOnly)
					{
						AddMemory(register == 7 ? M68kMemorySet.Stack : M68kMemorySet.Indirect, access);
					}
					break;
				case 7:
					if (register is 0 or 1)
					{
						AddMemory(M68kMemorySet.Known, access);
					}
					else if (register is 2 or 3)
					{
						AddMemory(M68kMemorySet.Indirect, access);
					}
					break;
			}
		}

		private void AddMemory(M68kMemorySet memory, EffectiveAddressAccess access)
		{
			if (access is EffectiveAddressAccess.Read or EffectiveAddressAccess.ReadWrite)
			{
				_readsMemory |= memory;
			}
			if (access is EffectiveAddressAccess.Write or EffectiveAddressAccess.ReadWrite)
			{
				_writesMemory |= memory;
			}
		}

		private void AdjustStack(int delta, M68kMemorySet readsMemory = M68kMemorySet.None, M68kMemorySet writesMemory = M68kMemorySet.None)
		{
			_stackDelta = _stackDelta.HasValue ? _stackDelta + delta : null;
			_readsMemory |= readsMemory;
			_writesMemory |= writesMemory;
		}

		private void UseData(int register) => _usesData |= (ushort)(1 << register);
		private void DefineData(int register) => _definesData |= (ushort)(1 << register);
		private void UseAddress(int register) => _usesAddress |= (ushort)(1 << register);
		private void DefineAddress(int register) => _definesAddress |= (ushort)(1 << register);

		private void WriteMoveConditions() =>
			_writesConditions |= M68kConditionCodeSet.Negative |
				M68kConditionCodeSet.Zero |
				M68kConditionCodeSet.Overflow |
				M68kConditionCodeSet.Carry;

		private void WriteArithmeticConditions() =>
			_writesConditions |= M68kConditionCodeSet.Negative |
				M68kConditionCodeSet.Zero |
				M68kConditionCodeSet.Overflow |
				M68kConditionCodeSet.Carry |
				M68kConditionCodeSet.Extend;

		private static int BitCount(ushort value)
		{
			var count = 0;
			while (value != 0)
			{
				value &= (ushort)(value - 1);
				count++;
			}
			return count;
		}

		private static int QuickCount(ushort opcode)
		{
			var count = (opcode >> 9) & 7;
			return count == 0 ? 8 : count;
		}

		private static int EffectiveAddressSize(ushort opcode) => (opcode & 0xF000) switch
		{
			0x2000 => 4,
			0x3000 => 2,
			0x4000 => ((opcode >> 6) & 3) == 2 ? 4 : 2,
			_ => 4
		};
	}

	private enum EffectiveAddressAccess : byte
	{
		AddressOnly,
		Read,
		Write,
		ReadWrite
	}
}
