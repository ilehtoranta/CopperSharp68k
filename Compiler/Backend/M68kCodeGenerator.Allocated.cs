/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
	private sealed record AllocatedIlLabelPlan(
		IReadOnlyDictionary<int, IReadOnlyList<int>> BlockEntryOffsets,
		IReadOnlyDictionary<int, int> InstructionOffsets);

	private readonly HashSet<int> _allocatedSuppressedInstructions = new();
	private readonly Dictionary<int, M68kMachineInstruction>
		_allocatedFoldedCopyConstants = new();
	private readonly HashSet<int> _allocatedExceptionFrameStores = new();
	private int _allocatedOutgoingStackBytes;

	private void EmitAllocatedMethod(
		CilMethod method,
		InternalCallAbi abi,
		M68kAllocatedFunction allocated,
		IReadOnlyDictionary<int, string?> platformBaseBlockEntries,
		IReadOnlySet<int>? exceptionStateBlockEntries)
	{
		var unsupported = allocated.Function.Blocks
			.SelectMany(static block => block.Instructions)
			.Where(instruction =>
				!CanEmitAllocatedInstruction(method, allocated, instruction))
			.ToArray();
		var unsupportedPhiEdge = allocated.ParallelCopies.EdgeCopies.Any(copy =>
			allocated.Function.Blocks.Single(
				block => block.Id == copy.Key.From).Successors.Count != 1);
		if (unsupported.Length != 0 ||
			unsupportedPhiEdge)
		{
			throw new InvalidOperationException(
				$"Allocated emission is incomplete for '{method.DisplayName}': " +
				$"phi={unsupportedPhiEdge}, operations=" +
				string.Join(
					", ",
					unsupported.Select(instruction =>
						$"{instruction.Operation}/" +
						$"{instruction.SourceInstruction?.OpCode.Name}")
						.Distinct()));
		}

		RecordUnwindLayout(method, abi, allocated);
		EmitAllocatedCalleeSaves(allocated.Frame.CalleeSavedRegisters);
		EmitAllocateFrame(allocated.Frame.FrameBytes);
		_allocatedSuppressedInstructions.Clear();
		_allocatedFoldedCopyConstants.Clear();
		_allocatedExceptionFrameStores.Clear();
		PrepareAllocatedConstantCopies(method, allocated);
		PrepareAllocatedFrameAddressFolds(method, allocated);
		PrepareAllocatedExceptionFrameStores(allocated);
		_allocatedOutgoingStackBytes = 0;
		var savedBytes = checked(
			(allocated.Frame.CalleeSavedRegisters.Count * 4) +
			allocated.Frame.FrameBytes);
		EmitAllocatedFrameHomeInitialization(
			method,
			abi,
			allocated,
			savedBytes);
		EmitAllocatedIncomingArguments(
			abi,
			allocated,
			savedBytes);
		var requiredIlLabels = GetBranchTargets(method.Instructions)
			.Concat(method.ExceptionRegions.SelectMany(static region =>
				new[]
				{
					region.TryOffset,
					region.HandlerOffset,
					region.FilterOffset
				}))
			.Where(static offset => offset >= 0)
			.ToHashSet();
		var ilLabelPlan = CreateAllocatedIlLabelPlan(
			allocated.Function,
			requiredIlLabels,
			allocated.FinalDestinations);
		var blocksById = allocated.Function.Blocks.ToDictionary(
			static block => block.Id);
		var layoutBlocks = allocated.BlockLayout.BlockIds
			.Select(blockId => blocksById[blockId])
			.ToArray();
		var naturalLoops = M68kLoopFootprintAnalysis.Discover(allocated.Function);
		RecordAllocatedLoopLayouts(method, allocated, naturalLoops);
		var alignedLoopHeaders = _request.Cpu == M68kCpuTarget.M68020
			? naturalLoops
				.Select(loop => allocated.FinalDestinations.Resolve(loop.HeaderBlockId))
				.ToHashSet()
			: new HashSet<int>();
		foreach (var header in alignedLoopHeaders)
		{
			_assembler.RequestLongAlignment(AllocatedBlockLabel(method, header));
		}
		_emittingUnwindMethod = method;
		_emittingAllocatedFunction = allocated;
		for (var blockIndex = 0; blockIndex < layoutBlocks.Length; blockIndex++)
		{
			var block = layoutBlocks[blockIndex];
			var nextBlockId = blockIndex + 1 < layoutBlocks.Length
				? layoutBlocks[blockIndex + 1].Id
				: (int?)null;
			if (platformBaseBlockEntries.TryGetValue(
					block.StartIlOffset,
					out var platformBaseIdentity))
			{
				ApplyPlatformBaseBlockEntry(platformBaseIdentity);
			}
			_assembler.Mark(AllocatedBlockLabel(method, block.Id));
			if (allocated.FinalDestinations.AliasesByDestination.TryGetValue(
					block.Id,
					out var blockAliases))
			{
				foreach (var alias in blockAliases)
				{
					_assembler.Mark(AllocatedBlockLabel(method, alias));
				}
			}
			if (ilLabelPlan.BlockEntryOffsets.TryGetValue(
					block.Id,
					out var blockEntryOffsets))
			{
				foreach (var offset in blockEntryOffsets)
				{
					_assembler.Mark(IlLabel(method, offset));
				}
			}
			if (method.ExceptionRegions.Any(region =>
				region.IsCatch &&
				region.HandlerOffset == block.StartIlOffset))
			{
				_assembler.EmitWord(0x588F); // ADDQ.L #4,A7; consume runtime catch value
			}
			foreach (var instruction in block.Instructions)
			{
				if (ilLabelPlan.InstructionOffsets.TryGetValue(
						instruction.Id,
						out var instructionOffset))
				{
					_assembler.Mark(IlLabel(method, instructionOffset));
				}
				if (_allocatedSuppressedInstructions.Contains(instruction.Id))
				{
					continue;
				}
				_emittingMachineInstruction = instruction;
				EmitAllocatedInstruction(
					method,
					abi,
					allocated,
					block,
					instruction,
					savedBytes,
					nextBlockId);
			}
			if (block.Successors.Count == 1 &&
				(block.Instructions.Count == 0 ||
				 !IsAllocatedTerminator(block.Instructions[^1].Operation)))
			{
				var originalSuccessor = block.Successors[0];
				var successor = allocated.FinalDestinations.Resolve(originalSuccessor);
				EmitAllocatedEdgeCopies(allocated, block.Id, originalSuccessor);
				if (nextBlockId != successor)
				{
					_assembler.EmitBranch(
						M68kCondition.True,
						AllocatedBlockLabel(method, successor));
				}
			}
			_assembler.MarkAnalysisAnchor(AllocatedBlockEndLabel(method, block.Id));
		}
		_emittingMachineInstruction = null;
		_emittingAllocatedFunction = null;
		_emittingUnwindMethod = null;
	}

	private void RecordAllocatedLoopLayouts(
		CilMethod method,
		M68kAllocatedFunction allocated,
		IReadOnlyList<M68kNaturalLoop> naturalLoops)
	{
		var blocks = allocated.Function.Blocks.ToDictionary(static block => block.Id);
		foreach (var loop in naturalLoops)
		{
			var emittedBlocks = loop.BlockIds
				.Select(allocated.FinalDestinations.Resolve)
				.Where(allocated.FinalDestinations.EmittedBlockIds.Contains)
				.Distinct()
				.OrderBy(static blockId => blockId)
				.ToArray();
			if (emittedBlocks.Length == 0)
			{
				continue;
			}
			var emittedHeader = allocated.FinalDestinations.Resolve(loop.HeaderBlockId);
			_loopLayouts.Add(new M68kLoopLayout(
				method.DisplayName,
				blocks[loop.HeaderBlockId].StartIlOffset,
				AllocatedBlockLabel(method, emittedHeader),
				emittedBlocks
					.Select(blockId => new M68kLoopBlockLayout(
						AllocatedBlockLabel(method, blockId),
						AllocatedBlockEndLabel(method, blockId)))
					.ToArray()));
		}
	}

	private static AllocatedIlLabelPlan CreateAllocatedIlLabelPlan(
		M68kMachineFunction function,
		IReadOnlySet<int> requiredIlLabels,
		M68kFinalDestinationPlan finalDestinations)
	{
		var claimed = new HashSet<int>();
		var blockEntries = new Dictionary<int, List<int>>();
		var instructionOffsets = new Dictionary<int, int>();
		for (var blockIndex = 0; blockIndex < function.Blocks.Count; blockIndex++)
		{
			var block = function.Blocks[blockIndex];
			var nextBlockOffset = blockIndex + 1 < function.Blocks.Count
				? function.Blocks[blockIndex + 1].StartIlOffset
				: int.MaxValue;
			foreach (var requiredOffset in requiredIlLabels
				.Where(offset =>
					offset >= block.StartIlOffset &&
					offset < nextBlockOffset)
				.Order())
			{
				ClaimBlockEntry(block.Id, requiredOffset);
			}
			ClaimBlockEntry(block.Id, block.StartIlOffset);
			foreach (var instruction in block.Instructions)
			{
				if (instruction.SourceInstruction is { } source &&
					claimed.Add(source.Offset))
				{
					if (finalDestinations.IsOmitted(block.Id))
					{
						AddBlockEntry(
							finalDestinations.Resolve(block.Id),
							source.Offset);
					}
					else
					{
						instructionOffsets.Add(instruction.Id, source.Offset);
					}
				}
			}
		}
		return new AllocatedIlLabelPlan(
			blockEntries.ToDictionary(
				static item => item.Key,
				static item => (IReadOnlyList<int>)item.Value),
			instructionOffsets);

		void ClaimBlockEntry(int blockId, int offset)
		{
			if (!claimed.Add(offset))
			{
				return;
			}
			AddBlockEntry(finalDestinations.Resolve(blockId), offset);
		}

		void AddBlockEntry(int blockId, int offset)
		{
			if (!blockEntries.TryGetValue(blockId, out var offsets))
			{
				offsets = [];
				blockEntries.Add(blockId, offsets);
			}
			offsets.Add(offset);
		}
	}

	private void EmitAllocatedIncomingArguments(
		InternalCallAbi abi,
		M68kAllocatedFunction allocated,
		int savedBytes)
	{
		var entry = allocated.Function.Blocks[0];
		var transfers = new List<(
			M68kMachineInstruction Argument,
			M68kMachineInstruction? Copy,
			InternalArgumentLocation Source,
			M68kRegister Destination)>();
		foreach (var argument in entry.Instructions.Where(static instruction =>
			instruction.Operation == M68kMachineOperation.Argument &&
			instruction.Definitions.Length == 1))
		{
			var incoming = argument.Definitions[0];
			if (argument.ArgumentIndex is not { } argumentIndex)
			{
				throw new InvalidOperationException(
					"Allocated incoming argument has no ABI index.");
			}
			var source = abi.Arguments[argumentIndex];
			M68kMachineInstruction? copy = null;
			var destinationValue = incoming;
			if (source.Register is not null)
			{
				var copies = entry.Instructions.Where(instruction =>
					instruction.Operation == M68kMachineOperation.Copy &&
					instruction.IlOffset == entry.StartIlOffset &&
					instruction.Uses is [var copySource] &&
					copySource == incoming &&
					instruction.Definitions.Length == 1).ToArray();
				if (copies.Length != 1)
				{
					throw new InvalidOperationException(
						"Allocated register argument does not have one canonical entry copy.");
				}
				copy = copies[0];
				destinationValue = copy.Definitions[0];
			}
			transfers.Add((
				argument,
				copy,
				source,
				allocated.Allocation.Registers[
					destinationValue].Register));
			_allocatedSuppressedInstructions.Add(argument.Id);
			if (copy is not null)
			{
				_allocatedSuppressedInstructions.Add(copy.Id);
			}
		}

		var registerTransfers = transfers
			.Where(static transfer => transfer.Source.Register is not null)
			.ToArray();
		var parallelCopies = registerTransfers
			.Select(transfer => new M68kParallelCopy(
				M68kStorageLocation.Register(transfer.Destination),
				M68kStorageLocation.Register(
					transfer.Source.Register!.Value)))
			.ToArray();
		var resolved = M68kParallelCopyResolver.Resolve(
			parallelCopies,
			M68kStorageLocation.Temporary());
		if (resolved.Any(static copy =>
			copy.Source.Kind == M68kStorageKind.Temporary ||
			copy.Destination.Kind == M68kStorageKind.Temporary))
		{
			for (var index = 0; index < registerTransfers.Length; index++)
			{
				EmitAllocatedPush(
					registerTransfers[index].Source.Register!.Value);
			}
			for (var index = 0; index < registerTransfers.Length; index++)
			{
				EmitAllocatedStackLoad(
					registerTransfers[index].Destination,
					checked((registerTransfers.Length - 1 - index) * 4));
			}
			EmitReleaseStackBytes(checked(registerTransfers.Length * 4));
		}
		else
		{
			foreach (var copy in resolved)
			{
				EmitAllocatedMove(
					(M68kRegister)copy.Source.Index,
					(M68kRegister)copy.Destination.Index,
					M68kMachineValueWidth.Long);
			}
		}

		foreach (var transfer in transfers.Where(static transfer =>
			transfer.Source.Register is null))
		{
			EmitAllocatedStackLoad(
				transfer.Destination,
				checked(savedBytes + 4 + transfer.Source.StackOffset));
		}
	}

	private void PrepareAllocatedConstantCopies(
		CilMethod method,
		M68kAllocatedFunction allocated)
	{
		var instructions = allocated.Function.Blocks
			.SelectMany(static block => block.Instructions)
			.ToArray();
		foreach (var constant in instructions.Where(static instruction =>
			instruction.Operation == M68kMachineOperation.Constant &&
			instruction.Definitions.Length == 1))
		{
			var value = constant.Definitions[0];
			var forwarding = new List<M68kMachineInstruction>();
			M68kMachineInstruction[] users;
			do
			{
				users = instructions
					.Where(instruction => instruction.Uses.Contains(value))
					.ToArray();
				if (users is not [var copy] ||
					copy.Uses.Length != 1 ||
					copy.Definitions.Length != 1 ||
					(copy.Operation != M68kMachineOperation.Copy &&
					 !IsAllocatedIdentityIntrinsic(method, copy)) ||
					allocated.Function.Values[value].Width !=
						allocated.Function.Values[copy.Definitions[0]].Width)
				{
					break;
				}
				forwarding.Add(copy);
				value = copy.Definitions[0];
			}
			while (true);
			if (forwarding.Count != 0)
			{
				_allocatedSuppressedInstructions.Add(constant.Id);
				foreach (var copy in forwarding.Take(forwarding.Count - 1))
				{
					_allocatedSuppressedInstructions.Add(copy.Id);
				}
				_allocatedFoldedCopyConstants.Add(forwarding[^1].Id, constant);
			}
		}
	}

	private bool IsAllocatedIdentityIntrinsic(
		CilMethod caller,
		M68kMachineInstruction instruction)
	{
		if (instruction.Operation != M68kMachineOperation.Call ||
			instruction.SourceInstruction is not { Operand: int token } source)
		{
			return false;
		}
		var target = _module.ResolveMethodToken(
			token,
			caller,
			source.Offset);
		return target.ImportName is
			"intrinsic:cstring-from-pointer" or
			"intrinsic:cstring-to-uint32" or
			"intrinsic:aptr-from-pointer" or
			"intrinsic:aptr-to-uint32" or
			"intrinsic:amiga-vararg-from-value" or
			"intrinsic:address-of-ref" or
			"intrinsic:address-to-ref" or
			"intrinsic:ref-cast" or
			"intrinsic:hook-address-of" or
			"intrinsic:boopsi-message-address-of";
	}

	private void PrepareAllocatedExceptionFrameStores(
		M68kAllocatedFunction allocated)
	{
		foreach (var block in allocated.Function.Blocks.Where(
			static block => block.IsExceptionEntry))
		{
			for (var index = 0; index + 1 < block.Instructions.Count; index++)
			{
				var exception = block.Instructions[index];
				var store = block.Instructions[index + 1];
				if (exception is not
					{
						Operation: M68kMachineOperation.Other,
						SourceInstruction: null,
						Definitions: [var exceptionValue]
					} ||
					allocated.Function.Values[exceptionValue].Kind !=
						CilStackValueKind.Reference ||
					store is not
					{
						Operation: M68kMachineOperation.LocalStore,
						Uses: [var storedValue],
						ArgumentIndex: not null
					} ||
					storedValue != exceptionValue ||
					CountAllocatedValueUses(
						allocated.Function,
						exceptionValue) != 1)
				{
					continue;
				}

				// The catch value already lives in the active-exception frame slot.
				// Keep the stloc as the emission point so its IL label remains valid,
				// but copy frame-to-frame without allocating a temporary register.
				_allocatedSuppressedInstructions.Add(exception.Id);
				_allocatedExceptionFrameStores.Add(store.Id);
			}
		}
	}

	private static bool IsAllocatedTerminator(M68kMachineOperation operation) =>
		operation is
			M68kMachineOperation.Branch or
			M68kMachineOperation.ConditionalBranch or
			M68kMachineOperation.Switch or
			M68kMachineOperation.Return or
			M68kMachineOperation.Throw;

	private void EmitAllocatedExceptionState(
		CilMethod method,
		CilInstruction instruction,
		bool forceExceptionState,
		ref string? emittedExceptionStateLabel)
	{
		if (method.ExceptionRegions.Count == 0)
		{
			return;
		}
		var stateLabel = RegisterExceptionState(
			method,
			GetActiveExceptionGroups(method, instruction.Offset));
		if (!forceExceptionState &&
			StringComparer.Ordinal.Equals(
				stateLabel,
				emittedExceptionStateLabel))
		{
			return;
		}
		if (stateLabel is null)
		{
			EmitRuntimeFrameImmediate(0, RuntimeFrameStateOffset);
		}
		else
		{
			EmitRuntimeFrameAddress(stateLabel, RuntimeFrameStateOffset);
		}
		emittedExceptionStateLabel = stateLabel;
	}

	private bool CanEmitAllocatedInstruction(
		CilMethod caller,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		if (instruction.Operation is
			M68kMachineOperation.Argument or
			M68kMachineOperation.Copy or
			M68kMachineOperation.Constant or
			M68kMachineOperation.Address or
			M68kMachineOperation.SpillLoad or
			M68kMachineOperation.SpillStore or
			M68kMachineOperation.SpillClear or
			M68kMachineOperation.RootStore or
			M68kMachineOperation.RootClear or
			M68kMachineOperation.OutgoingArgumentPush or
			M68kMachineOperation.OutgoingArgumentCleanup or
			M68kMachineOperation.Add or
			M68kMachineOperation.Subtract or
			M68kMachineOperation.Multiply or
			M68kMachineOperation.Divide or
			M68kMachineOperation.Remainder or
			M68kMachineOperation.And or
			M68kMachineOperation.Or or
			M68kMachineOperation.Xor or
			M68kMachineOperation.Negate or
			M68kMachineOperation.Not or
			M68kMachineOperation.Shift or
			M68kMachineOperation.Compare or
			M68kMachineOperation.Convert or
			M68kMachineOperation.Call or
			M68kMachineOperation.Branch or
			M68kMachineOperation.ConditionalBranch or
			M68kMachineOperation.Return or
			M68kMachineOperation.Throw)
		{
			if (instruction.Operation == M68kMachineOperation.Call)
			{
				return CanEmitAllocatedCall(caller, allocated, instruction);
			}
			return true;
		}
		if (instruction.Operation == M68kMachineOperation.Load)
		{
			return instruction.SourceInstruction is { } source &&
				(TryGetAllocatedArgumentIndex(source, out _) ||
				 source.OpCode == OpCodes.Ldsfld ||
				 source.OpCode == OpCodes.Ldsflda ||
				 source.OpCode == OpCodes.Ldfld ||
				 source.OpCode == OpCodes.Ldflda ||
				 IsIndirectLoad(source.OpCode));
		}
		if (instruction.Operation == M68kMachineOperation.Store)
		{
			return instruction.SourceInstruction?.OpCode is { } storeOp &&
				(storeOp == OpCodes.Stsfld ||
				 storeOp == OpCodes.Stfld ||
				 IsIndirectStore(storeOp));
		}
		if (instruction.Operation is
			M68kMachineOperation.LocalLoad or
			M68kMachineOperation.ArgumentLoad or
			M68kMachineOperation.LocalStore)
		{
			if (instruction.ArgumentIndex is not { } frameIndex)
			{
				return false;
			}
			return instruction.Operation == M68kMachineOperation.ArgumentLoad
				? allocated.Frame.ArgumentHomeOffsets.ContainsKey(frameIndex)
				: allocated.Frame.LocalOffsets.ContainsKey(frameIndex);
		}
		if (instruction.Operation == M68kMachineOperation.LocalAddress)
		{
			return instruction.ArgumentIndex is { } localIndex &&
				allocated.Frame.LocalOffsets.ContainsKey(localIndex);
		}
		if (instruction.Operation == M68kMachineOperation.ArgumentAddress)
		{
			return instruction.ArgumentIndex is { } argumentIndex &&
				allocated.Frame.ArgumentHomeOffsets.ContainsKey(argumentIndex) &&
				allocated.Function.ArgumentHomes[argumentIndex].Size == 4;
		}
		if (instruction.Operation == M68kMachineOperation.ObjectAllocate)
		{
			return _memoryManagement != M68kMemoryManagement.None &&
				instruction.SourceInstruction?.OpCode == OpCodes.Newobj;
		}
		if (instruction.Operation == M68kMachineOperation.ArrayAllocate)
		{
			return _memoryManagement != M68kMemoryManagement.None &&
				instruction.SourceInstruction?.OpCode == OpCodes.Newarr;
		}
		if (instruction.Operation is
			M68kMachineOperation.ArrayLoad or
			M68kMachineOperation.ArrayStore or
			M68kMachineOperation.ArrayAddress)
		{
			return instruction.SourceInstruction?.OpCode is { } arrayOp &&
				(IsArrayAccess(arrayOp) || arrayOp == OpCodes.Ldlen);
		}
		return instruction.Operation == M68kMachineOperation.Other &&
			(instruction.SourceInstruction is null ||
			 instruction.SourceInstruction.OpCode is { } otherOp &&
			 (otherOp == OpCodes.Nop ||
			  otherOp == OpCodes.Pop ||
			  otherOp == OpCodes.Initobj ||
			  otherOp == OpCodes.Rethrow ||
			  otherOp == OpCodes.Endfinally));
	}

	private bool CanEmitAllocatedCall(
		CilMethod caller,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		if (instruction.SourceInstruction is not { } source ||
			(source.OpCode != OpCodes.Call &&
			 source.OpCode != OpCodes.Callvirt))
		{
			return false;
		}
		var target = _module.ResolveMethodToken(
			(int)source.Operand!,
			caller,
			source.Offset);
		if (target.Definition is null)
		{
			return IsAllocatedIntrinsic(target.ImportName);
		}
		var definition = target.Definition;
		if (definition.ExternalCall is { } externalCall &&
			externalCall.Convention.CacheRegister is { } cache &&
			(externalCall.Abi.ReturnRegister == cache ||
			 externalCall.Abi.ParameterRegisters.Contains(cache)))
		{
			return false;
		}
		return instruction.Uses.All(value =>
			allocated.Function.Values[value].PrecoloredRegister is not null);
	}

	private void EmitAllocatedInstruction(
		CilMethod method,
		InternalCallAbi abi,
		M68kAllocatedFunction allocated,
		M68kMachineBlock block,
		M68kMachineInstruction instruction,
		int savedBytes,
		int? nextBlockId)
	{
		M68kAllocatedLocation Location(int value) =>
			allocated.Allocation.Registers[value];

		if (_allocatedFoldedCopyConstants.TryGetValue(
				instruction.Id,
				out var foldedConstant))
		{
			EmitAllocatedConstant(
				foldedConstant,
				Location(instruction.Definitions[0]));
			EmitAllocatedNormalize(
				Location(instruction.Definitions[0]).Register,
				allocated.Function.Values[instruction.Definitions[0]].Kind);
			return;
		}

		switch (instruction.Operation)
		{
			case M68kMachineOperation.Other:
				if (instruction.SourceInstruction?.OpCode == OpCodes.Initobj)
				{
					EmitAllocatedInitObject(method, allocated, instruction);
				}
				else if (instruction.SourceInstruction?.OpCode == OpCodes.Rethrow)
				{
					EmitAllocatedFrameLoad(
						M68kRegister.A0,
						M68kMachineValueWidth.Long,
						AllocatedFrameOffset(
							allocated,
							allocated.Frame.ActiveExceptionOffset ??
								throw new InvalidOperationException("Rethrow has no exception slot.")));
					EmitExceptionRaise(reason: 0, hasException: true);
				}
				else if (instruction.SourceInstruction?.OpCode == OpCodes.Endfinally)
				{
					_assembler.EmitJmp(
						RuntimeExceptionEndFinallyLabel,
						external: false);
				}
				else if (block.IsExceptionEntry &&
					instruction.SourceInstruction is null &&
					instruction.Definitions.Length == 1 &&
					allocated.Function.Values[
						instruction.Definitions[0]].Kind ==
						CilStackValueKind.Reference)
				{
					EmitAllocatedFrameLoad(
						M68kRegister.A0,
						M68kMachineValueWidth.Long,
						AllocatedFrameOffset(
							allocated,
							allocated.Frame.ActiveExceptionOffset ??
								throw new InvalidOperationException("Catch entry has no exception slot.")));
					EmitAllocatedMove(
						M68kRegister.A0,
						Location(instruction.Definitions[0]).Register,
						M68kMachineValueWidth.Long);
				}
				return;

			case M68kMachineOperation.Argument:
				EmitAllocatedArgumentDefinition(
					abi,
					instruction,
					Location(instruction.Definitions[0]),
					savedBytes);
				return;

			case M68kMachineOperation.Load:
				if (TryGetAllocatedArgumentIndex(
						instruction.SourceInstruction!,
						out _))
				{
					EmitAllocatedArgumentLoad(
						abi,
						instruction,
						Location(instruction.Definitions[0]),
						savedBytes);
				}
				else
				{
					if (instruction.SourceInstruction!.OpCode is
						var fieldOp &&
						(fieldOp == OpCodes.Ldfld ||
						 fieldOp == OpCodes.Ldflda))
					{
						EmitAllocatedInstanceFieldLoad(
							method,
							allocated,
							instruction);
					}
					else if (IsIndirectLoad(instruction.SourceInstruction.OpCode))
					{
						var address = Location(instruction.Uses[0]).Register;
						var destination = Location(instruction.Definitions[0]).Register;
						EmitAllocatedRequireNonNull(address);
						EmitAllocatedBaseLoad(
							address,
							destination,
							AllocatedIndirectWidth(instruction.SourceInstruction.OpCode),
							0);
						EmitAllocatedNormalize(
							destination,
							allocated.Function.Values[instruction.Definitions[0]].Kind);
					}
					else
					{
						EmitAllocatedStaticLoad(
							method,
							instruction,
							Location(instruction.Definitions[0]).Register);
					}
				}
				return;

			case M68kMachineOperation.Store:
				if (instruction.SourceInstruction!.OpCode == OpCodes.Stfld)
				{
					EmitAllocatedInstanceFieldStore(
						method,
						allocated,
							instruction);
				}
				else if (IsIndirectStore(instruction.SourceInstruction.OpCode))
				{
					var address = Location(instruction.Uses[0]).Register;
					EmitAllocatedRequireNonNull(address);
					EmitAllocatedBaseStore(
						Location(instruction.Uses[1]).Register,
						address,
						AllocatedIndirectWidth(instruction.SourceInstruction.OpCode),
						0);
				}
				else
				{
					EmitAllocatedStaticStore(
						method,
						allocated,
						instruction,
						Location(instruction.Uses[0]).Register);
				}
				return;

			case M68kMachineOperation.LocalLoad:
			case M68kMachineOperation.ArgumentLoad:
				EmitAllocatedFrameLoad(
					Location(instruction.Definitions[0]).Register,
					allocated.Function.Values[instruction.Definitions[0]].Width,
					AllocatedFrameOffset(
						allocated,
						instruction.Operation == M68kMachineOperation.ArgumentLoad
							? allocated.Frame.ArgumentHomeOffsets[
								instruction.ArgumentIndex!.Value]
							: allocated.Frame.LocalOffsets[
								instruction.ArgumentIndex!.Value]));
				if (instruction.SourceInstruction is { } frameLoadSource &&
					IsIndirectLoad(frameLoadSource.OpCode))
				{
					EmitAllocatedNormalize(
						Location(instruction.Definitions[0]).Register,
						allocated.Function.Values[instruction.Definitions[0]].Kind);
				}
				return;

			case M68kMachineOperation.LocalStore:
				if (_allocatedExceptionFrameStores.Contains(instruction.Id))
				{
					EmitAllocatedFrameCopy(
						AllocatedFrameOffset(
							allocated,
							allocated.Frame.ActiveExceptionOffset ??
								throw new InvalidOperationException(
									"Catch entry has no exception slot.")),
						AllocatedFrameOffset(
							allocated,
							allocated.Frame.LocalOffsets[
								instruction.ArgumentIndex!.Value]));
					return;
				}
				EmitAllocatedFrameStore(
					Location(instruction.Uses[0]).Register,
					allocated.Function.Values[instruction.Uses[0]].Width,
					AllocatedFrameOffset(
						allocated,
						allocated.Frame.LocalOffsets[
							instruction.ArgumentIndex!.Value]));
				return;

			case M68kMachineOperation.LocalAddress:
				EmitAllocatedFrameAddress(
					Location(instruction.Definitions[0]).Register,
					AllocatedFrameOffset(
						allocated,
						allocated.Frame.LocalOffsets[
							instruction.ArgumentIndex!.Value]));
				return;

			case M68kMachineOperation.ArgumentAddress:
				EmitAllocatedFrameAddress(
					Location(instruction.Definitions[0]).Register,
					AllocatedFrameOffset(
						allocated,
						allocated.Frame.ArgumentHomeOffsets[
							instruction.ArgumentIndex!.Value]));
				return;

			case M68kMachineOperation.ObjectAllocate:
				EmitAllocatedObjectAllocation(method, instruction);
				return;

			case M68kMachineOperation.ArrayAllocate:
				EmitAllocatedArrayAllocation(method, instruction);
				return;

			case M68kMachineOperation.ArrayLoad:
			case M68kMachineOperation.ArrayStore:
			case M68kMachineOperation.ArrayAddress:
				if (instruction.SourceInstruction!.OpCode == OpCodes.Ldlen)
				{
					EmitAllocatedArrayLength(allocated, instruction);
				}
				else
				{
					EmitAllocatedArrayAccess(allocated, instruction);
				}
				return;

			case M68kMachineOperation.Constant:
				EmitAllocatedConstant(
					instruction,
					Location(instruction.Definitions[0]));
				return;

			case M68kMachineOperation.Address:
				EmitAllocatedStringAddress(
					method,
					instruction,
					Location(instruction.Definitions[0]).Register);
				return;

			case M68kMachineOperation.SpillLoad:
				EmitAllocatedFrameLoad(
					Location(instruction.Definitions[0]).Register,
					allocated.Function.Values[instruction.Definitions[0]].Width,
					AllocatedFrameOffset(
						allocated,
						allocated.Frame.SpillOffsets[
							instruction.SpillSlotIndex!.Value]));
				return;

			case M68kMachineOperation.SpillStore:
				EmitAllocatedFrameStore(
					Location(instruction.Uses[0]).Register,
					allocated.Function.Values[instruction.Uses[0]].Width,
					AllocatedFrameOffset(
						allocated,
						allocated.Frame.SpillOffsets[
							instruction.SpillSlotIndex!.Value]));
				return;

			case M68kMachineOperation.SpillClear:
				EmitAllocatedFrameClear(
					AllocatedFrameOffset(
						allocated,
						allocated.Frame.SpillOffsets[
							instruction.SpillSlotIndex!.Value]));
				return;

			case M68kMachineOperation.RootStore:
				EmitAllocatedFrameStore(
					Location(instruction.Uses[0]).Register,
					M68kMachineValueWidth.Long,
					AllocatedFrameOffset(
						allocated,
						allocated.Frame.RootOffsets[
							instruction.SpillSlotIndex!.Value]));
				return;

			case M68kMachineOperation.RootClear:
				EmitAllocatedFrameClear(
					AllocatedFrameOffset(
						allocated,
						allocated.Frame.RootOffsets[
							instruction.SpillSlotIndex!.Value]));
				return;

			case M68kMachineOperation.OutgoingArgumentPush:
			{
				var location = Location(instruction.Uses[0]);
				var width = allocated.Function.Values[instruction.Uses[0]].Width;
				if (width == M68kMachineValueWidth.LongPair)
				{
					EmitAllocatedPush((M68kRegister)((int)location.Register + 1));
					EmitAllocatedPush(location.Register);
				}
				else
				{
					EmitAllocatedPush(location.Register);
				}
				_allocatedOutgoingStackBytes = checked(
					_allocatedOutgoingStackBytes +
					instruction.ArgumentIndex!.Value);
				return;
			}

			case M68kMachineOperation.OutgoingArgumentCleanup:
				EmitReleaseStackBytes(instruction.ArgumentIndex!.Value);
				_allocatedOutgoingStackBytes = checked(
					_allocatedOutgoingStackBytes -
					instruction.ArgumentIndex.Value);
				if (_allocatedOutgoingStackBytes < 0)
				{
					throw new InvalidOperationException(
						"Allocated outgoing argument stack depth became negative.");
				}
				return;

			case M68kMachineOperation.Copy:
				EmitAllocatedMove(
					Location(instruction.Uses[0]).Register,
					Location(instruction.Definitions[0]).Register,
					allocated.Function.Values[instruction.Definitions[0]].Width);
				EmitAllocatedNormalize(
					Location(instruction.Definitions[0]).Register,
					allocated.Function.Values[instruction.Definitions[0]].Kind);
				return;

			case M68kMachineOperation.Add:
			case M68kMachineOperation.Subtract:
			case M68kMachineOperation.And:
			case M68kMachineOperation.Or:
			case M68kMachineOperation.Xor:
				if (allocated.Function.Values[instruction.Definitions[0]].Kind is
					CilStackValueKind.Float32 or CilStackValueKind.Float64)
				{
					EmitAllocatedFloatingBinary(
						instruction.Operation,
						Location(instruction.Uses[0]),
						Location(instruction.Uses[1]),
						Location(instruction.Definitions[0]),
						allocated.Function.Values[instruction.Definitions[0]].Kind);
					return;
				}
				EmitAllocatedBinary(
					instruction.Operation,
					Location(instruction.Uses[0]).Register,
					Location(instruction.Uses[1]).Register,
					Location(instruction.Definitions[0]).Register,
					allocated.Function.Values[instruction.Definitions[0]].Width);
				EmitAllocatedNormalize(
					Location(instruction.Definitions[0]).Register,
					allocated.Function.Values[instruction.Definitions[0]].Kind);
				return;

			case M68kMachineOperation.Multiply:
				if (allocated.Function.Values[instruction.Definitions[0]].Kind is
					CilStackValueKind.Float32 or CilStackValueKind.Float64)
				{
					EmitAllocatedFloatingBinary(
						instruction.Operation,
						Location(instruction.Uses[0]),
						Location(instruction.Uses[1]),
						Location(instruction.Definitions[0]),
						allocated.Function.Values[instruction.Definitions[0]].Kind);
					return;
				}
				EmitMultiply(allocated.Function.Values[instruction.Definitions[0]].Kind);
				EmitAllocatedNormalize(
					M68kRegister.D0,
					allocated.Function.Values[instruction.Definitions[0]].Kind);
				return;

			case M68kMachineOperation.Divide:
			case M68kMachineOperation.Remainder:
				if (allocated.Function.Values[instruction.Definitions[0]].Kind is
					CilStackValueKind.Float32 or CilStackValueKind.Float64)
				{
					if (instruction.Operation == M68kMachineOperation.Remainder)
					{
						throw new M68kCompilationException(
							M68kDiagnosticIds.UnsupportedInstruction,
							"Floating-point remainder requires the CopperFloat runtime helper.",
							method.DisplayName,
							instruction.IlOffset);
					}
					EmitAllocatedFloatingBinary(
						instruction.Operation,
						Location(instruction.Uses[0]),
						Location(instruction.Uses[1]),
						Location(instruction.Definitions[0]),
						allocated.Function.Values[instruction.Definitions[0]].Kind);
					return;
				}
				EmitDivide(
					instruction.SourceInstruction!.OpCode != OpCodes.Div_Un &&
						instruction.SourceInstruction.OpCode != OpCodes.Rem_Un,
					instruction.Operation == M68kMachineOperation.Remainder);
				return;

			case M68kMachineOperation.Shift:
				EmitShift(
					instruction.SourceInstruction!.OpCode,
					allocated.Function.Values[instruction.Definitions[0]].Kind,
					instruction.Immediate);
				EmitAllocatedNormalize(
					M68kRegister.D0,
					allocated.Function.Values[instruction.Definitions[0]].Kind);
				return;

			case M68kMachineOperation.Negate:
			case M68kMachineOperation.Not:
				if (allocated.Function.Values[instruction.Definitions[0]].Kind is
					CilStackValueKind.Float32 or CilStackValueKind.Float64)
				{
					EmitAllocatedFloatingUnary(
						Location(instruction.Uses[0]),
						Location(instruction.Definitions[0]),
						allocated.Function.Values[instruction.Definitions[0]].Kind,
						M68kFpuOperation.Negate);
					return;
				}
				EmitAllocatedUnary(
					instruction.Operation,
					Location(instruction.Uses[0]).Register,
					Location(instruction.Definitions[0]).Register,
					allocated.Function.Values[instruction.Definitions[0]].Width);
				EmitAllocatedNormalize(
					Location(instruction.Definitions[0]).Register,
					allocated.Function.Values[instruction.Definitions[0]].Kind);
				return;

			case M68kMachineOperation.Convert:
				EmitAllocatedConversion(
					instruction.SourceInstruction!.OpCode,
					Location(instruction.Uses[0]).Register,
					Location(instruction.Definitions[0]).Register);
				return;

			case M68kMachineOperation.Compare:
				EmitAllocatedCompare(
					Location(instruction.Uses[0]).Register,
					Location(instruction.Uses[1]).Register,
					allocated.Function.Values[instruction.Uses[0]].Width);
				if (instruction.Definitions.Length != 0)
				{
					var destination = Location(instruction.Definitions[0]).Register;
					EmitAllocatedConditionResult(
						ComparisonCondition(instruction.SourceInstruction!.OpCode),
						destination);
				}
				return;

			case M68kMachineOperation.Call:
				if (!TryEmitAllocatedTailCall(
						method,
						allocated,
						block,
						instruction))
				{
					EmitAllocatedCall(method, allocated, instruction);
				}
				return;

			case M68kMachineOperation.Branch:
				if (instruction.SourceInstruction is { } branchSource &&
					branchSource.OpCode is var branchOp &&
					(branchOp == OpCodes.Leave ||
					 branchOp == OpCodes.Leave_S) &&
					branchSource.Operand is int leaveTarget &&
					TryEmitNormalLeave(
						method,
						instruction.IlOffset,
						leaveTarget))
				{
					return;
				}
				var originalSuccessor = block.Successors[0];
				var successor = allocated.FinalDestinations.Resolve(originalSuccessor);
				EmitAllocatedEdgeCopies(allocated, block.Id, originalSuccessor);
				if (nextBlockId != successor)
				{
					_assembler.EmitBranch(
						M68kCondition.True,
						AllocatedBlockLabel(method, successor));
				}
				return;

			case M68kMachineOperation.ConditionalBranch:
				EmitAllocatedConditionalBranch(
					method,
					allocated,
					block,
					instruction,
					nextBlockId);
				return;

			case M68kMachineOperation.Return:
				if (instruction.Uses.Length != 0)
				{
					var source = Location(instruction.Uses[0]).Register;
					var destination = IsInternalAddressReturn(
						method.Signature.ReturnType)
							? M68kRegister.A0
							: M68kRegister.D0;
					EmitAllocatedMove(
						source,
						destination,
						allocated.Function.Values[instruction.Uses[0]].Width);
					EmitAllocatedNormalize(
						destination,
						allocated.Function.Values[instruction.Uses[0]].Kind);
				}
				EmitAllocatedFrameTeardown(method, allocated);
				_assembler.EmitWord(0x4E75); // RTS
				return;

			case M68kMachineOperation.Throw:
				if (instruction.SourceInstruction?.OpCode == OpCodes.Rethrow)
				{
					EmitAllocatedFrameLoad(
						M68kRegister.A0,
						M68kMachineValueWidth.Long,
						AllocatedFrameOffset(
							allocated,
							allocated.Frame.ActiveExceptionOffset ??
								throw new InvalidOperationException("Rethrow has no exception slot.")));
					EmitExceptionRaise(reason: 0, hasException: true);
					return;
				}
				EmitAllocatedMove(
					Location(instruction.Uses[0]).Register,
					M68kRegister.A0,
					M68kMachineValueWidth.Long);
				EmitExceptionRaise(reason: 0, hasException: true);
				return;

			default:
				throw new InvalidOperationException(
					$"Allocated emitter accepted unsupported operation {instruction.Operation}.");
		}
	}

	private void EmitAllocatedInstanceFieldLoad(
		CilMethod method,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		var source = instruction.SourceInstruction!;
		var field = _module.ResolveFieldToken(
			(int)source.Operand!,
			method,
			source.Offset);
		if (field.IsStatic)
		{
			throw new InvalidOperationException(
				"Allocated instance load resolved a static field.");
		}
		ValidateType(field.Type, method, "field");
		var objectRegister =
			allocated.Allocation.Registers[instruction.Uses[0]].Register;
		var displacement = _module.IsTransparentScalarField(field)
			? (short)0
			: FieldDisplacement(field);
		if (!_module.IsTransparentScalarField(field))
		{
			EmitAllocatedRequireNonNull(objectRegister);
		}
		var destination =
			allocated.Allocation.Registers[instruction.Definitions[0]].Register;
		if (source.OpCode == OpCodes.Ldflda)
		{
			EmitAllocatedBaseAddress(objectRegister, destination, displacement);
			return;
		}
		EmitAllocatedBaseLoad(
			objectRegister,
			destination,
			M68kMachineValueWidth.Long,
			displacement);
	}

	private void EmitAllocatedObjectAllocation(
		CilMethod method,
		M68kMachineInstruction instruction)
	{
		EnsureManagedAllocationAllowed(
			method,
			instruction.SourceInstruction!,
			"object construction");
		var constructor = _module.ResolveMethodToken(
			(int)instruction.SourceInstruction!.Operand!,
			method,
			instruction.IlOffset).Definition ??
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"Could not resolve allocated object constructor.",
				method.DisplayName,
				instruction.IlOffset);
		var layout = _module.GetTypeLayout(constructor);
		_usedTypeLayouts.TryAdd(layout.Identity, layout);
		EmitAllocatedImmediate(layout.Size, M68kRegister.D0);
		EmitManagedAllocationFromD0(layout.Size);
		_assembler.EmitWord(0x2040); // MOVEA.L D0,A0
		_assembler.EmitWord(0x20BC); // MOVE.L #descriptor,(A0)
		_assembler.EmitAddress(TypeDescriptorLabel(layout));
		EmitAllocatedImmediate(layout.Size, M68kRegister.D1);
		EmitAllocatedBaseStore(
			M68kRegister.D1,
			M68kRegister.A0,
			M68kMachineValueWidth.Long,
			4);
	}

	private void EmitAllocatedArrayAllocation(
		CilMethod method,
		M68kMachineInstruction instruction)
	{
		EnsureManagedAllocationAllowed(
			method,
			instruction.SourceInstruction!,
			"array allocation");
		var elementType = _module.ResolveTypeToken(
			(int)instruction.SourceInstruction!.Operand!,
			method,
			instruction.IlOffset);
		if (elementType.Size is not (1 or 2 or 4) ||
			(!elementType.IsSupportedScalar && !elementType.IsReference))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				$"Arrays of '{elementType.DisplayName}' are not implemented; " +
				"array elements must occupy one, two, or four bytes.",
				method.DisplayName,
				instruction.IlOffset);
		}
		_arrayTypes.TryAdd(elementType.DisplayName, elementType);
		var lengthValid = UniqueLabel("allocated_array_length_valid");
		EmitAllocatedTest(M68kRegister.D2, M68kMachineValueWidth.Long);
		_assembler.EmitBranch(M68kCondition.Plus, lengthValid);
		EmitExceptionRaise(reason: 4, hasException: false);
		_assembler.Mark(lengthValid);
		EmitAllocatedMove(
			M68kRegister.D2,
			M68kRegister.D0,
			M68kMachineValueWidth.Long);
		EmitScaleD0(elementType.Size);
		EmitAllocatedAddImmediate(M68kRegister.D0, 12);
		EmitManagedAllocationFromD0();
		_assembler.EmitWord(0x2040); // MOVEA.L D0,A0
		_assembler.EmitWord(0x20BC); // MOVE.L #descriptor,(A0)
		_assembler.EmitAddress(ArrayDescriptorLabel(elementType));
		EmitAllocatedMove(
			M68kRegister.D2,
			M68kRegister.D1,
			M68kMachineValueWidth.Long);
		EmitScaleD1(elementType.Size);
		EmitAllocatedAddImmediate(M68kRegister.D1, 12);
		EmitAllocatedBaseStore(
			M68kRegister.D1,
			M68kRegister.A0,
			M68kMachineValueWidth.Long,
			4);
		EmitAllocatedBaseStore(
			M68kRegister.D2,
			M68kRegister.A0,
			M68kMachineValueWidth.Long,
			8);
		EmitAllocatedMove(
			M68kRegister.A0,
			M68kRegister.D0,
			M68kMachineValueWidth.Long);
	}

	private void EmitAllocatedArrayAccess(
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		var access = GetArrayAccess(instruction.SourceInstruction!.OpCode);
		var baseRegister = allocated.Allocation.Registers[
			instruction.Uses[0]].Register;
		var indexRegister = allocated.Allocation.Registers[
			instruction.Uses[1]].Register;
		EmitAllocatedArrayBoundsCheck(baseRegister, indexRegister);
		if (_request.Cpu == M68kCpuTarget.M68000)
		{
			for (var shift = access.Size; shift > 1; shift >>= 1)
			{
				EmitAllocatedShiftImmediate(indexRegister, left: true);
			}
		}
		if (instruction.Operation == M68kMachineOperation.ArrayAddress)
		{
			EmitAllocatedIndexedAddress(
				baseRegister,
				indexRegister,
				allocated.Allocation.Registers[
					instruction.Definitions.Single()].Register,
				access.Size,
				12);
			return;
		}
		var width = access.Size switch
		{
			1 => M68kMachineValueWidth.Byte,
			2 => M68kMachineValueWidth.Word,
			_ => M68kMachineValueWidth.Long
		};
		if (instruction.Operation == M68kMachineOperation.ArrayStore)
		{
			EmitAllocatedIndexedStore(
				allocated.Allocation.Registers[instruction.Uses[2]].Register,
				baseRegister,
				indexRegister,
				width,
				access.Size,
				12);
			return;
		}
		var destination = allocated.Allocation.Registers[
			instruction.Definitions.Single()].Register;
		EmitAllocatedIndexedLoad(
			baseRegister,
			indexRegister,
			destination,
			width,
			access.Size,
			12);
		if (access.Size == 1)
		{
			if (access.SignExtend)
			{
				EmitAllocatedSignExtendByte(destination);
			}
			else
			{
				_assembler.EmitWord((ushort)(0x0280 | (int)destination));
				_assembler.EmitLong(0xFF);
			}
		}
		else if (access.Size == 2)
		{
			if (access.SignExtend)
			{
				_assembler.EmitWord((ushort)(0x48C0 | (int)destination));
			}
			else
			{
				_assembler.EmitWord((ushort)(0x0280 | (int)destination));
				_assembler.EmitLong(0xFFFF);
			}
		}
	}

	private void EmitAllocatedArrayLength(
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		var source = allocated.Allocation.Registers[
			instruction.Uses.Single()].Register;
		var destination = allocated.Allocation.Registers[
			instruction.Definitions.Single()].Register;
		EmitAllocatedRequireNonNull(source);
		EmitAllocatedBaseLoad(
			source,
			destination,
			M68kMachineValueWidth.Long,
			8);
	}

	private void EmitAllocatedInitObject(
		CilMethod method,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		var type = _module.ResolveTypeToken(
			(int)instruction.SourceInstruction!.Operand!,
			method,
			instruction.IlOffset);
		var valueType = type.Kind == CilTypeKind.ManagedReference
			? type.ElementType ?? new CilType(
				CilTypeKind.ValueType,
				0,
				type.DisplayName)
			: type;
		if (valueType is null ||
			(!valueType.IsNullable ||
			 !_module.IsSupportedNullableType(valueType)) &&
			!_module.IsSupportedStructType(valueType))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				$"initobj is not supported for '{type.DisplayName}'.",
				method.DisplayName,
				instruction.IlOffset);
		}
		var baseRegister = allocated.Allocation.Registers[
			instruction.Uses.Single()].Register;
		if (_module.IsUninitializedStorageType(valueType))
		{
			return;
		}
		if (!UseClr)
		{
			EmitAllocatedImmediate(0, M68kRegister.D0);
		}
		var longs = _module.IsSupportedStructType(valueType)
			? SlotLongs(valueType)
			: IsCompactNullableType(valueType)
				? 1
				: 2;
		for (var index = 0; index < longs; index++)
		{
			if (UseClr)
			{
				EmitAllocatedBaseClearLong(baseRegister, checked((short)(index * 4)));
			}
			else
			{
				EmitAllocatedBaseStore(
					M68kRegister.D0,
					baseRegister,
					M68kMachineValueWidth.Long,
					checked((short)(index * 4)));
			}
		}
	}

	private void EmitAllocatedInstanceFieldStore(
		CilMethod method,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		var source = instruction.SourceInstruction!;
		var field = _module.ResolveFieldToken(
			(int)source.Operand!,
			method,
			source.Offset);
		if (field.IsStatic)
		{
			throw new InvalidOperationException(
				"Allocated instance store resolved a static field.");
		}
		ValidateType(field.Type, method, "field");
		var objectRegister =
			allocated.Allocation.Registers[instruction.Uses[0]].Register;
		var valueRegister =
			allocated.Allocation.Registers[instruction.Uses[1]].Register;
		var displacement = _module.IsTransparentScalarField(field)
			? (short)0
			: FieldDisplacement(field);
		if (!_module.IsTransparentScalarField(field))
		{
			EmitAllocatedRequireNonNull(objectRegister);
		}
		if (TryGetAllocatedConstant(
			allocated.Function,
			instruction.Uses[1],
			out var constant) &&
			constant == 0 &&
			UseClr)
		{
			// On MC68020+ CLR writes without the MC68000 read cycle.  On MC68000
			// the allocated zero value is already in valueRegister, so MOVE is
			// four cycles faster and also avoids the read-before-write bus access.
			EmitAllocatedBaseClearLong(objectRegister, displacement);
			return;
		}
		EmitAllocatedBaseStore(
			valueRegister,
			objectRegister,
			M68kMachineValueWidth.Long,
			displacement);
	}

	private void EmitAllocatedRequireNonNull(M68kRegister register)
	{
		var valid = UniqueLabel("allocated_nonnull");
		EmitAllocatedTest(register, M68kMachineValueWidth.Long);
		_assembler.EmitBranch(M68kCondition.NotEqual, valid);
		EmitExceptionRaise(reason: 1, hasException: false);
		_assembler.Mark(valid);
	}

	private void EmitAllocatedBaseLoad(
		M68kRegister baseRegister,
		M68kRegister destination,
		M68kMachineValueWidth width,
		short displacement)
	{
		if (baseRegister < M68kRegister.A0)
		{
			throw new InvalidOperationException(
				"Allocated memory load requires an address base.");
		}
		var baseIndex = (int)baseRegister - (int)M68kRegister.A0;
		var opcode = width switch
		{
			M68kMachineValueWidth.Byte => 0x1028,
			M68kMachineValueWidth.Word => 0x3028,
			M68kMachineValueWidth.Long => 0x2028,
			_ => throw new InvalidOperationException(
				"Pair memory loads must be expanded.")
		};
		var destinationEa = destination <= M68kRegister.D7
			? (int)destination << 9
			: (((int)destination - (int)M68kRegister.A0) << 9) | 0x40;
		_assembler.EmitWord((ushort)(
			opcode |
			destinationEa |
			baseIndex));
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitAllocatedBaseStore(
		M68kRegister source,
		M68kRegister baseRegister,
		M68kMachineValueWidth width,
		short displacement)
	{
		if (baseRegister < M68kRegister.A0)
		{
			throw new InvalidOperationException(
				"Allocated memory store requires an address base.");
		}
		var baseIndex = (int)baseRegister - (int)M68kRegister.A0;
		var opcode = width switch
		{
			M68kMachineValueWidth.Byte => 0x1140,
			M68kMachineValueWidth.Word => 0x3140,
			M68kMachineValueWidth.Long => 0x2140,
			_ => throw new InvalidOperationException(
				"Pair memory stores must be expanded.")
		};
		_assembler.EmitWord((ushort)(
			opcode |
			(baseIndex << 9) |
			AllocatedRegisterEa(source)));
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitAllocatedBaseAddress(
		M68kRegister baseRegister,
		M68kRegister destination,
		short displacement)
	{
		if (baseRegister < M68kRegister.A0 ||
			destination < M68kRegister.A0)
		{
			throw new InvalidOperationException(
				"Allocated LEA requires address registers.");
		}
		var baseIndex = (int)baseRegister - (int)M68kRegister.A0;
		var destinationIndex = (int)destination - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(
			0x41E8 |
			(destinationIndex << 9) |
			baseIndex));
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitAllocatedBaseClearLong(
		M68kRegister baseRegister,
		short displacement)
	{
		if (baseRegister < M68kRegister.A0)
		{
			throw new InvalidOperationException(
				"Allocated clear requires an address base.");
		}
		if (displacement == 0)
		{
			_assembler.EmitWord((ushort)(
				0x42A8 |
				((int)baseRegister - (int)M68kRegister.A0)));
			_assembler.EmitWord(0);
			return;
		}
		_assembler.EmitWord((ushort)(
			0x42A8 |
			((int)baseRegister - (int)M68kRegister.A0)));
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitAllocatedArrayBoundsCheck(
		M68kRegister baseRegister,
		M68kRegister indexRegister)
	{
		if (baseRegister < M68kRegister.A0 ||
			indexRegister > M68kRegister.D7)
		{
			throw new InvalidOperationException(
				"Allocated array access has invalid base or index registers.");
		}
		EmitAllocatedRequireNonNull(baseRegister);
		var indexNonNegative = UniqueLabel("allocated_array_index_nonnegative");
		var indexValid = UniqueLabel("allocated_array_index_valid");
		_assembler.EmitWord((ushort)(0x4A80 | (int)indexRegister)); // TST.L Dindex
		_assembler.EmitBranch(M68kCondition.Plus, indexNonNegative);
		EmitExceptionRaise(reason: 2, hasException: false);
		_assembler.Mark(indexNonNegative);
		_assembler.EmitWord((ushort)(
			0xB0A8 |
			((int)indexRegister << 9) |
			((int)baseRegister - (int)M68kRegister.A0))); // CMP.L 8(Abase),Dindex
		_assembler.EmitWord(0x0008);
		_assembler.EmitBranch(M68kCondition.CarrySet, indexValid);
		EmitExceptionRaise(reason: 2, hasException: false);
		_assembler.Mark(indexValid);
	}

	private void EmitAllocatedIndexedLoad(
		M68kRegister baseRegister,
		M68kRegister indexRegister,
		M68kRegister destination,
		M68kMachineValueWidth width,
		int elementSize,
		sbyte displacement)
	{
		var opcode = width switch
		{
			M68kMachineValueWidth.Byte => 0x1000,
			M68kMachineValueWidth.Word => 0x3000,
			M68kMachineValueWidth.Long => 0x2000,
			_ => throw new InvalidOperationException(
				"Pair indexed loads must be expanded.")
		};
		var destinationEa = destination <= M68kRegister.D7
			? (int)destination << 9
			: (((int)destination - (int)M68kRegister.A0) << 9) | 0x40;
		_assembler.EmitWord((ushort)(
			opcode |
			destinationEa |
			0x30 |
			((int)baseRegister - (int)M68kRegister.A0)));
		_assembler.EmitWord(AllocatedIndexExtension(
			indexRegister,
			elementSize,
			displacement));
	}

	private void EmitAllocatedIndexedStore(
		M68kRegister source,
		M68kRegister baseRegister,
		M68kRegister indexRegister,
		M68kMachineValueWidth width,
		int elementSize,
		sbyte displacement)
	{
		var opcode = width switch
		{
			M68kMachineValueWidth.Byte => 0x1000,
			M68kMachineValueWidth.Word => 0x3000,
			M68kMachineValueWidth.Long => 0x2000,
			_ => throw new InvalidOperationException(
				"Pair indexed stores must be expanded.")
		};
		_assembler.EmitWord((ushort)(
			opcode |
			(((int)baseRegister - (int)M68kRegister.A0) << 9) |
			0x0180 |
			AllocatedRegisterEa(source)));
		_assembler.EmitWord(AllocatedIndexExtension(
			indexRegister,
			elementSize,
			displacement));
	}

	private void EmitAllocatedIndexedAddress(
		M68kRegister baseRegister,
		M68kRegister indexRegister,
		M68kRegister destination,
		int elementSize,
		sbyte displacement)
	{
		if (destination < M68kRegister.A0)
		{
			throw new InvalidOperationException(
				"Indexed LEA requires an address destination.");
		}
		_assembler.EmitWord((ushort)(
			0x41F0 |
			(((int)destination - (int)M68kRegister.A0) << 9) |
			((int)baseRegister - (int)M68kRegister.A0)));
		_assembler.EmitWord(AllocatedIndexExtension(
			indexRegister,
			elementSize,
			displacement));
	}

	private ushort AllocatedIndexExtension(
		M68kRegister indexRegister,
		int elementSize,
		sbyte displacement)
	{
		if (indexRegister > M68kRegister.D7)
		{
			throw new InvalidOperationException(
				"Allocated indexed access requires a data index.");
		}
		var scaleBits = _request.Cpu == M68kCpuTarget.M68000
			? 0
			: elementSize switch
			{
				1 => 0,
				2 => 1,
				4 => 2,
				_ => throw new InvalidOperationException(
					$"Unsupported indexed element size {elementSize}.")
			};
		return (ushort)(
			((int)indexRegister << 12) |
			0x0800 |
			(scaleBits << 9) |
			(byte)displacement);
	}

	private static M68kMachineValueWidth AllocatedIndirectWidth(OpCode op) =>
		op == OpCodes.Ldind_I1 ||
		op == OpCodes.Ldind_U1 ||
		op == OpCodes.Stind_I1
			? M68kMachineValueWidth.Byte
			: op == OpCodes.Ldind_I2 ||
				op == OpCodes.Ldind_U2 ||
				op == OpCodes.Stind_I2
				? M68kMachineValueWidth.Word
				: M68kMachineValueWidth.Long;

	private int AllocatedFrameOffset(
		M68kAllocatedFunction allocated,
		int offset) =>
		checked(_allocatedOutgoingStackBytes + offset);

	private void EmitAllocatedFrameHomeInitialization(
		CilMethod method,
		InternalCallAbi abi,
		M68kAllocatedFunction allocated,
		int savedBytes)
	{
		if (method.InitializeLocals)
		{
			var clearDisplacements = allocated.Function.LocalHomes.Values
				.OrderBy(static home => home.Index)
				.SelectMany(home => Enumerable.Range(0, home.Size / 4)
					.Select(index => AllocatedFrameOffset(
						allocated,
						checked(allocated.Frame.LocalOffsets[home.Index] + (index * 4)))))
				.ToArray();
			if (_request.Cpu == M68kCpuTarget.M68000 &&
				_request.ClrPolicy != M68kClrPolicy.Always &&
				clearDisplacements.Length >= 2 &&
				TrySelectAllocatedFrameZeroRegister(abi, allocated, out var zeroRegister))
			{
				_assembler.EmitWord((ushort)(0x7000 | ((int)zeroRegister << 9)));
				foreach (var displacement in clearDisplacements)
				{
					EmitAllocatedFrameStore(
						zeroRegister,
						M68kMachineValueWidth.Long,
						displacement);
				}
			}
			else
			{
				foreach (var displacement in clearDisplacements)
				{
					EmitAllocatedFrameClear(displacement);
				}
			}
		}
		foreach (var home in allocated.Function.ArgumentHomes.Values
			.OrderBy(static home => home.Index))
		{
			if (home.Size != 4)
			{
				throw new InvalidOperationException(
					$"Allocated argument home {home.Index} has unsupported size {home.Size}.");
			}
			var source = abi.Arguments[home.Index];
			var register = source.Register ?? M68kRegister.D7;
			if (source.Register is null)
			{
				EmitAllocatedStackLoad(
					register,
					checked(savedBytes + 4 + source.StackOffset));
			}
			EmitAllocatedFrameStore(
				register,
				M68kMachineValueWidth.Long,
				AllocatedFrameOffset(
					allocated,
					allocated.Frame.ArgumentHomeOffsets[home.Index]));
		}
	}

	private static bool TrySelectAllocatedFrameZeroRegister(
		InternalCallAbi abi,
		M68kAllocatedFunction allocated,
		out M68kRegister register)
	{
		var incoming = abi.Arguments
			.Where(static argument => argument.Register is <= M68kRegister.D7)
			.Select(static argument => argument.Register!.Value)
			.ToHashSet();
		for (register = M68kRegister.D0; register <= M68kRegister.D7; register++)
		{
			if (!incoming.Contains(register) &&
				(register <= M68kRegister.D1 ||
				 allocated.Frame.CalleeSavedRegisters.Contains(register)))
			{
				return true;
			}
		}

		register = default;
		return false;
	}

	private void EmitAllocatedPush(M68kRegister register)
	{
		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(0x2F00 | (int)register));
			return;
		}
		_assembler.EmitWord((ushort)(
			0x2F08 |
			((int)register - (int)M68kRegister.A0)));
	}

	private void EmitAllocatedFrameTeardown(
		CilMethod method,
		M68kAllocatedFunction allocated)
	{
		EmitAllocatedCalleeRestores(
			allocated.Frame.CalleeSavedRegisters,
			allocated.Frame.FrameBytes);
	}

	private bool TryEmitAllocatedTailCall(
		CilMethod caller,
		M68kAllocatedFunction allocated,
		M68kMachineBlock block,
		M68kMachineInstruction call)
	{
		var callIndex = block.Instructions.IndexOf(call);
		if (callIndex < 0 ||
			block.Successors.Count != 0 ||
			callIndex + 1 >= block.Instructions.Count)
		{
			return false;
		}
		var trailing = block.Instructions.Skip(callIndex + 1).ToArray();
		if (trailing[^1].Operation != M68kMachineOperation.Return ||
			trailing[..^1].Any(static instruction =>
				instruction.Operation != M68kMachineOperation.Copy))
		{
			return false;
		}
		var source = call.SourceInstruction!;
		if (source.OpCode != OpCodes.Call)
		{
			return false;
		}
		var target = _module.ResolveMethodToken(
			(int)source.Operand!,
			caller,
			source.Offset);
		if (target.Definition is not { IsImport: false, ExternalCall: null } callee ||
			GetActiveExceptionGroups(caller, call.IlOffset).Length != 0 ||
			callee.Signature.ReturnType.IsVoid !=
				caller.Signature.ReturnType.IsVoid ||
			(!caller.Signature.ReturnType.IsVoid &&
			 IsInternalAddressReturn(caller.Signature.ReturnType) !=
			 IsInternalAddressReturn(callee.Signature.ReturnType)))
		{
			return false;
		}

		foreach (var instruction in trailing)
		{
			_allocatedSuppressedInstructions.Add(instruction.Id);
		}
		EmitAllocatedCalleeRestores(
			allocated.Frame.CalleeSavedRegisters,
			allocated.Frame.FrameBytes);
		_assembler.EmitJmp(MethodLabel(callee), external: false);
		_loadedPlatformBase = null;
		return true;
	}

	private void EmitAllocatedStringAddress(
		CilMethod method,
		M68kMachineInstruction instruction,
		M68kRegister destination)
	{
		var source = instruction.SourceInstruction!;
		if (source.OpCode != OpCodes.Ldstr)
		{
			throw new InvalidOperationException(
				$"Unsupported allocated address source {source.OpCode.Name}.");
		}
		var token = (int)source.Operand!;
		var identity = new CilUserStringIdentity(method.ModuleName, token);
		_stringLiterals.TryAdd(
			identity,
			_module.GetUserString(token, method, source.Offset));
		var destinationEa = destination <= M68kRegister.D7
			? (int)destination << 9
			: (((int)destination - (int)M68kRegister.A0) << 9) | 0x40;
		_assembler.EmitWord((ushort)(0x203C | destinationEa));
		_assembler.EmitAddress(StringLabel(identity));
	}

	private void EmitAllocatedStaticLoad(
		CilMethod method,
		M68kMachineInstruction instruction,
		M68kRegister destination)
	{
		var source = instruction.SourceInstruction!;
		var field = _module.ResolveFieldToken(
			(int)source.Operand!,
			method,
			source.Offset);
		if (!field.IsStatic)
		{
			throw new InvalidOperationException(
				"Allocated static load resolved an instance field.");
		}
		ValidateType(field.Type, method, "field");
		_staticFields.TryAdd(field.Identity, field);
		var label = StaticFieldLabel(field);
		if (source.OpCode == OpCodes.Ldsflda)
		{
			var destinationEa = destination <= M68kRegister.D7
				? (int)destination << 9
				: (((int)destination - (int)M68kRegister.A0) << 9) | 0x40;
			_assembler.EmitWord((ushort)(0x203C | destinationEa));
			_assembler.EmitAddress(label);
			return;
		}
		var loadDestination = destination <= M68kRegister.D7
			? (int)destination << 9
			: (((int)destination - (int)M68kRegister.A0) << 9) | 0x40;
		_assembler.EmitWord((ushort)(0x2039 | loadDestination));
		_assembler.EmitAddress(label);
	}

	private void EmitAllocatedStaticStore(
		CilMethod method,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction,
		M68kRegister sourceRegister)
	{
		var source = instruction.SourceInstruction!;
		var field = _module.ResolveFieldToken(
			(int)source.Operand!,
			method,
			source.Offset);
		if (!field.IsStatic)
		{
			throw new InvalidOperationException(
				"Allocated static store resolved an instance field.");
		}
		ValidateType(field.Type, method, "field");
		_staticFields.TryAdd(field.Identity, field);
		if (TryGetAllocatedConstant(
			allocated.Function,
			instruction.Uses[0],
			out var constant) &&
			constant == 0 &&
			UseClr)
		{
			EmitClearLabel(StaticFieldLabel(field));
			return;
		}
		_assembler.EmitWord((ushort)(
			0x23C0 |
			AllocatedRegisterEa(sourceRegister)));
		_assembler.EmitAddress(StaticFieldLabel(field));
	}

	private void EmitAllocatedCall(
		CilMethod caller,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		var source = instruction.SourceInstruction!;
		var target = _module.ResolveMethodToken(
			(int)source.Operand!,
			caller,
			source.Offset);
		if (instruction.StackVarargsRegister is { } varargsRegister)
		{
			EmitAllocatedStackPointerToAddressRegister(varargsRegister);
		}
		if (target.Definition is null)
		{
			EmitAllocatedIntrinsic(
				caller,
				target,
				allocated,
				instruction);
			return;
		}
		var definition = target.Definition;
		if (source.OpCode == OpCodes.Callvirt)
		{
			EmitAllocatedRequireNonNull(M68kRegister.A0);
		}
		if (definition.DeclaringTypeIsInterface)
		{
			EmitAllocatedInterfaceCall(definition);
			return;
		}
		if (RequiresVirtualDispatch(source, definition))
		{
			EmitAllocatedVirtualCall(definition);
			return;
		}
		if (IsAlwaysInlinedMethod(definition))
		{
			if (instruction.Definitions.Length != 0 &&
				instruction.Uses.Length != 0)
			{
				EmitAllocatedMove(
					allocated.Allocation.Registers[
						instruction.Uses[0]].Register,
					allocated.Allocation.Registers[
						instruction.Definitions[0]].Register,
					allocated.Function.Values[
						instruction.Definitions[0]].Width);
			}
			return;
		}
		if (definition.ExternalCall is { } externalCall)
		{
			EmitEnsurePlatformBase(externalCall.Convention, definition);
			EmitBaseRelativeJsr(
				externalCall.Convention.BaseRegister,
				externalCall.Convention.Displacement);
			EmitExternalExceptionStatusCheck(externalCall.Convention);
			return;
		}
		ValidateMethodSignature(definition, isEntry: false);
		if (definition.IsImport)
		{
			_assembler.EmitJsr(definition.ImportName!, external: true);
			return;
		}
		_assembler.EmitBsr(MethodLabel(definition));
		RegisterCurrentUnwindSite(instruction.MayThrow, instruction.IsSafepoint);
		_loadedPlatformBase = null;
	}

	private void EmitAllocatedVirtualCall(CilMethod declaration)
	{
		var slot = _module.GetVirtualSlot(declaration);
		if (slot > short.MaxValue / 4)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				$"Virtual slot {slot} exceeds the indexed vtable displacement range.",
				declaration.DisplayName);
		}
		_assembler.EmitWord(0x2450); // MOVEA.L (A0),A2 descriptor
		_assembler.EmitWord(0x246A); // MOVEA.L 12(A2),A2 vtable
		_assembler.EmitWord(0x000C);
		if (slot == 0)
		{
			_assembler.EmitWord(0x2452); // MOVEA.L (A2),A2 target
		}
		else
		{
			_assembler.EmitWord(0x246A); // MOVEA.L d16(A2),A2 target
			_assembler.EmitWord(checked((ushort)(slot * 4)));
		}
		_assembler.EmitWord(0x4E92); // JSR (A2)
		RegisterCurrentUnwindSite(exception: true, gc: _emittingMachineInstruction?.IsSafepoint == true);
		_loadedPlatformBase = null;
	}

	private void EmitAllocatedInterfaceCall(CilMethod declaration)
	{
		var interfaceDefinition = _module.GetInterfaceDefinition(declaration);
		_usedInterfaces.TryAdd(interfaceDefinition.Identity, interfaceDefinition);
		var slot = _module.GetInterfaceSlot(declaration);
		if (slot > short.MaxValue / 4)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				$"Interface slot {slot} exceeds the indexed method-table displacement range.",
				declaration.DisplayName);
		}
		_assembler.EmitWord(0x2450); // MOVEA.L (A0),A2 descriptor
		_assembler.EmitWord(0x246A); // MOVEA.L 16(A2),A2 interface map
		_assembler.EmitWord(0x0010);
		_assembler.EmitWord(0x241A); // MOVE.L (A2)+,D2 entry count
		EmitAddressImmediateToRegister(
			M68kRegister.A3,
			InterfaceIdentityLabel(interfaceDefinition));

		var loop = UniqueLabel("allocated_interface_lookup");
		var found = UniqueLabel("allocated_interface_found");
		_assembler.Mark(loop);
		_assembler.EmitWord(0xB7DA); // CMPA.L (A2)+,A3 interface identity
		_assembler.EmitBranch(M68kCondition.Equal, found);
		_assembler.EmitWord(0x588A); // ADDQ.L #4,A2 skip method-table pointer
		_assembler.EmitWord(0x5382); // SUBQ.L #1,D2
		_assembler.EmitBranch(M68kCondition.NotEqual, loop);
		_assembler.EmitWord(0x4AFC); // ILLEGAL: invalid object/interface pairing

		_assembler.Mark(found);
		_assembler.EmitWord(0x2452); // MOVEA.L (A2),A2 method table
		if (slot == 0)
		{
			_assembler.EmitWord(0x2452); // MOVEA.L (A2),A2 target
		}
		else
		{
			_assembler.EmitWord(0x246A); // MOVEA.L d16(A2),A2 target
			_assembler.EmitWord(checked((ushort)(slot * 4)));
		}
		_assembler.EmitWord(0x4E92); // JSR (A2)
		RegisterCurrentUnwindSite(exception: true, gc: _emittingMachineInstruction?.IsSafepoint == true);
		_loadedPlatformBase = null;
	}

	private void EmitAllocatedIntrinsic(
		CilMethod caller,
		MethodReference target,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		var name = target.ImportName!;
		M68kRegister Use(int index) =>
			allocated.Allocation.Registers[instruction.Uses[index]].Register;
		M68kRegister Definition() =>
			allocated.Allocation.Registers[instruction.Definitions[0]].Register;

		if (name == "intrinsic:object-ctor")
		{
			return;
		}
		if (name.StartsWith(
			"intrinsic:nullable-ctor:",
			StringComparison.Ordinal))
		{
			var nullableBase = Use(0);
			EmitAllocatedBaseStore(
				Use(1),
				nullableBase,
				M68kMachineValueWidth.Long,
				0);
			if (!IsCompactNullableIntrinsic(target))
			{
				EmitAllocatedImmediate(1, M68kRegister.D1);
				EmitAllocatedBaseStore(
					M68kRegister.D1,
					nullableBase,
					M68kMachineValueWidth.Long,
					4);
			}
			return;
		}
		if (name.StartsWith(
			"intrinsic:nullable-get-value-or-default:",
			StringComparison.Ordinal))
		{
			var nullableBase = Use(0);
			EmitAllocatedMove(
				Use(1),
				M68kRegister.D1,
				M68kMachineValueWidth.Long);
			if (IsCompactNullableIntrinsic(target))
			{
				EmitAllocatedBaseLoad(
					nullableBase,
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					0);
				var doneCompact = UniqueLabel("allocated_nullable_done");
				_assembler.EmitBranch(M68kCondition.NotEqual, doneCompact);
				EmitAllocatedMove(
					M68kRegister.D1,
					M68kRegister.D0,
					M68kMachineValueWidth.Long);
				_assembler.Mark(doneCompact);
			}
			else
			{
				EmitAllocatedBaseLoad(
					nullableBase,
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					4);
				EmitAllocatedTest(M68kRegister.D0, M68kMachineValueWidth.Long);
				var useDefault = UniqueLabel("allocated_nullable_default");
				var done = UniqueLabel("allocated_nullable_done");
				_assembler.EmitBranch(M68kCondition.Equal, useDefault);
				EmitAllocatedBaseLoad(
					nullableBase,
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					0);
				_assembler.EmitBranch(M68kCondition.True, done);
				_assembler.Mark(useDefault);
				EmitAllocatedMove(
					M68kRegister.D1,
					M68kRegister.D0,
					M68kMachineValueWidth.Long);
				_assembler.Mark(done);
			}
			if (instruction.Definitions.Length != 0)
			{
				EmitAllocatedMove(
					M68kRegister.D0,
					Definition(),
					M68kMachineValueWidth.Long);
			}
			return;
		}
		if (name == "intrinsic:runtime-dispose")
		{
			EmitAllocatedMove(
				Use(0),
				M68kRegister.A0,
				M68kMachineValueWidth.Long);
			EmitRuntimeJsr(RuntimeDisposeLabel, M68kRuntimeImports.Dispose);
			_loadedPlatformBase = null;
			return;
		}
		if (name == "intrinsic:runtime-gc-collect")
		{
			EmitManagedCollectWithRoots();
			_loadedPlatformBase = null;
			return;
		}
		if (name is
			"intrinsic:runtime-GetGcStaleBytes" or
			"intrinsic:runtime-GetGcStaleBlocks")
		{
			EmitRuntimeJsr(
				name.EndsWith("Bytes", StringComparison.Ordinal)
					? RuntimeGetStaleBytesTarget
					: RuntimeGetStaleBlocksTarget,
				name.EndsWith("Bytes", StringComparison.Ordinal)
					? M68kRuntimeImports.GcGetStaleBytes
					: M68kRuntimeImports.GcGetStaleBlocks);
			if (instruction.Definitions.Length != 0)
			{
				EmitAllocatedMove(
					M68kRegister.D0,
					Definition(),
					M68kMachineValueWidth.Long);
			}
			_loadedPlatformBase = null;
			return;
		}
		if (name is
			"intrinsic:cstring-from-pointer" or
			"intrinsic:cstring-to-uint32" or
			"intrinsic:aptr-from-pointer" or
			"intrinsic:aptr-to-uint32" or
			"intrinsic:amiga-vararg-from-value" or
			"intrinsic:address-of-ref" or
			"intrinsic:address-to-ref" or
			"intrinsic:ref-cast" or
			"intrinsic:hook-address-of" or
			"intrinsic:boopsi-message-address-of")
		{
			if (instruction.Definitions.Length != 0)
			{
				EmitAllocatedMove(
					Use(0),
					Definition(),
					M68kMachineValueWidth.Long);
			}
			return;
		}
		if (name is
			"intrinsic:cstring-from-literal" or
			"intrinsic:amiga-vararg-from-literal")
		{
			var literal = AllocatedPrecedingStringLiteral(caller, instruction);
			var identity = new CilUserStringIdentity(caller.ModuleName, literal);
			_cStringLiterals.TryAdd(
				identity,
				_module.GetUserString(literal, caller, instruction.IlOffset));
			EmitAllocatedAddress(CStringLabel(identity), Definition());
			return;
		}
		if (name == "intrinsic:aptr-export-address")
		{
			var literal = AllocatedPrecedingStringLiteral(caller, instruction);
			var exportName = _module.GetUserString(
				literal,
				caller,
				instruction.IlOffset);
			if (!_module.GetExports().Any(export => export.Name == exportName))
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnresolvedImport,
					$"No [M68kExport] method named '{exportName}' exists.",
					caller.DisplayName,
					instruction.IlOffset);
			}
			EmitAllocatedAddress(ExportLabel(exportName), Definition());
			return;
		}
		if (name == "intrinsic:aptr-read-uint32")
		{
			var baseRegister = Use(0);
			var destination = Definition();
			if (TryFoldAllocatedIntrinsicResultCopy(
					allocated,
					instruction,
					out var foldedDestination))
			{
				destination = foldedDestination;
			}
			var hasConstantOffset = instruction.Immediate.HasValue;
			var constantOffset = instruction.Immediate.GetValueOrDefault();
			if (!hasConstantOffset)
			{
				hasConstantOffset = TryGetAllocatedConstant(
					allocated.Function,
					instruction.Uses[1],
					out constantOffset);
			}
			var displacement = hasConstantOffset &&
				constantOffset is >= short.MinValue and <= short.MaxValue
					? (short)constantOffset
					: (short)0;
			if (!hasConstantOffset ||
				constantOffset is < short.MinValue or > short.MaxValue)
			{
				EmitAllocatedBinaryInPlace(
					M68kMachineOperation.Add,
					Use(1),
					baseRegister,
					M68kMachineValueWidth.Long);
			}
			EmitAllocatedBaseLoad(
				baseRegister,
				destination,
				M68kMachineValueWidth.Long,
				displacement);
			return;
		}

		if (name == "intrinsic:aptr-write-uint32")
		{
			var baseRegister = Use(0);
			var hasConstantOffset = instruction.Immediate.HasValue;
			var constantOffset = instruction.Immediate.GetValueOrDefault();
			if (!hasConstantOffset)
			{
				hasConstantOffset = TryGetAllocatedConstant(
					allocated.Function,
					instruction.Uses[1],
					out constantOffset);
			}
			var displacement = hasConstantOffset &&
				constantOffset is >= short.MinValue and <= short.MaxValue
					? (short)constantOffset
					: (short)0;
			if (!hasConstantOffset ||
				constantOffset is < short.MinValue or > short.MaxValue)
			{
				EmitAllocatedBinaryInPlace(
					M68kMachineOperation.Add,
					Use(1),
					baseRegister,
					M68kMachineValueWidth.Long);
			}
			EmitAllocatedBaseStore(
				Use(instruction.Immediate is null ? 2 : 1),
				baseRegister,
				M68kMachineValueWidth.Long,
				displacement);
			return;
		}
		if (name == "intrinsic:aptr-raw")
		{
			if (TryGetAllocatedFrameBackedAddressDisplacement(
				caller,
				allocated,
				instruction,
				out var frameDisplacement))
			{
				EmitAllocatedFrameLoad(
					Definition(),
					M68kMachineValueWidth.Long,
					frameDisplacement);
				return;
			}
			if (IsAllocatedPromotedLocalAddress(caller, allocated, instruction))
			{
				EmitAllocatedMove(
					Use(0),
					Definition(),
					M68kMachineValueWidth.Long);
				return;
			}
			EmitAllocatedBaseLoad(
				Use(0),
				Definition(),
				M68kMachineValueWidth.Long,
				0);
			return;
		}
		if (name.StartsWith(
			"intrinsic:nullable-has-value:",
			StringComparison.Ordinal))
		{
			var materialize = instruction.Definitions.Length != 0;
			var destination = materialize
				? Definition()
				: M68kRegister.D0;
			if (IsCompactNullableIntrinsic(target) &&
				IsAllocatedPromotedLocalAddress(caller, allocated, instruction))
			{
				if (materialize)
				{
					EmitAllocatedMove(
						Use(0),
						destination,
						M68kMachineValueWidth.Long);
				}
				else
				{
					destination = Use(0);
				}
			}
			else
			{
				EmitAllocatedBaseLoad(
					Use(0),
					destination,
					M68kMachineValueWidth.Long,
					IsCompactNullableIntrinsic(target) ? (short)0 : (short)4);
			}
			EmitAllocatedTest(destination, M68kMachineValueWidth.Long);
			if (materialize)
			{
				EmitAllocatedConditionResult(
					M68kCondition.NotEqual,
					destination);
			}
			return;
		}
		if (name.StartsWith(
			"intrinsic:nullable-get-value:",
			StringComparison.Ordinal))
		{
			if (IsCompactNullableIntrinsic(target) &&
				IsAllocatedPromotedLocalAddress(caller, allocated, instruction))
			{
				EmitAllocatedMove(
					Use(0),
					Definition(),
					M68kMachineValueWidth.Long);
				return;
			}
			EmitAllocatedBaseLoad(
				Use(0),
				Definition(),
				M68kMachineValueWidth.Long,
				0);
			return;
		}
		if (name == "intrinsic:boopsi-instance-data")
		{
			EmitAllocatedMove(Use(1), M68kRegister.A0, M68kMachineValueWidth.Long);
			EmitAllocatedMove(Use(0), M68kRegister.A1, M68kMachineValueWidth.Long);
			_assembler.EmitWord(0x7000); // MOVEQ #0,D0
			_assembler.EmitWord(0x3029); // MOVE.W 32(A1),D0
			_assembler.EmitWord(0x0020);
			_assembler.EmitWord(0xD1C0); // ADDA.L D0,A0
			EmitAllocatedMove(
				M68kRegister.A0,
				Definition(),
				M68kMachineValueWidth.Long);
			return;
		}
		if (name == "intrinsic:boopsi-do-method")
		{
			_assembler.EmitWord(0x224F); // MOVEA.L A7,A1
			_assembler.EmitJsr("amiga.boopsi.DoMethodA", external: true);
			_loadedPlatformBase = null;
			return;
		}
		if (name == "intrinsic:boopsi-do-method-stack-varargs")
		{
			_assembler.EmitJsr("amiga.boopsi.DoMethodA", external: true);
			_loadedPlatformBase = null;
			return;
		}
		if (name.StartsWith(
			"intrinsic:amiga-library-base-set:",
			StringComparison.Ordinal))
		{
			var libraryTypeName =
				name["intrinsic:amiga-library-base-set:".Length..];
			EnsureAmigaLibraryBaseSlot(caller, libraryTypeName);
			if (IsAllocatedAptrNullValue(
					caller,
					allocated.Function,
					instruction.Uses[0]))
			{
				EmitClearLabel(
					AmigaLibraryBaseSlotSymbol(libraryTypeName));
				_loadedPlatformBase = null;
				return;
			}
			_assembler.EmitWord((ushort)(
				0x23C0 |
				AllocatedRegisterEa(Use(0))));
			_assembler.EmitAddress(
				AmigaLibraryBaseSlotSymbol(libraryTypeName));
			_loadedPlatformBase = null;
			return;
		}
		if (name.StartsWith(
			"intrinsic:amiga-library-base-get:",
			StringComparison.Ordinal))
		{
			var libraryTypeName =
				name["intrinsic:amiga-library-base-get:".Length..];
			EnsureAmigaLibraryBaseSlot(caller, libraryTypeName);
			var destination = Definition();
			var destinationEa = destination <= M68kRegister.D7
				? (int)destination << 9
				: (((int)destination - (int)M68kRegister.A0) << 9) | 0x40;
			_assembler.EmitWord((ushort)(0x2039 | destinationEa));
			_assembler.EmitAddress(
				AmigaLibraryBaseSlotSymbol(libraryTypeName));
			return;
		}
		if (name == "intrinsic:iff-handle-stream")
		{
			var handle = Use(0);
			EmitAllocatedBaseLoad(
				handle,
				handle,
				M68kMachineValueWidth.Long,
				0);
			EmitAllocatedBaseLoad(
				handle,
				Definition(),
				M68kMachineValueWidth.Long,
				0);
			return;
		}
		if (name == "intrinsic:iff-handle-set-stream")
		{
			var handle = Use(0);
			EmitAllocatedBaseLoad(
				handle,
				handle,
				M68kMachineValueWidth.Long,
				0);
			EmitAllocatedBaseStore(
				Use(1),
				handle,
				M68kMachineValueWidth.Long,
				0);
			return;
		}
		if (name == "intrinsic:aptr-null")
		{
			EmitAllocatedImmediate(0, Definition());
			return;
		}
		if (name == "intrinsic:string-length")
		{
			var stringBase = Use(0);
			EmitAllocatedRequireNonNull(stringBase);
			EmitAllocatedBaseLoad(
				stringBase,
				Definition(),
				M68kMachineValueWidth.Long,
				8);
			return;
		}
		if (name == "intrinsic:file-info-block-file-name")
		{
			var destination = Definition();
			EmitAllocatedMove(
				Use(0),
				destination,
				M68kMachineValueWidth.Long);
			EmitAllocatedAddImmediate(destination, 8);
			return;
		}
		if (name == "intrinsic:bptr-address")
		{
			var destination = Definition();
			if (Use(0) >= M68kRegister.A0)
			{
				EmitAllocatedBaseLoad(
					Use(0),
					destination,
					M68kMachineValueWidth.Long,
					0);
			}
			else
			{
				EmitAllocatedMove(
					Use(0),
					destination,
					M68kMachineValueWidth.Long);
			}
			EmitAllocatedShiftImmediate(destination, left: true);
			EmitAllocatedShiftImmediate(destination, left: true);
			return;
		}
		if (name == "intrinsic:bptr-from-address")
		{
			var destination = Definition();
			EmitAllocatedMove(
				Use(0),
				destination,
				M68kMachineValueWidth.Long);
			EmitAllocatedShiftImmediate(destination, left: false);
			EmitAllocatedShiftImmediate(destination, left: false);
			return;
		}
		if (name is "intrinsic:aptr-is-null" or "intrinsic:aptr-is-not-null")
		{
			var materialize = instruction.Definitions.Length != 0;
			var destination = materialize
				? Definition()
				: M68kRegister.D0;
			var tested = Use(0);
			if (TryGetAllocatedFrameBackedAddressDisplacement(
				caller,
				allocated,
				instruction,
				out var frameDisplacement))
			{
				EmitAllocatedFrameLoad(
					destination,
					M68kMachineValueWidth.Long,
					frameDisplacement);
				tested = destination;
			}
			else
			{
				EmitAllocatedBaseLoad(
					tested,
					destination,
					M68kMachineValueWidth.Long,
					0);
				tested = destination;
			}
			EmitAllocatedTest(tested, M68kMachineValueWidth.Long);
			if (materialize)
			{
				EmitAllocatedConditionResult(
					name == "intrinsic:aptr-is-null"
						? M68kCondition.Equal
						: M68kCondition.NotEqual,
					destination);
			}
			return;
		}
		throw new InvalidOperationException(
			$"Allocated intrinsic '{name}' was accepted but not emitted.");
	}

	private bool TryFoldAllocatedIntrinsicResultCopy(
		M68kAllocatedFunction allocated,
		M68kMachineInstruction producer,
		out M68kRegister destination)
	{
		destination = default;
		if (producer.Definitions.Length != 1)
		{
			return false;
		}

		var definition = producer.Definitions[0];
		foreach (var block in allocated.Function.Blocks)
		{
			var producerIndex = block.Instructions.IndexOf(producer);
			if (producerIndex < 0 || producerIndex + 1 >= block.Instructions.Count)
			{
				continue;
			}

			var copy = block.Instructions[producerIndex + 1];
			if (copy.Operation != M68kMachineOperation.Copy ||
				copy.Uses is not [var source] ||
				source != definition ||
				copy.Definitions is not [var copyDefinition] ||
				allocated.Function.Blocks
					.SelectMany(static candidate => candidate.Instructions)
					.Sum(candidate => candidate.Uses.Count(use => use == definition)) != 1)
			{
				return false;
			}

			destination = allocated.Allocation.Registers[copyDefinition].Register;
			_allocatedSuppressedInstructions.Add(copy.Id);
			return true;
		}

		return false;
	}

	private static bool IsAllocatedPromotedLocalAddress(
		CilMethod caller,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		var sourceIndex = caller.Instructions
			.ToList()
			.FindIndex(candidate => candidate.Offset == instruction.IlOffset);
		return sourceIndex > 0 &&
			TryGetLoadLocalAddressIndex(
				caller.Instructions[sourceIndex - 1],
				out var localIndex) &&
			!allocated.Function.LocalHomes.ContainsKey(localIndex);
	}

	private void PrepareAllocatedFrameAddressFolds(
		CilMethod method,
		M68kAllocatedFunction allocated)
	{
		// Address-constrained copies are transparent only when the frame address
		// has one consumer. Suppress that chain before emission; the consumer can
		// then use the current frame base directly without changing SSA or liveness.
		foreach (var block in allocated.Function.Blocks)
		{
			for (var index = 0; index < block.Instructions.Count; index++)
			{
				var address = block.Instructions[index];
				if (address.Operation is not
						M68kMachineOperation.LocalAddress and not
						M68kMachineOperation.ArgumentAddress ||
					address.Definitions is not [var addressValue])
				{
					continue;
				}

				var chain = new List<M68kMachineInstruction> { address };
				var value = addressValue;
				var next = index + 1;
				while (next < block.Instructions.Count &&
					block.Instructions[next] is
					{
						Operation: M68kMachineOperation.Copy,
						Uses: [var source],
						Definitions: [var definition]
					} copy &&
					source == value &&
					CountAllocatedValueUses(allocated.Function, value) == 1)
				{
					chain.Add(copy);
					value = definition;
					next++;
				}
				if (next >= block.Instructions.Count ||
					block.Instructions[next] is not { } consumer ||
					consumer.Uses.Length == 0 ||
					consumer.Uses[0] != value)
				{
					continue;
				}

				var sourceInstruction = consumer.Operation ==
					M68kMachineOperation.ConditionalBranch &&
					consumer.BranchCondition?.SourceKind ==
						M68kMachineConditionSourceKind.Predicate
					? consumer.BranchCondition.ProducerInstruction
					: consumer.Operation == M68kMachineOperation.Call
						? consumer.SourceInstruction
						: null;
				if (sourceInstruction is not { Operand: int token } ||
					!IsFrameAddressIntrinsic(
						_module.ResolveMethodToken(
							token,
							method,
							sourceInstruction.Offset).ImportName) ||
					!TryGetAllocatedFrameBackedAddressHome(
						method,
						allocated,
						sourceInstruction.Offset))
				{
					continue;
				}

				if (chain.All(valueInstruction =>
						CountAllocatedValueUses(
							allocated.Function,
							valueInstruction.Definitions[0]) == 1))
				{
					foreach (var valueInstruction in chain)
					{
						_allocatedSuppressedInstructions.Add(valueInstruction.Id);
					}
				}
			}
		}
	}

	private static int CountAllocatedValueUses(
		M68kMachineFunction function,
		int value)
	{
		return function.Blocks
			.SelectMany(static block => block.Instructions)
			.Sum(instruction => instruction.Uses.Count(use => use == value)) +
			function.Blocks
			.SelectMany(static block => block.Phis)
			.Sum(phi => phi.Inputs.Values.Count(input => input == value));
	}

	private static bool IsFrameAddressIntrinsic(string? importName) =>
		importName is
			"intrinsic:aptr-raw" or
			"intrinsic:aptr-is-null" or
			"intrinsic:aptr-is-not-null";

	private static bool TryGetAllocatedFrameBackedAddressHome(
		CilMethod caller,
		M68kAllocatedFunction allocated,
		int instructionOffset)
	{
		var sourceIndex = caller.Instructions
			.ToList()
			.FindIndex(candidate => candidate.Offset == instructionOffset);
		if (sourceIndex <= 0)
		{
			return false;
		}
		var source = caller.Instructions[sourceIndex - 1];
		return TryGetLoadLocalAddressIndex(source, out var localIndex) &&
			allocated.Function.LocalHomes.ContainsKey(localIndex) ||
			TryGetLoadArgumentAddressIndex(source, out var argumentIndex) &&
			allocated.Function.ArgumentHomes.ContainsKey(argumentIndex);
	}

	private bool TryGetAllocatedFrameBackedAddressDisplacement(
		CilMethod caller,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction,
		out int displacement)
	{
		var sourceIndex = caller.Instructions
			.ToList()
			.FindIndex(candidate => candidate.Offset == instruction.IlOffset);
		if (sourceIndex > 0)
		{
			var source = caller.Instructions[sourceIndex - 1];
			if (TryGetLoadLocalAddressIndex(source, out var localIndex) &&
				allocated.Frame.LocalOffsets.TryGetValue(
					localIndex,
					out var localOffset))
			{
				displacement = AllocatedFrameOffset(allocated, localOffset);
				return true;
			}
			if (TryGetLoadArgumentAddressIndex(source, out var argumentIndex) &&
				allocated.Frame.ArgumentHomeOffsets.TryGetValue(
					argumentIndex,
					out var argumentOffset))
			{
				displacement = AllocatedFrameOffset(allocated, argumentOffset);
				return true;
			}
		}
		displacement = 0;
		return false;
	}

	private bool IsAllocatedAptrNullValue(
		CilMethod caller,
		M68kMachineFunction function,
		int value)
	{
		var visited = new HashSet<int>();
		while (visited.Add(value))
		{
			var definition = function.Blocks
				.SelectMany(static block => block.Instructions)
				.SingleOrDefault(instruction =>
					instruction.Definitions.Contains(value));
			if (definition is
				{
					Operation: M68kMachineOperation.Copy,
					Uses.Length: 1
				})
			{
				value = definition.Uses[0];
				continue;
			}
			if (definition is
				{
					Operation: M68kMachineOperation.Constant,
					SourceInstruction: { } constant
				})
			{
				return GetAllocatedIntConstant(constant) == 0;
			}
			if (definition is
				{
					Operation: M68kMachineOperation.Call,
					SourceInstruction: { Operand: int token } call
				})
			{
				return _module.ResolveMethodToken(
					token,
					caller,
					call.Offset).ImportName == "intrinsic:aptr-null";
			}
			return false;
		}
		return false;
	}

	private static bool IsAllocatedIntrinsic(string? name) =>
		name?.StartsWith(
			"intrinsic:amiga-library-base-set:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:amiga-library-base-get:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:nullable-has-value:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:nullable-get-value:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:nullable-get-value-or-default:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:nullable-ctor:",
			StringComparison.Ordinal) == true ||
		name is
			"intrinsic:object-ctor" or
			"intrinsic:cstring-from-pointer" or
			"intrinsic:cstring-to-uint32" or
			"intrinsic:aptr-from-pointer" or
			"intrinsic:aptr-to-uint32" or
			"intrinsic:amiga-vararg-from-value" or
			"intrinsic:address-of-ref" or
			"intrinsic:address-to-ref" or
			"intrinsic:ref-cast" or
			"intrinsic:hook-address-of" or
			"intrinsic:boopsi-message-address-of" or
			"intrinsic:iff-handle-stream" or
			"intrinsic:iff-handle-set-stream" or
			"intrinsic:cstring-from-literal" or
			"intrinsic:amiga-vararg-from-literal" or
			"intrinsic:aptr-export-address" or
			"intrinsic:aptr-read-uint32" or
			"intrinsic:aptr-write-uint32" or
			"intrinsic:aptr-raw" or
			"intrinsic:boopsi-instance-data" or
			"intrinsic:boopsi-do-method" or
			"intrinsic:boopsi-do-method-stack-varargs" or
			"intrinsic:aptr-null" or
			"intrinsic:string-length" or
			"intrinsic:file-info-block-file-name" or
			"intrinsic:bptr-address" or
			"intrinsic:bptr-from-address" or
			"intrinsic:aptr-is-null" or
			"intrinsic:aptr-is-not-null" or
			"intrinsic:runtime-dispose" or
			"intrinsic:runtime-gc-collect" or
			"intrinsic:runtime-GetGcStaleBytes" or
			"intrinsic:runtime-GetGcStaleBlocks";

	private int AllocatedPrecedingStringLiteral(
		CilMethod caller,
		M68kMachineInstruction instruction)
	{
		var index = caller.Instructions
			.ToList()
			.FindIndex(candidate => candidate.Offset == instruction.IlOffset);
		if (index <= 0 ||
			caller.Instructions[index - 1] is not
				{ OpCode: var op, Operand: int token } ||
			op != OpCodes.Ldstr)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"Allocated literal intrinsic requires an immediately preceding ldstr.",
				caller.DisplayName,
				instruction.IlOffset);
		}
		return token;
	}

	private void EmitAllocatedAddress(string label, M68kRegister destination)
	{
		var destinationEa = destination <= M68kRegister.D7
			? (int)destination << 9
			: (((int)destination - (int)M68kRegister.A0) << 9) | 0x40;
		_assembler.EmitWord((ushort)(0x203C | destinationEa));
		_assembler.EmitAddress(label);
	}

	private void EmitAllocatedAddImmediate(
		M68kRegister register,
		int value)
	{
		if (value is >= 1 and <= 8)
		{
			var encoded = value == 8 ? 0 : value;
			_assembler.EmitWord((ushort)(
				(register <= M68kRegister.D7 ? 0x5080 : 0x5088) |
				(encoded << 9) |
				(register <= M68kRegister.D7
					? (int)register
					: (int)register - (int)M68kRegister.A0)));
			return;
		}
		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(0x0680 | (int)register)); // ADDI.L
		}
		else
		{
			_assembler.EmitWord((ushort)(
				0xD1FC |
				(((int)register - (int)M68kRegister.A0) << 9))); // ADDA.L
		}
		_assembler.EmitLong(unchecked((uint)value));
	}

	private void EmitAllocatedShiftImmediate(
		M68kRegister register,
		bool left)
	{
		if (register > M68kRegister.D7)
		{
			throw new InvalidOperationException(
				"Allocated shift requires a data register.");
		}
		_assembler.EmitWord((ushort)(
			(left ? 0xE388 : 0xE288) |
			(int)register));
	}

	private void EmitAllocatedArgumentDefinition(
		InternalCallAbi abi,
		M68kMachineInstruction instruction,
		M68kAllocatedLocation destination,
		int savedBytes)
	{
		var source = abi.Arguments[instruction.ArgumentIndex!.Value];
		if (source.Register is { } register)
		{
			EmitAllocatedMove(
				register,
				destination.Register,
				destination.IsPair
					? M68kMachineValueWidth.LongPair
					: M68kMachineValueWidth.Long);
			return;
		}
		var displacement = checked(savedBytes + 4 + source.StackOffset);
		EmitAllocatedStackLoad(destination.Register, displacement);
		if (destination.IsPair)
		{
			EmitAllocatedStackLoad(
				(M68kRegister)((int)destination.Register + 1),
				checked(displacement + 4));
		}
	}

	private void EmitAllocatedArgumentLoad(
		InternalCallAbi abi,
		M68kMachineInstruction instruction,
		M68kAllocatedLocation destination,
		int savedBytes)
	{
		if (!TryGetAllocatedArgumentIndex(
				instruction.SourceInstruction!,
				out var argumentIndex))
		{
			throw new InvalidOperationException("Allocated argument load has no argument index.");
		}
		var source = abi.Arguments[argumentIndex];
		if (source.Register is { } register)
		{
			EmitAllocatedMove(
				register,
				destination.Register,
				destination.IsPair
					? M68kMachineValueWidth.LongPair
					: M68kMachineValueWidth.Long);
			return;
		}
		var displacement = checked(savedBytes + 4 + source.StackOffset);
		EmitAllocatedStackLoad(destination.Register, displacement);
		if (destination.IsPair)
		{
			EmitAllocatedStackLoad(
				(M68kRegister)((int)destination.Register + 1),
				checked(displacement + 4));
		}
	}

	private void EmitAllocatedConstant(
		M68kMachineInstruction instruction,
		M68kAllocatedLocation destination)
	{
		if (destination.IsPair)
		{
			var value = instruction.SourceInstruction is { } pairSource
				? GetAllocatedLongConstant(pairSource)
				: 0;
			EmitAllocatedImmediate(
				unchecked((int)(value >> 32)),
				destination.Register);
			EmitAllocatedImmediate(
				unchecked((int)value),
				(M68kRegister)((int)destination.Register + 1));
			return;
		}
		var scalar = instruction.SourceInstruction is { } source
			? GetAllocatedIntConstant(source)
			: 0;
		EmitAllocatedImmediate(scalar, destination.Register);
	}

	private void EmitAllocatedBinary(
		M68kMachineOperation operation,
		M68kRegister left,
		M68kRegister right,
		M68kRegister destination,
		M68kMachineValueWidth width)
	{
		var commutative = operation is
			M68kMachineOperation.Add or
			M68kMachineOperation.And or
			M68kMachineOperation.Or or
			M68kMachineOperation.Xor;
		if (destination == right && commutative)
		{
			(left, right) = (right, left);
		}
		if (destination != left)
		{
			EmitAllocatedMove(left, destination, width);
		}
		EmitAllocatedBinaryInPlace(operation, right, destination, width);
	}

	private void EmitAllocatedFloatingBinary(
		M68kMachineOperation operation,
		M68kAllocatedLocation left,
		M68kAllocatedLocation right,
		M68kAllocatedLocation destination,
		CilStackValueKind kind)
	{
		EnsureNativeFloatingPoint();
		if (_request.Cpu == M68kCpuTarget.M68060 &&
			kind == CilStackValueKind.Float64)
		{
			EmitM68060FloatingBinary(operation, left, right, destination);
			return;
		}
		if (_request.Cpu == M68kCpuTarget.M68040 &&
			kind == CilStackValueKind.Float64)
		{
			EmitM68040FloatingBinary(operation, left, right, destination);
			return;
		}
		EmitAllocatedFloatingLoad(left, kind, 0);
		EmitAllocatedFloatingLoad(right, kind, 1);
		_assembler.EmitFpuRegisterOperation(1, 0, FloatingOperation(operation));
		EmitAllocatedFloatingStore(0, destination, kind);
	}

	private void EmitAllocatedFloatingUnary(
		M68kAllocatedLocation source,
		M68kAllocatedLocation destination,
		CilStackValueKind kind,
		M68kFpuOperation operation)
	{
		EnsureNativeFloatingPoint();
		if (_request.Cpu is M68kCpuTarget.M68040 or M68kCpuTarget.M68060 &&
			kind == CilStackValueKind.Float64)
		{
			EmitAllocateFrame(8);
			EmitFpuScratchPairStore(source);
			_assembler.EmitFpuStackToRegister(0, M68kFpuFormat.Double);
			_assembler.EmitFpuUnaryOperation(0, operation);
			_assembler.EmitFpuRegisterToStack(0, M68kFpuFormat.Double);
			EmitFpuScratchPairLoad(destination);
			EmitReleaseStackBytes(8);
			return;
		}
		EmitAllocatedFloatingLoad(source, kind, 0);
		_assembler.EmitFpuUnaryOperation(0, operation);
		EmitAllocatedFloatingStore(0, destination, kind);
	}

	private void EmitM68040FloatingBinary(
		M68kMachineOperation operation,
		M68kAllocatedLocation left,
		M68kAllocatedLocation right,
		M68kAllocatedLocation destination)
	{
		EmitAllocateFrame(8);
		EmitFpuScratchPairStore(left);
		_assembler.EmitFpuStackToRegister(0, M68kFpuFormat.Double);
		EmitFpuScratchPairStore(right);
		_assembler.EmitFpuStackToRegister(1, M68kFpuFormat.Double);
		_assembler.EmitFpuRegisterOperation(1, 0, FloatingOperation(operation));
		_assembler.EmitFpuRegisterToStack(0, M68kFpuFormat.Double);
		EmitFpuScratchPairLoad(destination);
		EmitReleaseStackBytes(8);
	}

	private void EmitM68060FloatingBinary(
		M68kMachineOperation operation,
		M68kAllocatedLocation left,
		M68kAllocatedLocation right,
		M68kAllocatedLocation destination)
	{
		EmitAllocateFrame(16);
		EmitM68060StackPairStore(left, 0);
		EmitM68060StackPairStore(right, 8);
		_assembler.EmitFpuStackDisplacementToRegister(0, M68kFpuFormat.Double, 0);
		_assembler.EmitFpuStackDisplacementToRegister(1, M68kFpuFormat.Double, 8);
		_assembler.EmitFpuRegisterOperation(1, 0, FloatingOperation(operation));
		_assembler.EmitFpuRegisterToStackDisplacement(0, M68kFpuFormat.Double, 0);
		EmitM68060StackPairLoad(destination, 0);
		EmitReleaseStackBytes(16);
	}

	private void EmitFpuScratchPairStore(M68kAllocatedLocation source)
	{
		_assembler.EmitWord((ushort)(0x2E80 | (int)source.Register)); // MOVE.L Dn,(A7)
		_assembler.EmitWord((ushort)(0x2F40 | ((int)source.Register + 1))); // MOVE.L Dn,4(A7)
		_assembler.EmitWord(4);
	}

	private void EmitFpuScratchPairLoad(M68kAllocatedLocation destination)
	{
		_assembler.EmitWord((ushort)(0x2017 | ((int)destination.Register << 9))); // MOVE.L (A7),Dn
		_assembler.EmitWord((ushort)(0x202F | (((int)destination.Register + 1) << 9))); // MOVE.L 4(A7),Dn
		_assembler.EmitWord(4);
	}

	private static M68kFpuOperation FloatingOperation(M68kMachineOperation operation) =>
		operation switch
		{
			M68kMachineOperation.Add => M68kFpuOperation.Add,
			M68kMachineOperation.Subtract => M68kFpuOperation.Subtract,
			M68kMachineOperation.Multiply => M68kFpuOperation.Multiply,
			M68kMachineOperation.Divide => M68kFpuOperation.Divide,
			_ => throw new InvalidOperationException($"Unsupported floating operation {operation}.")
		};

	private void EmitM68060StackPairStore(M68kAllocatedLocation source, short displacement)
	{
		_assembler.EmitWord((ushort)(0x2F40 | (int)source.Register));
		_assembler.EmitWord(unchecked((ushort)displacement));
		_assembler.EmitWord((ushort)(0x2F40 | ((int)source.Register + 1)));
		_assembler.EmitWord(unchecked((ushort)(displacement + 4)));
	}

	private void EmitM68060StackPairLoad(M68kAllocatedLocation destination, short displacement)
	{
		_assembler.EmitWord((ushort)(0x202F | ((int)destination.Register << 9)));
		_assembler.EmitWord(unchecked((ushort)displacement));
		_assembler.EmitWord((ushort)(0x202F | (((int)destination.Register + 1) << 9)));
		_assembler.EmitWord(unchecked((ushort)(displacement + 4)));
	}

	private void EmitAllocatedFloatingLoad(
		M68kAllocatedLocation source,
		CilStackValueKind kind,
		int fpuRegister)
	{
		if (kind == CilStackValueKind.Float32)
		{
			_assembler.EmitFpuDataRegisterToRegister(
				(int)source.Register,
				fpuRegister,
				M68kFpuFormat.Single);
			return;
		}

		EmitAllocatedPush((M68kRegister)((int)source.Register + 1));
		EmitAllocatedPush(source.Register);
		_assembler.EmitFpuStackToRegister(fpuRegister, M68kFpuFormat.Double);
		EmitReleaseStackBytes(8);
	}

	private void EmitAllocatedFloatingStore(
		int fpuRegister,
		M68kAllocatedLocation destination,
		CilStackValueKind kind)
	{
		if (kind == CilStackValueKind.Float32)
		{
			_assembler.EmitFpuRegisterToDataRegister(
				fpuRegister,
				(int)destination.Register,
				M68kFpuFormat.Single);
			return;
		}

		EmitAllocateFrame(8);
		_assembler.EmitFpuRegisterToStack(fpuRegister, M68kFpuFormat.Double);
		_assembler.EmitWord((ushort)(0x201F | ((int)destination.Register << 9)));
		_assembler.EmitWord((ushort)(0x201F |
			(((int)destination.Register + 1) << 9)));
	}

	private void EnsureNativeFloatingPoint()
	{
		if (_request.FloatingPoint == M68kFloatingPointMode.SoftFloat)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"SoftFloat arithmetic runtime emission is not available for this operation.");
		}
	}

	private void EmitAllocatedBinaryInPlace(
		M68kMachineOperation operation,
		M68kRegister source,
		M68kRegister destination,
		M68kMachineValueWidth width)
	{
		if (destination >= M68kRegister.A0)
		{
			if (operation is not
				M68kMachineOperation.Add and not
				M68kMachineOperation.Subtract ||
				width != M68kMachineValueWidth.Long)
			{
				throw new InvalidOperationException(
					$"{operation} cannot target {destination}.");
			}
			var destinationIndex = (int)destination - (int)M68kRegister.A0;
			var effectiveAddress = AllocatedRegisterEa(source);
			_assembler.EmitWord((ushort)(
				(operation == M68kMachineOperation.Add ? 0xD1C0 : 0x91C0) |
				(destinationIndex << 9) |
				effectiveAddress));
			return;
		}
		if (source >= M68kRegister.A0)
		{
			throw new InvalidOperationException(
				$"{operation} cannot use address source {source} with data destination.");
		}
		var sizeBits = AllocatedSizeBits(width);
		var sourceIndex = (int)source;
		var destinationIndexData = (int)destination;
		var opcode = operation switch
		{
			M68kMachineOperation.Add => 0xD000,
			M68kMachineOperation.Subtract => 0x9000,
			M68kMachineOperation.And => 0xC000,
			M68kMachineOperation.Or => 0x8000,
			M68kMachineOperation.Xor => 0xB100,
			_ => throw new InvalidOperationException()
		};
		if (operation == M68kMachineOperation.Xor)
		{
			_assembler.EmitWord((ushort)(
				opcode |
				(sourceIndex << 9) |
				sizeBits |
				destinationIndexData));
		}
		else
		{
			_assembler.EmitWord((ushort)(
				opcode |
				(destinationIndexData << 9) |
				sizeBits |
				sourceIndex));
		}
	}

	private void EmitAllocatedUnary(
		M68kMachineOperation operation,
		M68kRegister source,
		M68kRegister destination,
		M68kMachineValueWidth width)
	{
		EmitAllocatedMove(source, destination, width);
		if (destination > M68kRegister.D7)
		{
			throw new InvalidOperationException(
				$"{operation} cannot target address register {destination}.");
		}
		var baseOpcode = operation == M68kMachineOperation.Negate
			? 0x4400
			: 0x4600;
		_assembler.EmitWord((ushort)(
			baseOpcode |
			AllocatedSizeBits(width) |
			(int)destination));
	}

	private void EmitAllocatedConversion(
		OpCode op,
		M68kRegister source,
		M68kRegister destination)
	{
		EmitAllocatedMove(source, destination, M68kMachineValueWidth.Long);
		if (destination > M68kRegister.D7)
		{
			throw new InvalidOperationException(
				$"Conversion {op.Name} cannot target {destination}.");
		}
		var register = (int)destination;
		if (op == OpCodes.Conv_I1)
		{
			EmitAllocatedSignExtendByte(destination);
		}
		else if (op == OpCodes.Conv_U1)
		{
			_assembler.EmitWord((ushort)(0x0280 | register)); // ANDI.L #$FF,Dn
			_assembler.EmitLong(0xFF);
		}
		else if (op == OpCodes.Conv_I2)
		{
			_assembler.EmitWord((ushort)(0x48C0 | register)); // EXT.L Dn
		}
		else if (op == OpCodes.Conv_U2)
		{
			_assembler.EmitWord((ushort)(0x0280 | register)); // ANDI.L #$FFFF,Dn
			_assembler.EmitLong(0xFFFF);
		}
	}

	private void EmitAllocatedNormalize(
		M68kRegister register,
		CilStackValueKind kind)
	{
		if (kind is CilStackValueKind.Int32 or
			CilStackValueKind.Int64 or
			CilStackValueKind.Reference or
			CilStackValueKind.ManagedPointer)
		{
			return;
		}
		if (register > M68kRegister.D7)
		{
			throw new InvalidOperationException(
				$"Narrow value cannot be normalized in {register}.");
		}
		var dataRegister = (int)register;
		switch (kind)
		{
			case CilStackValueKind.SignedByte:
				EmitAllocatedSignExtendByte(register);
				break;
			case CilStackValueKind.BooleanByte:
			case CilStackValueKind.UnsignedByte:
				_assembler.EmitWord((ushort)(0x0280 | dataRegister)); // ANDI.L #$FF,Dn
				_assembler.EmitLong(0xFF);
				break;
			case CilStackValueKind.SignedWord:
				_assembler.EmitWord((ushort)(0x48C0 | dataRegister)); // EXT.L Dn
				break;
			case CilStackValueKind.UnsignedWord:
				_assembler.EmitWord((ushort)(0x0280 | dataRegister)); // ANDI.L #$FFFF,Dn
				_assembler.EmitLong(0xFFFF);
				break;
		}
	}

	private void EmitAllocatedCompare(
		M68kRegister left,
		M68kRegister right,
		M68kMachineValueWidth width)
	{
		if (left <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(
				0xB000 |
				((int)left << 9) |
				AllocatedSizeBits(width) |
				AllocatedRegisterEa(right)));
			return;
		}
		if (width != M68kMachineValueWidth.Long)
		{
			throw new InvalidOperationException(
				"Address-register comparison must be long-sized.");
		}
		var destination = (int)left - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(
			0xB1C0 |
			(destination << 9) |
			AllocatedRegisterEa(right)));
	}

	private void EmitAllocatedConditionResult(
		M68kCondition condition,
		M68kRegister destination)
	{
		if (destination > M68kRegister.D7)
		{
			throw new InvalidOperationException(
				$"Condition result cannot target {destination}.");
		}
		var register = (int)destination;
		_assembler.EmitWord((ushort)(
			0x50C0 |
			((int)condition << 8) |
			register)); // Scc Dn
		EmitAllocatedSignExtendByte(destination);
		_assembler.EmitWord((ushort)(0x4480 | register)); // NEG.L Dn
	}

	private void EmitAllocatedSignExtendByte(M68kRegister register)
	{
		var dataRegister = (int)register;
		if (_request.Cpu >= M68kCpuTarget.M68020)
		{
			_assembler.EmitWord((ushort)(0x49C0 | dataRegister)); // EXTB.L Dn
			return;
		}
		_assembler.EmitWord((ushort)(0x4880 | dataRegister)); // EXT.W Dn
		_assembler.EmitWord((ushort)(0x48C0 | dataRegister)); // EXT.L Dn
	}

	private void EmitAllocatedConditionalBranch(
		CilMethod method,
		M68kAllocatedFunction allocated,
		M68kMachineBlock block,
		M68kMachineInstruction instruction,
		int? nextBlockId)
	{
		if (instruction.BranchCondition is { } branchCondition)
		{
			switch (branchCondition.SourceKind)
			{
				case M68kMachineConditionSourceKind.Test:
					EmitAllocatedTest(
						allocated.Allocation.Registers[instruction.Uses[0]].Register,
						allocated.Function.Values[instruction.Uses[0]].Width);
					break;

				case M68kMachineConditionSourceKind.Compare:
					EmitAllocatedCompare(
						allocated.Allocation.Registers[instruction.Uses[0]].Register,
						allocated.Allocation.Registers[instruction.Uses[1]].Register,
						allocated.Function.Values[instruction.Uses[0]].Width);
					break;

				case M68kMachineConditionSourceKind.Predicate:
					EmitAllocatedCall(
						method,
						allocated,
						instruction with
						{
							Operation = M68kMachineOperation.Call,
							IlOffset = branchCondition.ProducerInstruction!.Offset,
							SourceInstruction =
								branchCondition.ProducerInstruction
						});
					break;

				default:
					throw new InvalidOperationException(
						$"Unsupported condition source {branchCondition.SourceKind}.");
			}

			EmitAllocatedConditionalTargets(
				method,
				allocated,
				block,
				branchCondition.Condition,
				nextBlockId);
			return;
		}

		var source = instruction.SourceInstruction!;
		M68kCondition condition;
		if (instruction.ConsumesConditionCodes)
		{
			var producer = block.Instructions[
				block.Instructions.IndexOf(instruction) - 1];
			condition = AllocatedConditionProducerCondition(
				method,
				producer);
			if (source.OpCode == OpCodes.Brfalse ||
				source.OpCode == OpCodes.Brfalse_S)
			{
				condition = InvertCondition(condition);
			}
		}
		else if (instruction.Uses.Length == 1)
		{
			EmitAllocatedTest(
				allocated.Allocation.Registers[instruction.Uses[0]].Register,
				allocated.Function.Values[instruction.Uses[0]].Width);
			if (!TryGetBooleanBranchCondition(source.OpCode, out condition))
			{
				throw new InvalidOperationException(
					$"Unsupported unary allocated branch {source.OpCode.Name}.");
			}
		}
		else
		{
			EmitAllocatedCompare(
				allocated.Allocation.Registers[instruction.Uses[0]].Register,
				allocated.Allocation.Registers[instruction.Uses[1]].Register,
				allocated.Function.Values[instruction.Uses[0]].Width);
			condition = AllocatedRelationalBranchCondition(source.OpCode);
		}
		EmitAllocatedConditionalTargets(
			method,
			allocated,
			block,
			condition,
			nextBlockId);
	}

	private void EmitAllocatedConditionalTargets(
		CilMethod method,
		M68kAllocatedFunction allocated,
		M68kMachineBlock block,
		M68kCondition condition,
		int? nextBlockId)
	{
		var trueTarget = allocated.FinalDestinations.Resolve(block.Successors[0]);
		var falseTarget = allocated.FinalDestinations.Resolve(block.Successors[1]);
		if (trueTarget == falseTarget)
		{
			if (nextBlockId != trueTarget)
			{
				_assembler.EmitBranch(
					M68kCondition.True,
					AllocatedBlockLabel(method, trueTarget));
			}
			return;
		}
		if (nextBlockId == trueTarget)
		{
			_assembler.EmitBranch(
				InvertCondition(condition),
				AllocatedBlockLabel(method, falseTarget));
			return;
		}

		_assembler.EmitBranch(
			condition,
			AllocatedBlockLabel(method, trueTarget));
		if (nextBlockId != falseTarget)
		{
			_assembler.EmitBranch(
				M68kCondition.True,
				AllocatedBlockLabel(method, falseTarget));
		}
	}

	private M68kCondition AllocatedConditionProducerCondition(
		CilMethod method,
		M68kMachineInstruction producer)
	{
		if (producer.Operation == M68kMachineOperation.Compare)
		{
			return ComparisonCondition(producer.SourceInstruction!.OpCode);
		}
		if (producer.Operation == M68kMachineOperation.Call &&
			producer.SourceInstruction is { Operand: int token } source)
		{
			var target = _module.ResolveMethodToken(
				token,
				method,
				source.Offset);
			if (target.ImportName == "intrinsic:aptr-is-null")
			{
				return M68kCondition.Equal;
			}
			if (target.ImportName == "intrinsic:aptr-is-not-null" ||
				target.ImportName?.StartsWith(
					"intrinsic:nullable-has-value:",
					StringComparison.Ordinal) == true)
			{
				return M68kCondition.NotEqual;
			}
		}
		throw new InvalidOperationException(
			$"Allocated condition producer {producer.Operation} has no branch condition.");
	}

	private void EmitAllocatedTest(
		M68kRegister register,
		M68kMachineValueWidth width)
	{
		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(
				0x4A00 |
				AllocatedSizeBits(width) |
				(int)register));
			return;
		}
		var address = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0xB1FC | (address << 9))); // CMPA.L #0,An
		_assembler.EmitLong(0);
	}

	private void EmitAllocatedEdgeCopies(
		M68kAllocatedFunction allocated,
		int from,
		int to)
	{
		if (!allocated.ParallelCopies.EdgeCopies.TryGetValue(
				(from, to),
				out var copies))
		{
			return;
		}
		foreach (var copy in copies)
		{
			if (copy.Source.Kind == M68kStorageKind.Register &&
				copy.Destination.Kind == M68kStorageKind.Register)
			{
				EmitAllocatedMove(
					(M68kRegister)copy.Source.Index,
					(M68kRegister)copy.Destination.Index,
					M68kMachineValueWidth.Long);
			}
			else if (copy.Source.Kind == M68kStorageKind.Register &&
				copy.Destination.Kind == M68kStorageKind.Temporary)
			{
				EmitAllocatedFrameStore(
					(M68kRegister)copy.Source.Index,
					M68kMachineValueWidth.Long,
					AllocatedFrameOffset(
						allocated,
						allocated.Frame.ParallelCopyTemporaryOffset!.Value));
			}
			else if (copy.Source.Kind == M68kStorageKind.Temporary &&
				copy.Destination.Kind == M68kStorageKind.Register)
			{
				EmitAllocatedFrameLoad(
					(M68kRegister)copy.Destination.Index,
					M68kMachineValueWidth.Long,
					AllocatedFrameOffset(
						allocated,
						allocated.Frame.ParallelCopyTemporaryOffset!.Value));
			}
			else
			{
				throw new InvalidOperationException(
					"Allocated phi copy has unsupported storage locations.");
			}
		}
	}

	private void EmitAllocatedFrameLoad(
		M68kRegister destination,
		M68kMachineValueWidth width,
		int displacement)
	{
		ValidateAllocatedFrameDisplacement(displacement);
		if (width == M68kMachineValueWidth.LongPair)
		{
			EmitAllocatedFrameLoad(
				destination,
				M68kMachineValueWidth.Long,
				displacement);
			EmitAllocatedFrameLoad(
				(M68kRegister)((int)destination + 1),
				M68kMachineValueWidth.Long,
				checked(displacement + 4));
			return;
		}
		if (destination > M68kRegister.D7 &&
			width != M68kMachineValueWidth.Long)
		{
			throw new InvalidOperationException(
				$"{width} frame load cannot target {destination}.");
		}
		var baseOpcode = width switch
		{
			M68kMachineValueWidth.Byte => 0x102F,
			M68kMachineValueWidth.Word => 0x302F,
			M68kMachineValueWidth.Long => 0x202F,
			_ => throw new InvalidOperationException(
				"Pair frame loads must be expanded.")
		};
		var destinationEa = destination <= M68kRegister.D7
			? (int)destination << 9
			: (((int)destination - (int)M68kRegister.A0) << 9) | 0x40;
		_assembler.EmitWord((ushort)(baseOpcode | destinationEa));
		_assembler.EmitWord(unchecked((ushort)(short)displacement));
	}

	private void EmitAllocatedFrameAddress(
		M68kRegister destination,
		int displacement)
	{
		ValidateAllocatedFrameDisplacement(displacement);
		var addressDestination = destination >= M68kRegister.A0
			? destination
			: M68kRegister.A0;
		_assembler.EmitWord((ushort)(
			0x41EF |
			(((int)addressDestination - (int)M68kRegister.A0) << 9)));
		_assembler.EmitWord(unchecked((ushort)(short)displacement));
		if (addressDestination != destination)
		{
			EmitAllocatedMove(
				addressDestination,
				destination,
				M68kMachineValueWidth.Long);
		}
	}

	private void EmitAllocatedFrameStore(
		M68kRegister source,
		M68kMachineValueWidth width,
		int displacement)
	{
		ValidateAllocatedFrameDisplacement(displacement);
		if (width == M68kMachineValueWidth.LongPair)
		{
			EmitAllocatedFrameStore(
				source,
				M68kMachineValueWidth.Long,
				displacement);
			EmitAllocatedFrameStore(
				(M68kRegister)((int)source + 1),
				M68kMachineValueWidth.Long,
				checked(displacement + 4));
			return;
		}
		if (source > M68kRegister.D7 &&
			width != M68kMachineValueWidth.Long)
		{
			throw new InvalidOperationException(
				$"{width} frame store cannot use {source}.");
		}
		var baseOpcode = width switch
		{
			M68kMachineValueWidth.Byte => 0x1F40,
			M68kMachineValueWidth.Word => 0x3F40,
			M68kMachineValueWidth.Long => 0x2F40,
			_ => throw new InvalidOperationException(
				"Pair frame stores must be expanded.")
		};
		_assembler.EmitWord((ushort)(
			baseOpcode |
			AllocatedRegisterEa(source)));
		_assembler.EmitWord(unchecked((ushort)(short)displacement));
	}

	private void EmitAllocatedFrameCopy(
		int sourceDisplacement,
		int destinationDisplacement)
	{
		ValidateAllocatedFrameDisplacement(sourceDisplacement);
		ValidateAllocatedFrameDisplacement(destinationDisplacement);
		_assembler.EmitWord(0x2F6F); // MOVE.L d16(A7),d16(A7)
		_assembler.EmitWord(unchecked((ushort)(short)sourceDisplacement));
		_assembler.EmitWord(unchecked((ushort)(short)destinationDisplacement));
	}

	private void EmitAllocatedFrameClear(int displacement)
	{
		ValidateAllocatedFrameDisplacement(displacement);
		_assembler.EmitWord(0x42AF); // CLR.L d16(A7), safe frame memory
		_assembler.EmitWord(unchecked((ushort)(short)displacement));
	}

	private static void ValidateAllocatedFrameDisplacement(int displacement)
	{
		if (displacement is < 0 or > short.MaxValue)
		{
			throw new InvalidOperationException(
				"Allocated frame displacement exceeds d16(A7) range.");
		}
	}

	private void EmitAllocatedMove(
		M68kRegister source,
		M68kRegister destination,
		M68kMachineValueWidth width)
	{
		if (source == destination)
		{
			return;
		}
		if (width == M68kMachineValueWidth.LongPair)
		{
			if (destination > source)
			{
				EmitAllocatedMove(
					(M68kRegister)((int)source + 1),
					(M68kRegister)((int)destination + 1),
					M68kMachineValueWidth.Long);
				EmitAllocatedMove(
					source,
					destination,
					M68kMachineValueWidth.Long);
			}
			else
			{
				EmitAllocatedMove(
					source,
					destination,
					M68kMachineValueWidth.Long);
				EmitAllocatedMove(
					(M68kRegister)((int)source + 1),
					(M68kRegister)((int)destination + 1),
					M68kMachineValueWidth.Long);
			}
			return;
		}
		if (width is M68kMachineValueWidth.Byte or M68kMachineValueWidth.Word &&
			(source > M68kRegister.D7 || destination > M68kRegister.D7))
		{
			throw new InvalidOperationException(
				$"{width} move cannot use address registers.");
		}
		var baseOpcode = width switch
		{
			M68kMachineValueWidth.Byte => 0x1000,
			M68kMachineValueWidth.Word => 0x3000,
			M68kMachineValueWidth.Long => 0x2000,
			_ => throw new InvalidOperationException("Pair move must be expanded.")
		};
		var destinationEa = destination <= M68kRegister.D7
			? (int)destination << 9
			: (((int)destination - (int)M68kRegister.A0) << 9) | 0x40;
		_assembler.EmitWord((ushort)(
			baseOpcode |
			destinationEa |
			AllocatedRegisterEa(source)));
	}

	private void EmitAllocatedImmediate(int value, M68kRegister destination)
	{
		if (destination <= M68kRegister.D7 && value is >= -128 and <= 127)
		{
			_assembler.EmitWord((ushort)(
				0x7000 |
				((int)destination << 9) |
				(byte)(sbyte)value));
			return;
		}
		var destinationEa = destination <= M68kRegister.D7
			? (int)destination << 9
			: (((int)destination - (int)M68kRegister.A0) << 9) | 0x40;
		_assembler.EmitWord((ushort)(0x203C | destinationEa));
		_assembler.EmitLong(unchecked((uint)value));
	}

	private void EmitAllocatedStackPointerToAddressRegister(
		M68kRegister destination)
	{
		if (destination <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(
				0x200F |
				((int)destination << 9))); // MOVE.L A7,Dn
			return;
		}
		var addressIndex =
			(int)destination - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(
			0x204F |
			(addressIndex << 9))); // MOVEA.L A7,An
	}

	private void EmitAllocatedStackLoad(M68kRegister destination, int displacement)
	{
		if (displacement > short.MaxValue)
		{
			throw new InvalidOperationException(
				"Allocated stack argument exceeds d16(A7) range.");
		}
		var destinationEa = destination <= M68kRegister.D7
			? (int)destination << 9
			: (((int)destination - (int)M68kRegister.A0) << 9) | 0x40;
		_assembler.EmitWord((ushort)(0x202F | destinationEa));
		_assembler.EmitWord(unchecked((ushort)(short)displacement));
	}

	private void EmitAllocatedCalleeSaves(
		IReadOnlyList<M68kRegister> registers)
	{
		EmitPushRegisters(registers.ToArray());
	}

	private void EmitAllocatedCalleeRestores(
		IReadOnlyList<M68kRegister> registers,
		int frameBytes)
	{
		EmitReleaseFrame(frameBytes);
		EmitPopRegisters(registers.ToArray());
	}

	private static int AllocatedRegisterEa(M68kRegister register) =>
		register <= M68kRegister.D7
			? (int)register
			: 0x08 + (int)register - (int)M68kRegister.A0;

	private static int AllocatedSizeBits(M68kMachineValueWidth width) =>
		width switch
		{
			M68kMachineValueWidth.Byte => 0x0000,
			M68kMachineValueWidth.Word => 0x0040,
			M68kMachineValueWidth.Long => 0x0080,
			_ => throw new InvalidOperationException(
				"Register-pair operation must be expanded.")
		};

	private static bool TryGetAllocatedArgumentIndex(
		CilInstruction instruction,
		out int index)
	{
		var op = instruction.OpCode;
		if (op.Value >= OpCodes.Ldarg_0.Value &&
			op.Value <= OpCodes.Ldarg_3.Value)
		{
			index = op.Value - OpCodes.Ldarg_0.Value;
			return true;
		}
		if (op == OpCodes.Ldarg || op == OpCodes.Ldarg_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}
		index = default;
		return false;
	}

	private static bool TryGetAllocatedConstant(
		M68kMachineFunction function,
		int value,
		out int constant)
	{
		var visited = new HashSet<int>();
		while (visited.Add(value))
		{
			var definition = function.Blocks
				.SelectMany(static block => block.Instructions)
				.SingleOrDefault(instruction =>
					instruction.Definitions.Contains(value));
			if (definition is
				{
					Operation: M68kMachineOperation.Constant,
					SourceInstruction: { } source
				})
			{
				constant = GetAllocatedIntConstant(source);
				return true;
			}
			if (definition is
				{
					Operation: M68kMachineOperation.Copy,
					Uses.Length: 1
				})
			{
				value = definition.Uses[0];
				continue;
			}
			break;
		}
		constant = 0;
		return false;
	}

	private static int GetAllocatedIntConstant(CilInstruction instruction)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Ldnull)
		{
			return 0;
		}
		if (op == OpCodes.Ldc_I4_M1)
		{
			return -1;
		}
		if (op.Value >= OpCodes.Ldc_I4_0.Value &&
			op.Value <= OpCodes.Ldc_I4_8.Value)
		{
			return op.Value - OpCodes.Ldc_I4_0.Value;
		}
		if (op == OpCodes.Ldc_I4_S)
		{
			return Convert.ToSByte(instruction.Operand);
		}
		if (op == OpCodes.Ldc_I4)
		{
			return Convert.ToInt32(instruction.Operand);
		}
		if (op == OpCodes.Ldc_R4)
		{
			return BitConverter.SingleToInt32Bits(Convert.ToSingle(instruction.Operand));
		}
		throw new InvalidOperationException(
			$"Unsupported allocated constant {op.Name}.");
	}

	private static long GetAllocatedLongConstant(CilInstruction instruction)
	{
		if (instruction.OpCode == OpCodes.Ldc_I8)
		{
			return Convert.ToInt64(instruction.Operand);
		}
		if (instruction.OpCode == OpCodes.Ldc_R8)
		{
			return BitConverter.DoubleToInt64Bits(Convert.ToDouble(instruction.Operand));
		}
		return GetAllocatedIntConstant(instruction);
	}

	private static M68kCondition AllocatedRelationalBranchCondition(OpCode op) =>
		op == OpCodes.Beq || op == OpCodes.Beq_S
			? M68kCondition.Equal
			: op == OpCodes.Bne_Un || op == OpCodes.Bne_Un_S
				? M68kCondition.NotEqual
				: op == OpCodes.Bgt || op == OpCodes.Bgt_S
					? M68kCondition.GreaterThan
					: op == OpCodes.Bge || op == OpCodes.Bge_S
						? M68kCondition.GreaterOrEqual
						: op == OpCodes.Blt || op == OpCodes.Blt_S
							? M68kCondition.LessThan
							: op == OpCodes.Ble || op == OpCodes.Ble_S
								? M68kCondition.LessOrEqual
								: op == OpCodes.Bgt_Un || op == OpCodes.Bgt_Un_S
									? M68kCondition.Higher
									: op == OpCodes.Bge_Un || op == OpCodes.Bge_Un_S
										? M68kCondition.CarryClear
										: op == OpCodes.Blt_Un || op == OpCodes.Blt_Un_S
											? M68kCondition.CarrySet
											: M68kCondition.LowerOrSame;

	private string AllocatedBlockLabel(CilMethod method, int blockId) =>
		$"{MethodLabel(method)}:BB{blockId:X4}";

	private string AllocatedBlockEndLabel(CilMethod method, int blockId) =>
		$"{AllocatedBlockLabel(method, blockId)}:end";
}
