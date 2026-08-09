/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

[Flags]
internal enum M68kMachineMemoryEffect
{
	None = 0,
	Read = 1,
	Write = 2,
	Volatile = 4
}

internal enum M68kMachineOperation
{
	Phi,
	Argument,
	Copy,
	Constant,
	Load,
	Store,
	AggregateFieldLoad,
	LocalLoad,
	ArgumentLoad,
	LocalStore,
	ArgumentStore,
	LocalAddress,
	ArgumentAddress,
	DynamicStackAllocate,
	ObjectAllocate,
	ArrayAllocate,
	ArrayLoad,
	ArrayStore,
	ArrayAddress,
	AggregateArrayLoad,
	AggregateArrayStore,
	AggregateIndirectLoad,
	AggregateIndirectStore,
	AggregateIndirectCopy,
	AggregateIndirectInitialize,
	Box,
	Unbox,
	SpillLoad,
	SpillStore,
	SpillClear,
	RootStore,
	RootClear,
	ByrefOwnerKeepAlive,
	OutgoingArgumentPush,
	IncomingArgumentPush,
	OutgoingArgumentCleanup,
	Address,
	Add,
	Subtract,
	Multiply,
	Divide,
	Remainder,
	And,
	Or,
	Xor,
	Negate,
	Not,
	Shift,
	Compare,
	Convert,
	TypeTest,
	TypeInitialize,
	FunctionAddress,
	DelegateCreate,
	Call,
	Branch,
	ConditionalBranch,
	Switch,
	Return,
	Throw,
	Other
}

internal enum M68kMachineValueWidth
{
	Byte = 1,
	Word = 2,
	Long = 4,
	LongPair = 8
}

internal readonly record struct M68kRegisterSet(ushort Bits)
{
	public static M68kRegisterSet None => new(0);

	public static M68kRegisterSet Data =>
		new((ushort)((1 << 8) - 1));

	public static M68kRegisterSet Address =>
		new((ushort)(((1 << 7) - 1) << 8));

	public static M68kRegisterSet DataOrAddress =>
		new((ushort)(Data.Bits | Address.Bits));

	public static M68kRegisterSet DataPairStarts =>
		new((ushort)((1 << 7) - 1));

	public int Count => System.Numerics.BitOperations.PopCount(Bits);

	public bool IsEmpty => Bits == 0;

	public bool Contains(M68kRegister register) =>
		(Bits & Bit(register)) != 0;

	public M68kRegisterSet Add(M68kRegister register) =>
		new((ushort)(Bits | Bit(register)));

	public M68kRegisterSet Remove(M68kRegister register) =>
		new((ushort)(Bits & ~Bit(register)));

	public M68kRegisterSet Except(M68kRegisterSet other) =>
		new((ushort)(Bits & ~other.Bits));

	public M68kRegisterSet Intersect(M68kRegisterSet other) =>
		new((ushort)(Bits & other.Bits));

	public bool Overlaps(M68kRegisterSet other) =>
		(Bits & other.Bits) != 0;

	public IEnumerable<M68kRegister> Enumerate()
	{
		for (var register = M68kRegister.D0;
			register <= M68kRegister.A6;
			register++)
		{
			if (Contains(register))
			{
				yield return register;
			}
		}
	}

	public static M68kRegisterSet From(params M68kRegister[] registers)
	{
		var result = None;
		foreach (var register in registers)
		{
			result = result.Add(register);
		}
		return result;
	}

	private static ushort Bit(M68kRegister register) =>
		checked((ushort)(1 << (int)register));
}

internal sealed record M68kMachineValue(
	int Id,
	CilStackValueKind Kind,
	M68kMachineValueWidth Width,
	M68kRegisterSet AllowedRegisters,
	M68kRegister? PrecoloredRegister = null,
	bool IsGcReference = false,
	bool IsRematerializable = false,
	long SpillWeight = 1)
{
	public bool IsRegisterPair => Width == M68kMachineValueWidth.LongPair;
}

internal sealed record M68kFrameHome(
	int Index,
	int Size,
	bool IsGcReference,
	bool Initialize = true,
	IReadOnlyList<int>? GcReferenceOffsets = null)
{
	public bool HasGcReferences =>
		IsGcReference || GcReferenceOffsets is { Count: > 0 };
}

internal readonly record struct M68kManagedByrefType(
	CilType ReferentType,
	bool IsReadOnly);

internal sealed record M68kMachinePhi(
	int Definition,
	IReadOnlyDictionary<int, int> Inputs);

internal enum M68kMachineConditionSourceKind
{
	Test,
	Compare,
	Predicate
}

internal sealed record M68kMachineBranchCondition(
	M68kMachineConditionSourceKind SourceKind,
	M68kCondition Condition,
	CilInstruction? ProducerInstruction = null);

internal sealed record M68kMachineInstruction(
	int Id,
	M68kMachineOperation Operation,
	int IlOffset,
	ImmutableArray<int> Uses,
	ImmutableArray<int> Definitions,
	M68kRegisterSet Clobbers,
	M68kMachineMemoryEffect MemoryEffect = M68kMachineMemoryEffect.None,
	bool IsSafepoint = false,
	bool MayThrow = false,
	bool ProducesConditionCodes = false,
	bool ConsumesConditionCodes = false,
	CilInstruction? SourceInstruction = null,
	int? SpillSlotIndex = null,
	int? ArgumentIndex = null,
	M68kRegister? StackVarargsRegister = null,
	int? Immediate = null,
	bool AllowCopyCoalescing = true,
	bool TransportsManagedByrefOwner = false,
	M68kMachineBranchCondition? BranchCondition = null)
{
	public static M68kMachineInstruction Create(
		int id,
		M68kMachineOperation operation,
		int ilOffset,
		IEnumerable<int>? uses = null,
		IEnumerable<int>? definitions = null,
		M68kRegisterSet clobbers = default,
		M68kMachineMemoryEffect memoryEffect = M68kMachineMemoryEffect.None,
		bool isSafepoint = false,
		bool mayThrow = false,
		bool producesConditionCodes = false,
		bool consumesConditionCodes = false,
		CilInstruction? sourceInstruction = null,
		int? spillSlotIndex = null,
		int? argumentIndex = null,
		M68kRegister? stackVarargsRegister = null,
		int? immediate = null,
		bool allowCopyCoalescing = true,
		bool transportsManagedByrefOwner = false,
		M68kMachineBranchCondition? branchCondition = null) =>
		new(
			id,
			operation,
			ilOffset,
			uses?.ToImmutableArray() ?? ImmutableArray<int>.Empty,
			definitions?.ToImmutableArray() ?? ImmutableArray<int>.Empty,
			clobbers,
			memoryEffect,
			isSafepoint,
			mayThrow,
			producesConditionCodes,
			consumesConditionCodes,
			sourceInstruction,
			spillSlotIndex,
			argumentIndex,
			stackVarargsRegister,
			immediate,
			allowCopyCoalescing,
			transportsManagedByrefOwner,
			branchCondition);
}

internal sealed class M68kMachineBlock
{
	public M68kMachineBlock(int id, int startIlOffset)
	{
		Id = id;
		StartIlOffset = startIlOffset;
	}

	public int Id { get; }

	public int StartIlOffset { get; }

	public List<M68kMachinePhi> Phis { get; } = new();

	public List<M68kMachineInstruction> Instructions { get; } = new();

	public List<int> Predecessors { get; } = new();

	public List<int> Successors { get; } = new();

	public int LoopDepth { get; set; }

	public bool IsExceptionEntry { get; set; }
}

internal sealed class M68kMachineFunction
{
	private int _nextValueId;
	private int _nextInstructionId;

	public M68kMachineFunction(string displayName, int entryBlockId)
	{
		DisplayName = displayName;
		EntryBlockId = entryBlockId;
	}

	public string DisplayName { get; }

	public int EntryBlockId { get; }

	public Dictionary<int, M68kMachineValue> Values { get; } = new();

	public Dictionary<int, M68kManagedByrefType> ManagedByrefTypes { get; } = new();

	public List<M68kMachineBlock> Blocks { get; } = new();

	public M68kRegisterSet ReservedRegisters { get; set; }

	public bool PreserveCalleeSavedRegisters { get; set; } = true;

	public bool HasDynamicStackAllocation { get; set; }

	public bool HasExceptionHandlers { get; set; }

	public HashSet<int> GcSpillSlots { get; } = new();



	public Dictionary<int, M68kFrameHome> LocalHomes { get; } = new();

	public Dictionary<int, M68kFrameHome> ArgumentHomes { get; } = new();

	public M68kMachineValue CreateValue(
		CilStackValueKind kind,
		M68kMachineValueWidth width,
		M68kRegisterSet allowedRegisters,
		M68kRegister? precoloredRegister = null,
		bool isGcReference = false,
		bool isRematerializable = false,
		long spillWeight = 1)
	{
		var value = new M68kMachineValue(
			_nextValueId++,
			kind,
			width,
			allowedRegisters,
			precoloredRegister,
			isGcReference,
			isRematerializable,
			spillWeight);
		Values.Add(value.Id, value);
		return value;
	}

	public M68kMachineInstruction CreateInstruction(
		M68kMachineOperation operation,
		int ilOffset,
		IEnumerable<int>? uses = null,
		IEnumerable<int>? definitions = null,
		M68kRegisterSet clobbers = default,
		M68kMachineMemoryEffect memoryEffect = M68kMachineMemoryEffect.None,
		bool isSafepoint = false,
		bool mayThrow = false,
		bool producesConditionCodes = false,
		bool consumesConditionCodes = false,
		CilInstruction? sourceInstruction = null,
		int? spillSlotIndex = null,
		int? argumentIndex = null,
		M68kRegister? stackVarargsRegister = null,
		int? immediate = null,
		bool allowCopyCoalescing = true,
		bool transportsManagedByrefOwner = false,
		M68kMachineBranchCondition? branchCondition = null) =>
		M68kMachineInstruction.Create(
			_nextInstructionId++,
			operation,
			ilOffset,
			uses,
			definitions,
			clobbers,
			memoryEffect,
			isSafepoint,
			mayThrow,
			producesConditionCodes,
			consumesConditionCodes,
			sourceInstruction,
			spillSlotIndex,
			argumentIndex,
			stackVarargsRegister,
			immediate,
			allowCopyCoalescing,
			transportsManagedByrefOwner,
			branchCondition);
}

internal static class M68kMachineIrVerifier
{
	public static void Verify(M68kMachineFunction function)
	{
		if (function.Blocks.Count == 0)
		{
			throw Invalid(function, "Function has no basic blocks.");
		}

		var blocks = function.Blocks.ToDictionary(static block => block.Id);
		if (!blocks.ContainsKey(function.EntryBlockId))
		{
			throw Invalid(function, "Entry block does not exist.");
		}

		var definitions = new Dictionary<int, string>();
		var instructionIds = new HashSet<int>();
		foreach (var block in function.Blocks)
		{
			VerifyEdges(function, block, blocks);
			foreach (var phi in block.Phis)
			{
				VerifyValueExists(function, phi.Definition);
				AddDefinition(
					function,
					definitions,
					phi.Definition,
					$"phi in block {block.Id}");
				if (phi.Inputs.Count != block.Predecessors.Distinct().Count() ||
					phi.Inputs.Keys.Any(predecessor =>
						!block.Predecessors.Contains(predecessor)))
				{
					throw Invalid(
						function,
						$"Phi v{phi.Definition} does not have exactly one input per predecessor.");
				}
				foreach (var input in phi.Inputs.Values)
				{
					VerifyValueExists(function, input);
				}
			}

			var conditionCodesAvailable = false;
			foreach (var instruction in block.Instructions)
			{
				if (instruction.IlOffset < 0)
				{
					throw Invalid(
						function,
						$"Instruction {instruction.Id} has no original IL offset.");
				}
				if (!instructionIds.Add(instruction.Id))
				{
					throw Invalid(
						function,
						$"Instruction id {instruction.Id} is duplicated.");
				}
				if (!instruction.AllowCopyCoalescing &&
					instruction.Operation != M68kMachineOperation.Copy)
				{
					throw Invalid(
						function,
						$"Instruction {instruction.Id} disables coalescing but is not a copy.");
				}
				VerifyBranchCondition(function, instruction);
				VerifySpillInstruction(function, instruction);
				foreach (var use in instruction.Uses)
				{
					VerifyValueExists(function, use);
				}
				foreach (var definition in instruction.Definitions)
				{
					VerifyValueExists(function, definition);
					AddDefinition(
						function,
						definitions,
						definition,
						$"instruction {instruction.Id}");
				}
				if (instruction.ConsumesConditionCodes && !conditionCodesAvailable)
				{
					throw Invalid(
						function,
						$"Instruction {instruction.Id} consumes unavailable condition codes.");
				}
				if (instruction.ProducesConditionCodes)
				{
					conditionCodesAvailable = true;
				}
				else if (instruction.Operation is not
					M68kMachineOperation.Branch and not
					M68kMachineOperation.ConditionalBranch)
				{
					conditionCodesAvailable = false;
				}
			}
		}

		foreach (var value in function.Values.Values)
		{
			VerifyValueConstraints(function, value);
			if (!definitions.ContainsKey(value.Id))
			{
				throw Invalid(function, $"Value v{value.Id} is never defined.");
			}
		}
		VerifySsaDominance(function);
	}

	private static void VerifyBranchCondition(
		M68kMachineFunction function,
		M68kMachineInstruction instruction)
	{
		if (instruction.BranchCondition is null)
		{
			return;
		}
		if (instruction.Operation != M68kMachineOperation.ConditionalBranch ||
			instruction.Definitions.Length != 0)
		{
			throw Invalid(
				function,
				$"Instruction {instruction.Id} has a condition descriptor but is not a definition-free conditional branch.");
		}

		var expectedUses = instruction.BranchCondition.SourceKind ==
			M68kMachineConditionSourceKind.Compare
				? 2
				: 1;
		if (instruction.Uses.Length != expectedUses)
		{
			throw Invalid(
				function,
				$"Conditional branch {instruction.Id} has {instruction.Uses.Length} operands; expected {expectedUses}.");
		}
		if (instruction.BranchCondition.SourceKind ==
			M68kMachineConditionSourceKind.Predicate &&
			(instruction.BranchCondition.ProducerInstruction is not
					{ OpCode: var op } ||
			 op != System.Reflection.Emit.OpCodes.Call &&
			 op != System.Reflection.Emit.OpCodes.Callvirt))
		{
			throw Invalid(
				function,
				$"Predicate branch {instruction.Id} has no predicate call metadata.");
		}
	}

	private static void VerifySpillInstruction(
		M68kMachineFunction function,
		M68kMachineInstruction instruction)
	{
		if (instruction.TransportsManagedByrefOwner &&
			(instruction.Operation != M68kMachineOperation.Call ||
			 instruction.Uses.Length is < 1 or > 2 ||
			 function.Values[instruction.Uses[0]].Kind !=
				CilStackValueKind.ManagedPointer ||
			 instruction.Uses.Length == 2 &&
				!function.Values[instruction.Uses[1]].IsGcReference))
		{
			throw Invalid(
				function,
				$"Managed-byref owner transport {instruction.Id} has an invalid shape.");
		}

		switch (instruction.Operation)
		{
			case M68kMachineOperation.Argument:
				if (instruction.ArgumentIndex is null ||
					instruction.Uses.Length != 0 ||
					instruction.Definitions.Length != 1)
				{
					throw Invalid(
						function,
						$"Argument definition {instruction.Id} has an invalid shape.");
				}
				break;

			case M68kMachineOperation.LocalLoad:
			case M68kMachineOperation.ArgumentLoad:
				if (instruction.ArgumentIndex is null ||
					instruction.Uses.Length != 0 ||
					instruction.Definitions.Length != 1 ||
					instruction.MemoryEffect != M68kMachineMemoryEffect.Read)
				{
					throw Invalid(
						function,
						$"Local load {instruction.Id} has an invalid shape.");
				}
				break;

			case M68kMachineOperation.AggregateFieldLoad:
				if (instruction.ArgumentIndex is null ||
					instruction.Uses.Length > 1 ||
					instruction.Definitions.Length > 1 ||
					instruction.MemoryEffect !=
						(M68kMachineMemoryEffect.Read |
						 M68kMachineMemoryEffect.Write))
				{
					throw Invalid(
						function,
						$"Aggregate field load {instruction.Id} has an invalid shape.");
				}
				break;

			case M68kMachineOperation.AggregateArrayLoad:
				if (instruction.ArgumentIndex is null ||
					instruction.Uses.Length != 2 ||
					instruction.Definitions.Length > 1 ||
					instruction.MemoryEffect !=
						(M68kMachineMemoryEffect.Read |
						 M68kMachineMemoryEffect.Write))
				{
					throw Invalid(
						function,
						$"Aggregate array load {instruction.Id} has an invalid shape.");
				}
				break;

			case M68kMachineOperation.AggregateArrayStore:
				if (instruction.Uses.Length != 3 ||
					instruction.Definitions.Length != 0 ||
					instruction.MemoryEffect != M68kMachineMemoryEffect.Write)
				{
					throw Invalid(
						function,
						$"Aggregate array store {instruction.Id} has an invalid shape.");
				}
				break;

			case M68kMachineOperation.LocalStore:
			case M68kMachineOperation.ArgumentStore:
				if (instruction.ArgumentIndex is null ||
					instruction.Uses.Length != 1 ||
					instruction.Definitions.Length != 0 ||
					instruction.MemoryEffect != M68kMachineMemoryEffect.Write)
				{
					throw Invalid(
						function,
						$"Frame store {instruction.Id} has an invalid shape.");
				}
				break;

			case M68kMachineOperation.LocalAddress:
			case M68kMachineOperation.ArgumentAddress:
				if (instruction.ArgumentIndex is null ||
					instruction.Uses.Length != 0 ||
					instruction.Definitions.Length != 1)
				{
					throw Invalid(
						function,
						$"Frame address {instruction.Id} has an invalid shape.");
				}
				break;

			case M68kMachineOperation.DynamicStackAllocate:
				if (instruction.Uses.Length != 1 ||
					instruction.Definitions.Length != 1)
				{
					throw Invalid(
						function,
						$"Dynamic stack allocation {instruction.Id} has an invalid shape.");
				}
				break;

			case M68kMachineOperation.SpillLoad:
				if (instruction.SpillSlotIndex is null ||
					instruction.Uses.Length != 0 ||
					instruction.Definitions.Length != 1 ||
					instruction.MemoryEffect != M68kMachineMemoryEffect.Read)
				{
					throw Invalid(
						function,
						$"Spill load {instruction.Id} has an invalid shape.");
				}
				break;

			case M68kMachineOperation.SpillStore:
				if (instruction.SpillSlotIndex is null ||
					instruction.Uses.Length != 1 ||
					instruction.Definitions.Length != 0 ||
					instruction.MemoryEffect != M68kMachineMemoryEffect.Write)
				{
					throw Invalid(
						function,
						$"Spill store {instruction.Id} has an invalid shape.");
				}
				break;

			case M68kMachineOperation.SpillClear:
			case M68kMachineOperation.RootClear:
				if (instruction.SpillSlotIndex is null ||
					instruction.Uses.Length != 0 ||
					instruction.Definitions.Length != 0 ||
					instruction.MemoryEffect != M68kMachineMemoryEffect.Write)
				{
					throw Invalid(
						function,
						$"Frame-slot clear {instruction.Id} has an invalid shape.");
				}
				break;

			case M68kMachineOperation.RootStore:
				if (instruction.SpillSlotIndex is null ||
					instruction.Uses.Length != 1 ||
					instruction.Definitions.Length != 0 ||
					instruction.MemoryEffect != M68kMachineMemoryEffect.Write)
				{
					throw Invalid(
						function,
						$"Root store {instruction.Id} has an invalid shape.");
				}
				break;

			case M68kMachineOperation.ByrefOwnerKeepAlive:
				if (instruction.Uses.Length == 0 ||
					instruction.Uses.Any(value =>
						!function.Values[value].IsGcReference) ||
					instruction.Definitions.Length != 0 ||
					instruction.ArgumentIndex is not null ||
					instruction.SpillSlotIndex is not null ||
					instruction.MemoryEffect != M68kMachineMemoryEffect.None ||
					instruction.IsSafepoint)
				{
					throw Invalid(
						function,
						$"Byref owner keepalive {instruction.Id} has an invalid shape.");
				}
				break;

			case M68kMachineOperation.OutgoingArgumentPush:
				var outgoingBytes = instruction.ArgumentIndex ?? 0;
				var pushesAggregate = instruction.Uses.Length == 1 &&
					function.Values[instruction.Uses[0]].Kind is
						CilStackValueKind.ManagedPointer or
						CilStackValueKind.AggregateAddress;
				if (outgoingBytes <= 0 ||
					(outgoingBytes & 3) != 0 ||
					(!pushesAggregate && outgoingBytes is not 4 and not 8) ||
					instruction.Uses.Length != 1 ||
					instruction.Definitions.Length != 0 ||
					instruction.MemoryEffect != M68kMachineMemoryEffect.Write)
				{
					throw Invalid(
						function,
						$"Outgoing argument push {instruction.Id} has an invalid shape.");
				}
				break;

			case M68kMachineOperation.IncomingArgumentPush:
				if (instruction.ArgumentIndex is not 4 ||
					instruction.SpillSlotIndex is null or < 0 ||
					instruction.Uses.Length != 0 ||
					instruction.Definitions.Length != 1 ||
					function.Values[instruction.Definitions[0]].PrecoloredRegister !=
						M68kRegister.D0 ||
					instruction.MemoryEffect != M68kMachineMemoryEffect.Write)
				{
					throw Invalid(
						function,
						$"Incoming argument push {instruction.Id} has an invalid shape.");
				}
				break;

			case M68kMachineOperation.OutgoingArgumentCleanup:
				if (instruction.ArgumentIndex is null or <= 0 ||
					instruction.Uses.Length != 0 ||
					instruction.Definitions.Length != 0)
				{
					throw Invalid(
						function,
						$"Outgoing argument cleanup {instruction.Id} has an invalid shape.");
				}
				break;

			case M68kMachineOperation.Unbox when instruction.ArgumentIndex is not null:
				if (instruction.SourceInstruction?.OpCode !=
						System.Reflection.Emit.OpCodes.Unbox_Any ||
					instruction.Uses.Length != 1 ||
					instruction.Definitions.Length != 0 ||
					instruction.MemoryEffect !=
						(M68kMachineMemoryEffect.Read |
						 M68kMachineMemoryEffect.Write))
				{
					throw Invalid(
						function,
						$"Multiword unbox-to-local {instruction.Id} has an invalid shape.");
				}
				break;

			case M68kMachineOperation.AggregateIndirectLoad:
				if (instruction.ArgumentIndex is null ||
					instruction.Uses.Length != 1 ||
					instruction.Definitions.Length > 1 ||
					instruction.MemoryEffect !=
						(M68kMachineMemoryEffect.Read |
						 M68kMachineMemoryEffect.Write))
				{
					throw Invalid(
						function,
						$"Aggregate indirect load {instruction.Id} has an invalid shape.");
				}
				break;

			case M68kMachineOperation.AggregateIndirectCopy:
				if (instruction.ArgumentIndex is null ||
					instruction.Uses.Length != 2 ||
					instruction.Definitions.Length != 0 ||
					instruction.MemoryEffect !=
						(M68kMachineMemoryEffect.Read |
						 M68kMachineMemoryEffect.Write))
				{
					throw Invalid(
						function,
						$"Aggregate indirect copy {instruction.Id} has an invalid shape.");
				}
				break;

			case M68kMachineOperation.AggregateIndirectStore:
				if (instruction.ArgumentIndex is not null ||
					instruction.Uses.Length != 2 ||
					instruction.Definitions.Length != 0 ||
					instruction.MemoryEffect !=
						(M68kMachineMemoryEffect.Read |
						 M68kMachineMemoryEffect.Write))
				{
					throw Invalid(
						function,
						$"Aggregate indirect store {instruction.Id} has an invalid shape.");
				}
				break;

			case M68kMachineOperation.AggregateIndirectInitialize:
				if (instruction.ArgumentIndex is not null ||
					instruction.Uses.Length != 1 ||
					instruction.Definitions.Length != 0 ||
					instruction.MemoryEffect != M68kMachineMemoryEffect.Write)
				{
					throw Invalid(
						function,
						$"Aggregate indirect initialize {instruction.Id} has an invalid shape.");
				}
				break;

			default:
				if (instruction.SpillSlotIndex is not null)
				{
					throw Invalid(
						function,
						$"Non-spill instruction {instruction.Id} names a spill slot.");
				}
				if (instruction.ArgumentIndex is not null)
				{
					throw Invalid(
						function,
						$"Non-argument instruction {instruction.Id} names an argument.");
				}
				break;
		}
	}

	private static void VerifySsaDominance(M68kMachineFunction function)
	{
		var definitions = new Dictionary<int, (int Block, int Instruction)>();
		foreach (var block in function.Blocks)
		{
			foreach (var phi in block.Phis)
			{
				definitions.Add(phi.Definition, (block.Id, -1));
			}
			for (var index = 0; index < block.Instructions.Count; index++)
			{
				foreach (var definition in block.Instructions[index].Definitions)
				{
					definitions.Add(definition, (block.Id, index));
				}
			}
		}

		var dominators = M68kControlFlowAnalysis.ComputeDominators(function);
		foreach (var block in function.Blocks)
		{
			foreach (var phi in block.Phis)
			{
				foreach (var (predecessor, input) in phi.Inputs)
				{
					var definition = definitions[input];
					if (!dominators[predecessor].Contains(definition.Block))
					{
						throw Invalid(
							function,
							$"Phi v{phi.Definition} uses v{input} on edge " +
							$"{predecessor}->{block.Id}, but its definition does not dominate the edge.");
					}
				}
			}
			for (var index = 0; index < block.Instructions.Count; index++)
			{
				foreach (var use in block.Instructions[index].Uses)
				{
					var definition = definitions[use];
					if (!dominators[block.Id].Contains(definition.Block) ||
						(definition.Block == block.Id &&
						 definition.Instruction >= index))
					{
						throw Invalid(
							function,
							$"Instruction {block.Instructions[index].Id} uses v{use} " +
							"before a dominating definition.");
					}
				}
			}
		}
	}

	private static void VerifyEdges(
		M68kMachineFunction function,
		M68kMachineBlock block,
		IReadOnlyDictionary<int, M68kMachineBlock> blocks)
	{
		if (block.Predecessors.Count != block.Predecessors.Distinct().Count() ||
			block.Successors.Count != block.Successors.Distinct().Count())
		{
			throw Invalid(function, $"Block {block.Id} has duplicate CFG edges.");
		}

		foreach (var predecessor in block.Predecessors)
		{
			if (!blocks.TryGetValue(predecessor, out var predecessorBlock) ||
				!predecessorBlock.Successors.Contains(block.Id))
			{
				throw Invalid(
					function,
					$"Block {block.Id} has an inconsistent predecessor {predecessor}.");
			}
		}
		foreach (var successor in block.Successors)
		{
			if (!blocks.TryGetValue(successor, out var successorBlock) ||
				!successorBlock.Predecessors.Contains(block.Id))
			{
				throw Invalid(
					function,
					$"Block {block.Id} has an inconsistent successor {successor}.");
			}
		}
	}

	private static void VerifyValueConstraints(
		M68kMachineFunction function,
		M68kMachineValue value)
	{
		if (value.AllowedRegisters.IsEmpty)
		{
			throw Invalid(function, $"Value v{value.Id} has no legal registers.");
		}
		if (value.Width is M68kMachineValueWidth.Byte or M68kMachineValueWidth.Word &&
			value.AllowedRegisters.Overlaps(M68kRegisterSet.Address))
		{
			throw Invalid(
				function,
				$"{value.Width} value v{value.Id} allows an address register.");
		}
		if (value.IsRegisterPair &&
			value.AllowedRegisters.Except(M68kRegisterSet.DataPairStarts).Bits != 0)
		{
			throw Invalid(
				function,
				$"Register-pair value v{value.Id} has an illegal pair start.");
		}
		if (value.PrecoloredRegister is { } precolored &&
			!value.AllowedRegisters.Contains(precolored))
		{
			throw Invalid(
				function,
				$"Precolored register {precolored} is illegal for v{value.Id}.");
		}
		if (value.IsRegisterPair &&
			value.PrecoloredRegister == M68kRegister.D7)
		{
			throw Invalid(
				function,
				$"Register-pair value v{value.Id} cannot start at D7.");
		}
		if (value.IsGcReference &&
			value.Kind is not
				CilStackValueKind.Reference and not
				CilStackValueKind.ManagedPointer)
		{
			throw Invalid(
				function,
				$"GC value v{value.Id} does not have a reference kind.");
		}
	}

	private static void VerifyValueExists(
		M68kMachineFunction function,
		int valueId)
	{
		if (!function.Values.ContainsKey(valueId))
		{
			throw Invalid(function, $"Value v{valueId} does not exist.");
		}
	}

	private static void AddDefinition(
		M68kMachineFunction function,
		IDictionary<int, string> definitions,
		int valueId,
		string source)
	{
		if (definitions.TryGetValue(valueId, out var prior))
		{
			throw Invalid(
				function,
				$"Value v{valueId} is defined by both {prior} and {source}.");
		}
		definitions.Add(valueId, source);
	}

	private static InvalidOperationException Invalid(
		M68kMachineFunction function,
		string message) =>
		new($"Invalid machine IR for '{function.DisplayName}': {message}");
}
