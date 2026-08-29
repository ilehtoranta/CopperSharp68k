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
	PlatformBaseLoad,
	PlatformBaseStore,
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
	GcKeepAlive,
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

internal enum M68kMachineConstantKind
{
	Int32,
	Int64,
	Boolean,
	Null,
	Float32Bits,
	Float64Bits
}

internal readonly record struct M68kMachineConstant(
	M68kMachineConstantKind Kind,
	ulong Bits)
{
	public static M68kMachineConstant Int32(int value) =>
		new(M68kMachineConstantKind.Int32, unchecked((uint)value));

	public static M68kMachineConstant Int64(long value) =>
		new(M68kMachineConstantKind.Int64, unchecked((ulong)value));

	public static M68kMachineConstant Boolean(bool value) =>
		new(M68kMachineConstantKind.Boolean, value ? 1u : 0u);

	public static M68kMachineConstant Null =>
		new(M68kMachineConstantKind.Null, 0);

	public bool TryGetIntegral(out long value)
	{
		switch (Kind)
		{
			case M68kMachineConstantKind.Int32:
				value = unchecked((int)(uint)Bits);
				return true;
			case M68kMachineConstantKind.Int64:
				value = unchecked((long)Bits);
				return true;
			case M68kMachineConstantKind.Boolean:
			case M68kMachineConstantKind.Null:
				value = unchecked((long)Bits);
				return true;
			default:
				value = 0;
				return false;
		}
	}

	public static bool TryFromCil(
		CilInstruction instruction,
		bool boolean,
		out M68kMachineConstant constant)
	{
		var op = instruction.OpCode;
		if (op == System.Reflection.Emit.OpCodes.Ldnull)
		{
			constant = Null;
			return true;
		}
		if (op == System.Reflection.Emit.OpCodes.Ldc_I8)
		{
			constant = Int64((long)instruction.Operand!);
			return true;
		}
		if (op == System.Reflection.Emit.OpCodes.Ldc_R4)
		{
			constant = new M68kMachineConstant(
				M68kMachineConstantKind.Float32Bits,
				BitConverter.SingleToUInt32Bits((float)instruction.Operand!));
			return true;
		}
		if (op == System.Reflection.Emit.OpCodes.Ldc_R8)
		{
			constant = new M68kMachineConstant(
				M68kMachineConstantKind.Float64Bits,
				unchecked((ulong)BitConverter.DoubleToInt64Bits(
					(double)instruction.Operand!)));
			return true;
		}

		int value;
		if (op == System.Reflection.Emit.OpCodes.Ldc_I4)
		{
			value = (int)instruction.Operand!;
		}
		else if (op == System.Reflection.Emit.OpCodes.Ldc_I4_S)
		{
			value = Convert.ToSByte(instruction.Operand);
		}
		else
		{
			var delta = op.Value - System.Reflection.Emit.OpCodes.Ldc_I4_0.Value;
			if (op == System.Reflection.Emit.OpCodes.Ldc_I4_M1)
			{
				value = -1;
			}
			else if (delta is >= 0 and <= 8)
			{
				value = delta;
			}
			else
			{
				constant = default;
				return false;
			}
		}

		constant = boolean && value is 0 or 1
			? Boolean(value != 0)
			: Int32(value);
		return true;
	}
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
	long SpillWeight = 1,
	bool IsSpillTemporary = false)
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

internal enum M68kMachineEdgeKind
{
	Normal,
	ExceptionDispatch,
	LeaveToFinally,
	FinallyContinuation
}

internal readonly record struct M68kMachineEdge(
	int SourceBlockId,
	int TargetBlockId,
	M68kMachineEdgeKind Kind,
	int? ExceptionRegionId = null);

internal sealed class M68kMachineExceptionRegion
{
	public M68kMachineExceptionRegion(
		int id,
		CilExceptionRegion sourceRegion,
		int handlerEntryBlockId,
		IReadOnlyList<int> tryBlockIds,
		IReadOnlyList<int> handlerBlockIds,
		int? parentRegionId)
	{
		Id = id;
		SourceRegion = sourceRegion;
		HandlerEntryBlockId = handlerEntryBlockId;
		TryBlockIds = tryBlockIds.ToList();
		HandlerBlockIds = handlerBlockIds.ToList();
		ParentRegionId = parentRegionId;
	}

	public int Id { get; }

	public CilExceptionRegion SourceRegion { get; }

	public int HandlerEntryBlockId { get; }

	public List<int> TryBlockIds { get; }

	public List<int> HandlerBlockIds { get; }

	public int? ParentRegionId { get; }

	public int? CatchValueId { get; set; }
}

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

internal readonly record struct M68kMachineInlineSite(
	CilMethodIdentity Caller,
	CilInstruction CallInstruction);

internal sealed record M68kMachineInstructionOrigin(
	CilMethod SourceMethod,
	CilInstruction SourceInstruction,
	ImmutableArray<M68kMachineInlineSite> InlineSites)
{
	public CilMethodIdentity SourceMethodIdentity => SourceMethod.Identity;

	public static M68kMachineInstructionOrigin Create(
		CilMethod sourceMethod,
		CilInstruction sourceInstruction) =>
		new(sourceMethod, sourceInstruction, ImmutableArray<M68kMachineInlineSite>.Empty);

	public M68kMachineInstructionOrigin AtInlineSite(
		CilMethod caller,
		CilInstruction callInstruction) =>
		this with
		{
			InlineSites = InlineSites.Add(new M68kMachineInlineSite(
				caller.Identity,
				callInstruction))
	};
}

internal enum M68kMachineCallDispatchKind
{
	Direct,
	Virtual,
	Interface,
	Constrained,
	Import,
	External
}

internal sealed record M68kMachineLogicalCall(
	M68kMachineCallDispatchKind DispatchKind,
	ImmutableArray<CilMethodIdentity> ResolvedTargets,
	ImmutableArray<int> ArgumentValueIds,
	ImmutableArray<int> ResultValueIds,
	bool RequiresNullCheck,
	M68kMachineInstructionOrigin Origin);

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
	M68kMachineBranchCondition? BranchCondition = null,
	bool RequiresLiveCallerFrame = false,
	M68kMachineConstant? ConstantValue = null,
	M68kMachineInstructionOrigin? Origin = null,
	M68kMachineLogicalCall? LogicalCall = null,
	ImmutableArray<M68kExactMemoryAccess> ExactMemoryAccesses = default,
	M68kExternalCallConvention? PlatformBaseConvention = null,
	bool HasExplicitPlatformBase = false)
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
		M68kMachineBranchCondition? branchCondition = null,
		bool requiresLiveCallerFrame = false,
		M68kMachineConstant? constantValue = null,
		M68kMachineInstructionOrigin? origin = null,
		M68kMachineLogicalCall? logicalCall = null,
		IEnumerable<M68kExactMemoryAccess>? exactMemoryAccesses = null,
		M68kExternalCallConvention? platformBaseConvention = null,
		bool hasExplicitPlatformBase = false) =>
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
			branchCondition,
			requiresLiveCallerFrame,
			constantValue,
			origin,
			logicalCall,
			exactMemoryAccesses?.ToImmutableArray() ??
				ImmutableArray<M68kExactMemoryAccess>.Empty,
			platformBaseConvention,
			hasExplicitPlatformBase);
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

	public List<M68kMachineEdge> PredecessorEdges { get; } = new();

	public List<M68kMachineEdge> SuccessorEdges { get; } = new();

	public List<int> ActiveExceptionRegionIds { get; } = new();

	public IEnumerable<int> ControlFlowPredecessors =>
		Predecessors.Concat(PredecessorEdges
			.Where(static edge => edge.Kind != M68kMachineEdgeKind.Normal)
			.Select(static edge => edge.SourceBlockId)).Distinct();

	public IEnumerable<int> ControlFlowSuccessors =>
		Successors.Concat(SuccessorEdges
			.Where(static edge => edge.Kind != M68kMachineEdgeKind.Normal)
			.Select(static edge => edge.TargetBlockId)).Distinct();

	public int LoopDepth { get; set; }

	public bool IsExceptionEntry { get; set; }
}

internal sealed class M68kMachineFunction
{
	private int _nextValueId;
	private int _nextInstructionId;

	public M68kMachineFunction(
		string displayName,
		int entryBlockId,
		CilMethod? sourceMethod = null)
	{
		DisplayName = displayName;
		EntryBlockId = entryBlockId;
		SourceMethod = sourceMethod;
	}

	public string DisplayName { get; }

	public int EntryBlockId { get; }

	public CilMethod? SourceMethod { get; }

	public Dictionary<int, M68kMachineValue> Values { get; } = new();

	public Dictionary<int, M68kManagedByrefType> ManagedByrefTypes { get; } = new();

	public List<M68kMachineBlock> Blocks { get; } = new();

	public List<M68kMachineExceptionRegion> ExceptionRegions { get; } = new();

	public M68kMachineOptimizationStatistics? OptimizationStatistics { get; set; }

	public M68kRegisterSet ReservedRegisters { get; set; }

	public bool PreserveCalleeSavedRegisters { get; set; } = true;

	public bool HasDynamicStackAllocation { get; set; }

	public bool HasExceptionHandlers { get; set; }

	public HashSet<int> GcSpillSlots { get; } = new();



	public Dictionary<int, M68kFrameHome> LocalHomes { get; } = new();

	public Dictionary<int, M68kFrameHome> ArgumentHomes { get; } = new();

	public Dictionary<(int Size, string GcOffsets), int>
		ReusableAggregateReturnHomes { get; } = new();

	public M68kMachineValue CreateValue(
		CilStackValueKind kind,
		M68kMachineValueWidth width,
		M68kRegisterSet allowedRegisters,
		M68kRegister? precoloredRegister = null,
		bool isGcReference = false,
		bool isRematerializable = false,
		long spillWeight = 1,
		bool isSpillTemporary = false)
	{
		var value = new M68kMachineValue(
			_nextValueId++,
			kind,
			width,
			allowedRegisters,
			precoloredRegister,
			isGcReference,
			isRematerializable,
			spillWeight,
			isSpillTemporary);
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
		M68kMachineBranchCondition? branchCondition = null,
		bool requiresLiveCallerFrame = false,
		M68kMachineConstant? constantValue = null,
		M68kMachineInstructionOrigin? origin = null,
		M68kMachineLogicalCall? logicalCall = null,
		IEnumerable<M68kExactMemoryAccess>? exactMemoryAccesses = null,
		M68kExternalCallConvention? platformBaseConvention = null,
		bool hasExplicitPlatformBase = false) =>
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
			branchCondition,
			requiresLiveCallerFrame,
			constantValue,
			origin ?? OriginAt(ilOffset, sourceInstruction),
			logicalCall,
			exactMemoryAccesses,
			platformBaseConvention,
			hasExplicitPlatformBase);

	public M68kMachineInstructionOrigin? OriginAt(
		int ilOffset,
		CilInstruction? preferredInstruction = null)
	{
		if (SourceMethod is null)
		{
			return null;
		}
		var sourceInstruction = preferredInstruction ??
			SourceMethod.Instructions.FirstOrDefault(instruction =>
				instruction.Offset == ilOffset) ??
			SourceMethod.Instructions.LastOrDefault(instruction =>
				instruction.Offset <= ilOffset) ??
			SourceMethod.Instructions.First();
		return M68kMachineInstructionOrigin.Create(SourceMethod, sourceInstruction);
	}

	public void AddEdge(
		M68kMachineBlock source,
		M68kMachineBlock target,
		M68kMachineEdgeKind kind = M68kMachineEdgeKind.Normal,
		int? exceptionRegionId = null)
	{
		var edge = new M68kMachineEdge(
			source.Id,
			target.Id,
			kind,
			exceptionRegionId);
		if (!source.SuccessorEdges.Contains(edge))
		{
			source.SuccessorEdges.Add(edge);
			target.PredecessorEdges.Add(edge);
		}
		if (kind == M68kMachineEdgeKind.Normal)
		{
			if (!source.Successors.Contains(target.Id))
			{
				source.Successors.Add(target.Id);
			}
			if (!target.Predecessors.Contains(source.Id))
			{
				target.Predecessors.Add(source.Id);
			}
		}
	}

	public void SynchronizeNormalEdges()
	{
		foreach (var block in Blocks)
		{
			block.SuccessorEdges.RemoveAll(static edge =>
				edge.Kind == M68kMachineEdgeKind.Normal);
			block.PredecessorEdges.RemoveAll(static edge =>
				edge.Kind == M68kMachineEdgeKind.Normal);
		}
		var blocks = Blocks.ToDictionary(static block => block.Id);
		foreach (var source in Blocks)
		{
			foreach (var targetId in source.Successors)
			{
				if (blocks.TryGetValue(targetId, out var target))
				{
					AddEdge(source, target);
				}
			}
		}
	}

	public void RemoveBlocks(IReadOnlySet<int> removedBlockIds)
	{
		if (removedBlockIds.Count == 0)
		{
			return;
		}
		foreach (var block in Blocks.Where(block =>
			!removedBlockIds.Contains(block.Id)))
		{
			block.Predecessors.RemoveAll(removedBlockIds.Contains);
			block.Successors.RemoveAll(removedBlockIds.Contains);
			block.PredecessorEdges.RemoveAll(edge =>
				removedBlockIds.Contains(edge.SourceBlockId) ||
				removedBlockIds.Contains(edge.TargetBlockId));
			block.SuccessorEdges.RemoveAll(edge =>
				removedBlockIds.Contains(edge.SourceBlockId) ||
				removedBlockIds.Contains(edge.TargetBlockId));
		}
		Blocks.RemoveAll(block => removedBlockIds.Contains(block.Id));
		foreach (var region in ExceptionRegions)
		{
			region.TryBlockIds.RemoveAll(removedBlockIds.Contains);
			region.HandlerBlockIds.RemoveAll(removedBlockIds.Contains);
		}
		var removedRegions = ExceptionRegions
			.Where(region => removedBlockIds.Contains(region.HandlerEntryBlockId))
			.Select(static region => region.Id)
			.ToHashSet();
		ExceptionRegions.RemoveAll(region => removedRegions.Contains(region.Id));
		foreach (var block in Blocks)
		{
			block.ActiveExceptionRegionIds.RemoveAll(removedRegions.Contains);
			block.PredecessorEdges.RemoveAll(edge =>
				edge.ExceptionRegionId is { } id && removedRegions.Contains(id));
			block.SuccessorEdges.RemoveAll(edge =>
				edge.ExceptionRegionId is { } id && removedRegions.Contains(id));
		}
		SynchronizeNormalEdges();
	}
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
		var uses = new HashSet<int>();
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
					uses.Add(input);
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
				if (function.SourceMethod is not null &&
					instruction.Origin is not { } origin)
				{
					throw Invalid(
						function,
						$"Instruction {instruction.Id} has no source origin.");
				}
				if (instruction.Origin is { } instructionOrigin &&
					(instructionOrigin.SourceInstruction.Offset < 0 ||
					 string.IsNullOrWhiteSpace(
						 instructionOrigin.SourceMethodIdentity.ModuleName)))
				{
					throw Invalid(
						function,
						$"Instruction {instruction.Id} has an invalid source origin.");
				}
				if (!instruction.AllowCopyCoalescing &&
					instruction.Operation != M68kMachineOperation.Copy)
				{
					throw Invalid(
						function,
						$"Instruction {instruction.Id} disables coalescing but is not a copy.");
				}
				if (instruction.ConstantValue is not null &&
					(instruction.Operation != M68kMachineOperation.Constant ||
					 instruction.Uses.Length != 0 ||
					 instruction.Definitions.Length != 1))
				{
					throw Invalid(
						function,
						$"Instruction {instruction.Id} has constant metadata but is not a definition-only constant.");
				}
				if (instruction.LogicalCall is { } logicalCall &&
					(instruction.Operation != M68kMachineOperation.Call &&
					 instruction.Operation != M68kMachineOperation.ConditionalBranch ||
					 instruction.Origin is null ||
					 logicalCall.Origin != instruction.Origin ||
					 logicalCall.ArgumentValueIds.Any(value =>
						 !function.Values.ContainsKey(value)) ||
					 logicalCall.ResultValueIds.Any(value =>
						 !function.Values.ContainsKey(value)) ||
					 (logicalCall.DispatchKind is
						 M68kMachineCallDispatchKind.Direct or
						 M68kMachineCallDispatchKind.Virtual or
						 M68kMachineCallDispatchKind.Interface or
						 M68kMachineCallDispatchKind.Constrained) &&
						 logicalCall.ResolvedTargets.Length == 0))
				{
					throw Invalid(
						function,
						$"Instruction {instruction.Id} has invalid logical-call metadata " +
						$"(operation={instruction.Operation}, origin={instruction.Origin is not null}, " +
						$"originMatches={logicalCall.Origin == instruction.Origin}, " +
						$"arguments=[{string.Join(',', logicalCall.ArgumentValueIds)}], " +
						$"results=[{string.Join(',', logicalCall.ResultValueIds)}], " +
						$"targets={logicalCall.ResolvedTargets.Length}, dispatch={logicalCall.DispatchKind}).");
				}
				VerifyBranchCondition(function, instruction);
				VerifySpillInstruction(function, instruction);
				foreach (var use in instruction.Uses)
				{
					VerifyValueExists(function, use);
					uses.Add(use);
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
		function.SynchronizeNormalEdges();
		VerifyTypedControlFlow(function, blocks);
		VerifyExceptionRegions(function, blocks);

		foreach (var value in function.Values.Values)
		{
			VerifyValueConstraints(function, value);
			if (!definitions.ContainsKey(value.Id) && uses.Contains(value.Id))
			{
				throw Invalid(function,
					$"Value v{value.Id} is never defined; uses: {DescribeUses(function, value.Id)}.");
			}
		}
		VerifySsaDominance(function);
	}

	private static void VerifyTypedControlFlow(
		M68kMachineFunction function,
		IReadOnlyDictionary<int, M68kMachineBlock> blocks)
	{
		foreach (var block in function.Blocks)
		{
			if (block.SuccessorEdges.Count != block.SuccessorEdges.Distinct().Count() ||
				block.PredecessorEdges.Count != block.PredecessorEdges.Distinct().Count())
			{
				throw Invalid(function, $"Block {block.Id} has duplicate typed CFG edges.");
			}
			foreach (var edge in block.SuccessorEdges)
			{
				if (edge.SourceBlockId != block.Id ||
					!blocks.TryGetValue(edge.TargetBlockId, out var target) ||
					!target.PredecessorEdges.Contains(edge))
				{
					throw Invalid(function, $"Block {block.Id} has an inconsistent typed CFG edge.");
				}
				if (edge.Kind != M68kMachineEdgeKind.Normal &&
					edge.ExceptionRegionId is null)
				{
					throw Invalid(function, $"Typed CFG edge {edge} has no exception region.");
				}
			}
			foreach (var edge in block.PredecessorEdges)
			{
				if (edge.TargetBlockId != block.Id ||
					!blocks.TryGetValue(edge.SourceBlockId, out var source) ||
					!source.SuccessorEdges.Contains(edge))
				{
					throw Invalid(function, $"Block {block.Id} has an inconsistent incoming typed CFG edge.");
				}
			}
		}
	}

	private static void VerifyExceptionRegions(
		M68kMachineFunction function,
		IReadOnlyDictionary<int, M68kMachineBlock> blocks)
	{
		var regions = function.ExceptionRegions.ToDictionary(static region => region.Id);
		if (regions.Count != function.ExceptionRegions.Count)
		{
			throw Invalid(function, "Exception region ids are duplicated.");
		}
		foreach (var region in function.ExceptionRegions)
		{
			if (!blocks.ContainsKey(region.HandlerEntryBlockId) ||
				region.TryBlockIds.Any(id => !blocks.ContainsKey(id)) ||
				region.HandlerBlockIds.Any(id => !blocks.ContainsKey(id)) ||
				region.ParentRegionId is { } parent && !regions.ContainsKey(parent))
			{
				throw Invalid(function, $"Exception region {region.Id} owns an invalid block or parent.");
			}
			if (region.SourceRegion.IsCatch && region.CatchValueId is { } catchValue)
			{
				VerifyValueExists(function, catchValue);
				var handler = blocks[region.HandlerEntryBlockId];
				if (!handler.Instructions.Any(instruction =>
					instruction.Definitions.Contains(catchValue)))
				{
					throw Invalid(function, $"Catch value v{catchValue} is not defined at handler entry.");
				}
			}
		}
		foreach (var block in function.Blocks)
		{
			if (block.ActiveExceptionRegionIds.Distinct().Count() !=
					block.ActiveExceptionRegionIds.Count ||
				block.ActiveExceptionRegionIds.Any(id => !regions.ContainsKey(id)))
			{
				throw Invalid(function, $"Block {block.Id} has an invalid active exception scope.");
			}
		}
		foreach (var edge in function.Blocks.SelectMany(static block => block.SuccessorEdges))
		{
			if (edge.ExceptionRegionId is { } regionId && !regions.ContainsKey(regionId))
			{
				throw Invalid(function, $"Typed CFG edge {edge} names an invalid exception region.");
			}
		}
	}

	private static string DescribeUses(M68kMachineFunction function, int value)
	{
		var sites = new List<string>();
		foreach (var block in function.Blocks)
		{
			foreach (var phi in block.Phis)
				if (phi.Inputs.Any(input => input.Value == value))
					sites.Add($"B{block.Id}:phi-v{phi.Definition}");
			foreach (var instruction in block.Instructions)
				if (instruction.Uses.Contains(value))
					sites.Add($"B{block.Id}:I{instruction.Id}:{instruction.Operation}:IL_{instruction.IlOffset:X4}");
		}
		return sites.Count == 0 ? "<none>" : string.Join(", ", sites);
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

			case M68kMachineOperation.PlatformBaseLoad:
				if (instruction.PlatformBaseConvention is null ||
					instruction.Uses.Length != 0 ||
					instruction.Definitions.Length != 1 ||
					instruction.MemoryEffect is not (
						M68kMachineMemoryEffect.None or
						M68kMachineMemoryEffect.Read))
				{
					throw Invalid(
						function,
						$"Platform-base load {instruction.Id} has an invalid shape.");
				}
				break;

			case M68kMachineOperation.PlatformBaseStore:
				if (instruction.PlatformBaseConvention is null ||
					instruction.Uses.Length > 1 ||
					instruction.Definitions.Length != 0 ||
					instruction.MemoryEffect != M68kMachineMemoryEffect.Write ||
					instruction.Uses.Length == 0 && instruction.Immediate is null)
				{
					throw Invalid(
						function,
						$"Platform-base store {instruction.Id} has an invalid shape.");
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
			case M68kMachineOperation.GcKeepAlive:
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
						if (IsEntryArgumentDerived(function, definitions, use, new HashSet<int>()))
							continue;
						throw Invalid(
							function,
							$"Instruction {block.Instructions[index].Id} in block {block.Id} " +
							$"uses v{use} before its definition in block {definition.Block}, " +
							$"instruction index {definition.Instruction} " +
							$"({(definition.Instruction < 0 ? "phi" : function.Blocks.First(candidate => candidate.Id == definition.Block).Instructions[definition.Instruction].Operation)}), " +
							"dominates the use.");
					}
				}
			}
		}
	}

	private static bool IsEntryArgumentDerived(
		M68kMachineFunction function,
		IReadOnlyDictionary<int, (int Block, int Instruction)> definitions,
		int value,
		HashSet<int> visiting)
	{
		if (!visiting.Add(value) || !definitions.TryGetValue(value, out var definition) ||
			definition.Block != function.EntryBlockId || definition.Instruction < 0)
			return false;
		var entry = function.Blocks.First(block => block.Id == function.EntryBlockId);
		var instruction = entry.Instructions[definition.Instruction];
		return instruction.Operation == M68kMachineOperation.Argument ||
			instruction.Operation == M68kMachineOperation.Copy && instruction.Uses.Length == 1 &&
			IsEntryArgumentDerived(function, definitions, instruction.Uses[0], visiting);
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
