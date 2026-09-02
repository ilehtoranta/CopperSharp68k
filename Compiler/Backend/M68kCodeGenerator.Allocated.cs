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
	private readonly Dictionary<int, M68kMachineInstruction>
		_allocatedFoldedCopyAddresses = new();
	private readonly Dictionary<int, (int Factor, int SourceValue)>
		_allocatedConstantMultiplies = new();
	private readonly Dictionary<int, (int Delta, int SourceValue)>
		_allocatedQuickArithmetic = new();
	private readonly record struct ConstantMultiplyTerm(int Bit, bool Subtract);
	private sealed record ConstantMultiplyPlan(
		ConstantMultiplyTerm[] Terms,
		int EncodedBytes,
		int Cycles,
		int TieBreak);
	private readonly HashSet<int> _allocatedDeferredNormalizations = new();
	private readonly HashSet<int> _allocatedExceptionFrameStores = new();
	private readonly HashSet<int> _allocatedKnownNonNullValues = new();
	private readonly HashSet<M68kRegister> _allocatedKnownNonNullRegisters = new();
	private readonly HashSet<M68kRegister> _allocatedFrameAddressesEmitted = new();
	private readonly Dictionary<int, AllocatedFunctionAddressSwitchPlan>
		_allocatedFunctionAddressSwitches = new();
	private int _allocatedOutgoingStackBytes;

	private readonly record struct AllocatedFunctionAddressTarget(
		string Label,
		int Addend = 0);

	private sealed record AllocatedFunctionAddressSwitchPlan(
		string TableLabel,
		IReadOnlyList<AllocatedFunctionAddressTarget> Targets,
		string FallthroughTarget,
		M68kRegister Selector,
		M68kRegister ValueRegister);

	private MethodReference ResolveAllocatedMachineMethod(
		CilMethod caller,
		M68kMachineInstruction instruction)
	{
		var sourceMethod = instruction.Origin?.SourceMethod ?? caller;
		var source = instruction.Origin?.SourceInstruction ??
			instruction.SourceInstruction ??
			throw new InvalidOperationException(
				"Machine method reference has no source instruction.");
		return _module.ResolveMethodToken(
			(int)source.Operand!,
			sourceMethod,
			 source.Offset);
	}

	private CilField ResolveAllocatedField(
		CilMethod caller,
		M68kMachineInstruction instruction)
	{
		var sourceMethod = instruction.Origin?.SourceMethod ?? caller;
		var source = instruction.Origin?.SourceInstruction ??
			instruction.SourceInstruction ??
			throw new InvalidOperationException(
				"Machine field reference has no source instruction.");
		return _module.ResolveFieldToken(
			(int)source.Operand!,
			sourceMethod,
			source.Offset);
	}

	private M68kAllocatedFunction EmitAllocatedMethod(
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
						$"{instruction.SourceInstruction?.OpCode.Name}" +
						$"@IL_{instruction.IlOffset:X4}" +
						$" uses=[{string.Join(",", instruction.Uses.Select(value =>
							allocated.Function.Values[value].PrecoloredRegister?.ToString() ?? "uncolored"))}]")
						.Distinct()));
		}

		_emittingUnwindMethod = method;
		_emittingAllocatedFunction = allocated;
		_allocatedSuppressedInstructions.Clear();
		_allocatedFoldedCopyConstants.Clear();
		_allocatedFoldedCopyAddresses.Clear();
		_allocatedConstantMultiplies.Clear();
		_allocatedQuickArithmetic.Clear();
		_allocatedDeferredNormalizations.Clear();
		_allocatedExceptionFrameStores.Clear();
		_allocatedKnownNonNullValues.Clear();
		_allocatedKnownNonNullRegisters.Clear();
		_allocatedFrameAddressesEmitted.Clear();
		_allocatedFunctionAddressSwitches.Clear();
		PrepareAllocatedConstantCopies(method, allocated);
		PrepareAllocatedLiteralAddressCopies(method, allocated);
		PrepareAllocatedQuickArithmetic(allocated);
		PrepareAllocatedConstantMultiplies(allocated);
		PrepareAllocatedIntegerArithmetic(allocated);
		PrepareAllocatedDeferredNormalizations(allocated);
		PrepareAllocatedFrameAddressFolds(method, allocated);
		PrepareAllocatedExceptionFrameStores(allocated);
		PrepareAllocatedFunctionAddressSwitches(method, allocated);
		allocated = RefineAllocatedPreservation(allocated);
		allocated = ReuseIncomingArgumentHomes(abi, allocated);
		_emittingAllocatedFunction = allocated;
		// Finalize preservation before recording either unwind layouts or
		// incoming stack displacements. No already-emitted frame is resized.
		RecordUnwindLayout(method, abi, allocated);
		EmitAllocatedCalleeSaves(allocated.Frame.CalleeSavedRegisters);
		EmitAllocateFrame(allocated.Frame.FrameBytes);
		if (allocated.Function.HasDynamicStackAllocation)
		{
			_assembler.EmitWord(0x2A4F); // MOVEA.L A7,A5 fixed-frame anchor
		}
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
		var nonNullBlockEntries = CreateAllocatedNonNullBlockEntryFacts(
			method,
			allocated);
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
		for (var blockIndex = 0; blockIndex < layoutBlocks.Length; blockIndex++)
		{
			var block = layoutBlocks[blockIndex];
			_allocatedKnownNonNullValues.Clear();
			_allocatedKnownNonNullRegisters.Clear();
			if (nonNullBlockEntries.TryGetValue(block.Id, out var knownNonNull))
			{
				_allocatedKnownNonNullValues.UnionWith(knownNonNull);
				foreach (var value in knownNonNull.Where(
					allocated.BlockLiveness.LiveIn[block.Id].Contains))
				{
					AddAllocatedNonNullValueRegister(allocated, value);
				}
			}
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
					// Suppressed entry arguments and their canonical copies were
					// already materialized by EmitAllocatedIncomingArguments.  They
					// still participate in SSA dataflow: skipping their transfer
					// facts loses properties such as the inherent non-nullness of a
					// managed by-reference argument and emits a redundant fatal check
					// at its first use.
					_allocatedKnownNonNullValues.UnionWith(
						GetAllocatedNonNullTransferValues(
							method,
							allocated,
							instruction));
					PropagateAllocatedNonNullCopy(
						instruction,
						_allocatedKnownNonNullValues);
					UpdateAllocatedNonNullRegistersAfterInstruction(
						allocated,
						instruction);
					continue;
				}
				_emittingMachineInstruction = instruction;
				_allocatedFrameAddressesEmitted.Clear();
				EmitAllocatedInstruction(
					method,
					abi,
					allocated,
					block,
					instruction,
					savedBytes,
					nextBlockId);
				_allocatedKnownNonNullValues.UnionWith(
					GetAllocatedNonNullTransferValues(
						method,
						allocated,
						instruction));
				PropagateAllocatedNonNullCopy(
					instruction,
					_allocatedKnownNonNullValues);
				UpdateAllocatedNonNullRegistersAfterInstruction(
					allocated,
					instruction);
			}
			if (_allocatedOutgoingStackBytes != 0)
			{
				throw new InvalidOperationException(
					$"Allocated block {block.Id} in '{method.DisplayName}' leaves " +
					$"an unbalanced outgoing argument depth of {_allocatedOutgoingStackBytes} bytes.");
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
		return allocated;
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
			var argumentPosition = entry.Instructions.IndexOf(argument);
			var directSpill = argumentPosition >= 0 &&
				argumentPosition + 1 < entry.Instructions.Count
				? entry.Instructions[argumentPosition + 1]
				: null;
			if (allocated.Function.Values[incoming].IsSpillTemporary &&
				allocated.Function.Values[incoming].Width == M68kMachineValueWidth.Long &&
				directSpill is
				{
					Operation: M68kMachineOperation.SpillStore,
					Uses: [var stored]
				} &&
				stored == incoming)
			{
				var destinationFrame = AllocatedFrameOffset(
					allocated,
					allocated.Frame.SpillOffsets[
						directSpill.SpillSlotIndex!.Value]);
				if (source.Register is { } sourceRegister)
				{
					EmitAllocatedFrameStore(sourceRegister,
						M68kMachineValueWidth.Long, destinationFrame);
				}
				else
				{
					EmitAllocatedIncomingStackToFrame(
						checked(savedBytes + 4 + source.StackOffset),
						destinationFrame);
				}
				_allocatedSuppressedInstructions.Add(argument.Id);
				_allocatedSuppressedInstructions.Add(directSpill.Id);
				continue;
			}
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
			var destination = allocated.Allocation.Registers[destinationValue];
			transfers.Add((argument, copy, source, destination.Register));
			if (destination.IsPair)
			{
				var lowSource = source with
				{
					Register = source.LowRegister,
					LowRegister = null,
					StackOffset = source.IsStack
						? checked(source.StackOffset + 4)
						: -1,
					SlotLongs = 1
				};
				transfers.Add((
					argument,
					copy,
					lowSource,
					(M68kRegister)((int)destination.Register + 1)));
			}
			_allocatedSuppressedInstructions.Add(argument.Id);
			if (copy is not null)
			{
				_allocatedSuppressedInstructions.Add(copy.Id);
			}
		}

		var parallelCopies = transfers
			.Select(transfer => new M68kParallelCopy(
				M68kStorageLocation.Register(transfer.Destination),
				transfer.Source.Register is { } sourceRegister
					? M68kStorageLocation.Register(sourceRegister)
					: M68kStorageLocation.Spill(transfer.Source.StackOffset)))
			.ToArray();
		var resolved = M68kParallelCopyResolver.Resolve(
			parallelCopies,
			M68kStorageLocation.Temporary());
		if (resolved.Any(static copy =>
			copy.Source.Kind == M68kStorageKind.Temporary ||
			copy.Destination.Kind == M68kStorageKind.Temporary))
		{
			var registerTransfers = transfers
				.Where(static transfer => transfer.Source.Register is not null)
				.ToArray();
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
				if (copy.Source.Kind == M68kStorageKind.Register)
				{
					EmitAllocatedMove(
						(M68kRegister)copy.Source.Index,
						(M68kRegister)copy.Destination.Index,
						M68kMachineValueWidth.Long);
				}
				else if (copy.Source.Kind == M68kStorageKind.SpillSlot)
				{
					EmitAllocatedStackLoad(
						(M68kRegister)copy.Destination.Index,
						checked(savedBytes + 4 + copy.Source.Index));
				}
				else
				{
					throw new InvalidOperationException(
						"Incoming parallel transfer has an invalid source.");
				}
			}
		}
	}

	private void PrepareAllocatedConstantCopies(
		CilMethod method,
		M68kAllocatedFunction allocated)
	{
		var instructions = allocated.Function.Blocks
			.SelectMany(static block => block.Instructions)
			.ToArray();
		var phiInputs = allocated.Function.Blocks
			.SelectMany(static block => block.Phis)
			.SelectMany(static phi => phi.Inputs.Values)
			.ToHashSet();
		foreach (var constant in instructions.Where(static instruction =>
			instruction.Operation == M68kMachineOperation.Constant &&
			instruction.Definitions.Length == 1))
		{
			var value = constant.Definitions[0];
			var forwarding = new List<M68kMachineInstruction>();
			M68kMachineInstruction[] users;
			do
			{
				if (phiInputs.Contains(value))
				{
					break;
				}
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
			var finalUsers = instructions
				.Where(instruction => instruction.Uses.Contains(value))
				.ToArray();
			if (finalUsers is [var call] &&
				call.Operation == M68kMachineOperation.Call &&
				call.SourceInstruction is { Operand: int token } callSource)
			{
				var target = ResolveAllocatedMachineMethod(method, call);
				if (target.Definition is { } definition &&
					GetInternalRegisterAbi(definition) is { } inlineAbi &&
					TryGetInlineCandidate(definition, inlineAbi, out var candidate) &&
					candidate.Kind == InlineCandidateKind.ConstantAddressWriteWord)
				{
					_allocatedSuppressedInstructions.Add(constant.Id);
					foreach (var copy in forwarding)
					{
						_allocatedSuppressedInstructions.Add(copy.Id);
					}
					continue;
				}
			}
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
		var target = ResolveAllocatedMachineMethod(caller, instruction);
		return target.ImportName is
			"intrinsic:runtime-bitcast-32" or
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

	private void PrepareAllocatedLiteralAddressCopies(
		CilMethod method,
		M68kAllocatedFunction allocated)
	{
		var instructions = allocated.Function.Blocks
			.SelectMany(static block => block.Instructions)
			.ToArray();
		var phiInputs = allocated.Function.Blocks
			.SelectMany(static block => block.Phis)
			.SelectMany(static phi => phi.Inputs.Values)
			.ToHashSet();
		foreach (var address in instructions.Where(instruction =>
			IsAllocatedLiteralAddressMaterialization(method, instruction) &&
			instruction.Definitions.Length == 1))
		{
			var value = address.Definitions[0];
			var forwarding = new List<M68kMachineInstruction>();
			M68kMachineInstruction[] users;
			do
			{
				if (phiInputs.Contains(value))
				{
					break;
				}
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

			if (forwarding.Count == 0)
			{
				continue;
			}
			_allocatedSuppressedInstructions.Add(address.Id);
			foreach (var copy in forwarding.Take(forwarding.Count - 1))
			{
				_allocatedSuppressedInstructions.Add(copy.Id);
			}
			_allocatedFoldedCopyAddresses.Add(forwarding[^1].Id, address);
		}
	}

	private bool IsAllocatedLiteralAddressMaterialization(
		CilMethod caller,
		M68kMachineInstruction instruction)
	{
		if (instruction.Operation != M68kMachineOperation.Call ||
			instruction.SourceInstruction is not { Operand: int token } source)
		{
			return false;
		}
		var name = ResolveAllocatedMachineMethod(caller, instruction).ImportName;
		return name is
			"intrinsic:cstring-from-literal" or
			"intrinsic:amiga-vararg-from-literal";
	}

	private void PrepareAllocatedConstantMultiplies(
		M68kAllocatedFunction allocated)
	{
		if (_request.Cpu != M68kCpuTarget.M68000)
		{
			return;
		}

		var instructions = allocated.Function.Blocks
			.SelectMany(static block => block.Instructions)
			.ToArray();
		foreach (var multiply in instructions.Where(MultiplyIsEligible))
		{
			for (var operand = 0; operand < multiply.Uses.Length; operand++)
			{
				var constantValue = multiply.Uses[operand];
				if (!TryGetAllocatedConstant(
						allocated.Function,
						constantValue,
						out var factor) ||
					factor < 0 ||
					ConstantMultiplyInstructionCount(factor) > 16 ||
					!TryCollectExclusiveConstantChain(
						instructions,
						constantValue,
						multiply,
						out var constantChain))
				{
					continue;
				}

				var sourceValue = multiply.Uses[1 - operand];
				_allocatedConstantMultiplies.Add(
					multiply.Id,
					(factor, sourceValue));
				foreach (var constantInstruction in constantChain)
				{
					_allocatedSuppressedInstructions.Add(constantInstruction.Id);
					_allocatedFoldedCopyConstants.Remove(constantInstruction.Id);
				}
				break;
			}
		}

		bool MultiplyIsEligible(M68kMachineInstruction instruction) =>
			instruction.Operation == M68kMachineOperation.Multiply &&
			!_integerArithmeticPlans.ContainsKey(instruction.Id) &&
			instruction.SourceInstruction?.OpCode != OpCodes.Mul_Ovf_Un &&
			instruction.Uses.Length == 2 &&
			instruction.Definitions.Length == 1 &&
			!CilStackValueLayout.IsSmall(
				allocated.Function.Values[instruction.Definitions[0]].Kind);
	}

	private void PrepareAllocatedQuickArithmetic(M68kAllocatedFunction allocated)
	{
		var instructions = allocated.Function.Blocks
			.SelectMany(static block => block.Instructions)
			.ToArray();
		foreach (var arithmetic in instructions.Where(static instruction =>
			instruction.Operation is
				M68kMachineOperation.Add or M68kMachineOperation.Subtract &&
			instruction.Uses.Length == 2 &&
			instruction.Definitions.Length == 1))
		{
			var constantOperand = 1;
			if (arithmetic.Operation == M68kMachineOperation.Add &&
				TryGetAllocatedConstant(
					allocated.Function,
					arithmetic.Uses[0],
					out _))
			{
				constantOperand = 0;
			}
			var constantValue = arithmetic.Uses[constantOperand];
			if (!TryGetAllocatedConstant(
					allocated.Function,
					constantValue,
					out var constant) ||
				!TryCollectExclusiveConstantChain(
					instructions,
					constantValue,
					arithmetic,
					out var constantChain))
			{
				continue;
			}
			if (arithmetic.Operation == M68kMachineOperation.Subtract &&
				constant == int.MinValue)
			{
				continue;
			}
			var delta = arithmetic.Operation == M68kMachineOperation.Subtract
				? -constant
				: constant;
			if (delta is 0 or < -8 or > 8)
			{
				continue;
			}
			var sourceValue = arithmetic.Uses[1 - constantOperand];
			var destination = allocated.Allocation.Registers[
				arithmetic.Definitions[0]].Register;
			var width = allocated.Function.Values[
				arithmetic.Definitions[0]].Width;
			if (destination >= M68kRegister.A0 &&
				width != M68kMachineValueWidth.Long)
			{
				continue;
			}
			_allocatedQuickArithmetic.Add(
				arithmetic.Id,
				(delta, sourceValue));
			foreach (var constantInstruction in constantChain)
			{
				_allocatedSuppressedInstructions.Add(constantInstruction.Id);
				_allocatedFoldedCopyConstants.Remove(constantInstruction.Id);
			}
		}
	}

	private static int ConstantMultiplyInstructionCount(int factor)
	{
		return factor == 0
			? 1
			: SelectConstantMultiplyPlan(factor).EncodedBytes / 2;
	}

	private static ConstantMultiplyPlan SelectConstantMultiplyPlan(int factor)
	{
		if (factor <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(factor));
		}

		var binaryTerms = Enumerable.Range(0, 31)
			.Where(bit => ((uint)factor & (1u << bit)) != 0)
			.Select(static bit => new ConstantMultiplyTerm(bit, Subtract: false))
			.Reverse()
			.ToArray();
		var binary = Measure(binaryTerms, tieBreak: 0);

		var signedTerms = new List<ConstantMultiplyTerm>();
		var remaining = (long)factor;
		var signedBit = 0;
		while (remaining != 0)
		{
			if ((remaining & 1) != 0)
			{
				var digit = (remaining & 3) == 1 ? 1 : -1;
				signedTerms.Add(new ConstantMultiplyTerm(signedBit, digit < 0));
				remaining -= digit;
			}
			remaining >>= 1;
			signedBit++;
		}
		signedTerms.Reverse();
		var signed = Measure(signedTerms.ToArray(), tieBreak: 1);

		return new[] { binary, signed }
			.OrderBy(static plan => plan.EncodedBytes)
			.ThenBy(static plan => plan.Cycles)
			.ThenBy(static plan => plan.TieBreak)
			.First();

		static ConstantMultiplyPlan Measure(
			ConstantMultiplyTerm[] terms,
			int tieBreak)
		{
			if (terms.Length == 1 && terms[0].Bit == 0 && !terms[0].Subtract)
			{
				return new ConstantMultiplyPlan(terms, EncodedBytes: 2, Cycles: 4, tieBreak);
			}

			var instructions = 2; // Source-to-accumulator and accumulator-to-result moves.
			var cycles = 8;
			var previousBit = terms[0].Bit;
			for (var index = 1; index < terms.Length; index++)
			{
				MeasureShift(previousBit - terms[index].Bit, ref instructions, ref cycles);
				instructions++;
				cycles += 8; // ADD.L/SUB.L Dn,Dn on MC68000.
				previousBit = terms[index].Bit;
			}
			MeasureShift(previousBit, ref instructions, ref cycles);
			return new ConstantMultiplyPlan(terms, instructions * 2, cycles, tieBreak);
		}

		static void MeasureShift(int count, ref int instructions, ref int cycles)
		{
			while (count >= 16)
			{
				instructions += 2;
				cycles += 8; // SWAP + CLR.W after the flag-dead shift-by-16 peephole.
				count -= 16;
			}
			while (count != 0)
			{
				var chunk = Math.Min(count, 8);
				instructions++;
				cycles += 8 + (2 * chunk);
				count -= chunk;
			}
		}
	}

	private static bool TryCollectExclusiveConstantChain(
		IReadOnlyList<M68kMachineInstruction> instructions,
		int value,
		M68kMachineInstruction finalUser,
		out IReadOnlyList<M68kMachineInstruction> chain)
	{
		var result = new List<M68kMachineInstruction>();
		var user = finalUser;
		var visited = new HashSet<int>();
		while (visited.Add(value))
		{
			var users = instructions
				.Where(instruction => instruction.Uses.Contains(value))
				.ToArray();
			var definition = instructions.SingleOrDefault(instruction =>
				instruction.Definitions.Contains(value));
			if (users is not [var onlyUser] ||
				onlyUser.Id != user.Id ||
				definition is null)
			{
				break;
			}

			result.Add(definition);
			if (definition.Operation == M68kMachineOperation.Constant)
			{
				chain = result;
				return true;
			}
			if (definition is not
				{
					Operation: M68kMachineOperation.Copy,
					Uses: [var source]
				})
			{
				break;
			}

			user = definition;
			value = source;
		}

		chain = [];
		return false;
	}

	private void PrepareAllocatedDeferredNormalizations(
		M68kAllocatedFunction allocated)
	{
		var instructions = allocated.Function.Blocks
			.SelectMany(static block => block.Instructions)
			.ToArray();
		var instructionUsers = new Dictionary<int, List<M68kMachineInstruction>>();
		foreach (var instruction in instructions)
		{
			foreach (var use in instruction.Uses)
			{
				if (!instructionUsers.TryGetValue(use, out var users))
				{
					users = [];
					instructionUsers.Add(use, users);
				}
				users.Add(instruction);
			}
		}
		var phiUsers = new Dictionary<int, List<int>>();
		foreach (var phi in allocated.Function.Blocks.SelectMany(
			static block => block.Phis))
		{
			foreach (var input in phi.Inputs.Values.Distinct())
			{
				if (!phiUsers.TryGetValue(input, out var definitions))
				{
					definitions = [];
					phiUsers.Add(input, definitions);
				}
				definitions.Add(phi.Definition);
			}
		}
		foreach (var instruction in instructions.Where(instruction =>
			instruction.Definitions.Length == 1 &&
			IsNarrowIntegerKind(
				allocated.Function.Values[instruction.Definitions[0]].Kind)))
		{
			if (CanDeferNarrowNormalization(
				allocated.Function,
				instruction.Definitions[0],
				allocated.Function.Values[instruction.Definitions[0]].Width,
				instructionUsers,
				phiUsers))
			{
				_allocatedDeferredNormalizations.Add(instruction.Id);
			}
		}
	}

	private static bool IsNarrowIntegerKind(CilStackValueKind kind) =>
		kind is
			CilStackValueKind.BooleanByte or
			CilStackValueKind.UnsignedByte or
			CilStackValueKind.SignedByte or
			CilStackValueKind.UnsignedWord or
			CilStackValueKind.SignedWord;

	private static bool CanDeferNarrowNormalization(
		M68kMachineFunction function,
		int initialValue,
		M68kMachineValueWidth width,
		IReadOnlyDictionary<int, List<M68kMachineInstruction>> instructionUsers,
		IReadOnlyDictionary<int, List<int>> phiUsers)
	{
		var pending = new Stack<int>();
		var visited = new HashSet<int>();
		pending.Push(initialValue);

		while (pending.Count != 0)
		{
			var value = pending.Pop();
			if (!visited.Add(value) || function.Values[value].Width != width)
			{
				continue;
			}

			if (phiUsers.TryGetValue(value, out var phiDefinitions))
			{
				foreach (var phiDefinition in phiDefinitions)
				{
					if (function.Values[phiDefinition].Width != width)
					{
						return false;
					}
					pending.Push(phiDefinition);
				}
			}

			if (instructionUsers.TryGetValue(value, out var users))
			{
				foreach (var user in users)
				{
					if (!ConsumesOnlyNarrowValue(function, user, value, width))
					{
						return false;
					}
				}
			}
		}

		return true;
	}

	private static bool ConsumesOnlyNarrowValue(
		M68kMachineFunction function,
		M68kMachineInstruction instruction,
		int consumedValue,
		M68kMachineValueWidth width)
	{
		if (instruction.Operation is
			M68kMachineOperation.Compare or
			M68kMachineOperation.ConditionalBranch)
		{
			return instruction.Uses.Length != 0 &&
				instruction.Uses.Contains(consumedValue) &&
				function.Values[instruction.Uses[0]].Width == width;
		}

		if (instruction.Operation is
			M68kMachineOperation.LocalStore or
			M68kMachineOperation.ArgumentStore or
			M68kMachineOperation.SpillStore)
		{
			return instruction.Uses.All(value =>
				function.Values[value].Width == width);
		}

		if (instruction.Operation is
			M68kMachineOperation.OutgoingArgumentPush or
			M68kMachineOperation.Return)
		{
			return instruction.Uses.Length == 1 &&
				function.Values[instruction.Uses[0]].Width == width;
		}

		if (instruction.Operation == M68kMachineOperation.Store &&
			instruction.SourceInstruction is { } storeSource &&
			instruction.Uses.Length >= 1)
		{
			return AllocatedIndirectWidth(storeSource.OpCode) == width &&
				function.Values[instruction.Uses[^1]].Width == width;
		}

		if (instruction.Operation == M68kMachineOperation.ArrayStore &&
			instruction.SourceInstruction is { } arrayStoreSource &&
			arrayStoreSource.OpCode != OpCodes.Stelem &&
			instruction.Uses.Length == 3)
		{
			var access = GetArrayAccess(arrayStoreSource.OpCode);
			var accessWidth = access.Size == 1
				? M68kMachineValueWidth.Byte
				: access.Size == 2
					? M68kMachineValueWidth.Word
					: M68kMachineValueWidth.Long;
			return accessWidth == width &&
				function.Values[instruction.Uses[2]].Width == width;
		}

		if (instruction.Operation == M68kMachineOperation.Convert)
		{
			return instruction.SourceInstruction?.OpCode is var op &&
				(op == OpCodes.Conv_I1 ||
				 op == OpCodes.Conv_U1 ||
				 op == OpCodes.Conv_I2 ||
				 op == OpCodes.Conv_U2);
		}

		if (instruction.Operation == M68kMachineOperation.Copy)
		{
			return instruction.Definitions.Length == 1 &&
				(function.Values[instruction.Definitions[0]].Width == width ||
				 function.Values[instruction.Definitions[0]].Width ==
					M68kMachineValueWidth.Long);
		}

		if (instruction.Operation is
			M68kMachineOperation.Add or
			M68kMachineOperation.Subtract or
			M68kMachineOperation.Multiply or
			M68kMachineOperation.And or
			M68kMachineOperation.Or or
			M68kMachineOperation.Xor or
			M68kMachineOperation.Negate or
			M68kMachineOperation.Not or
			M68kMachineOperation.Shift)
		{
			return instruction.Definitions.Length != 0 &&
				instruction.Definitions.All(value =>
					function.Values[value].Width == width);
		}

		return false;
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
			EmitEhFrameImmediate(0, RuntimeFrameStateOffset);
		}
		else
		{
			EmitEhFrameAddress(stateLabel, RuntimeFrameStateOffset);
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
			M68kMachineOperation.AggregateFieldLoad or
			M68kMachineOperation.AggregateArrayLoad or
			M68kMachineOperation.AggregateArrayStore or
			M68kMachineOperation.PlatformBaseLoad or
			M68kMachineOperation.PlatformBaseStore or
			M68kMachineOperation.AggregateIndirectLoad or
			M68kMachineOperation.AggregateIndirectStore or
			M68kMachineOperation.AggregateIndirectCopy or
			M68kMachineOperation.AggregateIndirectInitialize or
			M68kMachineOperation.SpillLoad or
			M68kMachineOperation.SpillStore or
			M68kMachineOperation.SpillClear or
			M68kMachineOperation.RootStore or
			M68kMachineOperation.RootClear or
			M68kMachineOperation.ByrefOwnerKeepAlive or
			M68kMachineOperation.GcKeepAlive or
			M68kMachineOperation.OutgoingArgumentPush or
			M68kMachineOperation.IncomingArgumentPush or
			M68kMachineOperation.OutgoingArgumentReserve or
			M68kMachineOperation.ReturnBufferAddress or
			M68kMachineOperation.BulkCopy or
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
			M68kMachineOperation.TypeTest or
			M68kMachineOperation.TypeInitialize or
			M68kMachineOperation.FunctionAddress or
			M68kMachineOperation.Call or
			M68kMachineOperation.Branch or
			M68kMachineOperation.ConditionalBranch or
			M68kMachineOperation.Switch or
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
			M68kMachineOperation.LocalStore or
			M68kMachineOperation.ArgumentStore)
		{
			if (instruction.ArgumentIndex is not { } frameIndex)
			{
				return false;
			}
			return instruction.Operation is
				M68kMachineOperation.ArgumentLoad or
				M68kMachineOperation.ArgumentStore
				? allocated.Frame.ArgumentHomeOffsets.ContainsKey(frameIndex)
				: allocated.Frame.LocalOffsets.ContainsKey(frameIndex);
		}
		if (instruction.Operation == M68kMachineOperation.LocalAddress)
		{
			return instruction.ArgumentIndex is { } localIndex &&
				allocated.Frame.LocalOffsets.ContainsKey(localIndex);
		}
		if (instruction.Operation == M68kMachineOperation.DynamicStackAllocate)
		{
			return instruction.Uses.Length == 1 &&
				instruction.Definitions.Length == 1;
		}
		if (instruction.Operation == M68kMachineOperation.ArgumentAddress)
		{
			return instruction.ArgumentIndex is { } argumentIndex &&
				allocated.Frame.ArgumentHomeOffsets.ContainsKey(argumentIndex);
		}
		if (instruction.Operation == M68kMachineOperation.ObjectAllocate)
		{
			return _memoryManagement != M68kMemoryManagement.None &&
				instruction.SourceInstruction?.OpCode == OpCodes.Newobj;
		}
		if (instruction.Operation == M68kMachineOperation.DelegateCreate)
		{
			return _memoryManagement != M68kMemoryManagement.None &&
				instruction.SourceInstruction is { OpCode: var delegateOp, Operand: int token } &&
				delegateOp == OpCodes.Newobj &&
				_module.ResolveMethodToken(
					token,
					caller,
					instruction.IlOffset).ImportName == "intrinsic:delegate-ctor";
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
		if (instruction.Operation is M68kMachineOperation.Box or M68kMachineOperation.Unbox)
		{
			return instruction.SourceInstruction?.OpCode is var boxOp &&
				(boxOp == OpCodes.Box || boxOp == OpCodes.Unbox || boxOp == OpCodes.Unbox_Any);
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
			 source.OpCode != OpCodes.Callvirt &&
			 source.OpCode != OpCodes.Newobj))
		{
			return false;
		}
		var target = ResolveAllocatedMachineMethod(caller, instruction);
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

		if (_allocatedFoldedCopyAddresses.TryGetValue(
				instruction.Id,
				out var foldedAddress))
		{
			EmitAllocatedLiteralAddress(
				method,
				foldedAddress,
				Location(instruction.Definitions[0]).Register);
			return;
		}

		if (_allocatedFoldedCopyConstants.TryGetValue(
				instruction.Id,
				out var foldedConstant))
		{
			EmitAllocatedConstant(
				method,
				foldedConstant,
				Location(instruction.Definitions[0]));
			EmitAllocatedDefinitionNormalization(
				allocated,
				instruction,
				Location(instruction.Definitions[0]).Register);
			return;
		}

		if (_allocatedQuickArithmetic.TryGetValue(
				instruction.Id,
				out var quickArithmetic))
		{
			var destination = Location(instruction.Definitions[0]).Register;
			var width = allocated.Function.Values[instruction.Definitions[0]].Width;
			EmitAllocatedMove(
				Location(quickArithmetic.SourceValue).Register,
				destination,
				width);
			EmitAllocatedQuickImmediate(
				destination,
				quickArithmetic.Delta,
				width);
			EmitAllocatedDefinitionNormalization(
				allocated,
				instruction,
				destination);
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
						var destination = Location(instruction.Definitions[0]);
						EmitAllocatedRequireNonNull(instruction.Uses[0], address);
						if (destination.IsPair)
						{
							EmitAllocatedBaseLoad(
								address,
								destination.Register,
								M68kMachineValueWidth.Long,
								checked((short)instruction.MemoryOffset));
							EmitAllocatedBaseLoad(
								address,
								(M68kRegister)((int)destination.Register + 1),
								M68kMachineValueWidth.Long,
								checked((short)(instruction.MemoryOffset + 4)));
						}
						else
						{
							EmitAllocatedBaseLoad(
								address,
								destination.Register,
								instruction.SourceInstruction.OpCode == OpCodes.Ldobj
									? allocated.Function.Values[
										instruction.Definitions[0]].Width
									: AllocatedIndirectWidth(
										instruction.SourceInstruction.OpCode),
								checked((short)instruction.MemoryOffset));
							EmitAllocatedDefinitionNormalization(
								allocated,
								instruction,
								destination.Register);
						}
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
					var source = Location(instruction.Uses[1]);
					EmitAllocatedRequireNonNull(instruction.Uses[0], address);
						EmitAllocatedBaseStore(
							source.Register,
							address,
							source.IsPair
								? M68kMachineValueWidth.Long
								: instruction.SourceInstruction.OpCode == OpCodes.Stobj
									? allocated.Function.Values[instruction.Uses[1]].Width
									: AllocatedIndirectWidth(instruction.SourceInstruction.OpCode),
						checked((short)instruction.MemoryOffset));
					if (source.IsPair)
					{
						EmitAllocatedBaseStore(
							(M68kRegister)((int)source.Register + 1),
							address,
							M68kMachineValueWidth.Long,
							checked((short)(instruction.MemoryOffset + 4)));
					}
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

			case M68kMachineOperation.AggregateFieldLoad:
				EmitAllocatedAggregateFieldLoad(
					method,
					allocated,
					instruction);
				return;

			case M68kMachineOperation.LocalLoad:
			case M68kMachineOperation.ArgumentLoad:
				EmitAllocatedFrameLoad(
					Location(instruction.Definitions[0]).Register,
					allocated.Function.Values[instruction.Definitions[0]].Width,
						checked((short)(AllocatedFrameOffset(
							allocated,
							instruction.Operation == M68kMachineOperation.ArgumentLoad
								? allocated.Frame.ArgumentHomeOffsets[
									instruction.ArgumentIndex!.Value]
								: allocated.Frame.LocalOffsets[
									instruction.ArgumentIndex!.Value]) +
							instruction.MemoryOffset)));
				EmitAllocatedDefinitionNormalization(
					allocated,
					instruction,
					Location(instruction.Definitions[0]).Register);
				return;

			case M68kMachineOperation.LocalStore:
				var storedValue = allocated.Function.Values[instruction.Uses[0]];
				var localHome = allocated.Function.LocalHomes[
					instruction.ArgumentIndex!.Value];
				CilTypeLayout? storedLayout = null;
				var storesAggregate = localHome.Index < method.Locals.Length &&
					_module.TryGetReferenceFreeStructLayout(
						method.Locals[localHome.Index],
						method.ModuleName,
						out storedLayout) &&
					storedLayout.Size > 4;
				if (storedValue.Kind == CilStackValueKind.AggregateAddress &&
					storesAggregate)
				{
					var source = Location(instruction.Uses[0]).Register;
					if (source < M68kRegister.A0)
					{
						throw new InvalidOperationException(
							"Aggregate local-store source was not allocated to an address register.");
					}
					var destination = AllocatedFrameOffset(
						allocated,
						allocated.Frame.LocalOffsets[localHome.Index]);
					for (var offset = 0; offset < storedLayout!.Size; offset += 4)
					{
						EmitAllocatedBaseLoad(
							source,
							M68kRegister.D0,
							M68kMachineValueWidth.Long,
							checked((short)offset));
						EmitAllocatedFrameStore(
							M68kRegister.D0,
							M68kMachineValueWidth.Long,
							checked(destination + offset));
					}
					return;
				}
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
					localHome.Index < method.Locals.Length
						? AllocatedFrameStorageWidth(
							method.Locals[localHome.Index],
							storedValue.Width)
						: storedValue.Width,
					checked((short)(AllocatedFrameOffset(
						allocated,
						allocated.Frame.LocalOffsets[
							instruction.ArgumentIndex!.Value]) +
						instruction.MemoryOffset)));
				return;

			case M68kMachineOperation.ArgumentStore:
				var storedArgumentIndex = instruction.ArgumentIndex!.Value;
				var storedArgumentType = TypeForArgument(
					method,
					storedArgumentIndex);
				if (!_module.TryGetReferenceFreeStructLayout(
						storedArgumentType,
						method.ModuleName,
						out var storedArgumentLayout) ||
					storedArgumentLayout.Size <= 4 ||
					allocated.Function.Values[instruction.Uses[0]].Kind !=
						CilStackValueKind.AggregateAddress)
				{
					throw new InvalidOperationException(
						"Aggregate argument store has an invalid source or destination type.");
				}
				var argumentSource = Location(instruction.Uses[0]).Register;
				if (argumentSource < M68kRegister.A0)
				{
					throw new InvalidOperationException(
						"Aggregate argument-store source was not allocated to an address register.");
				}
				var argumentDestination = AllocatedFrameOffset(
					allocated,
					allocated.Frame.ArgumentHomeOffsets[storedArgumentIndex]);
				for (var offset = 0;
					offset < storedArgumentLayout.Size;
					offset += 4)
				{
					EmitAllocatedBaseLoad(
						argumentSource,
						M68kRegister.D0,
						M68kMachineValueWidth.Long,
						checked((short)offset));
					EmitAllocatedFrameStore(
						M68kRegister.D0,
						M68kMachineValueWidth.Long,
						checked(argumentDestination + offset));
				}
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

			case M68kMachineOperation.DynamicStackAllocate:
				EmitAllocatedDynamicStackAllocation(allocated, instruction);
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
					EmitAllocatedArrayAccess(method, allocated, instruction);
				}
				return;

			case M68kMachineOperation.AggregateArrayLoad:
			case M68kMachineOperation.AggregateArrayStore:
				EmitAllocatedAggregateArrayAccess(
					method,
					allocated,
					instruction);
				return;

			case M68kMachineOperation.PlatformBaseLoad:
				EmitAllocatedPlatformBaseLoad(
					method,
					instruction,
					Location(instruction.Definitions[0]).Register);
				return;

			case M68kMachineOperation.PlatformBaseStore:
				EmitAllocatedPlatformBaseStore(
					method,
					allocated,
					instruction);
				return;

			case M68kMachineOperation.AggregateIndirectLoad:
			case M68kMachineOperation.AggregateIndirectStore:
			case M68kMachineOperation.AggregateIndirectCopy:
			case M68kMachineOperation.AggregateIndirectInitialize:
				EmitAllocatedAggregateIndirectOperation(
					method,
					allocated,
					instruction);
				return;

		case M68kMachineOperation.Constant:
				EmitAllocatedConstant(
					method,
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
				EmitAllocatedDefinitionNormalization(
					allocated,
					instruction,
					Location(instruction.Definitions[0]).Register);
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

			case M68kMachineOperation.ByrefOwnerKeepAlive:
			case M68kMachineOperation.GcKeepAlive:
				return;

			case M68kMachineOperation.OutgoingArgumentReserve:
				var reservedBytes = instruction.ArgumentIndex!.Value;
				if (reservedBytes <= short.MaxValue)
				{
					EmitAllocateFrame(reservedBytes);
				}
				else
				{
					_assembler.EmitWord(0x9FFC); // SUBA.L #bytes,A7
					_assembler.EmitLong((uint)reservedBytes);
				}
				_allocatedOutgoingStackBytes = checked(_allocatedOutgoingStackBytes + reservedBytes);
				EmitAllocatedStackPointerToAddressRegister(Location(instruction.Definitions[0]).Register);
				return;

			case M68kMachineOperation.ReturnBufferAddress:
				EmitAllocatedStackLoad(Location(instruction.Definitions[0]).Register,
					checked(savedBytes + 4 + _allocatedOutgoingStackBytes +
						(abi.ReturnBufferStackOffset ?? throw new InvalidOperationException("Method has no hidden return buffer."))));
				return;

			case M68kMachineOperation.BulkCopy:
				EmitAllocatedBulkCopy(instruction);
				return;

			case M68kMachineOperation.OutgoingArgumentPush:
			{
				var location = Location(instruction.Uses[0]);
				var value = allocated.Function.Values[instruction.Uses[0]];
				var bytes = instruction.ArgumentIndex!.Value;
				var emittedNarrowScratch =
					TryEmitAllocatedZeroExtendedStackArgument(
						allocated,
						instruction,
						location.Register,
						value.Kind);
				if (!emittedNarrowScratch)
				{
					EmitAllocatedNormalize(location.Register, value.Kind);
				}
				if ((value.Kind is
						CilStackValueKind.ManagedPointer or
						CilStackValueKind.AggregateAddress) &&
					bytes > 4)
				{
					if (location.Register < M68kRegister.A0)
					{
						throw new InvalidOperationException(
							"Aggregate argument source was not allocated to an address register.");
					}
					var addressRegister =
						(int)location.Register - (int)M68kRegister.A0;
					for (var offset = bytes - 4; offset >= 0; offset -= 4)
					{
						_assembler.EmitWord((ushort)(
							0x2F28 | addressRegister)); // MOVE.L d16(An),-(A7)
						_assembler.EmitWord(unchecked((ushort)(short)offset));
					}
				}
				else if (value.Width == M68kMachineValueWidth.LongPair)
				{
					EmitAllocatedPush((M68kRegister)((int)location.Register + 1));
					EmitAllocatedPush(location.Register);
				}
				else if (!emittedNarrowScratch)
				{
					EmitAllocatedPush(location.Register);
				}
				_allocatedOutgoingStackBytes = checked(
					_allocatedOutgoingStackBytes +
					bytes);
				return;
			}

			case M68kMachineOperation.IncomingArgumentPush:
			{
				var source = abi.Arguments[instruction.SpillSlotIndex!.Value];
				if (!source.IsStack || source.SlotLongs != 1)
				{
					throw new InvalidOperationException(
						"Forwarded incoming argument push requires one stack slot.");
				}
				EmitAllocatedStackLoad(
					M68kRegister.D0,
					checked(
						savedBytes +
						4 +
						source.StackOffset +
						_allocatedOutgoingStackBytes));
				EmitAllocatedPush(M68kRegister.D0);
				_allocatedOutgoingStackBytes = checked(
					_allocatedOutgoingStackBytes + 4);
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
				var copiedValue = allocated.Function.Values[instruction.Uses[0]];
				var copiedKind = copiedValue.Kind;
				var copyDestinationValue =
					allocated.Function.Values[instruction.Definitions[0]];
				var copySource = Location(instruction.Uses[0]).Register;
				var copyDestination =
					Location(instruction.Definitions[0]).Register;
				if (copyDestination >= M68kRegister.A0 &&
					copyDestinationValue.Width == M68kMachineValueWidth.Long &&
					copyDestinationValue.Kind == CilStackValueKind.Int32 &&
					copiedValue.Width is
						M68kMachineValueWidth.Byte or M68kMachineValueWidth.Word)
				{
					// Narrow internal-call values use an A register only as a canonical
					// 32-bit transport slot. Normalize while the value is still in its
					// legal data register, then transfer the complete longword.
					EmitAllocatedNormalize(copySource, copiedKind);
					EmitAllocatedMove(
						copySource,
						copyDestination,
						M68kMachineValueWidth.Long);
					return;
				}
				if (copySource >= M68kRegister.A0 &&
					copiedValue.Width == M68kMachineValueWidth.Long &&
					copyDestination <= M68kRegister.D7 &&
					copyDestinationValue.Width is
						M68kMachineValueWidth.Byte or M68kMachineValueWidth.Word)
				{
					// The reverse internal-call adapter receives a canonical narrow
					// scalar through an address register.  Transfer its complete
					// longword into the legal data bank before applying the target's
					// signed/unsigned normalization; MOVE.B cannot address An.
					EmitAllocatedMove(
						copySource,
						copyDestination,
						M68kMachineValueWidth.Long);
					EmitAllocatedDefinitionNormalization(
						allocated,
						instruction,
						copyDestination);
					return;
				}
				EmitAllocatedMove(
					copySource,
					copyDestination,
					copyDestinationValue.Width);
				EmitAllocatedDefinitionNormalization(
					allocated,
					instruction,
					copyDestination,
					IsNarrowIntegerKind(copiedKind) ? copiedKind : null);
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
				if (instruction.SourceInstruction?.OpCode == OpCodes.Add_Ovf)
				{
					var noOverflow = UniqueLabel("checked-add-no-overflow");
					_assembler.EmitBranch(M68kCondition.OverflowClear, noOverflow);
					EmitExceptionRaise(reason: 4, hasException: false);
					_assembler.Mark(noOverflow);
				}
				EmitAllocatedDefinitionNormalization(
					allocated,
					instruction,
					Location(instruction.Definitions[0]).Register);
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
				if (instruction.SourceInstruction?.OpCode == OpCodes.Mul_Ovf_Un)
				{
					EmitAllocatedCheckedUnsignedMultiply();
				}
				else if (TryEmitAllocatedIntegerMultiply(allocated, instruction))
				{
				}
				else if (_allocatedConstantMultiplies.TryGetValue(
					instruction.Id,
					out var constantMultiply))
				{
					var source = Location(constantMultiply.SourceValue).Register;
					if (constantMultiply.Factor == 0)
					{
						EmitAllocatedImmediate(0, M68kRegister.D0);
					}
					else if (constantMultiply.Factor == 1)
					{
						EmitAllocatedMove(
							source,
							M68kRegister.D0,
							M68kMachineValueWidth.Long);
					}
					else
					{
						EmitAllocatedMultiplyByConstant(
							source,
							M68kRegister.D2,
							constantMultiply.Factor);
						EmitAllocatedMove(
							M68kRegister.D2,
							M68kRegister.D0,
							M68kMachineValueWidth.Long);
					}
				}
				else
				{
					EmitMultiply(allocated.Function.Values[instruction.Definitions[0]].Kind);
				}
				EmitAllocatedDefinitionNormalization(
					allocated,
					instruction,
					M68kRegister.D0);
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
				if (TryEmitAllocatedIntegerDivide(instruction)) return;
				EmitAllocatedGeneralDivide(
					instruction,
					_allocatedKnownNonNullValues.Contains(instruction.Uses[1]) ||
					TryGetAllocatedConstant(
						allocated.Function,
						instruction.Uses[1],
						out var divisor) && divisor != 0);
				return;

			case M68kMachineOperation.Shift:
				EmitShift(
					instruction.SourceInstruction!.OpCode,
					allocated.Function.Values[instruction.Definitions[0]].Kind,
					instruction.Immediate);
				EmitAllocatedDefinitionNormalization(
					allocated,
					instruction,
					M68kRegister.D0);
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
				EmitAllocatedDefinitionNormalization(
					allocated,
					instruction,
					Location(instruction.Definitions[0]).Register);
				return;

			case M68kMachineOperation.Convert:
				EmitAllocatedConversion(
					instruction.SourceInstruction!.OpCode,
					Location(instruction.Uses[0]).Register,
					allocated.Function.Values[instruction.Uses[0]].Width,
					Location(instruction.Definitions[0]).Register,
					allocated.Function.Values[instruction.Definitions[0]].Width,
					normalize: !_allocatedDeferredNormalizations.Contains(
						instruction.Id));
				return;

			case M68kMachineOperation.Compare:
				var comparisonWidth =
					allocated.Function.Values[instruction.Uses[0]].Width;
				if (comparisonWidth == M68kMachineValueWidth.LongPair)
				{
					if (instruction.Uses.Length != 2 ||
						instruction.Definitions.Length != 1)
					{
						throw new InvalidOperationException(
							"Int64 comparison must materialize one Boolean result.");
					}
					EmitAllocatedInt64Comparison(
						instruction.SourceInstruction!.OpCode,
						Location(instruction.Uses[0]).Register,
						Location(instruction.Uses[1]).Register,
						Location(instruction.Definitions[0]).Register);
					return;
				}
				if (instruction.Uses.Length == 1 && instruction.Immediate == 0)
				{
					EmitAllocatedTest(
						Location(instruction.Uses[0]).Register,
						comparisonWidth);
				}
				else if (instruction.Uses.Length == 2 && instruction.Immediate is null)
				{
					EmitAllocatedCompare(
						Location(instruction.Uses[0]).Register,
						Location(instruction.Uses[1]).Register,
						comparisonWidth);
				}
				else
				{
					throw new InvalidOperationException(
						"Scalar comparison must have two operands or an implicit zero.");
				}
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

			case M68kMachineOperation.TypeInitialize:
				EmitAllocatedTypeInitialization(method, instruction);
				return;

			case M68kMachineOperation.TypeTest:
				EmitAllocatedTypeTest(method, allocated, instruction);
				return;

			case M68kMachineOperation.FunctionAddress:
				EmitAllocatedFunctionAddress(method, allocated, instruction);
				return;

			case M68kMachineOperation.DelegateCreate:
				EmitAllocatedDelegateCreate(method, instruction);
				return;

			case M68kMachineOperation.Box:
				EmitAllocatedBox(method, instruction);
				return;

			case M68kMachineOperation.Unbox:
				EmitAllocatedUnbox(method, allocated, instruction);
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

			case M68kMachineOperation.Switch:
				EmitAllocatedSwitch(method, allocated, block, instruction);
				return;

			case M68kMachineOperation.Return:
				if (instruction.ReturnBufferWritten)
				{
					if (abi.ReturnBufferStackOffset is null || instruction.Uses.Length != 0)
						throw new InvalidOperationException("Explicit aggregate return copy has an invalid ABI shape.");
				}
				else if (abi.ReturnBufferStackOffset is { } returnBufferOffset)
				{
					if (!_module.TryGetReferenceFreeStructLayout(
							method.Signature.ReturnType,
							method.ModuleName,
							out var returnLayout) ||
						returnLayout.Size <= 4 ||
						instruction.Uses.Length != 1)
					{
						throw new InvalidOperationException(
							$"Multiword return in '{method.DisplayName}' has an invalid ABI shape.");
					}
					var source = Location(instruction.Uses[0]).Register;
					if (source < M68kRegister.A0)
					{
						throw new InvalidOperationException(
							"Multiword return source was not allocated to an address register.");
					}
					var destination = source == M68kRegister.A0
						? M68kRegister.A1
						: M68kRegister.A0;
					EmitAllocatedStackLoad(
						destination,
						checked(savedBytes + 4 + returnBufferOffset));
					for (var offset = 0; offset < returnLayout.Size; offset += 4)
					{
						EmitAllocatedBaseLoad(
							source,
							M68kRegister.D0,
							M68kMachineValueWidth.Long,
							checked((short)offset));
						EmitAllocatedBaseStore(
							M68kRegister.D0,
							destination,
							M68kMachineValueWidth.Long,
							checked((short)offset));
					}
				}
				else if (instruction.Uses.Length != 0)
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
				EmitAllocatedReturn(method);
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
		var field = ResolveAllocatedField(method, instruction);
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
		displacement = checked((short)(displacement + instruction.MemoryOffset));
		if (!_module.IsTransparentScalarField(field))
		{
			EmitAllocatedRequireNonNull(instruction.Uses[0], objectRegister);
		}
		var destination =
			allocated.Allocation.Registers[instruction.Definitions[0]];
		if (source.OpCode == OpCodes.Ldflda)
		{
			EmitAllocatedBaseAddress(
				objectRegister,
				destination.Register,
				displacement);
			return;
		}
		var width = InstanceFieldAccessWidth(field, instruction.MemorySize);
		EmitAllocatedBaseLoad(
			objectRegister,
			destination.Register,
			width,
			displacement);
		if (destination.IsPair)
		{
			EmitAllocatedBaseLoad(
				objectRegister,
				(M68kRegister)((int)destination.Register + 1),
				M68kMachineValueWidth.Long,
				checked((short)(displacement + 4)));
		}
		else if (width is M68kMachineValueWidth.Byte or M68kMachineValueWidth.Word)
		{
			// A field address and ldfld refer to the same leading byte/word,
			// even when the containing managed layout reserves a four-byte slot.
			EmitAllocatedNormalize(destination.Register,
				CilStackAnalyzer.StackKindForType(field.Type));
		}
	}

	private void EmitAllocatedAggregateFieldLoad(
		CilMethod method,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		var source = instruction.SourceInstruction!;
		var field = ResolveAllocatedField(method, instruction);
		if (!_module.TryGetReferenceFreeStructLayout(
				field.Type,
				field.ModuleName,
				out var layout) ||
			layout.Size <= 4)
		{
			throw new InvalidOperationException(
				"Aggregate field load resolved a non-aggregate field.");
		}

		M68kRegister fieldBase;
		short fieldDisplacement;
		if (field.IsStatic)
		{
			_staticFields.TryAdd(field.Identity, field);
			fieldBase = M68kRegister.A1;
			fieldDisplacement = 0;
			EmitAllocatedAbsoluteAddress(
				fieldBase,
				StaticFieldLabel(field));
		}
		else
		{
			if (instruction.Uses.Length != 1)
			{
				throw new InvalidOperationException(
					"Aggregate instance field load has no object operand.");
			}
			fieldBase = allocated.Allocation.Registers[
				instruction.Uses[0]].Register;
			fieldDisplacement = FieldDisplacement(field);
			EmitAllocatedRequireNonNull(instruction.Uses[0], fieldBase);
		}

		var destination = AllocatedFrameOffset(
			allocated,
			allocated.Frame.LocalOffsets[instruction.ArgumentIndex!.Value]);
		for (var offset = 0; offset < layout.Size; offset += 4)
		{
			EmitAllocatedBaseLoad(
				fieldBase,
				M68kRegister.D0,
				M68kMachineValueWidth.Long,
				checked((short)(fieldDisplacement + offset)));
			EmitAllocatedFrameStore(
				M68kRegister.D0,
				M68kMachineValueWidth.Long,
				checked(destination + offset));
		}
		if (instruction.Definitions.Length == 1)
		{
			EmitAllocatedFrameAddress(
				allocated.Allocation.Registers[
					instruction.Definitions[0]].Register,
				destination);
		}
	}

	private void EmitAllocatedAbsoluteAddress(
		M68kRegister destination,
		string label)
	{
		var destinationEa = destination <= M68kRegister.D7
			? (int)destination << 9
			: (((int)destination - (int)M68kRegister.A0) << 9) | 0x40;
		_assembler.EmitWord((ushort)(0x203C | destinationEa));
		_assembler.EmitAddress(label);
	}

	private void EmitAllocatedObjectAllocation(
		CilMethod method,
		M68kMachineInstruction instruction)
	{
		EnsureManagedAllocationAllowed(
			method,
			instruction.SourceInstruction!,
			"object construction");
		var constructorReference = _module.ResolveMethodToken(
			(int)instruction.SourceInstruction!.Operand!,
			method,
			instruction.IlOffset);
		var constructor = constructorReference.Definition ??
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"Could not resolve allocated object constructor.",
				method.DisplayName,
				instruction.IlOffset);
		var layout = _module.GetTypeLayout(constructor);
		var descriptorLabel = TypeDescriptorLabel(layout);
		if (constructor.ConstructedDeclaringType is { } constructedType)
		{
			_constructedTypeDescriptors.TryAdd(
				constructedType.DisplayName,
				(constructedType, layout));
			descriptorLabel = ConstructedTypeDescriptorLabel(layout, constructedType);
		}
		else
		{
			_usedTypeLayouts.TryAdd(layout.Identity, layout);
		}
		EmitAllocatedImmediate(layout.Size, M68kRegister.D0);
		EmitManagedAllocationFromD0(layout.Size);
		_assembler.EmitWord(0x2040); // MOVEA.L D0,A0
		_assembler.EmitWord(0x20BC); // MOVE.L #descriptor,(A0)
		_assembler.EmitAddress(descriptorLabel);
		EmitAllocatedImmediate(layout.Size, M68kRegister.D1);
		EmitAllocatedBaseStore(
			M68kRegister.D1,
			M68kRegister.A0,
			M68kMachineValueWidth.Long,
			4);
		if (_module.TryGetEffectiveFinalizer(layout) is not null)
		{
			// ObjectAllocate defines its result in A0. Register it after the
			// descriptor is valid and before the separately lowered constructor.
			EmitPushRegister(M68kRegister.A0);
			_assembler.EmitWord(0x2008); // MOVE.L A0,D0
			_assembler.EmitBsr(RuntimeRegisterFinalizerLabel);
			EmitPopRegister(M68kRegister.A0);
		}
	}

	private void EmitAllocatedFunctionAddress(
		CilMethod caller,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		var source = instruction.SourceInstruction ??
			throw new InvalidOperationException("Function address has no source instruction.");
		var target = _module.ResolveMethodToken(
			(int)source.Operand!,
			caller,
			instruction.IlOffset).Definition;
		if (target is null || target.IsImport)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"Delegate targets must currently be reachable managed methods.",
				caller.DisplayName,
				instruction.IlOffset);
		}
		if (source.OpCode == OpCodes.Ldvirtftn)
		{
			var receiver = allocated.Allocation.Registers[instruction.Uses.Single()].Register;
			EmitAllocatedMove(receiver, M68kRegister.A0, M68kMachineValueWidth.Long);
			EmitAllocatedRequireNonNull(instruction.Uses.Single(), M68kRegister.A0);
			if (target.DeclaringTypeIsInterface)
			{
				var interfaceDefinition = _module.GetInterfaceDefinition(target);
				_usedInterfaces.TryAdd(interfaceDefinition.Identity, interfaceDefinition);
				var slot = _module.GetInterfaceSlot(target);
				if (slot > short.MaxValue / 4)
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.UnsupportedSignature,
						$"Interface slot {slot} exceeds the indexed method-table displacement range.",
						target.DisplayName,
						instruction.IlOffset);
				}
				_assembler.EmitWord(0x2450); // MOVEA.L (A0),A2 descriptor
				_assembler.EmitWord(0x246A); // MOVEA.L interface-map(A2),A2
				_assembler.EmitWord(unchecked((ushort)M68kRuntimeAbi.TypeInterfaceMapOffset));
				_assembler.EmitWord(0x241A); // MOVE.L (A2)+,D2 entry count
				EmitAddressImmediateToRegister(
					M68kRegister.A3,
					InterfaceIdentityLabel(interfaceDefinition));
				var loop = UniqueLabel("delegate-interface-lookup");
				var found = UniqueLabel("delegate-interface-found");
				_assembler.Mark(loop);
				_assembler.EmitWord(0xB7DA); // CMPA.L (A2)+,A3 identity
				_assembler.EmitBranch(M68kCondition.Equal, found);
				_assembler.EmitWord(0x588A); // ADDQ.L #4,A2 skip table
				_assembler.EmitWord(0x5382); // SUBQ.L #1,D2
				_assembler.EmitBranch(M68kCondition.NotEqual, loop);
				_assembler.EmitWord(0x4AFC); // ILLEGAL: invalid pairing
				_assembler.Mark(found);
				_assembler.EmitWord(0x2452); // MOVEA.L (A2),A2 table
				if (slot == 0)
				{
					_assembler.EmitWord(0x2452); // MOVEA.L (A2),A2 target
				}
				else
				{
					_assembler.EmitWord(0x246A); // MOVEA.L d16(A2),A2 target
					_assembler.EmitWord(checked((ushort)(slot * 4)));
				}
				EmitAllocatedMove(
					M68kRegister.A2,
					allocated.Allocation.Registers[instruction.Definitions.Single()].Register,
					M68kMachineValueWidth.Long);
				return;
			}
			if (target.IsVirtual && !target.IsFinal && !target.DeclaringTypeIsSealed)
			{
				var slot = _module.GetVirtualSlot(target);
				if (slot > short.MaxValue / 4)
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.UnsupportedSignature,
						$"Virtual slot {slot} exceeds the indexed vtable displacement range.",
						target.DisplayName,
						instruction.IlOffset);
				}
				_assembler.EmitWord(0x2450); // MOVEA.L (A0),A2 descriptor
				_assembler.EmitWord(0x246A); // MOVEA.L vtable(A2),A2
				_assembler.EmitWord(unchecked((ushort)M68kRuntimeAbi.TypeVirtualTableOffset));
				if (slot == 0)
				{
					_assembler.EmitWord(0x2452); // MOVEA.L (A2),A2 target
				}
				else
				{
					_assembler.EmitWord(0x246A); // MOVEA.L d16(A2),A2 target
					_assembler.EmitWord(checked((ushort)(slot * 4)));
				}
				EmitAllocatedMove(
					M68kRegister.A2,
					allocated.Allocation.Registers[instruction.Definitions.Single()].Register,
					M68kMachineValueWidth.Long);
				return;
			}
		}
		EmitAllocatedAddress(
			MethodLabel(target),
			allocated.Allocation.Registers[instruction.Definitions.Single()].Register);
	}

	private void EmitAllocatedDelegateCreate(
		CilMethod caller,
		M68kMachineInstruction instruction)
	{
		var source = instruction.SourceInstruction ??
			throw new InvalidOperationException("Delegate construction has no source instruction.");
		EnsureManagedAllocationAllowed(caller, source, "delegate construction");
		var reference = _module.ResolveMethodToken(
			(int)source.Operand!,
			caller,
			instruction.IlOffset);
		var delegateType = reference.ConstructedDeclaringType ??
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"Delegate construction requires a constructed delegate type.",
				caller.DisplayName,
				instruction.IlOffset);
		RegisterDelegateType(delegateType);
		EmitAllocatedImmediate(M68kRuntimeAbi.DelegateObjectBytes, M68kRegister.D0);
		EmitManagedAllocationFromD0(M68kRuntimeAbi.DelegateObjectBytes);
		_assembler.EmitWord(0x2040); // MOVEA.L D0,A0
		_assembler.EmitWord(0x20BC); // MOVE.L #descriptor,(A0)
		_assembler.EmitAddress(DelegateTypeDescriptorLabel(delegateType));
		EmitAllocatedImmediate(M68kRuntimeAbi.DelegateObjectBytes, M68kRegister.D1);
		EmitAllocatedBaseStore(
			M68kRegister.D1,
			M68kRegister.A0,
			M68kMachineValueWidth.Long,
			M68kRuntimeAbi.ObjectSizeOffset);
		EmitAllocatedBaseStore(
			M68kRegister.A2,
			M68kRegister.A0,
			M68kMachineValueWidth.Long,
			M68kRuntimeAbi.DelegateTargetOffset);
		EmitAllocatedBaseStore(
			M68kRegister.A3,
			M68kRegister.A0,
			M68kMachineValueWidth.Long,
			M68kRuntimeAbi.DelegateThunkOffset);
		EmitAllocatedImmediate(0, M68kRegister.D1);
		EmitAllocatedBaseStore(
			M68kRegister.D1,
			M68kRegister.A0,
			M68kMachineValueWidth.Long,
			M68kRuntimeAbi.DelegateInvocationListOffset);
		EmitAllocatedTest(M68kRegister.A2, M68kMachineValueWidth.Long);
		var openTarget = UniqueLabel("delegate-open-target");
		_assembler.EmitBranch(M68kCondition.Equal, openTarget);
		EmitAllocatedImmediate(
			unchecked((int)M68kRuntimeAbi.DelegateFlagClosedInstance),
			M68kRegister.D1);
		_assembler.Mark(openTarget);
		EmitAllocatedBaseStore(
			M68kRegister.D1,
			M68kRegister.A0,
			M68kMachineValueWidth.Long,
			M68kRuntimeAbi.DelegateFlagsOffset);
		EmitAllocatedMove(M68kRegister.A0, M68kRegister.D0, M68kMachineValueWidth.Long);
	}

	private void EmitAllocatedLoadDelegateTypeIdentity(
		M68kRegister delegateObject,
		M68kRegister destination)
	{
		EmitAllocatedBaseLoad(
			delegateObject,
			destination,
			M68kMachineValueWidth.Long,
			M68kRuntimeAbi.ObjectDescriptorOffset);
		EmitAllocatedBaseLoad(
			delegateObject,
			M68kRegister.D0,
			M68kMachineValueWidth.Long,
			M68kRuntimeAbi.DelegateFlagsOffset);
		_assembler.EmitWord(0x0280); // ANDI.L #multicast,D0
		_assembler.EmitLong(M68kRuntimeAbi.DelegateFlagMulticast);
		var done = UniqueLabel("delegate-type-identity-done");
		_assembler.EmitBranch(M68kCondition.Equal, done);
		EmitAllocatedBaseLoad(
			delegateObject,
			destination,
			M68kMachineValueWidth.Long,
			M68kRuntimeAbi.DelegateThunkOffset);
		_assembler.Mark(done);
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
		var elementSize = elementType.Size;
		if (_module.TryGetReferenceFreeStructLayout(
				elementType,
				method.ModuleName,
				out var aggregateLayout) &&
			aggregateLayout.Size > 4)
		{
			elementSize = aggregateLayout.Size;
		}
		else if (elementType.Size is not (1 or 2 or 4 or 8) ||
			(!elementType.IsSupportedScalar && !elementType.IsReference))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				$"Arrays of '{elementType.DisplayName}' are not implemented; " +
				"array elements must be supported scalars/references or reference-free structs.",
				method.DisplayName,
				instruction.IlOffset);
		}
		_arrayTypes.TryAdd(elementType.DisplayName, elementType);
		if (elementType.IsReference)
		{
			var elementTarget = _module.ResolveRuntimeTypeIdentity(
				elementType,
				method.ModuleName);
			_arrayElementRuntimeTypes.TryAdd(
				elementType.DisplayName,
				elementTarget);
			_ = RuntimeTypeTestIdentityLabel(elementTarget);
		}
		var lengthValid = UniqueLabel("allocated_array_length_valid");
		EmitAllocatedTest(M68kRegister.D2, M68kMachineValueWidth.Long);
		_assembler.EmitBranch(M68kCondition.Plus, lengthValid);
		EmitExceptionRaise(reason: 4, hasException: false);
		_assembler.Mark(lengthValid);
		var maximumLength = (uint.MaxValue -
			(uint)M68kRuntimeAbi.ArrayDataOffset) / (uint)elementSize;
		if (maximumLength < int.MaxValue)
		{
			var sizeValid = UniqueLabel("allocated_array_size_valid");
			_assembler.EmitWord(0x0C82); // CMPI.L #maximum,D2
			_assembler.EmitLong(maximumLength);
			_assembler.EmitBranch(M68kCondition.LowerOrSame, sizeValid);
			EmitExceptionRaise(reason: 4, hasException: false);
			_assembler.Mark(sizeValid);
		}
		EmitAllocatedMultiplyByConstant(
			M68kRegister.D2,
			M68kRegister.D0,
			elementSize);
		EmitAllocatedAddImmediate(M68kRegister.D0, 12);
		EmitManagedAllocationFromD0();
		_assembler.EmitWord(0x2040); // MOVEA.L D0,A0
		_assembler.EmitWord(0x20BC); // MOVE.L #descriptor,(A0)
		_assembler.EmitAddress(ArrayDescriptorLabel(elementType));
		EmitAllocatedMultiplyByConstant(
			M68kRegister.D2,
			M68kRegister.D1,
			elementSize);
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

	private void EmitAllocatedBox(
		CilMethod caller,
		M68kMachineInstruction instruction)
	{
		var source = instruction.SourceInstruction ??
			throw new InvalidOperationException("Box operation has no source instruction.");
		EnsureManagedAllocationAllowed(caller, source, "boxing");
		var type = _module.ResolveTypeToken(
			(int)source.Operand!,
			caller,
			instruction.IlOffset);
		var isReferenceFreeStruct = _module.TryGetReferenceFreeStructLayout(
			type,
			caller.ModuleName,
			out var structLayout);
		if ((!type.IsSupportedScalar || type.IsReference || type.Size is not (1 or 2 or 4 or 8)) &&
			!isReferenceFreeStruct)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				$"Boxing '{type.DisplayName}' is not implemented; the compact profile currently supports scalar value types up to eight bytes, single-word reference-free structs, and multiword reference-free structs loaded directly from locals.",
				caller.DisplayName,
				instruction.IlOffset);
		}
		RegisterBoxedType(type);
		var payloadBytes = isReferenceFreeStruct ? structLayout.Size : type.Size;
		var objectBytes = checked(8 + Math.Max(4, payloadBytes));
		EmitAllocatedImmediate(objectBytes, M68kRegister.D0);
		EmitManagedAllocationFromD0(objectBytes);
		_assembler.EmitWord(0x2040); // MOVEA.L D0,A0
		_assembler.EmitWord(0x20BC); // MOVE.L #descriptor,(A0)
		_assembler.EmitAddress(BoxedTypeDescriptorLabel(type));
		EmitAllocatedImmediate(objectBytes, M68kRegister.D1);
		EmitAllocatedBaseStore(
			M68kRegister.D1,
			M68kRegister.A0,
			M68kMachineValueWidth.Long,
			M68kRuntimeAbi.ObjectSizeOffset);
		if (isReferenceFreeStruct && structLayout.Size > 4)
		{
			EmitAllocatedMove(
				M68kRegister.D2,
				M68kRegister.A1,
				M68kMachineValueWidth.Long);
			for (var offset = 0; offset < structLayout.Size; offset += 4)
			{
				EmitAllocatedBaseLoad(
					M68kRegister.A1,
					M68kRegister.D1,
					M68kMachineValueWidth.Long,
					checked((short)offset));
				EmitAllocatedBaseStore(
					M68kRegister.D1,
					M68kRegister.A0,
					M68kMachineValueWidth.Long,
					checked((short)(8 + offset)));
			}
		}
		else
		{
			EmitAllocatedBaseStore(
				M68kRegister.D2,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				8);
			if (type.Size == 8)
			{
				EmitAllocatedBaseStore(
					M68kRegister.D3,
					M68kRegister.A0,
					M68kMachineValueWidth.Long,
					12);
			}
		}
		EmitAllocatedMove(M68kRegister.A0, M68kRegister.D0, M68kMachineValueWidth.Long);
	}

	private void EmitAllocatedUnbox(
		CilMethod caller,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		var sourceInstruction = instruction.SourceInstruction ??
			throw new InvalidOperationException("Unbox operation has no source instruction.");
		var type = _module.ResolveTypeToken(
			(int)sourceInstruction.Operand!,
			caller,
			instruction.IlOffset);
		var isReferenceFreeStruct = _module.TryGetReferenceFreeStructLayout(
			type,
			caller.ModuleName,
			out var structLayout);
		if ((!type.IsSupportedScalar || type.IsReference || type.Size is not (1 or 2 or 4 or 8)) &&
			!isReferenceFreeStruct)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				$"Unboxing '{type.DisplayName}' is not implemented; the compact profile currently supports scalar value types up to eight bytes and reference-free structs.",
				caller.DisplayName,
				instruction.IlOffset);
		}
		var storesMultiwordLocal = isReferenceFreeStruct &&
			structLayout.Size > 4 &&
			sourceInstruction.OpCode == OpCodes.Unbox_Any &&
			instruction.Definitions.Length == 0 &&
			instruction.ArgumentIndex is not null;
		if (isReferenceFreeStruct &&
			structLayout.Size > 4 &&
			sourceInstruction.OpCode == OpCodes.Unbox_Any &&
			!storesMultiwordLocal)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				$"unbox.any for multiword struct '{type.DisplayName}' requires a direct local destination until the general multiword evaluation-stack representation is available.",
				caller.DisplayName,
				instruction.IlOffset);
		}
		RegisterBoxedType(type);
		var source = allocated.Allocation.Registers[instruction.Uses.Single()].Register;
		EmitAllocatedRequireNonNull(instruction.Uses.Single(), source);
		EmitAllocatedMove(source, M68kRegister.A0, M68kMachineValueWidth.Long);
		_assembler.EmitWord(0x2210); // MOVE.L (A0),D1 descriptor
		EmitAddressImmediateToRegister(M68kRegister.A1, BoxedTypeDescriptorLabel(type));
		_assembler.EmitWord(0xB289); // CMP.L A1,D1
		var compatible = UniqueLabel("unbox-compatible");
		_assembler.EmitBranch(M68kCondition.Equal, compatible);
		RegisterRuntimeTypeDescriptor("System.InvalidCastException");
		EmitExceptionRaise(reason: 7, hasException: false);
		_assembler.Mark(compatible);
		if (storesMultiwordLocal)
		{
			var localOffset = AllocatedFrameOffset(
				allocated,
				allocated.Frame.LocalOffsets[instruction.ArgumentIndex!.Value]);
			for (var offset = 0; offset < structLayout.Size; offset += 4)
			{
				EmitAllocatedBaseLoad(
					M68kRegister.A0,
					M68kRegister.D1,
					M68kMachineValueWidth.Long,
					checked((short)(8 + offset)));
				EmitAllocatedFrameStore(
					M68kRegister.D1,
					M68kMachineValueWidth.Long,
					checked(localOffset + offset));
			}
			return;
		}
		var destination = allocated.Allocation.Registers[
			instruction.Definitions.Single()].Register;
		if (sourceInstruction.OpCode == OpCodes.Unbox)
		{
			EmitAllocatedBaseAddress(M68kRegister.A0, destination, 8);
			return;
		}
		EmitAllocatedBaseLoad(
			M68kRegister.A0,
			destination,
			M68kMachineValueWidth.Long,
			8);
		if (type.Size == 8)
		{
			EmitAllocatedBaseLoad(
				M68kRegister.A0,
				(M68kRegister)((int)destination + 1),
				M68kMachineValueWidth.Long,
				12);
		}
		EmitAllocatedDefinitionNormalization(
			allocated,
			instruction,
			destination);
	}

	private void EmitAllocatedArrayAccess(
		CilMethod method,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		var source = instruction.SourceInstruction!;
		if (instruction.MemorySize == sizeof(uint) &&
			source.OpCode is var laneOp &&
			(laneOp == OpCodes.Ldelem || laneOp == OpCodes.Stelem) &&
			source.Operand is int laneTypeToken)
		{
			var laneType = _module.ResolveTypeToken(
				laneTypeToken,
				method,
				instruction.IlOffset);
			if (_module.TryGetReferenceFreeStructLayout(
					laneType,
					method.ModuleName,
					out var laneLayout) &&
				laneLayout.Size > 4)
			{
				EmitAllocatedAggregateArrayLaneAccess(
					allocated,
					instruction,
					laneLayout);
				return;
			}
		}
		var access = source.OpCode is var genericOp &&
			(genericOp == OpCodes.Ldelem || genericOp == OpCodes.Stelem)
			? GetGenericArrayAccess(
				_module.ResolveTypeToken(
					(int)source.Operand!,
					method,
					instruction.IlOffset),
				genericOp == OpCodes.Stelem)
			: GetArrayAccess(source.OpCode);
		var baseRegister = allocated.Allocation.Registers[
			instruction.Uses[0]].Register;
		var indexRegister = allocated.Allocation.Registers[
			instruction.Uses[1]].Register;
		if (source.OpCode == OpCodes.Stelem_Ref)
		{
			EmitAllocatedReferenceArrayStoreCheck(
				baseRegister,
				allocated.Allocation.Registers[instruction.Uses[2]].Register);
		}
		EmitAllocatedArrayBoundsCheck(
			instruction.Uses[0],
			baseRegister,
			indexRegister);
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
			var sourceRegister = allocated.Allocation.Registers[
				instruction.Uses[2]].Register;
			EmitAllocatedIndexedStore(
				sourceRegister,
				baseRegister,
				indexRegister,
				width,
				access.Size,
				12);
			if (access.Size == 8)
			{
				EmitAllocatedIndexedStore(
					(M68kRegister)((int)sourceRegister + 1),
					baseRegister,
					indexRegister,
					M68kMachineValueWidth.Long,
					access.Size,
					16);
			}
			return;
		}
		var destination = allocated.Allocation.Registers[
			instruction.Definitions.Single()].Register;
		if (access.Size == 8 && indexRegister == destination)
		{
			// The MC68000 expands the scaled index in place. If the first
			// destination word is also the index register, loading it first
			// destroys the address needed for the second word (and can create
			// an odd address). Load the second word first in that allocation.
			EmitAllocatedIndexedLoad(
				baseRegister,
				indexRegister,
				(M68kRegister)((int)destination + 1),
				M68kMachineValueWidth.Long,
				access.Size,
				16);
			EmitAllocatedIndexedLoad(
				baseRegister,
				indexRegister,
				destination,
				width,
				access.Size,
				12);
		}
		else
		{
			EmitAllocatedIndexedLoad(
				baseRegister,
				indexRegister,
				destination,
				width,
				access.Size,
				12);
			if (access.Size == 8)
			{
				EmitAllocatedIndexedLoad(
					baseRegister,
					indexRegister,
					(M68kRegister)((int)destination + 1),
					M68kMachineValueWidth.Long,
					access.Size,
					16);
			}
		}
		if (_allocatedDeferredNormalizations.Contains(instruction.Id))
		{
			return;
		}
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

	private void EmitAllocatedAggregateArrayLaneAccess(
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction,
		CilTypeLayout layout)
	{
		if (instruction.MemoryOffset < 0 ||
			instruction.MemoryOffset > layout.Size - sizeof(uint) ||
			(instruction.MemoryOffset & 3) != 0)
		{
			throw new InvalidOperationException(
				"Aggregate array lane has an invalid byte range.");
		}
		var array = allocated.Allocation.Registers[instruction.Uses[0]].Register;
		var index = allocated.Allocation.Registers[instruction.Uses[1]].Register;
		EmitAllocatedArrayBoundsCheck(instruction.Uses[0], array, index);
		EmitAllocatedMultiplyByConstant(index, M68kRegister.D1, layout.Size);
		EmitAllocatedAddImmediate(
			M68kRegister.D1,
			checked(M68kRuntimeAbi.ArrayDataOffset + instruction.MemoryOffset));
		if (instruction.Operation == M68kMachineOperation.ArrayStore)
		{
			EmitAllocatedIndexedStore(
				allocated.Allocation.Registers[instruction.Uses[2]].Register,
				array,
				M68kRegister.D1,
				M68kMachineValueWidth.Long,
				elementSize: 1,
				displacement: 0);
			return;
		}
		EmitAllocatedIndexedLoad(
			array,
			M68kRegister.D1,
			allocated.Allocation.Registers[
				instruction.Definitions.Single()].Register,
			M68kMachineValueWidth.Long,
			elementSize: 1,
			displacement: 0);
	}

	private void EmitAllocatedAggregateArrayAccess(
		CilMethod method,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		var type = _module.ResolveTypeToken(
			(int)instruction.SourceInstruction!.Operand!,
			method,
			instruction.IlOffset);
		if (!_module.TryGetReferenceFreeStructLayout(
				type,
				method.ModuleName,
				out var layout) ||
			layout.Size <= 4)
		{
			throw new InvalidOperationException(
				"Aggregate array access resolved a non-aggregate element type.");
		}
		var array = allocated.Allocation.Registers[instruction.Uses[0]].Register;
		var index = allocated.Allocation.Registers[instruction.Uses[1]].Register;
		if (array != M68kRegister.A2 || index != M68kRegister.D2)
		{
			throw new InvalidOperationException(
				"Aggregate array base/index do not satisfy their fixed ABI.");
		}
		EmitAllocatedArrayBoundsCheck(
			instruction.Uses[0],
			array,
			index);
		EmitAllocatedMultiplyByConstant(
			index,
			M68kRegister.D1,
			layout.Size);
		EmitAllocatedIndexedAddress(
			array,
			M68kRegister.D1,
			M68kRegister.A0,
			elementSize: 1,
			displacement: (sbyte)M68kRuntimeAbi.ArrayDataOffset);

		if (instruction.Operation == M68kMachineOperation.AggregateArrayStore)
		{
			var source = allocated.Allocation.Registers[
				instruction.Uses[2]].Register;
			if (source != M68kRegister.A3)
			{
				throw new InvalidOperationException(
					"Aggregate array-store source does not satisfy its fixed ABI.");
			}
			for (var offset = 0; offset < layout.Size; offset += 4)
			{
				EmitAllocatedBaseLoad(
					source,
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					checked((short)offset));
				EmitAllocatedBaseStore(
					M68kRegister.D0,
					M68kRegister.A0,
					M68kMachineValueWidth.Long,
					checked((short)offset));
			}
			return;
		}

		var destination = AllocatedFrameOffset(
			allocated,
			allocated.Frame.LocalOffsets[instruction.ArgumentIndex!.Value]);
		for (var offset = 0; offset < layout.Size; offset += 4)
		{
			EmitAllocatedBaseLoad(
				M68kRegister.A0,
				M68kRegister.D0,
				M68kMachineValueWidth.Long,
				checked((short)offset));
			EmitAllocatedFrameStore(
				M68kRegister.D0,
				M68kMachineValueWidth.Long,
				checked(destination + offset));
		}
		if (instruction.Definitions.Length == 1)
		{
			EmitAllocatedFrameAddress(
				allocated.Allocation.Registers[
					instruction.Definitions[0]].Register,
				destination);
		}
	}

	private void EmitAllocatedAggregateIndirectOperation(
		CilMethod method,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		var type = _module.ResolveTypeToken(
			(int)instruction.SourceInstruction!.Operand!,
			method,
			instruction.IlOffset);
		var hasLayout = instruction.Operation ==
			M68kMachineOperation.AggregateIndirectInitialize
				? _module.TryGetIndirectInitializeLayout(
					type,
					method.ModuleName,
					out var layout)
				: _module.TryGetReferenceFreeStructLayout(
					type,
					method.ModuleName,
					out layout);
		if (!hasLayout ||
			layout.Size <= 0 ||
			(layout.Size & 3) != 0)
		{
			throw new InvalidOperationException(
				"Aggregate indirect operation resolved an invalid layout.");
		}

		var destination = allocated.Allocation.Registers[
			instruction.Uses[0]].Register;
		EmitAllocatedRequireNonNull(instruction.Uses[0], destination);
		if (instruction.Operation ==
			M68kMachineOperation.AggregateIndirectInitialize)
		{
			if (layout.Size >= 32)
			{
				EmitAllocatedAggregateZero(destination, layout.Size);
				return;
			}
			if (!UseClr)
			{
				EmitAllocatedImmediate(0, M68kRegister.D0);
			}
			for (var offset = 0; offset < layout.Size; offset += 4)
			{
				if (UseClr)
				{
					EmitAllocatedBaseClearLong(
						destination,
						checked((short)offset));
				}
				else
				{
					EmitAllocatedBaseStore(
						M68kRegister.D0,
						destination,
						M68kMachineValueWidth.Long,
						checked((short)offset));
				}
			}
			return;
		}

		if (instruction.Operation == M68kMachineOperation.AggregateIndirectLoad)
		{
			var frameDestination = AllocatedFrameOffset(
				allocated,
				allocated.Frame.LocalOffsets[instruction.ArgumentIndex!.Value]);
			for (var offset = 0; offset < layout.Size; offset += 4)
			{
				EmitAllocatedBaseLoad(
					destination,
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					checked((short)offset));
				EmitAllocatedFrameStore(
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					checked(frameDestination + offset));
			}
			if (instruction.Definitions.Length == 1)
			{
				EmitAllocatedFrameAddress(
					allocated.Allocation.Registers[
						instruction.Definitions[0]].Register,
					frameDestination);
			}
			return;
		}

		var source = allocated.Allocation.Registers[
			instruction.Uses[1]].Register;
		if (instruction.Operation == M68kMachineOperation.AggregateIndirectCopy)
		{
			EmitAllocatedRequireNonNull(instruction.Uses[1], source);
			var temporary = AllocatedFrameOffset(
				allocated,
				allocated.Frame.LocalOffsets[instruction.ArgumentIndex!.Value]);
			for (var offset = 0; offset < layout.Size; offset += 4)
			{
				EmitAllocatedBaseLoad(
					source,
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					checked((short)offset));
				EmitAllocatedFrameStore(
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					checked(temporary + offset));
			}
			for (var offset = 0; offset < layout.Size; offset += 4)
			{
				EmitAllocatedFrameLoad(
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					checked(temporary + offset));
				EmitAllocatedBaseStore(
					M68kRegister.D0,
					destination,
					M68kMachineValueWidth.Long,
					checked((short)offset));
			}
			return;
		}

		for (var offset = 0; offset < layout.Size; offset += 4)
		{
			EmitAllocatedBaseLoad(
				source,
				M68kRegister.D0,
				M68kMachineValueWidth.Long,
				checked((short)offset));
			EmitAllocatedBaseStore(
				M68kRegister.D0,
				destination,
				M68kMachineValueWidth.Long,
				checked((short)offset));
		}
	}

	private void EmitAllocatedMultiplyByConstant(
		M68kRegister source,
		M68kRegister destination,
		int factor)
	{
		if (source > M68kRegister.D7 ||
			destination > M68kRegister.D7 ||
			factor <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(factor));
		}
		if (source == destination)
		{
			throw new ArgumentException(
				"Constant multiplication requires a distinct accumulator.",
				nameof(destination));
		}
		if (factor == 1)
		{
			EmitAllocatedMove(
				source,
				destination,
				M68kMachineValueWidth.Long);
			return;
		}
		EmitAllocatedMove(
			source,
			destination,
			M68kMachineValueWidth.Long);
		var plan = SelectConstantMultiplyPlan(factor);
		var previousBit = plan.Terms[0].Bit;
		for (var index = 1; index < plan.Terms.Length; index++)
		{
			var term = plan.Terms[index];
			EmitShift(previousBit - term.Bit);
			_assembler.EmitWord((ushort)(
				(term.Subtract ? 0x9080 : 0xD080) |
					((int)destination << 9) |
					(int)source)); // ADD.L/SUB.L Dsource,Ddestination
			previousBit = term.Bit;
		}
		EmitShift(previousBit);

		void EmitShift(int count)
		{
			while (count != 0)
			{
				var chunk = Math.Min(count, 8);
				EmitAllocatedShiftImmediate(destination, left: true, chunk);
				count -= chunk;
			}
		}
	}

	private int GetAllocatedSpanElementSize(
		CilMethod caller,
		CilType element)
	{
		if (_module.TryGetReferenceFreeStructLayout(
				element,
				caller.ModuleName,
				out var layout))
		{
			return layout.Size;
		}
		return element.Size;
	}

	private void EmitAllocatedCopyKernel(
		int elementSize,
		M68kRegister? successResult = null)
	{
		var destinationLongEnough = UniqueLabel(
			"allocated_copy_destination_long_enough");
		var success = UniqueLabel("allocated_copy_success");
		var complete = UniqueLabel("allocated_copy_complete");
		EmitAllocatedCompare(
			M68kRegister.D2,
			M68kRegister.D1,
			M68kMachineValueWidth.Long);
		_assembler.EmitBranch(M68kCondition.CarrySet, destinationLongEnough);
		_assembler.EmitBranch(M68kCondition.Equal, destinationLongEnough);
		if (successResult is { } failedResult)
		{
			EmitAllocatedImmediate(0, failedResult);
			_assembler.EmitBranch(M68kCondition.True, complete);
		}
		else
		{
			RegisterRuntimeTypeDescriptor("System.ArgumentException");
			EmitExceptionRaise(reason: 9, hasException: false);
		}
		_assembler.Mark(destinationLongEnough);

		EmitAllocatedTest(M68kRegister.D2, M68kMachineValueWidth.Long);
		_assembler.EmitBranch(M68kCondition.Equal, success);

		var copyWidth = elementSize switch
		{
			1 => M68kMachineValueWidth.Byte,
			2 => M68kMachineValueWidth.Word,
			4 => M68kMachineValueWidth.Long,
			_ => M68kMachineValueWidth.Byte
		};
		var stride = elementSize is 1 or 2 or 4 ? elementSize : 1;
		EmitAllocatedMultiplyByConstant(
			M68kRegister.D2,
			M68kRegister.D0,
			elementSize);
		if (stride == 1 && elementSize != 1)
		{
			EmitAllocatedMove(
				M68kRegister.D0,
				M68kRegister.D2,
				M68kMachineValueWidth.Long);
		}
		EmitAllocatedIndexedAddress(
			M68kRegister.A2,
			M68kRegister.D0,
			M68kRegister.A3,
			elementSize: 1,
			displacement: 0);

		var forward = UniqueLabel("allocated_copy_forward");
		var forwardLoop = UniqueLabel("allocated_copy_forward_loop");
		var backwardLoop = UniqueLabel("allocated_copy_backward_loop");
		EmitAllocatedMove(
			M68kRegister.A2,
			M68kRegister.D1,
			M68kMachineValueWidth.Long);
		EmitAllocatedMove(
			M68kRegister.A1,
			M68kRegister.D3,
			M68kMachineValueWidth.Long);
		EmitAllocatedCompare(
			M68kRegister.D1,
			M68kRegister.D3,
			M68kMachineValueWidth.Long);
		_assembler.EmitBranch(M68kCondition.CarryClear, forward);
		EmitAllocatedMove(
			M68kRegister.A1,
			M68kRegister.D1,
			M68kMachineValueWidth.Long);
		EmitAllocatedMove(
			M68kRegister.A3,
			M68kRegister.D3,
			M68kMachineValueWidth.Long);
		EmitAllocatedCompare(
			M68kRegister.D1,
			M68kRegister.D3,
			M68kMachineValueWidth.Long);
		_assembler.EmitBranch(M68kCondition.CarryClear, forward);

		EmitAllocatedIndexedAddress(
			M68kRegister.A1,
			M68kRegister.D0,
			M68kRegister.A1,
			elementSize: 1,
			displacement: 0);
		EmitAllocatedMove(
			M68kRegister.A3,
			M68kRegister.A2,
			M68kMachineValueWidth.Long);
		_assembler.Mark(backwardLoop);
		EmitAllocatedAddImmediate(M68kRegister.A2, -stride);
		EmitAllocatedAddImmediate(M68kRegister.A1, -stride);
		EmitAllocatedBaseLoad(
			M68kRegister.A2,
			M68kRegister.D0,
			copyWidth,
			0);
		EmitAllocatedBaseStore(
			M68kRegister.D0,
			M68kRegister.A1,
			copyWidth,
			0);
		_assembler.EmitWord(0x5382); // SUBQ.L #1,D2
		_assembler.EmitBranch(M68kCondition.NotEqual, backwardLoop);
		_assembler.EmitBranch(M68kCondition.True, success);

		_assembler.Mark(forward);
		_assembler.Mark(forwardLoop);
		EmitAllocatedBaseLoad(
			M68kRegister.A2,
			M68kRegister.D0,
			copyWidth,
			0);
		EmitAllocatedBaseStore(
			M68kRegister.D0,
			M68kRegister.A1,
			copyWidth,
			0);
		EmitAllocatedAddImmediate(M68kRegister.A2, stride);
		EmitAllocatedAddImmediate(M68kRegister.A1, stride);
		_assembler.EmitWord(0x5382); // SUBQ.L #1,D2
		_assembler.EmitBranch(M68kCondition.NotEqual, forwardLoop);

		_assembler.Mark(success);
		if (successResult is { } succeededResult)
		{
			EmitAllocatedImmediate(1, succeededResult);
		}
		_assembler.Mark(complete);
	}

	private void EmitAllocatedSpanElementAddress(
		M68kRegister baseRegister,
		M68kRegister indexRegister,
		M68kRegister destination,
		int elementSize,
		M68kRegister scaledIndexScratch)
	{
		if (elementSize is 1 or 2 or 4)
		{
			if (_request.Cpu == M68kCpuTarget.M68000)
			{
				for (var shift = elementSize; shift > 1; shift >>= 1)
				{
					EmitAllocatedShiftImmediate(indexRegister, left: true);
				}
			}
			EmitAllocatedIndexedAddress(
				baseRegister,
				indexRegister,
				destination,
				elementSize,
				0);
			return;
		}

		EmitAllocatedMultiplyByConstant(
			indexRegister,
			scaledIndexScratch,
			elementSize);
		EmitAllocatedIndexedAddress(
			baseRegister,
			scaledIndexScratch,
			destination,
			elementSize: 1,
			displacement: 0);
	}

	private void EmitAllocatedReferenceArrayStoreCheck(
		M68kRegister arrayRegister,
		M68kRegister valueRegister)
	{
		var success = UniqueLabel("array-store-type-success");
		var classLoop = UniqueLabel("array-store-class-loop");
		var interfaceLoop = UniqueLabel("array-store-interface-loop");
		var interfaceMap = UniqueLabel("array-store-interface-map");
		var failure = UniqueLabel("array-store-type-failure");

		EmitAllocatedTest(valueRegister, M68kMachineValueWidth.Long);
		_assembler.EmitBranch(M68kCondition.Equal, success);
		_assembler.EmitWord(0x2053); // MOVEA.L (A3),A0 array descriptor
		if (arrayRegister != M68kRegister.A3)
		{
			throw new InvalidOperationException("Reference array base is not constrained to A3.");
		}
		if (valueRegister != M68kRegister.A4)
		{
			throw new InvalidOperationException("Reference array value is not constrained to A4.");
		}
		_assembler.EmitWord(0x2468); // MOVEA.L element-type(A0),A2
		_assembler.EmitWord(unchecked((ushort)M68kRuntimeAbi.ArrayElementTypeOffset));
		_assembler.EmitWord(0x2428); // MOVE.L element-kind(A0),D2
		_assembler.EmitWord(unchecked((ushort)M68kRuntimeAbi.ArrayElementKindOffset));
		_assembler.EmitWord(0x2254); // MOVEA.L (A4),A1 value descriptor
		_assembler.EmitWord(0x4A82); // TST.L D2
		_assembler.EmitBranch(M68kCondition.NotEqual, interfaceMap);

		_assembler.Mark(classLoop);
		_assembler.EmitWord(0xB3CA); // CMPA.L A2,A1
		_assembler.EmitBranch(M68kCondition.Equal, success);
		_assembler.EmitWord(0x2269); // MOVEA.L base-type(A1),A1
		_assembler.EmitWord(unchecked((ushort)M68kRuntimeAbi.TypeBaseOffset));
		EmitMoveRegister(M68kRegister.A1, M68kRegister.D1);
		_assembler.EmitWord(0x4A81); // TST.L D1
		_assembler.EmitBranch(M68kCondition.NotEqual, classLoop);
		_assembler.EmitBranch(M68kCondition.True, failure);

		_assembler.Mark(interfaceMap);
		_assembler.EmitWord(0x2269); // MOVEA.L interface-map(A1),A1
		_assembler.EmitWord(unchecked((ushort)M68kRuntimeAbi.TypeInterfaceMapOffset));
		EmitMoveRegister(M68kRegister.A1, M68kRegister.D1);
		_assembler.EmitWord(0x4A81); // TST.L D1
		_assembler.EmitBranch(M68kCondition.Equal, failure);
		_assembler.EmitWord(0x2219); // MOVE.L (A1)+,D1 count
		_assembler.Mark(interfaceLoop);
		_assembler.EmitWord(0x2059); // MOVEA.L (A1)+,A0 identity
		_assembler.EmitWord(0xB1CA); // CMPA.L A2,A0
		_assembler.EmitBranch(M68kCondition.Equal, success);
		_assembler.EmitWord(0x5889); // ADDQ.L #4,A1
		_assembler.EmitWord(0x5381); // SUBQ.L #1,D1
		_assembler.EmitBranch(M68kCondition.NotEqual, interfaceLoop);

		_assembler.Mark(failure);
		RegisterRuntimeTypeDescriptor("System.ArrayTypeMismatchException");
		EmitExceptionRaise(reason: 8, hasException: false);
		_assembler.Mark(success);
	}

	private void EmitAllocatedArrayLength(
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		var source = allocated.Allocation.Registers[
			instruction.Uses.Single()].Register;
		var destination = allocated.Allocation.Registers[
			instruction.Definitions.Single()].Register;
		EmitAllocatedRequireNonNull(instruction.Uses.Single(), source);
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
		var hasInitializeLayout = _module.TryGetIndirectInitializeLayout(
			valueType,
			method.ModuleName,
			out var initializeLayout);
		var scalarSize = type.IsSupportedScalar
			? type.Size
			: hasInitializeLayout && initializeLayout.Size <= 4
				? initializeLayout.Size
				: 0;
		var isSupportedScalar = scalarSize is 1 or 2 or 4 or 8;
		if (valueType is null ||
			!isSupportedScalar &&
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
		if (isSupportedScalar)
		{
			EmitAllocatedImmediate(0, M68kRegister.D0);
			if (scalarSize == 8)
			{
				EmitAllocatedBaseStore(
					M68kRegister.D0,
					baseRegister,
					M68kMachineValueWidth.Long,
					0);
				EmitAllocatedBaseStore(
					M68kRegister.D0,
					baseRegister,
					M68kMachineValueWidth.Long,
					4);
			}
			else
			{
				EmitAllocatedBaseStore(
					M68kRegister.D0,
					baseRegister,
					scalarSize switch
					{
						1 => M68kMachineValueWidth.Byte,
						2 => M68kMachineValueWidth.Word,
						_ => M68kMachineValueWidth.Long
					},
					0);
			}
			return;
		}
		var longs = _module.IsSupportedStructType(valueType)
			? SlotLongs(valueType)
			: IsCompactNullableType(valueType)
				? 1
				: 2;
		if (longs >= 8)
		{
			EmitAllocatedAggregateZero(baseRegister, checked(longs * 4));
			return;
		}
		if (!UseClr)
		{
			EmitAllocatedImmediate(0, M68kRegister.D0);
		}
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

	private void EmitAllocatedAggregateZero(M68kRegister destination, int size)
	{
		if ((size & 3) != 0 || size < 4 || size > 4 * 65_536)
		{
			throw new InvalidOperationException(
				"Allocated aggregate zero loop requires a positive word-counted long size.");
		}

		var address = destination == M68kRegister.A0
			? M68kRegister.A1
			: M68kRegister.A0;
		EmitAllocatedMove(destination, address, M68kMachineValueWidth.Long);
		EmitAllocatedImmediate(0, M68kRegister.D0);
		EmitAllocatedImmediate((size / 4) - 1, M68kRegister.D1);
		var loop = UniqueLabel("allocated_aggregate_zero_loop");
		_assembler.Mark(loop);
		_assembler.EmitWord((ushort)(
			0x20C0 | (((int)address - (int)M68kRegister.A0) << 9))); // MOVE.L D0,(An)+
		_assembler.EmitDbra(1, loop);
	}

	private void EmitAllocatedInstanceFieldStore(
		CilMethod method,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		var source = instruction.SourceInstruction!;
		var field = ResolveAllocatedField(method, instruction);
		if (field.IsStatic)
		{
			throw new InvalidOperationException(
				"Allocated instance store resolved a static field.");
		}
		ValidateType(field.Type, method, "field");
		var objectRegister =
			allocated.Allocation.Registers[instruction.Uses[0]].Register;
		var displacement = _module.IsTransparentScalarField(field)
			? (short)0
			: FieldDisplacement(field);
		displacement = checked((short)(displacement + instruction.MemoryOffset));
		if (!_module.IsTransparentScalarField(field) &&
			!IsConstructorReceiver(method, allocated.Function, instruction.Uses[0]))
		{
			EmitAllocatedRequireNonNull(instruction.Uses[0], objectRegister);
		}
		var hasAggregateLayout = _module.TryGetReferenceFreeStructLayout(
				field.Type,
				field.ModuleName,
				out var aggregateLayout);
		if (instruction.MemorySize == 0 &&
			hasAggregateLayout &&
			aggregateLayout.Size > 4)
		{
			var aggregateSource = allocated.Allocation.Registers[
				instruction.Uses[1]].Register;
			if (aggregateSource < M68kRegister.A0)
			{
				throw new InvalidOperationException(
					"Aggregate instance-field source is not an address register.");
			}
			for (var offset = 0; offset < aggregateLayout.Size; offset += 4)
			{
				EmitAllocatedBaseLoad(
					aggregateSource,
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					checked((short)offset));
				EmitAllocatedBaseStore(
					M68kRegister.D0,
					objectRegister,
					M68kMachineValueWidth.Long,
					checked((short)(displacement + offset)));
			}
			return;
		}
		var valueLocation =
			allocated.Allocation.Registers[instruction.Uses[1]];
		if (instruction.MemorySize == 0 &&
			hasAggregateLayout &&
			aggregateLayout.Size == 4 &&
			allocated.Function.Values[instruction.Uses[1]].Kind ==
				CilStackValueKind.AggregateAddress)
		{
			var aggregateSource = valueLocation.Register;
			if (aggregateSource < M68kRegister.A0)
			{
				throw new InvalidOperationException(
					"Single-word aggregate instance-field source is not an address register.");
			}
			// Value-type constructors produce the address of their temporary.  A
			// single-word struct field stores the temporary's payload, not that
			// compiler-owned address.
			EmitAllocatedBaseLoad(
				aggregateSource,
				M68kRegister.D0,
				M68kMachineValueWidth.Long,
				0);
			EmitAllocatedBaseStore(
				M68kRegister.D0,
				objectRegister,
				M68kMachineValueWidth.Long,
				displacement);
			return;
		}
		if (valueLocation.IsPair)
		{
			EmitAllocatedBaseStore(
				valueLocation.Register,
				objectRegister,
				M68kMachineValueWidth.Long,
				displacement);
			EmitAllocatedBaseStore(
				(M68kRegister)((int)valueLocation.Register + 1),
				objectRegister,
				M68kMachineValueWidth.Long,
				checked((short)(displacement + 4)));
			return;
		}
		var valueRegister = valueLocation.Register;
		var width = InstanceFieldAccessWidth(field, instruction.MemorySize);
		if (TryGetAllocatedConstant(
			allocated.Function,
			instruction.Uses[1],
			out var constant) &&
			constant == 0 &&
			width == M68kMachineValueWidth.Long &&
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
			width,
			displacement);
	}

	private IReadOnlyDictionary<int, HashSet<int>> CreateAllocatedNonNullBlockEntryFacts(
		CilMethod method,
		M68kAllocatedFunction allocated)
	{
		var function = allocated.Function;
		var universe = function.Values.Keys.ToHashSet();
		var entries = function.Blocks.ToDictionary(
			static block => block.Id,
			block => block.Id == function.EntryBlockId ||
				block.IsExceptionEntry ||
				block.Predecessors.Count == 0
					? new HashSet<int>()
					: new HashSet<int>(universe));
		var edgeFacts = function.Blocks
			.SelectMany(block => block.Successors.Select(successor => (block.Id, successor)))
			.ToDictionary(static edge => edge, _ => new HashSet<int>(universe));
		var copySources = function.Blocks
			.SelectMany(static block => block.Instructions)
			.Where(static instruction => instruction is
				{
					Operation: M68kMachineOperation.Copy,
					Uses.Length: 1,
					Definitions.Length: 1
				})
			.ToDictionary(
				static instruction => instruction.Definitions[0],
				static instruction => instruction.Uses[0]);

		var changed = true;
		while (changed)
		{
			changed = false;
			foreach (var block in function.Blocks)
			{
				HashSet<int> incoming;
				if (block.Id == function.EntryBlockId ||
					block.IsExceptionEntry ||
					block.Predecessors.Count == 0)
				{
					incoming = [];
				}
				else
				{
					incoming = new HashSet<int>(universe);
					foreach (var predecessor in block.Predecessors)
					{
						incoming.IntersectWith(edgeFacts[(predecessor, block.Id)]);
					}
					foreach (var phi in block.Phis)
					{
						if (block.Predecessors.All(predecessor =>
							phi.Inputs.TryGetValue(predecessor, out var input) &&
							edgeFacts[(predecessor, block.Id)].Contains(input)))
						{
							incoming.Add(phi.Definition);
						}
					}
				}

				if (!incoming.SetEquals(entries[block.Id]))
				{
					entries[block.Id] = incoming;
					changed = true;
				}

				var outgoing = new HashSet<int>(incoming);
				foreach (var instruction in block.Instructions)
				{
					outgoing.UnionWith(GetAllocatedNonNullTransferValues(
						method,
						allocated,
						instruction));
					PropagateAllocatedNonNullCopy(instruction, outgoing);
				}

				foreach (var successor in block.Successors)
				{
					var edge = new HashSet<int>(outgoing);
					AddAllocatedNonNullBranchEdgeFact(
						function,
						block,
						successor,
						edge,
						copySources);
					if (!edge.SetEquals(edgeFacts[(block.Id, successor)]))
					{
						edgeFacts[(block.Id, successor)] = edge;
						changed = true;
					}
				}
			}
		}

		return entries;
	}

	private static void PropagateAllocatedNonNullCopy(
		M68kMachineInstruction instruction,
		HashSet<int> facts)
	{
		if (instruction is
			{
				Operation: M68kMachineOperation.Copy,
				Uses: [var source],
				Definitions: [var destination]
			} &&
			facts.Contains(source))
		{
			facts.Add(destination);
		}
	}

	private void UpdateAllocatedNonNullRegistersAfterInstruction(
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		var emittedFrameAddresses = _allocatedFrameAddressesEmitted.ToArray();
		var propagatedDefinitions = new HashSet<int>(
			instruction.Definitions.Where(
				_allocatedKnownNonNullValues.Contains));
		if (instruction.Definitions is [var copyDestination] &&
			instruction.Uses is [var copySource] &&
			(instruction.Operation == M68kMachineOperation.Copy ||
			 instruction.SourceInstruction?.OpCode == OpCodes.Ldflda) &&
			allocated.Allocation.Registers.TryGetValue(copySource, out var sourceLocation) &&
			_allocatedKnownNonNullRegisters.Contains(sourceLocation.Register))
		{
			propagatedDefinitions.Add(copyDestination);
			_allocatedKnownNonNullValues.Add(copyDestination);
		}

		foreach (var register in instruction.Clobbers.Enumerate())
		{
			_allocatedKnownNonNullRegisters.Remove(register);
		}
		foreach (var definition in instruction.Definitions)
		{
			if (!allocated.Allocation.Registers.TryGetValue(definition, out var location))
			{
				continue;
			}
			_allocatedKnownNonNullRegisters.Remove(location.Register);
			if (location.IsPair)
			{
				_allocatedKnownNonNullRegisters.Remove(
					(M68kRegister)((int)location.Register + 1));
			}
		}
		foreach (var definition in instruction.Definitions.Where(
			propagatedDefinitions.Contains))
		{
			AddAllocatedNonNullValueRegister(allocated, definition);
		}
		foreach (var register in emittedFrameAddresses)
		{
			_allocatedKnownNonNullRegisters.Add(register);
		}
		_allocatedFrameAddressesEmitted.Clear();
	}

	private void AddAllocatedNonNullValueRegister(
		M68kAllocatedFunction allocated,
		int value)
	{
		if (!allocated.Allocation.IsSpilled(value) &&
			allocated.Allocation.Registers.TryGetValue(value, out var location) &&
			!location.IsPair)
		{
			_allocatedKnownNonNullRegisters.Add(location.Register);
		}
	}

	private IEnumerable<int> GetAllocatedNonNullTransferValues(
		CilMethod method,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		// This set began as address non-nullness, but an SSA value proven to be
		// non-zero is the same fact.  Retaining non-zero scalar constants and
		// branch facts here also lets integer division consume an explicit source
		// guard instead of emitting a second fatal check.
		if (instruction.Operation == M68kMachineOperation.Constant &&
			instruction.Definitions is [var constantDefinition] &&
			instruction.ConstantValue is { } constant &&
			constant.TryGetIntegral(out var integral) &&
			integral != 0)
		{
			yield return constantDefinition;
		}

		if (instruction.Operation is
			M68kMachineOperation.LocalAddress or
			M68kMachineOperation.ArgumentAddress or
			M68kMachineOperation.Address or
			M68kMachineOperation.ObjectAllocate or
			M68kMachineOperation.ArrayAllocate or
			M68kMachineOperation.Box or
			M68kMachineOperation.DelegateCreate)
		{
			foreach (var definition in instruction.Definitions)
			{
					yield return definition;
				}
			}
		else if (instruction.Operation == M68kMachineOperation.Argument)
		{
			var isValueTypeThis = instruction.ArgumentIndex == 0 &&
				method.Signature.Header.IsInstance &&
				_module.GetMethodDeclaringType(method).Kind == CilTypeKind.ValueType;
			foreach (var definition in instruction.Definitions.Where(definition =>
				isValueTypeThis || allocated.Function.Values[definition].Kind is
					CilStackValueKind.ManagedPointer or
					CilStackValueKind.AggregateAddress))
			{
				yield return definition;
			}
		}
		else if (instruction.Operation == M68kMachineOperation.SpillLoad)
		{
			// Spill rewriting clones the exact SSA value kind.  Reloading a managed
			// by-reference or aggregate address restores that address; it cannot turn
			// a compiler-created frame/byref value into a nullable guest pointer.
			foreach (var definition in instruction.Definitions.Where(definition =>
				allocated.Function.Values[definition].Kind is
					CilStackValueKind.ManagedPointer or
					CilStackValueKind.AggregateAddress))
			{
				yield return definition;
			}
		}

		if (instruction.Operation == M68kMachineOperation.Load &&
			instruction.SourceInstruction is { } load)
		{
			if (IsIndirectLoad(load.OpCode))
			{
				yield return instruction.Uses[0];
			}
			else if (load.OpCode == OpCodes.Ldfld || load.OpCode == OpCodes.Ldflda)
			{
				var field = ResolveAllocatedField(method, instruction);
				if (!field.IsStatic && !_module.IsTransparentScalarField(field))
				{
					yield return instruction.Uses[0];
					if (load.OpCode == OpCodes.Ldflda && instruction.Definitions.Length != 0)
					{
						yield return instruction.Definitions[0];
					}
				}
			}
		}
		else if (instruction.Operation == M68kMachineOperation.Store &&
			instruction.SourceInstruction is { } store)
		{
			if (IsIndirectStore(store.OpCode))
			{
				yield return instruction.Uses[0];
			}
			else if (store.OpCode == OpCodes.Stfld)
			{
				var field = ResolveAllocatedField(method, instruction);
				if (!field.IsStatic &&
					!_module.IsTransparentScalarField(field) &&
					!IsConstructorReceiver(method, allocated.Function, instruction.Uses[0]))
				{
					yield return instruction.Uses[0];
				}
			}
		}
		else if (instruction.Operation == M68kMachineOperation.AggregateFieldLoad)
		{
			var field = ResolveAllocatedField(method, instruction);
			if (!field.IsStatic)
			{
				yield return instruction.Uses[0];
			}
		}
		else if (instruction.Operation is
			M68kMachineOperation.ArrayLoad or
			M68kMachineOperation.ArrayStore or
			M68kMachineOperation.ArrayAddress or
			M68kMachineOperation.AggregateArrayLoad or
			M68kMachineOperation.AggregateArrayStore or
			M68kMachineOperation.AggregateIndirectLoad or
			M68kMachineOperation.AggregateIndirectStore or
			M68kMachineOperation.AggregateIndirectCopy or
			M68kMachineOperation.AggregateIndirectInitialize or
			M68kMachineOperation.Unbox)
		{
			yield return instruction.Uses[0];
			if (instruction.Operation == M68kMachineOperation.AggregateIndirectCopy)
			{
				yield return instruction.Uses[1];
			}
		}
		else if (instruction.Operation == M68kMachineOperation.FunctionAddress &&
			instruction.SourceInstruction?.OpCode == OpCodes.Ldvirtftn)
		{
			yield return instruction.Uses[0];
		}
		else if (instruction.Operation == M68kMachineOperation.Call &&
			instruction.SourceInstruction is { } call &&
			call.OpCode == OpCodes.Callvirt &&
			call.ConstrainedTypeToken is null &&
			instruction.Uses.Length != 0)
		{
			yield return instruction.Uses[0];
		}
	}

	private static void AddAllocatedNonNullBranchEdgeFact(
		M68kMachineFunction function,
		M68kMachineBlock block,
		int successor,
		HashSet<int> facts,
		IReadOnlyDictionary<int, int> copySources)
	{
		if (block.Instructions.LastOrDefault() is not
			{
				Operation: M68kMachineOperation.ConditionalBranch,
				Uses.Length: > 0
			} branch ||
			block.Successors.Count < 2)
		{
			return;
		}

		var branchTarget = block.Successors[0];
		if (branch.BranchCondition is
			{
				SourceKind: M68kMachineConditionSourceKind.Test,
				Condition: M68kCondition.Equal or M68kCondition.NotEqual
			} testCondition &&
			branch.Uses.Length == 1)
		{
			var nonZeroEdge = testCondition.Condition == M68kCondition.NotEqual
				? branchTarget
				: block.Successors[1];
			if (successor == nonZeroEdge)
			{
				AddAllocatedNonZeroFactWithCopySources(
					branch.Uses[0],
					facts,
					copySources);
			}
			return;
		}
		if (branch.SourceInstruction?.OpCode is var op &&
			(op == OpCodes.Brtrue || op == OpCodes.Brtrue_S ||
			 op == OpCodes.Brfalse || op == OpCodes.Brfalse_S) &&
			branch.Uses.Length == 1)
		{
			var nonZeroEdge = op == OpCodes.Brtrue || op == OpCodes.Brtrue_S
				? branchTarget
				: block.Successors[1];
			if (successor == nonZeroEdge)
			{
				AddAllocatedNonZeroFactWithCopySources(
					branch.Uses[0],
					facts,
					copySources);
			}
			return;
		}

		if (branch.BranchCondition is not
			{
				SourceKind: M68kMachineConditionSourceKind.Compare,
				Condition: M68kCondition.Equal or M68kCondition.NotEqual
			} condition ||
			branch.Uses.Length != 2)
		{
			return;
		}
		var zeroIndex = TryGetAllocatedConstant(
			function,
			branch.Uses[1],
			out var rightConstant) && rightConstant == 0 ? 1 :
			TryGetAllocatedConstant(
				function,
				branch.Uses[0],
				out var leftConstant) && leftConstant == 0 ? 0 : -1;
		if (zeroIndex < 0)
		{
			return;
		}
		var valueIndex = 1 - zeroIndex;
		if (TryGetAllocatedConstant(
				function,
				branch.Uses[valueIndex],
				out var constant) &&
			constant == 0)
		{
			return;
		}
		var nonZeroTarget = condition.Condition == M68kCondition.NotEqual
			? branchTarget
			: block.Successors[1];
		if (successor == nonZeroTarget)
		{
			AddAllocatedNonZeroFactWithCopySources(
				branch.Uses[valueIndex],
				facts,
				copySources);
		}
	}

	private static void AddAllocatedNonZeroFactWithCopySources(
		int value,
		HashSet<int> facts,
		IReadOnlyDictionary<int, int> copySources)
	{
		do
		{
			facts.Add(value);
		}
		while (copySources.TryGetValue(value, out value));
	}

	private static bool IsAddressLike(CilStackValueKind kind) =>
		kind is CilStackValueKind.Reference or
			CilStackValueKind.ManagedPointer or
			CilStackValueKind.AggregateAddress;

	private void EmitAllocatedRequireNonNull(
		int value,
		M68kRegister register)
	{
		if (!_allocatedKnownNonNullValues.Add(value) ||
			_allocatedKnownNonNullRegisters.Contains(register))
		{
			_allocatedKnownNonNullRegisters.Add(register);
			return;
		}
		EmitAllocatedRequireNonNull(register);
		_allocatedKnownNonNullRegisters.Add(register);
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
		_assembler.EmitWord((ushort)(
			0x42A8 |
			((int)baseRegister - (int)M68kRegister.A0)));
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitAllocatedArrayBoundsCheck(
		int arrayValue,
		M68kRegister baseRegister,
		M68kRegister indexRegister)
	{
		if (baseRegister < M68kRegister.A0 ||
			indexRegister > M68kRegister.D7)
		{
			throw new InvalidOperationException(
				"Allocated array access has invalid base or index registers.");
		}
		EmitAllocatedRequireNonNull(arrayValue, baseRegister);
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
				8 => 3,
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

	private static M68kMachineValueWidth AllocatedFrameStorageWidth(
		CilType type,
		M68kMachineValueWidth valueWidth) =>
		type.Size switch
		{
			1 => M68kMachineValueWidth.Byte,
			2 => M68kMachineValueWidth.Word,
			_ => valueWidth
		};

	private int AllocatedFrameOffset(
		M68kAllocatedFunction allocated,
		int offset) =>
		allocated.Function.HasDynamicStackAllocation
			? offset
			: checked(_allocatedOutgoingStackBytes + offset);

	private void EmitAllocatedFrameHomeInitialization(
		CilMethod method,
		InternalCallAbi abi,
		M68kAllocatedFunction allocated,
		int savedBytes)
	{
		if (method.InitializeLocals)
		{
			var overwrittenLocals = M68kFrameInitializationAnalysis.FindEntryOverwrites(
				allocated.Function,
				instruction => AllocatedEntryLocalWriteSize(method, allocated, instruction));
			var clearDisplacements = allocated.Function.LocalHomes.Values
				.Where(static home => home.Initialize)
				.OrderBy(static home => home.Index)
				.SelectMany(home => Enumerable.Range(0, home.Size / 4)
					.Where(index => !overwrittenLocals.Contains((home.Index, index * 4)))
					.Select(index => AllocatedFrameOffset(
						allocated,
						checked(allocated.Frame.LocalOffsets[home.Index] + (index * 4)))))
				.ToArray();
			var isContiguousLargeClear = clearDisplacements.Length >= 8 &&
				clearDisplacements.Select((displacement, index) =>
					displacement == clearDisplacements[0] + (index * 4)).All(static value => value);
			if (TryEmitAllocatedFrameClearRuns(abi, allocated, clearDisplacements))
			{
				// The complete run plan wins before any bytes are emitted.
			}
			else if (_request.Cpu == M68kCpuTarget.M68000 &&
				_request.ClrPolicy != M68kClrPolicy.Always &&
				isContiguousLargeClear &&
				TrySelectAllocatedFrameScratchRegister(
					abi,
					allocated,
					address: false,
					excluded: null,
					out var counterRegister) &&
				TrySelectAllocatedFrameScratchRegister(
					abi,
					allocated,
					address: true,
					excluded: null,
					out var addressRegister) &&
				TrySelectAllocatedFrameScratchRegister(
					abi,
					allocated,
					address: false,
					excluded: counterRegister,
					out var zeroRegister))
			{
				EmitAllocatedScratchFrameClear(
					clearDisplacements, counterRegister, addressRegister, zeroRegister);
			}
			else if (_request.Cpu == M68kCpuTarget.M68000 &&
				_request.ClrPolicy != M68kClrPolicy.Always &&
				isContiguousLargeClear &&
				clearDisplacements.Length >= 13 &&
				TryEmitAllocatedPreservedScratchFrameClear(
					abi,
					allocated,
					clearDisplacements))
			{
				// Emitted by the helper.
			}
			else if (_request.Cpu == M68kCpuTarget.M68000 &&
				_request.ClrPolicy != M68kClrPolicy.Always &&
				clearDisplacements.Length >= 2 &&
				TrySelectAllocatedFrameZeroRegister(
					abi,
					allocated,
					out var unrolledZeroRegister))
			{
				_assembler.EmitWord((ushort)(0x7000 | ((int)unrolledZeroRegister << 9)));
				foreach (var displacement in clearDisplacements)
				{
					EmitAllocatedFrameStore(
						unrolledZeroRegister,
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
			if (_allocatedIncomingArgumentHomes.Contains(home.Index)) continue;
			var source = abi.Arguments[home.Index];
			if (home.Size > 4)
			{
				if (!source.IsStack || source.SlotLongs * 4 != home.Size)
				{
					throw new InvalidOperationException(
						$"Multiword argument home {home.Index} does not match its incoming stack value.");
				}
				for (var offset = 0; offset < home.Size; offset += 4)
				{
					EmitAllocatedIncomingStackToFrame(
						checked(savedBytes + 4 + source.StackOffset + offset),
						checked(AllocatedFrameOffset(
							allocated,
							allocated.Frame.ArgumentHomeOffsets[home.Index]) +
							offset));
				}
				continue;
			}
			if (home.Size != 4)
			{
				throw new InvalidOperationException(
					$"Allocated argument home {home.Index} has unsupported size {home.Size}.");
			}
			if (source.Register is null)
			{
				// MC68000 MOVE supports memory-to-memory copies. No implicit D7
				// scratch (or temporary stack-depth adjustment) is needed here.
				EmitAllocatedIncomingStackToFrame(
					checked(savedBytes + 4 + source.StackOffset),
					AllocatedFrameOffset(
						allocated,
						allocated.Frame.ArgumentHomeOffsets[home.Index]));
				continue;
			}
			EmitAllocatedFrameStore(
				source.Register.Value,
				M68kMachineValueWidth.Long,
				AllocatedFrameOffset(
					allocated,
					allocated.Frame.ArgumentHomeOffsets[home.Index]));
		}
	}

	private bool TryEmitAllocatedPreservedScratchFrameClear(
		InternalCallAbi abi,
		M68kAllocatedFunction allocated,
		IReadOnlyList<int> clearDisplacements)
	{
		var preserveAddress = !TrySelectAllocatedFrameScratchRegister(
			abi,
			allocated,
			address: true,
			excluded: null,
			out var addressRegister);
		if (preserveAddress)
		{
			// Saving a third register first wins both bytes and deterministic
			// MC68000 cycles at 17 longwords.  With an already available address
			// scratch, preserving only D6/D7 breaks even at 13.
			if (clearDisplacements.Count < 17)
			{
				return false;
			}
			addressRegister = M68kRegister.A6;
		}

		M68kRegister[] preservedRegisters = preserveAddress
			? [M68kRegister.D6, M68kRegister.D7, M68kRegister.A6]
			: [M68kRegister.D6, M68kRegister.D7];
		EmitPushRegisters(preservedRegisters);
		var temporaryStackBytes = UsesAllocatedFrameAnchor
			? 0
			: preservedRegisters.Length * 4;
		EmitAllocatedFrameAddress(
			addressRegister,
			checked(clearDisplacements[0] + temporaryStackBytes),
			trackNonNull: false);
		_assembler.EmitWord(0x7C00); // MOVEQ #0,D6
		var remainder = clearDisplacements.Count % 4;
		for (var index = 0; index < remainder; index++)
		{
			_assembler.EmitWord((ushort)(
				0x20C0 |
				(((int)addressRegister - (int)M68kRegister.A0) << 9) |
				(int)M68kRegister.D6));
		}
		EmitAllocatedImmediate(
			(clearDisplacements.Count / 4) - 1,
			M68kRegister.D7);
		var loop = UniqueLabel("allocated-preserved-frame-zero-loop");
		_assembler.Mark(loop);
		for (var index = 0; index < 4; index++)
		{
			_assembler.EmitWord((ushort)(
				0x20C0 |
				(((int)addressRegister - (int)M68kRegister.A0) << 9) |
				(int)M68kRegister.D6));
		}
		_assembler.EmitDbra((int)M68kRegister.D7, loop);
		EmitPopRegisters(preservedRegisters);
		return true;
	}

	private static bool TrySelectAllocatedFrameZeroRegister(
		InternalCallAbi abi,
		M68kAllocatedFunction allocated,
		out M68kRegister register)
	{
		var incoming = new HashSet<M68kRegister>();
		foreach (var argument in abi.Arguments)
		{
			if (argument.Register is { } highRegister &&
				highRegister <= M68kRegister.D7)
			{
				incoming.Add(highRegister);
			}
			if (argument.LowRegister is { } lowRegister &&
				lowRegister <= M68kRegister.D7)
			{
				incoming.Add(lowRegister);
			}
		}
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

	private static bool TrySelectAllocatedFrameScratchRegister(
		InternalCallAbi abi,
		M68kAllocatedFunction allocated,
		bool address,
		M68kRegister? excluded,
		out M68kRegister register)
	{
		var incoming = new HashSet<M68kRegister>();
		foreach (var argument in abi.Arguments)
		{
			if (argument.Register is { } highRegister)
			{
				incoming.Add(highRegister);
			}
			if (argument.LowRegister is { } lowRegister)
			{
				incoming.Add(lowRegister);
			}
		}

		var first = address ? M68kRegister.A0 : M68kRegister.D0;
		var last = address ? M68kRegister.A6 : M68kRegister.D7;
		for (register = first; register <= last; register++)
		{
			var callerSaved = address
				? register <= M68kRegister.A1
				: register <= M68kRegister.D1;
			if (register != excluded &&
				!incoming.Contains(register) &&
				(callerSaved || allocated.Frame.CalleeSavedRegisters.Contains(register)))
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
		if (allocated.Function.HasDynamicStackAllocation)
		{
			_assembler.EmitWord(0x2E4D); // MOVEA.L A5,A7 discard dynamic allocations
		}
		EmitAllocatedCalleeRestores(
			allocated.Frame.CalleeSavedRegisters,
			allocated.Frame.FrameBytes);
	}

	private void EmitAllocatedDynamicStackAllocation(
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		var source = allocated.Allocation.Registers[instruction.Uses[0]].Register;
		var destination = allocated.Allocation.Registers[instruction.Definitions[0]].Register;
		var overflow = UniqueLabel("dynamic-localloc-overflow");
		var done = UniqueLabel("dynamic-localloc-done");

		EmitAllocatedMove(source, M68kRegister.D0, M68kMachineValueWidth.Long);
		_assembler.EmitWord(0x5680); // ADDQ.L #3,D0
		_assembler.EmitBranch(M68kCondition.CarrySet, overflow);
		_assembler.EmitWord(0x0280); // ANDI.L #-4,D0
		_assembler.EmitLong(0xFFFF_FFFC);
		_assembler.EmitWord(0x220F); // MOVE.L A7,D1
		_assembler.EmitWord(0x9280); // SUB.L D0,D1
		_assembler.EmitBranch(M68kCondition.CarrySet, overflow);
		_assembler.EmitWord(0x2E41); // MOVEA.L D1,A7
		EmitAllocatedMove(M68kRegister.D1, destination, M68kMachineValueWidth.Long);
		_assembler.EmitBranch(M68kCondition.True, done);
		_assembler.Mark(overflow);
		EmitExceptionRaise(reason: 4, hasException: false);
		_assembler.Mark(done);
	}

	private void EmitAllocatedCheckedUnsignedMultiply()
	{
		var loop = UniqueLabel("checked-unsigned-multiply-loop");
		var skipAdd = UniqueLabel("checked-unsigned-multiply-skip-add");
		var done = UniqueLabel("checked-unsigned-multiply-done");
		var overflow = UniqueLabel("checked-unsigned-multiply-overflow");

		_assembler.EmitWord(0x7400); // MOVEQ #0,D2 accumulator
		_assembler.Mark(loop);
		_assembler.EmitWord(0xE289); // LSR.L #1,D1
		_assembler.EmitBranch(M68kCondition.CarryClear, skipAdd);
		_assembler.EmitWord(0xD480); // ADD.L D0,D2
		_assembler.EmitBranch(M68kCondition.CarrySet, overflow);
		_assembler.Mark(skipAdd);
		_assembler.EmitWord(0x4A81); // TST.L D1
		_assembler.EmitBranch(M68kCondition.Equal, done);
		_assembler.EmitWord(0xD080); // ADD.L D0,D0 (shift left)
		_assembler.EmitBranch(M68kCondition.CarrySet, overflow);
		_assembler.EmitBranch(M68kCondition.True, loop);
		_assembler.Mark(overflow);
		EmitExceptionRaise(reason: 4, hasException: false);
		_assembler.Mark(done);
		_assembler.EmitWord(0x2002); // MOVE.L D2,D0
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
		if (!AllocatedTailReturnUsesCallResult(
			call,
			trailing,
			caller.Signature.ReturnType.IsVoid))
		{
			return false;
		}
		if (call.RequiresLiveCallerFrame)
		{
			return false;
		}
		var source = call.SourceInstruction!;
		if (source.OpCode != OpCodes.Call)
		{
			return false;
		}
		var target = ResolveAllocatedMachineMethod(caller, call);
		if (target.Definition is not { IsImport: false, ExternalCall: null } callee ||
			IsNativeShadowMathLeaf(callee) ||
			IsAlwaysInlinedMethod(callee) ||
			GetActiveExceptionGroups(caller, call.IlOffset).Length != 0 ||
			callee.Signature.ReturnType.IsVoid !=
				caller.Signature.ReturnType.IsVoid ||
			(!caller.Signature.ReturnType.IsVoid &&
			 IsInternalAddressReturn(caller.Signature.ReturnType) !=
			 IsInternalAddressReturn(callee.Signature.ReturnType)))
		{
			return false;
		}
		if (_loadedPlatformBase is { } activePlatformBase &&
			(!_platformBaseMethodEntries.TryGetValue(caller.Identity, out var entryIdentity) ||
			 entryIdentity != activePlatformBase.Binding.Identity))
		{
			// Teardown restores callee-saved registers to their method-entry
			// values. Keep a normal call when that would change the base identity
			// promised to the callee by interprocedural analysis.
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

	private static bool AllocatedTailReturnUsesCallResult(
		M68kMachineInstruction call,
		IReadOnlyList<M68kMachineInstruction> trailing,
		bool returnsVoid)
	{
		var returned = trailing[^1];
		if (returnsVoid)
		{
			return returned.Uses.Length == 0;
		}
		if (returned.Uses.Length != 1 || call.Definitions.Length == 0)
		{
			return false;
		}

		// A syntactically final call is not necessarily the value-producing tail
		// expression. Release CIL commonly discards a status-returning call and
		// then returns a previously computed local. Follow the return SSA value
		// backwards through the only operations admitted above; only a value rooted
		// in this call may replace Call/Copy*/Return with a JMP.
		var value = returned.Uses[0];
		for (var index = trailing.Count - 2; index >= 0; index--)
		{
			var copy = trailing[index];
			if (!copy.Definitions.Contains(value)) continue;
			if (copy.Definitions.Length != 1 || copy.Uses.Length != 1)
			{
				return false;
			}
			value = copy.Uses[0];
		}
		return call.Definitions.Contains(value);
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
		var field = ResolveAllocatedField(method, instruction);
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

	private void EmitAllocatedPlatformBaseLoad(
		CilMethod method,
		M68kMachineInstruction instruction,
		M68kRegister destination)
	{
		var requested = instruction.PlatformBaseConvention ??
			throw new InvalidOperationException(
				$"Platform-base load {instruction.Id} has no convention.");
		var binding = ResolveAllocatedPlatformBase(requested, method).Binding;
		switch (binding.BaseSource)
		{
			case M68kExternalBaseSource.CachedPointer:
				EmitAllocatedMove(
					binding.CacheRegister ??
						throw new InvalidOperationException(
							"Cached platform-base load has no cache register."),
					destination,
					M68kMachineValueWidth.Long);
				return;

			case M68kExternalBaseSource.WritableSlot:
				if (UsesRomSourceAddress(binding))
				{
					EmitAllocatedAbsoluteLongLoad(binding.SourceAddress, destination);
					return;
				}
				EmitAllocatedPlatformSlotLoad(
					binding.SlotSymbol ??
						throw new InvalidOperationException(
							"Writable platform-base load has no slot symbol."),
					destination);
				return;

			case M68kExternalBaseSource.Immediate:
				EmitAllocatedImmediate(unchecked((int)binding.InitialValue), destination);
				return;

			default:
				throw new InvalidOperationException(
					$"Explicit platform-base load cannot use {binding.BaseSource}.");
		}
	}

	private void EmitAllocatedPlatformBaseStore(
		CilMethod method,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		var requested = instruction.PlatformBaseConvention ??
			throw new InvalidOperationException(
				$"Platform-base store {instruction.Id} has no convention.");
		var binding = ResolveAllocatedPlatformBase(requested, method).Binding;
		if (binding.BaseSource != M68kExternalBaseSource.WritableSlot ||
			binding.SlotSymbol is not { } slotSymbol)
		{
			throw new InvalidOperationException(
				"Platform-base stores require a writable slot.");
		}
		if (instruction.Immediate == 0)
		{
			EmitClearLabel(slotSymbol);
		}
		else
		{
			if (instruction.Uses is not [var source])
			{
				throw new InvalidOperationException(
					$"Platform-base store {instruction.Id} has no source value.");
			}
			EmitStoreRegisterDirectToLabel(
				allocated.Allocation.Registers[source].Register,
				slotSymbol);
		}
		_loadedPlatformBase = null;
	}

	private GeneratedPlatformBase ResolveAllocatedPlatformBase(
		M68kExternalCallConvention requested,
		CilMethod method)
	{
		if (_usedPlatformBases.TryGetValue(requested.Identity, out var existing))
		{
			if (existing.Binding.BaseSource != requested.BaseSource ||
				existing.Binding.BaseRegister != requested.BaseRegister ||
				existing.Binding.SlotSymbol != requested.SlotSymbol)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					$"Platform base '{requested.Identity}' has conflicting declarations.",
					method.DisplayName);
			}
			return existing;
		}
		return GetOrAddPlatformBase(requested, method);
	}

	private void EmitAllocatedPlatformSlotLoad(
		string label,
		M68kRegister destination)
	{
		var destinationEa = destination <= M68kRegister.D7
			? (int)destination << 9
			: (((int)destination - (int)M68kRegister.A0) << 9) | 0x40;
		if (TryGetResidentInvocationOffset(label, out var offset))
		{
			_assembler.EmitWord((ushort)(0x202D | destinationEa)); // MOVE.L d16(A5),reg
			_assembler.EmitWord(unchecked((ushort)offset));
			return;
		}
		_assembler.EmitWord((ushort)(0x2039 | destinationEa)); // MOVE.L abs.l,reg
		_assembler.EmitAddress(label);
	}

	private void EmitAllocatedAbsoluteLongLoad(
		uint address,
		M68kRegister destination)
	{
		var destinationEa = destination <= M68kRegister.D7
			? (int)destination << 9
			: (((int)destination - (int)M68kRegister.A0) << 9) | 0x40;
		if (address <= short.MaxValue)
		{
			_assembler.EmitWord((ushort)(0x2038 | destinationEa)); // MOVE.L abs.w,reg
			_assembler.EmitWord((ushort)address);
			return;
		}
		_assembler.EmitWord((ushort)(0x2039 | destinationEa)); // MOVE.L abs.l,reg
		_assembler.EmitLong(address);
	}

	private void EmitAllocatedTypeInitialization(
		CilMethod caller,
		M68kMachineInstruction instruction)
	{
		var source = instruction.SourceInstruction ??
			throw new InvalidOperationException("Type initialization has no source instruction.");
		var initializer = _module.GetTriggeredTypeInitializer(caller, source) ??
			throw new InvalidOperationException("Type initialization trigger has no initializer.");
		_typeInitializers.TryAdd(initializer.Identity, initializer);

		var stateLabel = TypeInitializationStateLabel(initializer);
		var doneLabel = UniqueLabel("type-init-done");
		var failedLabel = UniqueLabel("type-init-failed");
		var canFail = TypeInitializerCanFail(initializer);
		_assembler.EmitWord(0x2039); // MOVE.L state,D0
		_assembler.EmitAddress(stateLabel);
		_assembler.EmitWord(0x0C80); // CMPI.L #initialized,D0
		_assembler.EmitLong(2);
		_assembler.EmitBranch(M68kCondition.Equal, doneLabel);
		if (canFail)
		{
			_assembler.EmitWord(0x0C80); // CMPI.L #failed,D0
			_assembler.EmitLong(3);
			_assembler.EmitBranch(M68kCondition.Equal, failedLabel);
		}
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.NotEqual, doneLabel); // Recursive initialization.
		_assembler.EmitWord(0x23FC); // MOVE.L #initializing,state
		_assembler.EmitLong(1);
		_assembler.EmitAddress(stateLabel);
		_assembler.EmitCall(MethodLabel(initializer));
		RegisterCurrentUnwindSite(
			exception: true,
			gc: true,
			exceptionCleanupLabel: canFail
				? TypeInitializationFailureThunkLabel(initializer)
				: null);
		_assembler.EmitWord(0x23FC); // MOVE.L #initialized,state
		_assembler.EmitLong(2);
		_assembler.EmitAddress(stateLabel);
		if (canFail)
		{
			_assembler.EmitBranch(M68kCondition.True, doneLabel);
			_assembler.Mark(failedLabel);
			_assembler.EmitWord(0x2079); // MOVEA.L cached-exception,A0
			_assembler.EmitAddress(TypeInitializationExceptionLabel(initializer));
			EmitExceptionRaise(reason: 0, hasException: true);
		}
		_assembler.Mark(doneLabel);
		_loadedPlatformBase = null;
	}

	private void EmitAllocatedTypeTest(
		CilMethod caller,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		var sourceInstruction = instruction.SourceInstruction ??
			throw new InvalidOperationException("Runtime type test has no source instruction.");
		var target = _module.ResolveRuntimeTypeToken(
			(int)sourceInstruction.Operand!,
			caller,
			instruction.IlOffset);
		var targetLabel = RuntimeTypeTestIdentityLabel(target);
		var success = UniqueLabel("type-test-success");
		var failure = UniqueLabel("type-test-failure");
		var done = UniqueLabel("type-test-done");

		var source = allocated.Allocation.Registers[instruction.Uses.Single()].Register;
		var destination = allocated.Allocation.Registers[
			instruction.Definitions.Single()].Register;
		EmitAllocatedMove(source, M68kRegister.A0, M68kMachineValueWidth.Long);
		EmitAllocatedMove(source, M68kRegister.D0, M68kMachineValueWidth.Long);
		EmitAllocatedTest(M68kRegister.A0, M68kMachineValueWidth.Long);
		_assembler.EmitBranch(M68kCondition.Equal, success); // Null casts and tests succeed.
		_assembler.EmitWord(0x2250); // MOVEA.L (A0),A1 descriptor
		EmitAddressImmediateToRegister(M68kRegister.A2, targetLabel);
		if (IsFrameworkDelegateType(target.Type))
		{
			EmitAllocatedBaseLoad(
				M68kRegister.A0,
				M68kRegister.D1,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.DelegateFlagsOffset);
			_assembler.EmitWord(0x0281); // ANDI.L #multicast,D1
			_assembler.EmitLong(M68kRuntimeAbi.DelegateFlagMulticast);
			var singlecastDelegate = UniqueLabel("type-test-singlecast-delegate");
			_assembler.EmitBranch(M68kCondition.Equal, singlecastDelegate);
			EmitAllocatedBaseLoad(
				M68kRegister.A0,
				M68kRegister.A1,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.DelegateThunkOffset);
			_assembler.Mark(singlecastDelegate);
		}

		if (target.IsInterface)
		{
			var loop = UniqueLabel("type-test-interface-loop");
			_assembler.EmitWord(0x2269); // MOVEA.L interface-map(A1),A1
			_assembler.EmitWord(unchecked((ushort)M68kRuntimeAbi.TypeInterfaceMapOffset));
			EmitMoveRegister(M68kRegister.A1, M68kRegister.D1);
			_assembler.EmitWord(0x4A81); // TST.L D1
			_assembler.EmitBranch(M68kCondition.Equal, failure);
			_assembler.EmitWord(0x2219); // MOVE.L (A1)+,D1 entry count
			_assembler.Mark(loop);
			_assembler.EmitWord(0x2059); // MOVEA.L (A1)+,A0 interface identity
			_assembler.EmitWord(0xB1CA); // CMPA.L A2,A0
			_assembler.EmitBranch(M68kCondition.Equal, success);
			_assembler.EmitWord(0x5889); // ADDQ.L #4,A1 skip method table
			_assembler.EmitWord(0x5381); // SUBQ.L #1,D1
			_assembler.EmitBranch(M68kCondition.NotEqual, loop);
		}
		else
		{
			var loop = UniqueLabel("type-test-base-loop");
			_assembler.Mark(loop);
			_assembler.EmitWord(0xB3CA); // CMPA.L A2,A1
			_assembler.EmitBranch(M68kCondition.Equal, success);
			_assembler.EmitWord(0x2269); // MOVEA.L base-type(A1),A1
			_assembler.EmitWord(unchecked((ushort)M68kRuntimeAbi.TypeBaseOffset));
			EmitMoveRegister(M68kRegister.A1, M68kRegister.D1);
			_assembler.EmitWord(0x4A81); // TST.L D1
			_assembler.EmitBranch(M68kCondition.NotEqual, loop);
		}

		_assembler.Mark(failure);
		if (sourceInstruction.OpCode == OpCodes.Castclass)
		{
			RegisterRuntimeTypeDescriptor("System.InvalidCastException");
			EmitExceptionRaise(reason: 7, hasException: false);
		}
		else
		{
			EmitAllocatedImmediate(0, M68kRegister.D0);
		}
		_assembler.EmitBranch(M68kCondition.True, done);
		_assembler.Mark(success);
		_assembler.Mark(done);
		EmitAllocatedMove(M68kRegister.D0, destination, M68kMachineValueWidth.Long);
	}

	private void EmitTypeInitializationFailureThunks()
	{
		if (!_usesExceptionRuntime)
		{
			return;
		}
		foreach (var initializer in _typeInitializers.Values
			.Where(TypeInitializerCanFail)
			.OrderBy(item => item.ModuleName, StringComparer.Ordinal)
			.ThenBy(item => System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(item.DeclaringType)))
		{
			_assembler.AlignWord();
			_assembler.Mark(TypeInitializationFailureThunkLabel(initializer));
			_assembler.EmitWord(0x23FC); // MOVE.L #failed,state
			_assembler.EmitLong(3);
			_assembler.EmitAddress(TypeInitializationStateLabel(initializer));
			RegisterRuntimeTypeDescriptor("System.TypeInitializationException");
			_assembler.EmitWord(0x23D6); // MOVE.L original-exception,wrapper-inner
			_assembler.EmitAddress(TypeInitializationWrapperInnerLabel(initializer));
			_assembler.EmitWord(0x2CBC); // MOVE.L #wrapper,(A6) active exception
			_assembler.EmitAddress(TypeInitializationWrapperLabel(initializer));
			_assembler.EmitWord(0x23FC); // MOVE.L #wrapper,cached-exception
			_assembler.EmitAddress(TypeInitializationWrapperLabel(initializer));
			_assembler.EmitAddress(TypeInitializationExceptionLabel(initializer));
			_assembler.EmitWord(0x4E75); // RTS
		}
	}

	private void EmitBoxedInterfaceThunks()
	{
		foreach (var item in _boxedStructLayouts.OrderBy(item => item.Key, StringComparer.Ordinal))
		{
			var type = _boxedTypes[item.Key];
			var transparent = _module.IsTransparentScalarType(type);
			foreach (var implementation in GetUsedInterfaceImplementations(item.Value))
			{
				foreach (var method in implementation.Methods)
				{
					var parameters = method.Signature.ParameterTypes;
					var adaptsSingleDataArgument = transparent &&
						parameters is [var parameter] &&
						parameter.IsSupportedScalar &&
						!IsBoxedThunkAddressArgument(parameter) &&
						parameter.Size <= 4;
					var adaptsSingleAddressArgument = transparent &&
						parameters is [var addressParameter] &&
						IsBoxedThunkAddressArgument(addressParameter);
					var adaptsSingleLongPairArgument = transparent &&
						parameters is [var longPairParameter] &&
						longPairParameter.IsSupportedScalar &&
						longPairParameter.Size == 8;
					var adaptsTwoRegisterArguments = transparent &&
						parameters.Length == 2 &&
						parameters.All(IsBoxedThunkRegisterArgument);
					if (transparent &&
						parameters.Length != 0 &&
						!adaptsSingleDataArgument &&
						!adaptsSingleAddressArgument &&
						!adaptsSingleLongPairArgument &&
						!adaptsTwoRegisterArguments)
					{
						throw new M68kCompilationException(
							M68kDiagnosticIds.UnsupportedSignature,
							$"Boxed interface method '{method.DisplayName}' requires an argument-adaptation thunk outside the compact profile; transparent scalar boxes currently support one 64-bit scalar or up to two register-sized data/address arguments.",
							method.DisplayName);
					}

					_assembler.AlignWord();
					_assembler.Mark(BoxedInterfaceThunkLabel(type, implementation, method));
					if (transparent)
					{
						var dataArgumentCount = adaptsSingleLongPairArgument
							? 0
							: parameters.Count(parameterType =>
								!IsBoxedThunkAddressArgument(parameterType));
						var addressArgumentCount = parameters.Length - dataArgumentCount;
						if (addressArgumentCount == 2)
						{
							// With A0 occupied by the interface receiver, the first
							// address argument uses A1 and the second falls back to D0.
							// Preserve the latter before D0 becomes the payload receiver.
							EmitAllocatedMove(
								M68kRegister.D0,
								M68kRegister.A2,
								M68kMachineValueWidth.Long);
						}
						if (dataArgumentCount != 0)
						{
							// Interface ABI: A0=box, D0=argument. Value-type ABI:
							// D0=payload address, D1=first scalar argument.
							EmitAllocatedMove(
								M68kRegister.D0,
								M68kRegister.D1,
								M68kMachineValueWidth.Long);
						}
						_assembler.EmitWord(0x41E8); // LEA 8(A0),A0 payload receiver
						_assembler.EmitWord(8);
						EmitAllocatedMove(
							M68kRegister.A0,
							M68kRegister.D0,
							M68kMachineValueWidth.Long);
						if (dataArgumentCount == 2)
						{
							// The interface caller duplicates its second data argument
							// at 4(SP), because D1 must be reused for the first argument
							// after D0 becomes the transparent payload receiver. The
							// value-type body receives that overflow scalar through A0.
							_assembler.EmitWord(0x206F); // MOVEA.L 4(A7),A0
							_assembler.EmitWord(4);
						}
						if (addressArgumentCount != 0)
						{
							// Interface ABI reserves A0 for the box, so its first
							// address argument arrives in A1. The value-type body
							// uses D0 for this and therefore expects that argument in A0.
							EmitAllocatedMove(
								M68kRegister.A1,
								M68kRegister.A0,
								M68kMachineValueWidth.Long);
						}
						if (addressArgumentCount == 2)
						{
							EmitAllocatedMove(
								M68kRegister.A2,
								M68kRegister.A1,
								M68kMachineValueWidth.Long);
						}
					}
					else
					{
						_assembler.EmitWord(0x41E8); // LEA 8(A0),A0 payload receiver
						_assembler.EmitWord(8);
					}
					_assembler.EmitJmp(MethodLabel(method), external: false);
				}
			}
		}
	}

	private static bool IsBoxedThunkAddressArgument(CilType type) =>
		type.Kind is
			CilTypeKind.ManagedReference or
			CilTypeKind.ManagedPointer or
			CilTypeKind.UnmanagedPointer or
			CilTypeKind.FunctionPointer;

	private static bool IsBoxedThunkRegisterArgument(CilType type) =>
		IsBoxedThunkAddressArgument(type) ||
		type.IsSupportedScalar && type.Size <= 4;

	private void EmitAllocatedStaticStore(
		CilMethod method,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction,
		M68kRegister sourceRegister)
	{
		var source = instruction.SourceInstruction!;
		var field = ResolveAllocatedField(method, instruction);
		if (!field.IsStatic)
		{
			throw new InvalidOperationException(
				"Allocated static store resolved an instance field.");
		}
		ValidateType(field.Type, method, "field");
		_staticFields.TryAdd(field.Identity, field);
		var hasAggregateLayout = _module.TryGetReferenceFreeStructLayout(
				field.Type,
				field.ModuleName,
				out var aggregateLayout);
		if (hasAggregateLayout &&
			aggregateLayout.Size > 4)
		{
			if (sourceRegister != M68kRegister.A0)
			{
				throw new InvalidOperationException(
					"Aggregate static-field source is not constrained to A0.");
			}
			EmitAllocatedAbsoluteAddress(
				M68kRegister.A1,
				StaticFieldLabel(field));
			for (var offset = 0; offset < aggregateLayout.Size; offset += 4)
			{
				EmitAllocatedBaseLoad(
					sourceRegister,
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					checked((short)offset));
				EmitAllocatedBaseStore(
					M68kRegister.D0,
					M68kRegister.A1,
					M68kMachineValueWidth.Long,
					checked((short)offset));
			}
			return;
		}
		if (hasAggregateLayout &&
			aggregateLayout.Size == 4 &&
			allocated.Function.Values[instruction.Uses[0]].Kind ==
				CilStackValueKind.AggregateAddress)
		{
			if (sourceRegister < M68kRegister.A0)
			{
				throw new InvalidOperationException(
					"Single-word aggregate static-field source is not an address register.");
			}
			EmitAllocatedBaseLoad(
				sourceRegister,
				M68kRegister.D0,
				M68kMachineValueWidth.Long,
				0);
			EmitAllocatedAbsoluteAddress(
				M68kRegister.A1,
				StaticFieldLabel(field));
			EmitAllocatedBaseStore(
				M68kRegister.D0,
				M68kRegister.A1,
				M68kMachineValueWidth.Long,
				0);
			return;
		}
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
		var target = ResolveAllocatedMachineMethod(caller, instruction);
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
		if (source.ConstrainedTypeToken is { } constrainedTypeToken)
		{
			if (_module.TryResolveConstrainedValueInterfaceImplementation(
					caller, constrainedTypeToken, source.Offset, definition,
					out var constrainedImplementation))
				definition = constrainedImplementation;
		}
		else if (source.OpCode == OpCodes.Callvirt)
		{
			EmitAllocatedRequireNonNull(instruction.Uses[0], M68kRegister.A0);
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
		if (TryEmitAllocatedNativeShadowMathUnary(definition, allocated, instruction))
		{
			return;
		}
		if (GetInternalRegisterAbi(definition) is { } inlineAbi &&
			TryGetInlineCandidate(definition, inlineAbi, out var inlineCandidate) &&
			inlineCandidate.Kind is InlineCandidateKind.ConstantAddressReadByte or
				InlineCandidateKind.ConstantAddressReadWord or
				InlineCandidateKind.ConstantAddressWriteWord)
		{
			if (inlineCandidate.Kind == InlineCandidateKind.ConstantAddressWriteWord)
			{
				if (TryGetAllocatedConstant(
						allocated.Function,
						instruction.Uses[0],
						out var constant))
				{
					var value = (ushort)(((ushort)constant & inlineCandidate.AndMask) |
						inlineCandidate.OrMask);
					_assembler.EmitWord(0x33FC); // MOVE.W #imm,abs.l
					_assembler.EmitWord(value);
					_assembler.EmitLong(inlineCandidate.Address);
					return;
				}

				var sourceRegister = allocated.Allocation.Registers[
					instruction.Uses[0]].Register;
				EmitAllocatedMove(
					sourceRegister,
					M68kRegister.D0,
					M68kMachineValueWidth.Word);
				if (inlineCandidate.AndMask != ushort.MaxValue)
				{
					_assembler.EmitWord(0x0240); // ANDI.W #mask,D0
					_assembler.EmitWord(inlineCandidate.AndMask);
				}
				if (inlineCandidate.OrMask != 0)
				{
					_assembler.EmitWord(0x0040); // ORI.W #mask,D0
					_assembler.EmitWord(inlineCandidate.OrMask);
				}
				_assembler.EmitWord(0x33C0); // MOVE.W D0,abs.l
				_assembler.EmitLong(inlineCandidate.Address);
				return;
			}

			var destination = allocated.Allocation.Registers[
				instruction.Definitions[0]].Register;
			EmitAllocatedImmediate(0, destination);
			var destinationEa = (int)destination << 9;
			_assembler.EmitWord((ushort)(
				(inlineCandidate.Kind == InlineCandidateKind.ConstantAddressReadByte
					? 0x1039
					: 0x3039) |
				destinationEa));
			_assembler.EmitLong(inlineCandidate.Address);
			return;
		}
		if (IsAlwaysInlinedMethod(definition))
		{
			if (TryGetConstantReturnBody(definition, out var constant))
			{
				EmitAllocatedImmediate(
					constant,
					instruction.Definitions.Length != 0
						? allocated.Allocation.Registers[
							instruction.Definitions[0]].Register
						: M68kRegister.D0);
			}
			else if (instruction.Definitions.Length != 0 &&
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
			if (!instruction.HasExplicitPlatformBase &&
				externalCall.Convention.BaseSource != M68kExternalBaseSource.Argument)
			{
				throw new InvalidOperationException(
					$"Allocated external call '{definition.DisplayName}' has no " +
					"explicit platform-base SSA operand.");
			}
			_loadedPlatformBase = null;
			var callOffset = _assembler.Offset;
			EmitBaseRelativeJsr(
				externalCall.Convention.BaseRegister,
				externalCall.Convention.Displacement);
			AnnotateAllocatedExternalCallEffects(callOffset, definition, externalCall);
			EmitExternalExceptionStatusCheck(externalCall.Convention);
			return;
		}
		ValidateMethodSignature(definition, isEntry: false, isExport: false);
		if (definition.ImportName == AmigaRunCommandOnStackImport)
		{
			ValidateAmigaRunCommandOnStack(definition);
			EmitAmigaRunCommandOnStack();
			return;
		}
		if (definition.ImportName is
			"intrinsic:copperstart-probe-cpu" or
			"intrinsic:copperstart-disable-rom-overlay" or
			"intrinsic:copperstart-disable-interrupts" or
			"intrinsic:copperstart-restore-interrupts" or
			"intrinsic:copperstart-stop" or
			"intrinsic:copperstart-bootstrap-stack")
		{
			EmitAllocatedIntrinsic(caller, target, allocated, instruction);
			return;
		}
		if (definition.IsImport)
		{
			_assembler.EmitJsr(definition.ImportName!, external: true);
			return;
		}
		_assembler.EmitCall(MethodLabel(definition));
		RegisterCurrentUnwindSite(instruction.MayThrow, instruction.IsSafepoint);
		PreservePlatformBaseAcrossInternalCall();
	}

	private bool TryEmitAllocatedNativeShadowMathUnary(
		CilMethod definition,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		if (!IsNativeShadowMathLeaf(definition) ||
			instruction.Uses.Length != 1 ||
			instruction.Definitions.Length != 1)
		{
			return false;
		}

		var operation = definition.DisplayName switch
		{
			"CopperSharp.Runtime.ShadowMath::Sqrt" => M68kFpuOperation.SquareRoot,
			"CopperSharp.Runtime.ShadowMath::Truncate" => M68kFpuOperation.TruncateToInteger,
			_ => (M68kFpuOperation?)null
		};
		if (operation is null)
		{
			return false;
		}

		EmitAllocatedFloatingUnary(
			allocated.Allocation.Registers[instruction.Uses[0]],
			allocated.Allocation.Registers[instruction.Definitions[0]],
			CilStackValueKind.Float64,
			operation.Value);
		return true;
	}

	private void AnnotateAllocatedExternalCallEffects(
		int offset,
		CilMethod method,
		CilExternalCall call)
	{
		ushort usesData = 0;
		ushort definesData = 0;
		ushort usesAddress = 0;
		ushort definesAddress = 0;

		void AddUse(M68kRegister register)
		{
			if (register <= M68kRegister.D7)
			{
				usesData |= (ushort)(1 << (int)register);
				definesData |= (ushort)(1 << (int)register);
			}
			else if (register <= M68kRegister.A6)
			{
				var bit = (int)register - (int)M68kRegister.A0;
				usesAddress |= (ushort)(1 << bit);
				definesAddress |= (ushort)(1 << bit);
			}
		}

		void AddDefinition(M68kRegister register)
		{
			if (register <= M68kRegister.D7)
			{
				definesData |= (ushort)(1 << (int)register);
			}
			else if (register <= M68kRegister.A6)
			{
				definesAddress |= (ushort)(1 << ((int)register - (int)M68kRegister.A0));
			}
		}

		for (var index = 0; index < call.Abi.ParameterRegisters.Count; index++)
		{
			var register = call.Abi.ParameterRegisters[index];
			AddUse(register);
			if (index < method.Signature.ParameterTypes.Length &&
				method.Signature.ParameterTypes[index].IsSupportedScalar &&
				method.Signature.ParameterTypes[index].Size == 8)
			{
				AddUse(register + 1);
			}
		}
		AddUse(call.Convention.BaseRegister);
		if (call.Convention.CacheRegister is { } cache)
		{
			AddUse(cache);
		}
		AddDefinition(call.Abi.ReturnRegister);
		if (method.Signature.ReturnType.IsSupportedScalar &&
			method.Signature.ReturnType.Size == 8)
		{
			AddDefinition(call.Abi.ReturnRegister + 1);
		}
		if (call.Convention.ExceptionStatusRegister is { } status)
		{
			AddDefinition(status);
		}
		foreach (var register in call.Convention.ClobberedRegisters ?? [])
		{
			AddDefinition(register);
		}

		_assembler.SetInstructionEffects(
			offset,
			new M68kInstructionEffects(
				usesData,
				definesData,
				usesAddress,
				definesAddress,
				M68kConditionCodeSet.None,
				M68kConditionCodeSet.All,
				M68kMemorySet.All,
				M68kMemorySet.All,
				0,
				true,
				false));
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

		if (name == "intrinsic:copperstart-disable-rom-overlay")
		{
			EmitCopperStartDisableRomOverlay();
			return;
		}
		if (name == "intrinsic:copperstart-disable-interrupts")
		{
			EmitCopperStartDisableInterruptsAllocated(Definition());
			return;
		}
		if (name == "intrinsic:copperstart-restore-interrupts")
		{
			EmitCopperStartRestoreInterrupts(Use(0));
			return;
		}
		if (name == "intrinsic:copperstart-stop")
		{
			EmitCopperStartStop();
			return;
		}
		if (name == "intrinsic:copperstart-bootstrap-stack")
		{
			EmitCopperStartBootstrapStack();
			return;
		}
		if (name == "intrinsic:object-ctor")
		{
			return;
		}
		if (name == "intrinsic:copperstart-probe-cpu")
		{
			EmitCopperStartCpuProbeAllocated(Definition());
			return;
		}
		if (name.StartsWith(
				"intrinsic:runtime-integral-equals:",
				StringComparison.Ordinal))
		{
			var left = Use(0);
			var right = Use(1);
			var notEqual = UniqueLabel("integral-equals-not-equal");
			var done = UniqueLabel("integral-equals-done");
			EmitAllocatedCompare(left, right, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.NotEqual, notEqual);
			if (name == "intrinsic:runtime-integral-equals:64")
			{
				EmitAllocatedCompare(
					(M68kRegister)((int)left + 1),
					(M68kRegister)((int)right + 1),
					M68kMachineValueWidth.Long);
				_assembler.EmitBranch(M68kCondition.NotEqual, notEqual);
			}
			EmitAllocatedImmediate(1, Definition());
			_assembler.EmitBranch(M68kCondition.True, done);
			_assembler.Mark(notEqual);
			EmitAllocatedImmediate(0, Definition());
			_assembler.Mark(done);
			return;
		}
		if (name.StartsWith(
			"intrinsic:runtime-integral-hash:",
				StringComparison.Ordinal))
		{
			var source = Use(0);
			var destination = Definition();
			EmitAllocatedMove(
				source,
				destination,
				M68kMachineValueWidth.Long);
			if (name == "intrinsic:runtime-integral-hash:64")
			{
				EmitAllocatedBinaryInPlace(
					M68kMachineOperation.Xor,
					(M68kRegister)((int)source + 1),
					destination,
					M68kMachineValueWidth.Long);
			}
			return;
		}
		if (name == "intrinsic:runtime-int64-split")
		{
			var source = Use(0);
			EmitAllocatedBaseStore(
				source,
				Use(1),
				M68kMachineValueWidth.Long,
				displacement: 0);
			EmitAllocatedMove(
				(M68kRegister)((int)source + 1),
				Definition(),
				M68kMachineValueWidth.Long);
			return;
		}
		if (name == "intrinsic:runtime-int64-combine")
		{
			var destination = Definition();
			EmitAllocatedMove(
				Use(0),
				destination,
				M68kMachineValueWidth.Long);
			EmitAllocatedMove(
				Use(1),
				(M68kRegister)((int)destination + 1),
				M68kMachineValueWidth.Long);
			return;
		}
		if (name.StartsWith(
				"intrinsic:runtime-floating-hash:",
				StringComparison.Ordinal))
		{
			EmitAllocatedFloatingHash(
				Use(0),
				Definition(),
				name.EndsWith(":64", StringComparison.Ordinal));
			return;
		}
		if (name.StartsWith(
				"intrinsic:runtime-floating-equals:",
				StringComparison.Ordinal))
		{
			EmitAllocatedFloatingEquals(
				Use(0),
				Use(1),
				Definition(),
				name.EndsWith(":64", StringComparison.Ordinal));
			return;
		}
		if (name == "intrinsic:runtime-string-hash")
		{
			var source = Use(0);
			var destination = Definition();
			var nullValue = UniqueLabel("string-hash-null");
			var loop = UniqueLabel("string-hash-loop");
			var done = UniqueLabel("string-hash-done");

			EmitAllocatedTest(source, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, nullValue);
			EmitAllocatedMove(source, M68kRegister.D0, M68kMachineValueWidth.Long);
			EmitAllocatedMove(M68kRegister.D0, M68kRegister.A2, M68kMachineValueWidth.Long);
			EmitAllocatedBaseLoad(
				M68kRegister.A2,
				M68kRegister.D2,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.StringLengthOffset);
			EmitAllocatedImmediate(5381, M68kRegister.D1);
			EmitAllocatedTest(M68kRegister.D2, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, done);
			EmitAllocatedAddImmediate(M68kRegister.A2, M68kRuntimeAbi.StringDataOffset);

			_assembler.Mark(loop);
			EmitAllocatedImmediate(0, M68kRegister.D0);
			EmitAllocatedBaseLoad(
				M68kRegister.A2,
				M68kRegister.D0,
				M68kMachineValueWidth.Word,
				0);
			EmitAllocatedMove(
				M68kRegister.D1,
				M68kRegister.D3,
				M68kMachineValueWidth.Long);
			_assembler.EmitWord(0xEB89); // LSL.L #5,D1
			EmitAllocatedBinaryInPlace(
				M68kMachineOperation.Add,
				M68kRegister.D3,
				M68kRegister.D1,
				M68kMachineValueWidth.Long);
			EmitAllocatedBinaryInPlace(
				M68kMachineOperation.Xor,
				M68kRegister.D0,
				M68kRegister.D1,
				M68kMachineValueWidth.Long);
			EmitAllocatedAddImmediate(M68kRegister.A2, 2);
			_assembler.EmitWord(0x5382); // SUBQ.L #1,D2
			_assembler.EmitBranch(M68kCondition.NotEqual, loop);
			_assembler.EmitBranch(M68kCondition.True, done);

			_assembler.Mark(nullValue);
			EmitAllocatedImmediate(0, M68kRegister.D1);
			_assembler.Mark(done);
			EmitAllocatedMove(
				M68kRegister.D1,
				destination,
				M68kMachineValueWidth.Long);
			return;
		}
		if (name == "intrinsic:runtime-nullable-integral-hash:32")
		{
			var nullable = Use(0);
			var destination = Definition();
			var done = UniqueLabel("nullable-hash-done");
			EmitAllocatedBaseLoad(
				nullable,
				destination,
				M68kMachineValueWidth.Long,
				4);
			EmitAllocatedTest(destination, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, done);
			EmitAllocatedBaseLoad(
				nullable,
				destination,
				M68kMachineValueWidth.Long,
				0);
			_assembler.Mark(done);
			return;
		}
		if (name == "intrinsic:object-reference-equals")
		{
			var equal = UniqueLabel("object-reference-equals-equal");
			var done = UniqueLabel("object-reference-equals-done");
			EmitAllocatedCompare(
				Use(0),
				Use(1),
				M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, equal);
			EmitAllocatedImmediate(0, Definition());
			_assembler.EmitBranch(M68kCondition.True, done);
			_assembler.Mark(equal);
			EmitAllocatedImmediate(1, Definition());
			_assembler.Mark(done);
			return;
		}
		if (name is "intrinsic:string-equality" or "intrinsic:string-inequality")
		{
			var invert = name == "intrinsic:string-inequality";
			var left = Use(0);
			var right = Use(1);
			var matchingLength = UniqueLabel("string-equality-matching-length");
			var loop = UniqueLabel("string-equality-loop");
			var equal = UniqueLabel("string-equality-equal");
			var notEqual = UniqueLabel("string-equality-not-equal");
			var done = UniqueLabel("string-equality-done");

			EmitAllocatedCompare(left, right, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, equal);
			EmitAllocatedTest(left, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, notEqual);
			EmitAllocatedTest(right, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, notEqual);

			// Snapshot both reference arguments through data registers before
			// assigning the fixed pointer temporaries, so swapped A-register
			// allocations cannot overwrite either input.
			EmitAllocatedMove(left, M68kRegister.D0, M68kMachineValueWidth.Long);
			EmitAllocatedMove(right, M68kRegister.D1, M68kMachineValueWidth.Long);
			EmitAllocatedMove(M68kRegister.D0, M68kRegister.A2, M68kMachineValueWidth.Long);
			EmitAllocatedMove(M68kRegister.D1, M68kRegister.A3, M68kMachineValueWidth.Long);
			EmitAllocatedBaseLoad(
				M68kRegister.A2,
				M68kRegister.D2,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.StringLengthOffset);
			EmitAllocatedBaseLoad(
				M68kRegister.A3,
				M68kRegister.D1,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.StringLengthOffset);
			EmitAllocatedCompare(
				M68kRegister.D2,
				M68kRegister.D1,
				M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, matchingLength);
			_assembler.EmitBranch(M68kCondition.True, notEqual);

			_assembler.Mark(matchingLength);
			EmitAllocatedTest(M68kRegister.D2, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, equal);
			EmitAllocatedAddImmediate(M68kRegister.A2, M68kRuntimeAbi.StringDataOffset);
			EmitAllocatedAddImmediate(M68kRegister.A3, M68kRuntimeAbi.StringDataOffset);
			_assembler.Mark(loop);
			EmitAllocatedBaseLoad(
				M68kRegister.A2,
				M68kRegister.D0,
				M68kMachineValueWidth.Word,
				0);
			EmitAllocatedBaseLoad(
				M68kRegister.A3,
				M68kRegister.D1,
				M68kMachineValueWidth.Word,
				0);
			EmitAllocatedCompare(
				M68kRegister.D1,
				M68kRegister.D0,
				M68kMachineValueWidth.Word);
			_assembler.EmitBranch(M68kCondition.NotEqual, notEqual);
			EmitAllocatedAddImmediate(M68kRegister.A2, 2);
			EmitAllocatedAddImmediate(M68kRegister.A3, 2);
			_assembler.EmitWord(0x5382); // SUBQ.L #1,D2
			_assembler.EmitBranch(M68kCondition.NotEqual, loop);

			_assembler.Mark(equal);
			EmitAllocatedImmediate(invert ? 0 : 1, Definition());
			_assembler.EmitBranch(M68kCondition.True, done);
			_assembler.Mark(notEqual);
			EmitAllocatedImmediate(invert ? 1 : 0, Definition());
			_assembler.Mark(done);
			return;
		}
		if (name is
			"intrinsic:string-starts-with-ordinal" or
			"intrinsic:string-ends-with-ordinal")
		{
			var startsWith = name == "intrinsic:string-starts-with-ordinal";
			var receiverNonNull = UniqueLabel("string-search-receiver-nonnull");
			var valueNonNull = UniqueLabel("string-search-value-nonnull");
			var comparisonValid = UniqueLabel("string-search-comparison-valid");
			var loop = UniqueLabel("string-search-edge-loop");
			var success = UniqueLabel("string-search-edge-success");
			var failure = UniqueLabel("string-search-edge-failure");
			var done = UniqueLabel("string-search-edge-done");

			RegisterRuntimeTypeDescriptor("System.ArgumentNullException");
			EmitAllocatedTest(M68kRegister.A2, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.NotEqual, receiverNonNull);
			EmitExceptionRaise(reason: 1, hasException: false);
			_assembler.Mark(receiverNonNull);
			EmitAllocatedTest(M68kRegister.A3, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.NotEqual, valueNonNull);
			EmitExceptionRaise(reason: 11, hasException: false);
			_assembler.Mark(valueNonNull);
			if (instruction.Uses.Length == 3)
			{
				RegisterRuntimeTypeDescriptor("System.ArgumentException");
				EmitCompareImmediateLong(M68kRegister.D3, 4);
				_assembler.EmitBranch(M68kCondition.Equal, comparisonValid);
				EmitExceptionRaise(reason: 9, hasException: false);
				_assembler.Mark(comparisonValid);
			}

			EmitAllocatedBaseLoad(
				M68kRegister.A2,
				M68kRegister.D2,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.StringLengthOffset);
			EmitAllocatedBaseLoad(
				M68kRegister.A3,
				M68kRegister.D1,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.StringLengthOffset);
			EmitAllocatedCompare(
				M68kRegister.D2,
				M68kRegister.D1,
				M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.CarrySet, failure);
			EmitAllocatedTest(M68kRegister.D1, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, success);
			if (!startsWith)
			{
				EmitAllocatedBinaryInPlace(
					M68kMachineOperation.Subtract,
					M68kRegister.D1,
					M68kRegister.D2,
					M68kMachineValueWidth.Long);
				EmitAllocatedShiftImmediate(M68kRegister.D2, left: true);
				EmitAllocatedBinaryInPlace(
					M68kMachineOperation.Add,
					M68kRegister.D2,
					M68kRegister.A2,
					M68kMachineValueWidth.Long);
			}
			EmitAllocatedAddImmediate(M68kRegister.A2, M68kRuntimeAbi.StringDataOffset);
			EmitAllocatedAddImmediate(M68kRegister.A3, M68kRuntimeAbi.StringDataOffset);
			EmitAllocatedMove(
				M68kRegister.D1,
				M68kRegister.D2,
				M68kMachineValueWidth.Long);
			_assembler.Mark(loop);
			EmitAllocatedBaseLoad(
				M68kRegister.A2,
				M68kRegister.D0,
				M68kMachineValueWidth.Word,
				0);
			EmitAllocatedBaseLoad(
				M68kRegister.A3,
				M68kRegister.D1,
				M68kMachineValueWidth.Word,
				0);
			EmitAllocatedCompare(
				M68kRegister.D1,
				M68kRegister.D0,
				M68kMachineValueWidth.Word);
			_assembler.EmitBranch(M68kCondition.NotEqual, failure);
			EmitAllocatedAddImmediate(M68kRegister.A2, 2);
			EmitAllocatedAddImmediate(M68kRegister.A3, 2);
			_assembler.EmitWord(0x5382); // SUBQ.L #1,D2
			_assembler.EmitBranch(M68kCondition.NotEqual, loop);

			_assembler.Mark(success);
			EmitAllocatedImmediate(1, Definition());
			_assembler.EmitBranch(M68kCondition.True, done);
			_assembler.Mark(failure);
			EmitAllocatedImmediate(0, Definition());
			_assembler.Mark(done);
			return;
		}
		if (name is
			"intrinsic:string-contains-ordinal" or
			"intrinsic:string-index-of-ordinal")
		{
			var contains = name == "intrinsic:string-contains-ordinal";
			var receiverNonNull = UniqueLabel("string-search-receiver-nonnull");
			var valueNonNull = UniqueLabel("string-search-value-nonnull");
			var comparisonValid = UniqueLabel("string-search-comparison-valid");
			var outer = UniqueLabel("string-search-outer");
			var inner = UniqueLabel("string-search-inner");
			var next = UniqueLabel("string-search-next");
			var found = UniqueLabel("string-search-found");
			var notFound = UniqueLabel("string-search-not-found");
			var done = UniqueLabel("string-search-done");

			RegisterRuntimeTypeDescriptor("System.ArgumentNullException");
			EmitAllocatedTest(M68kRegister.A2, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.NotEqual, receiverNonNull);
			EmitExceptionRaise(reason: 1, hasException: false);
			_assembler.Mark(receiverNonNull);
			EmitAllocatedTest(M68kRegister.A3, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.NotEqual, valueNonNull);
			EmitExceptionRaise(reason: 11, hasException: false);
			_assembler.Mark(valueNonNull);
			if (instruction.Uses.Length == 3)
			{
				RegisterRuntimeTypeDescriptor("System.ArgumentException");
				EmitCompareImmediateLong(M68kRegister.D3, 4);
				_assembler.EmitBranch(M68kCondition.Equal, comparisonValid);
				EmitExceptionRaise(reason: 9, hasException: false);
				_assembler.Mark(comparisonValid);
			}

			EmitAllocatedBaseLoad(
				M68kRegister.A2,
				M68kRegister.D2,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.StringLengthOffset);
			EmitAllocatedImmediate(0, M68kRegister.D0);
			EmitAllocatedBaseLoad(
				M68kRegister.A3,
				M68kRegister.D1,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.StringLengthOffset);
			EmitAllocatedTest(M68kRegister.D1, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, found);
			EmitAllocatedCompare(
				M68kRegister.D2,
				M68kRegister.D1,
				M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.CarrySet, notFound);
			EmitAllocatedBinaryInPlace(
				M68kMachineOperation.Subtract,
				M68kRegister.D1,
				M68kRegister.D2,
				M68kMachineValueWidth.Long);
			EmitAllocatedAddImmediate(M68kRegister.D2, 1);
			EmitAllocatedMove(
				M68kRegister.D2,
				M68kRegister.D4,
				M68kMachineValueWidth.Long);
			EmitAllocatedAddImmediate(M68kRegister.A2, M68kRuntimeAbi.StringDataOffset);

			_assembler.Mark(outer);
			EmitAllocatedMove(M68kRegister.A2, M68kRegister.A0, M68kMachineValueWidth.Long);
			EmitAllocatedMove(M68kRegister.A3, M68kRegister.A1, M68kMachineValueWidth.Long);
			EmitAllocatedAddImmediate(M68kRegister.A1, M68kRuntimeAbi.StringDataOffset);
			EmitAllocatedBaseLoad(
				M68kRegister.A3,
				M68kRegister.D2,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.StringLengthOffset);
			_assembler.Mark(inner);
			EmitAllocatedBaseLoad(
				M68kRegister.A0,
				M68kRegister.D3,
				M68kMachineValueWidth.Word,
				0);
			EmitAllocatedBaseLoad(
				M68kRegister.A1,
				M68kRegister.D1,
				M68kMachineValueWidth.Word,
				0);
			EmitAllocatedCompare(
				M68kRegister.D1,
				M68kRegister.D3,
				M68kMachineValueWidth.Word);
			_assembler.EmitBranch(M68kCondition.NotEqual, next);
			EmitAllocatedAddImmediate(M68kRegister.A0, 2);
			EmitAllocatedAddImmediate(M68kRegister.A1, 2);
			_assembler.EmitWord(0x5382); // SUBQ.L #1,D2
			_assembler.EmitBranch(M68kCondition.NotEqual, inner);
			_assembler.EmitBranch(M68kCondition.True, found);

			_assembler.Mark(next);
			EmitAllocatedAddImmediate(M68kRegister.A2, 2);
			EmitAllocatedAddImmediate(M68kRegister.D0, 1);
			_assembler.EmitWord(0x5384); // SUBQ.L #1,D4
			_assembler.EmitBranch(M68kCondition.NotEqual, outer);
			_assembler.EmitBranch(M68kCondition.True, notFound);

			_assembler.Mark(found);
			if (contains)
			{
				EmitAllocatedImmediate(1, Definition());
			}
			else
			{
				EmitAllocatedMove(M68kRegister.D0, Definition(), M68kMachineValueWidth.Long);
			}
			_assembler.EmitBranch(M68kCondition.True, done);
			_assembler.Mark(notFound);
			EmitAllocatedImmediate(contains ? 0 : -1, Definition());
			_assembler.Mark(done);
			return;
		}
		if (name == "intrinsic:string-concat-two")
		{
			var left = Use(0);
			var right = Use(1);
			var leftNonNull = UniqueLabel("string-concat-left-nonnull");
			var leftNonEmpty = UniqueLabel("string-concat-left-nonempty");
			var rightNonNull = UniqueLabel("string-concat-right-nonnull");
			var allocate = UniqueLabel("string-concat-allocate");
			var lengthValid = UniqueLabel("string-concat-length-valid");
			var returnLeft = UniqueLabel("string-concat-return-left");
			var returnRight = UniqueLabel("string-concat-return-right");
			var returnEmpty = UniqueLabel("string-concat-return-empty");
			var done = UniqueLabel("string-concat-done");

			_usesRuntimeEmptyString = true;
			RegisterRuntimeTypeDescriptor("System.Object");
			RegisterRuntimeTypeDescriptor("System.OutOfMemoryException");

			EmitAllocatedTest(left, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.NotEqual, leftNonNull);
			EmitAllocatedTest(right, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.NotEqual, returnRight);
			_assembler.EmitBranch(M68kCondition.True, returnEmpty);

			_assembler.Mark(leftNonNull);
			EmitAllocatedBaseLoad(
				left,
				M68kRegister.D2,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.StringLengthOffset);
			EmitAllocatedTest(M68kRegister.D2, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.NotEqual, leftNonEmpty);
			EmitAllocatedTest(right, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.NotEqual, returnRight);
			_assembler.EmitBranch(M68kCondition.True, returnLeft);

			_assembler.Mark(leftNonEmpty);
			EmitAllocatedTest(right, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.NotEqual, rightNonNull);
			_assembler.EmitBranch(M68kCondition.True, returnLeft);

			_assembler.Mark(rightNonNull);
			EmitAllocatedBaseLoad(
				right,
				M68kRegister.D1,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.StringLengthOffset);
			EmitAllocatedTest(M68kRegister.D1, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, returnLeft);
			_assembler.EmitBranch(M68kCondition.True, allocate);

			_assembler.Mark(allocate);
			EmitAllocatedBinaryInPlace(
				M68kMachineOperation.Add,
				M68kRegister.D1,
				M68kRegister.D2,
				M68kMachineValueWidth.Long);
			var invalidLength = UniqueLabel("string-concat-invalid-length");
			_assembler.EmitBranch(M68kCondition.OverflowSet, invalidLength);
			const int maximumLength =
				(int.MaxValue - (M68kRuntimeAbi.StringDataOffset + 2)) / 2;
			EmitCompareImmediateLong(M68kRegister.D2, maximumLength);
			_assembler.EmitBranch(M68kCondition.LowerOrSame, lengthValid);
			_assembler.Mark(invalidLength);
			EmitExceptionRaise(reason: 6, hasException: false);
			_assembler.Mark(lengthValid);

			EnsureManagedAllocationAllowed(
				caller,
				instruction.SourceInstruction ??
					throw new InvalidOperationException("String concatenation has no source instruction."),
				"string concatenation");
			EmitAllocatedMultiplyByConstant(
				M68kRegister.D2,
				M68kRegister.D0,
				2);
			EmitAllocatedAddImmediate(
				M68kRegister.D0,
				M68kRuntimeAbi.StringDataOffset + 2);
			EmitManagedAllocationFromD0();
			EmitAllocatedMove(M68kRegister.D0, M68kRegister.A0, M68kMachineValueWidth.Long);

			EmitAllocatedAddress("runtime:string-descriptor", M68kRegister.A1);
			EmitAllocatedBaseStore(
				M68kRegister.A1,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.ObjectDescriptorOffset);
			EmitAllocatedMove(
				M68kRegister.D2,
				M68kRegister.D1,
				M68kMachineValueWidth.Long);
			EmitAllocatedShiftImmediate(M68kRegister.D1, left: true);
			EmitAllocatedAddImmediate(
				M68kRegister.D1,
				M68kRuntimeAbi.StringDataOffset + 2);
			EmitAllocatedBaseStore(
				M68kRegister.D1,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.ObjectSizeOffset);
			EmitAllocatedBaseStore(
				M68kRegister.D2,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.StringLengthOffset);

			EmitAllocatedMove(M68kRegister.A0, M68kRegister.A4, M68kMachineValueWidth.Long);
			EmitAllocatedAddImmediate(M68kRegister.A4, M68kRuntimeAbi.StringDataOffset);

			void EmitCopyString(M68kRegister source)
			{
				EmitAllocatedBaseLoad(
					source,
					M68kRegister.D2,
					M68kMachineValueWidth.Long,
					M68kRuntimeAbi.StringLengthOffset);
				EmitAllocatedMove(source, M68kRegister.A5, M68kMachineValueWidth.Long);
				EmitAllocatedAddImmediate(M68kRegister.A5, M68kRuntimeAbi.StringDataOffset);
				var loop = UniqueLabel("string-concat-copy-loop");
				_assembler.Mark(loop);
				EmitAllocatedBaseLoad(
					M68kRegister.A5,
					M68kRegister.D0,
					M68kMachineValueWidth.Word,
					0);
				EmitAllocatedBaseStore(
					M68kRegister.D0,
					M68kRegister.A4,
					M68kMachineValueWidth.Word,
					0);
				EmitAllocatedAddImmediate(M68kRegister.A5, 2);
				EmitAllocatedAddImmediate(M68kRegister.A4, 2);
				_assembler.EmitWord(0x5382); // SUBQ.L #1,D2
				_assembler.EmitBranch(M68kCondition.NotEqual, loop);
			}

			EmitCopyString(left);
			EmitCopyString(right);
			EmitAllocatedImmediate(0, M68kRegister.D0);
			EmitAllocatedBaseStore(
				M68kRegister.D0,
				M68kRegister.A4,
				M68kMachineValueWidth.Word,
				0);
			EmitAllocatedMove(M68kRegister.A0, Definition(), M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.True, done);

			_assembler.Mark(returnLeft);
			EmitAllocatedMove(left, Definition(), M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.True, done);
			_assembler.Mark(returnRight);
			EmitAllocatedMove(right, Definition(), M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.True, done);
			_assembler.Mark(returnEmpty);
			EmitAllocatedAddress(RuntimeEmptyStringLabel, Definition());
			_assembler.Mark(done);
			return;
		}
		if (name == "intrinsic:string-substring")
		{
			var hasExplicitLength = instruction.Uses.Length == 3;
			var receiverNonNull = UniqueLabel("string-substring-receiver-nonnull");
			var invalidRange = UniqueLabel("string-substring-invalid-range");
			var allocate = UniqueLabel("string-substring-allocate");
			var copyLoop = UniqueLabel("string-substring-copy-loop");
			var returnSource = UniqueLabel("string-substring-return-source");
			var returnEmpty = UniqueLabel("string-substring-return-empty");
			var done = UniqueLabel("string-substring-done");

			_usesRuntimeEmptyString = true;
			RegisterRuntimeTypeDescriptor("System.Object");
			RegisterRuntimeTypeDescriptor("System.ArgumentOutOfRangeException");
			RegisterRuntimeTypeDescriptor("System.OutOfMemoryException");

			EmitAllocatedTest(M68kRegister.A2, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.NotEqual, receiverNonNull);
			EmitExceptionRaise(reason: 1, hasException: false);
			_assembler.Mark(receiverNonNull);

			EmitAllocatedBaseLoad(
				M68kRegister.A2,
				M68kRegister.D2,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.StringLengthOffset);
			EmitAllocatedCompare(
				M68kRegister.D2,
				M68kRegister.D3,
				M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.CarrySet, invalidRange);

			if (hasExplicitLength)
			{
				// available = source.Length - startIndex; the unsigned compare
				// rejects a negative length without start + length overflow.
				EmitAllocatedMove(
					M68kRegister.D2,
					M68kRegister.D1,
					M68kMachineValueWidth.Long);
				EmitAllocatedBinaryInPlace(
					M68kMachineOperation.Subtract,
					M68kRegister.D3,
					M68kRegister.D1,
					M68kMachineValueWidth.Long);
				EmitAllocatedCompare(
					M68kRegister.D1,
					M68kRegister.D4,
					M68kMachineValueWidth.Long);
				_assembler.EmitBranch(M68kCondition.CarrySet, invalidRange);

				// The two-argument overload returns String.Empty before its
				// full-range identity fast path.
				EmitAllocatedTest(M68kRegister.D4, M68kMachineValueWidth.Long);
				_assembler.EmitBranch(M68kCondition.Equal, returnEmpty);
				EmitAllocatedTest(M68kRegister.D3, M68kMachineValueWidth.Long);
				_assembler.EmitBranch(M68kCondition.NotEqual, allocate);
				EmitAllocatedCompare(
					M68kRegister.D4,
					M68kRegister.D2,
					M68kMachineValueWidth.Long);
				_assembler.EmitBranch(M68kCondition.Equal, returnSource);
			}
			else
			{
				EmitAllocatedMove(
					M68kRegister.D2,
					M68kRegister.D4,
					M68kMachineValueWidth.Long);
				EmitAllocatedBinaryInPlace(
					M68kMachineOperation.Subtract,
					M68kRegister.D3,
					M68kRegister.D4,
					M68kMachineValueWidth.Long);

				// Substring(0) preserves receiver identity.
				EmitAllocatedTest(M68kRegister.D3, M68kMachineValueWidth.Long);
				_assembler.EmitBranch(M68kCondition.Equal, returnSource);
				EmitAllocatedTest(M68kRegister.D4, M68kMachineValueWidth.Long);
				_assembler.EmitBranch(M68kCondition.Equal, returnEmpty);
			}
			_assembler.EmitBranch(M68kCondition.True, allocate);

			_assembler.Mark(invalidRange);
			EmitExceptionRaise(reason: 10, hasException: false);

			_assembler.Mark(allocate);
			EnsureManagedAllocationAllowed(
				caller,
				instruction.SourceInstruction ??
					throw new InvalidOperationException(
						"String substring has no source instruction."),
				"string substring");
			EmitAllocatedMultiplyByConstant(
				M68kRegister.D4,
				M68kRegister.D0,
				2);
			EmitAllocatedAddImmediate(
				M68kRegister.D0,
				M68kRuntimeAbi.StringDataOffset + 2);
			EmitManagedAllocationFromD0();
			EmitAllocatedMove(
				M68kRegister.D0,
				M68kRegister.A0,
				M68kMachineValueWidth.Long);

			EmitAllocatedAddress("runtime:string-descriptor", M68kRegister.A1);
			EmitAllocatedBaseStore(
				M68kRegister.A1,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.ObjectDescriptorOffset);
			EmitAllocatedMove(
				M68kRegister.D4,
				M68kRegister.D1,
				M68kMachineValueWidth.Long);
			EmitAllocatedShiftImmediate(M68kRegister.D1, left: true);
			EmitAllocatedAddImmediate(
				M68kRegister.D1,
				M68kRuntimeAbi.StringDataOffset + 2);
			EmitAllocatedBaseStore(
				M68kRegister.D1,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.ObjectSizeOffset);
			EmitAllocatedBaseStore(
				M68kRegister.D4,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.StringLengthOffset);

			EmitAllocatedMove(
				M68kRegister.A2,
				M68kRegister.A3,
				M68kMachineValueWidth.Long);
			EmitAllocatedMove(
				M68kRegister.D3,
				M68kRegister.D1,
				M68kMachineValueWidth.Long);
			EmitAllocatedShiftImmediate(M68kRegister.D1, left: true);
			EmitAllocatedBinaryInPlace(
				M68kMachineOperation.Add,
				M68kRegister.D1,
				M68kRegister.A3,
				M68kMachineValueWidth.Long);
			EmitAllocatedAddImmediate(
				M68kRegister.A3,
				M68kRuntimeAbi.StringDataOffset);
			EmitAllocatedMove(
				M68kRegister.A0,
				M68kRegister.A4,
				M68kMachineValueWidth.Long);
			EmitAllocatedAddImmediate(
				M68kRegister.A4,
				M68kRuntimeAbi.StringDataOffset);
			EmitAllocatedMove(
				M68kRegister.D4,
				M68kRegister.D2,
				M68kMachineValueWidth.Long);

			_assembler.Mark(copyLoop);
			EmitAllocatedBaseLoad(
				M68kRegister.A3,
				M68kRegister.D0,
				M68kMachineValueWidth.Word,
				0);
			EmitAllocatedBaseStore(
				M68kRegister.D0,
				M68kRegister.A4,
				M68kMachineValueWidth.Word,
				0);
			EmitAllocatedAddImmediate(M68kRegister.A3, 2);
			EmitAllocatedAddImmediate(M68kRegister.A4, 2);
			_assembler.EmitWord(0x5382); // SUBQ.L #1,D2
			_assembler.EmitBranch(M68kCondition.NotEqual, copyLoop);

			EmitAllocatedImmediate(0, M68kRegister.D0);
			EmitAllocatedBaseStore(
				M68kRegister.D0,
				M68kRegister.A4,
				M68kMachineValueWidth.Word,
				0);
			EmitAllocatedMove(
				M68kRegister.A0,
				Definition(),
				M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.True, done);

			_assembler.Mark(returnSource);
			EmitAllocatedMove(
				M68kRegister.A2,
				Definition(),
				M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.True, done);
			_assembler.Mark(returnEmpty);
			EmitAllocatedAddress(RuntimeEmptyStringLabel, Definition());
			_assembler.Mark(done);
			return;
		}
		if (name == "intrinsic:string-copy-to-char-array")
		{
			var receiverNonNull = UniqueLabel("string-copy-receiver-nonnull");
			var destinationNonNull = UniqueLabel("string-copy-destination-nonnull");
			var invalidRange = UniqueLabel("string-copy-invalid-range");
			var copy = UniqueLabel("string-copy-loop-start");
			var loop = UniqueLabel("string-copy-loop");
			var complete = UniqueLabel("string-copy-complete");

			RegisterRuntimeTypeDescriptor("System.ArgumentNullException");
			RegisterRuntimeTypeDescriptor("System.ArgumentOutOfRangeException");

			EmitAllocatedTest(M68kRegister.A2, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.NotEqual, receiverNonNull);
			EmitExceptionRaise(reason: 1, hasException: false);
			_assembler.Mark(receiverNonNull);
			EmitAllocatedTest(M68kRegister.A3, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.NotEqual, destinationNonNull);
			EmitExceptionRaise(reason: 11, hasException: false);
			_assembler.Mark(destinationNonNull);

			EmitAllocatedBaseLoad(
				M68kRegister.A2,
				M68kRegister.D2,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.StringLengthOffset);
			EmitAllocatedBaseLoad(
				M68kRegister.A3,
				M68kRegister.D1,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.ArrayLengthOffset);
			EmitAllocatedCompare(
				M68kRegister.D2,
				M68kRegister.D3,
				M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.CarrySet, invalidRange);
			EmitAllocatedCompare(
				M68kRegister.D1,
				M68kRegister.D4,
				M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.CarrySet, invalidRange);

			EmitAllocatedBinaryInPlace(
				M68kMachineOperation.Subtract,
				M68kRegister.D3,
				M68kRegister.D2,
				M68kMachineValueWidth.Long);
			EmitAllocatedBinaryInPlace(
				M68kMachineOperation.Subtract,
				M68kRegister.D4,
				M68kRegister.D1,
				M68kMachineValueWidth.Long);
			EmitAllocatedCompare(
				M68kRegister.D2,
				M68kRegister.D5,
				M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.CarrySet, invalidRange);
			EmitAllocatedCompare(
				M68kRegister.D1,
				M68kRegister.D5,
				M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.CarrySet, invalidRange);
			EmitAllocatedTest(M68kRegister.D5, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, complete);
			_assembler.EmitBranch(M68kCondition.True, copy);

			_assembler.Mark(invalidRange);
			EmitExceptionRaise(reason: 10, hasException: false);

			_assembler.Mark(copy);
			EmitAllocatedMove(
				M68kRegister.D3,
				M68kRegister.D0,
				M68kMachineValueWidth.Long);
			EmitAllocatedShiftImmediate(M68kRegister.D0, left: true);
			EmitAllocatedBinaryInPlace(
				M68kMachineOperation.Add,
				M68kRegister.D0,
				M68kRegister.A2,
				M68kMachineValueWidth.Long);
			EmitAllocatedAddImmediate(
				M68kRegister.A2,
				M68kRuntimeAbi.StringDataOffset);
			EmitAllocatedMove(
				M68kRegister.D4,
				M68kRegister.D0,
				M68kMachineValueWidth.Long);
			EmitAllocatedShiftImmediate(M68kRegister.D0, left: true);
			EmitAllocatedBinaryInPlace(
				M68kMachineOperation.Add,
				M68kRegister.D0,
				M68kRegister.A3,
				M68kMachineValueWidth.Long);
			EmitAllocatedAddImmediate(
				M68kRegister.A3,
				M68kRuntimeAbi.ArrayDataOffset);
			EmitAllocatedMove(
				M68kRegister.D5,
				M68kRegister.D2,
				M68kMachineValueWidth.Long);

			_assembler.Mark(loop);
			EmitAllocatedBaseLoad(
				M68kRegister.A2,
				M68kRegister.D0,
				M68kMachineValueWidth.Word,
				0);
			EmitAllocatedBaseStore(
				M68kRegister.D0,
				M68kRegister.A3,
				M68kMachineValueWidth.Word,
				0);
			EmitAllocatedAddImmediate(M68kRegister.A2, 2);
			EmitAllocatedAddImmediate(M68kRegister.A3, 2);
			_assembler.EmitWord(0x5382); // SUBQ.L #1,D2
			_assembler.EmitBranch(M68kCondition.NotEqual, loop);
			_assembler.Mark(complete);
			return;
		}
		if (name == "intrinsic:string-copy-to-span-char")
		{
			var receiverNonNull = UniqueLabel("string-copy-span-receiver-nonnull");
			var destinationLongEnough = UniqueLabel(
				"string-copy-span-destination-long-enough");
			var loop = UniqueLabel("string-copy-span-loop");
			var complete = UniqueLabel("string-copy-span-complete");

			RegisterRuntimeTypeDescriptor("System.ArgumentException");

			EmitAllocatedTest(M68kRegister.A2, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.NotEqual, receiverNonNull);
			EmitExceptionRaise(reason: 1, hasException: false);
			_assembler.Mark(receiverNonNull);

			EmitAllocatedBaseLoad(
				M68kRegister.A2,
				M68kRegister.D2,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.StringLengthOffset);
			EmitAllocatedBaseLoad(
				M68kRegister.A3,
				M68kRegister.D1,
				M68kMachineValueWidth.Long,
				4);
			EmitAllocatedCompare(
				M68kRegister.D2,
				M68kRegister.D1,
				M68kMachineValueWidth.Long);
			_assembler.EmitBranch(
				M68kCondition.CarrySet,
				destinationLongEnough);
			_assembler.EmitBranch(
				M68kCondition.Equal,
				destinationLongEnough);
			EmitExceptionRaise(reason: 9, hasException: false);
			_assembler.Mark(destinationLongEnough);

			EmitAllocatedTest(M68kRegister.D2, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, complete);
			EmitAllocatedBaseLoad(
				M68kRegister.A3,
				M68kRegister.A3,
				M68kMachineValueWidth.Long,
				0);
			EmitAllocatedAddImmediate(
				M68kRegister.A2,
				M68kRuntimeAbi.StringDataOffset);

			_assembler.Mark(loop);
			EmitAllocatedBaseLoad(
				M68kRegister.A2,
				M68kRegister.D0,
				M68kMachineValueWidth.Word,
				0);
			EmitAllocatedBaseStore(
				M68kRegister.D0,
				M68kRegister.A3,
				M68kMachineValueWidth.Word,
				0);
			EmitAllocatedAddImmediate(M68kRegister.A2, 2);
			EmitAllocatedAddImmediate(M68kRegister.A3, 2);
			_assembler.EmitWord(0x5382); // SUBQ.L #1,D2
			_assembler.EmitBranch(M68kCondition.NotEqual, loop);
			_assembler.Mark(complete);
			return;
		}
		if (name == "intrinsic:string-to-char-array")
		{
			var charType = target.Signature.ReturnType.ElementType ??
				throw new InvalidOperationException(
					"String.ToCharArray intrinsic has no char element type.");
			var hasExplicitRange = instruction.Uses.Length == 3;
			var receiverNonNull = UniqueLabel("string-to-char-array-receiver-nonnull");
			var invalidRange = UniqueLabel("string-to-char-array-invalid-range");
			var allocate = UniqueLabel("string-to-char-array-allocate");
			var loop = UniqueLabel("string-to-char-array-copy-loop");
			var returnEmpty = UniqueLabel("string-to-char-array-return-empty");
			var done = UniqueLabel("string-to-char-array-done");

			_arrayTypes.TryAdd(charType.DisplayName, charType);
			_runtimeEmptyCharArrayElementType = charType;
			RegisterRuntimeTypeDescriptor("System.Object");
			RegisterRuntimeTypeDescriptor("System.ArgumentOutOfRangeException");
			RegisterRuntimeTypeDescriptor("System.OutOfMemoryException");

			EmitAllocatedTest(M68kRegister.A2, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.NotEqual, receiverNonNull);
			EmitExceptionRaise(reason: 1, hasException: false);
			_assembler.Mark(receiverNonNull);
			EmitAllocatedBaseLoad(
				M68kRegister.A2,
				M68kRegister.D2,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.StringLengthOffset);

			if (hasExplicitRange)
			{
				EmitAllocatedCompare(
					M68kRegister.D2,
					M68kRegister.D3,
					M68kMachineValueWidth.Long);
				_assembler.EmitBranch(M68kCondition.CarrySet, invalidRange);
				EmitAllocatedMove(
					M68kRegister.D2,
					M68kRegister.D1,
					M68kMachineValueWidth.Long);
				EmitAllocatedBinaryInPlace(
					M68kMachineOperation.Subtract,
					M68kRegister.D3,
					M68kRegister.D1,
					M68kMachineValueWidth.Long);
				EmitAllocatedCompare(
					M68kRegister.D1,
					M68kRegister.D4,
					M68kMachineValueWidth.Long);
				_assembler.EmitBranch(M68kCondition.CarrySet, invalidRange);
			}
			else
			{
				EmitAllocatedImmediate(0, M68kRegister.D3);
				EmitAllocatedMove(
					M68kRegister.D2,
					M68kRegister.D4,
					M68kMachineValueWidth.Long);
			}
			EmitAllocatedTest(M68kRegister.D4, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, returnEmpty);
			_assembler.EmitBranch(M68kCondition.True, allocate);

			_assembler.Mark(invalidRange);
			EmitExceptionRaise(reason: 10, hasException: false);

			_assembler.Mark(allocate);
			EnsureManagedAllocationAllowed(
				caller,
				instruction.SourceInstruction ??
					throw new InvalidOperationException(
						"String.ToCharArray has no source instruction."),
				"string to char array");
			EmitAllocatedMultiplyByConstant(
				M68kRegister.D4,
				M68kRegister.D0,
				2);
			EmitAllocatedAddImmediate(
				M68kRegister.D0,
				M68kRuntimeAbi.ArrayDataOffset);
			EmitManagedAllocationFromD0();
			EmitAllocatedMove(
				M68kRegister.D0,
				M68kRegister.A0,
				M68kMachineValueWidth.Long);

			EmitAllocatedAddress(ArrayDescriptorLabel(charType), M68kRegister.A1);
			EmitAllocatedBaseStore(
				M68kRegister.A1,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.ObjectDescriptorOffset);
			EmitAllocatedMove(
				M68kRegister.D4,
				M68kRegister.D1,
				M68kMachineValueWidth.Long);
			EmitAllocatedShiftImmediate(M68kRegister.D1, left: true);
			EmitAllocatedAddImmediate(
				M68kRegister.D1,
				M68kRuntimeAbi.ArrayDataOffset);
			EmitAllocatedBaseStore(
				M68kRegister.D1,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.ObjectSizeOffset);
			EmitAllocatedBaseStore(
				M68kRegister.D4,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.ArrayLengthOffset);

			EmitAllocatedMove(
				M68kRegister.A2,
				M68kRegister.A3,
				M68kMachineValueWidth.Long);
			EmitAllocatedMove(
				M68kRegister.D3,
				M68kRegister.D1,
				M68kMachineValueWidth.Long);
			EmitAllocatedShiftImmediate(M68kRegister.D1, left: true);
			EmitAllocatedBinaryInPlace(
				M68kMachineOperation.Add,
				M68kRegister.D1,
				M68kRegister.A3,
				M68kMachineValueWidth.Long);
			EmitAllocatedAddImmediate(
				M68kRegister.A3,
				M68kRuntimeAbi.StringDataOffset);
			EmitAllocatedMove(
				M68kRegister.A0,
				M68kRegister.A4,
				M68kMachineValueWidth.Long);
			EmitAllocatedAddImmediate(
				M68kRegister.A4,
				M68kRuntimeAbi.ArrayDataOffset);
			EmitAllocatedMove(
				M68kRegister.D4,
				M68kRegister.D2,
				M68kMachineValueWidth.Long);

			_assembler.Mark(loop);
			EmitAllocatedBaseLoad(
				M68kRegister.A3,
				M68kRegister.D0,
				M68kMachineValueWidth.Word,
				0);
			EmitAllocatedBaseStore(
				M68kRegister.D0,
				M68kRegister.A4,
				M68kMachineValueWidth.Word,
				0);
			EmitAllocatedAddImmediate(M68kRegister.A3, 2);
			EmitAllocatedAddImmediate(M68kRegister.A4, 2);
			_assembler.EmitWord(0x5382); // SUBQ.L #1,D2
			_assembler.EmitBranch(M68kCondition.NotEqual, loop);
			EmitAllocatedMove(
				M68kRegister.A0,
				Definition(),
				M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.True, done);

			_assembler.Mark(returnEmpty);
			EmitAllocatedAddress(RuntimeEmptyCharArrayLabel, Definition());
			_assembler.Mark(done);
			return;
		}
		if (name == "intrinsic:runtime-allocate-string")
		{
			EnsureManagedAllocationAllowed(
				caller,
				instruction.SourceInstruction ??
					throw new InvalidOperationException(
						"Dynamic string allocation has no source instruction."),
				"string allocation");
			_usesDynamicStrings = true;
			RegisterRuntimeTypeDescriptor("System.ArgumentOutOfRangeException");
			RegisterRuntimeTypeDescriptor("System.OutOfMemoryException");

			var length = Use(0);
			if (length != M68kRegister.D2)
			{
				throw new InvalidOperationException(
					"Dynamic string length must use the preserved D2 register.");
			}
			var lengthValid = UniqueLabel(
				"allocated_string_length_valid");
			EmitAllocatedTest(length, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Plus, lengthValid);
			EmitExceptionRaise(reason: 10, hasException: false);
			_assembler.Mark(lengthValid);

			const int maximumLength = int.MaxValue - 7;
			var sizeValid = UniqueLabel("allocated_string_size_valid");
			EmitCompareImmediateLong(length, maximumLength);
			_assembler.EmitBranch(M68kCondition.LowerOrSame, sizeValid);
			EmitExceptionRaise(reason: 6, hasException: false);
			_assembler.Mark(sizeValid);

			EmitAllocatedMultiplyByConstant(
				length,
				M68kRegister.D0,
				2);
			EmitAllocatedAddImmediate(
				M68kRegister.D0,
				M68kRuntimeAbi.StringDataOffset + 2);
			EmitManagedAllocationFromD0();
			EmitAllocatedMove(
				M68kRegister.D0,
				M68kRegister.A0,
				M68kMachineValueWidth.Long);
			EmitAllocatedAddress(
				"runtime:string-descriptor",
				M68kRegister.D1);
			EmitAllocatedBaseStore(
				M68kRegister.D1,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.ObjectDescriptorOffset);
			EmitAllocatedMove(
				length,
				M68kRegister.D1,
				M68kMachineValueWidth.Long);
			EmitAllocatedShiftImmediate(M68kRegister.D1, left: true);
			EmitAllocatedAddImmediate(
				M68kRegister.D1,
				M68kRuntimeAbi.StringDataOffset + 2);
			EmitAllocatedBaseStore(
				M68kRegister.D1,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.ObjectSizeOffset);
			EmitAllocatedBaseStore(
				length,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.StringLengthOffset);
			EmitAllocatedMove(
				M68kRegister.A0,
				Definition(),
				M68kMachineValueWidth.Long);
			return;
		}
		if (name == "intrinsic:runtime-set-string-char")
		{
			EmitAllocatedMove(
				M68kRegister.D3,
				M68kRegister.D0,
				M68kMachineValueWidth.Long);
			if (_request.Cpu == M68kCpuTarget.M68000)
			{
				EmitAllocatedShiftImmediate(M68kRegister.D0, left: true);
			}
			EmitAllocatedIndexedStore(
				M68kRegister.D4,
				M68kRegister.A2,
				M68kRegister.D0,
				M68kMachineValueWidth.Word,
				_request.Cpu == M68kCpuTarget.M68000 ? 1 : 2,
				(sbyte)M68kRuntimeAbi.StringDataOffset);
			return;
		}
		if (name == "intrinsic:delegate-combine")
		{
			var left = Use(0);
			var right = Use(1);
			var returnRight = UniqueLabel("delegate-combine-return-right");
			var returnLeft = UniqueLabel("delegate-combine-return-left");
			var done = UniqueLabel("delegate-combine-done");

			EmitAllocatedTest(left, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, returnRight);
			EmitAllocatedTest(right, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, returnLeft);

			EmitAllocatedLoadDelegateTypeIdentity(left, M68kRegister.A4);
			EmitAllocatedLoadDelegateTypeIdentity(right, M68kRegister.A5);
			EmitAllocatedCompare(
				M68kRegister.A4,
				M68kRegister.A5,
				M68kMachineValueWidth.Long);
			var matchingTypes = UniqueLabel("delegate-combine-matching-types");
			_assembler.EmitBranch(M68kCondition.Equal, matchingTypes);
			RegisterRuntimeTypeDescriptor("System.ArgumentException");
			EmitRuntimeObjectAddress(M68kRegister.A0, "System.ArgumentException");
			EmitExceptionRaise(reason: 0, hasException: true);
			_assembler.Mark(matchingTypes);

			void EmitInvocationCount(M68kRegister source, M68kRegister count)
			{
				EmitAllocatedBaseLoad(
					source,
					count,
					M68kMachineValueWidth.Long,
					M68kRuntimeAbi.DelegateFlagsOffset);
				EmitAllocatedMove(
					count,
					M68kRegister.D0,
					M68kMachineValueWidth.Long);
				_assembler.EmitWord(0x0280); // ANDI.L #multicast,D0
				_assembler.EmitLong(M68kRuntimeAbi.DelegateFlagMulticast);
				var multicast = UniqueLabel("delegate-combine-count-multicast");
				var countDone = UniqueLabel("delegate-combine-count-done");
				_assembler.EmitBranch(M68kCondition.NotEqual, multicast);
				EmitAllocatedImmediate(1, count);
				_assembler.EmitBranch(M68kCondition.True, countDone);
				_assembler.Mark(multicast);
				_assembler.EmitWord((ushort)(0x4840 | (int)count)); // SWAP Dn
				_assembler.EmitWord((ushort)(0x0280 | (int)count)); // ANDI.L #$ffff,Dn
				_assembler.EmitLong(0x0000FFFF);
				_assembler.Mark(countDone);
			}

			EmitInvocationCount(left, M68kRegister.D2);
			EmitInvocationCount(right, M68kRegister.D3);
			EmitAllocatedMove(
				M68kRegister.D2,
				M68kRegister.D4,
				M68kMachineValueWidth.Long);
			EmitAllocatedBinaryInPlace(
				M68kMachineOperation.Add,
				M68kRegister.D3,
				M68kRegister.D4,
				M68kMachineValueWidth.Long);
			EmitCompareImmediateLong(
				M68kRegister.D4,
				M68kRuntimeAbi.DelegateMaximumInvocationCount);
			var countValid = UniqueLabel("delegate-combine-count-valid");
			_assembler.EmitBranch(M68kCondition.LowerOrSame, countValid);
			EmitRuntimeObjectAddress(M68kRegister.A0, "System.ArgumentException");
			EmitExceptionRaise(reason: 0, hasException: true);
			_assembler.Mark(countValid);

			EnsureManagedAllocationAllowed(
				caller,
				instruction.SourceInstruction ??
					throw new InvalidOperationException("Delegate combination has no source instruction."),
				"delegate combination");
			EmitAllocatedMove(
				M68kRegister.D4,
				M68kRegister.D0,
				M68kMachineValueWidth.Long);
			EmitAllocatedShiftImmediate(M68kRegister.D0, left: true);
			EmitAllocatedShiftImmediate(M68kRegister.D0, left: true);
			EmitAllocatedAddImmediate(M68kRegister.D0, M68kRuntimeAbi.DelegateInvocationTailOffset);
			EmitManagedAllocationFromD0();
			EmitAllocatedMove(M68kRegister.D0, M68kRegister.A0, M68kMachineValueWidth.Long);

			EmitAllocatedAddress(DelegateMulticastDescriptorTableLabel, M68kRegister.A5);
			EmitAllocatedMove(M68kRegister.D4, M68kRegister.D0, M68kMachineValueWidth.Long);
			EmitAllocatedShiftImmediate(M68kRegister.D0, left: true);
			EmitAllocatedShiftImmediate(M68kRegister.D0, left: true);
			EmitAllocatedBinaryInPlace(
				M68kMachineOperation.Add,
				M68kRegister.D0,
				M68kRegister.A5,
				M68kMachineValueWidth.Long);
			EmitAllocatedBaseLoad(
				M68kRegister.A5,
				M68kRegister.D0,
				M68kMachineValueWidth.Long,
				0);
			EmitAllocatedBaseStore(
				M68kRegister.D0,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.ObjectDescriptorOffset);
			EmitAllocatedMove(M68kRegister.D4, M68kRegister.D1, M68kMachineValueWidth.Long);
			EmitAllocatedShiftImmediate(M68kRegister.D1, left: true);
			EmitAllocatedShiftImmediate(M68kRegister.D1, left: true);
			EmitAllocatedAddImmediate(M68kRegister.D1, M68kRuntimeAbi.DelegateInvocationTailOffset);
			EmitAllocatedBaseStore(
				M68kRegister.D1,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.ObjectSizeOffset);
			EmitAllocatedImmediate(0, M68kRegister.D1);
			foreach (var offset in new short[]
			{
				M68kRuntimeAbi.DelegateTargetOffset,
				M68kRuntimeAbi.DelegateInvocationListOffset
			})
			{
				EmitAllocatedBaseStore(
					M68kRegister.D1,
					M68kRegister.A0,
					M68kMachineValueWidth.Long,
					offset);
			}
			EmitAllocatedBaseStore(
				M68kRegister.A4,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.DelegateThunkOffset);
			EmitAllocatedMove(M68kRegister.D4, M68kRegister.D1, M68kMachineValueWidth.Long);
			_assembler.EmitWord(0x4841); // SWAP D1: count occupies the upper word
			_assembler.EmitWord(0x0081); // ORI.L #multicast,D1
			_assembler.EmitLong(M68kRuntimeAbi.DelegateFlagMulticast);
			EmitAllocatedBaseStore(
				M68kRegister.D1,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.DelegateFlagsOffset);
			EmitAllocatedMove(M68kRegister.A0, M68kRegister.A4, M68kMachineValueWidth.Long);
			EmitAllocatedAddImmediate(M68kRegister.A4, M68kRuntimeAbi.DelegateInvocationTailOffset);

			void EmitAppendInvocationList(
				M68kRegister source,
				M68kRegister count)
			{
				EmitAllocatedBaseLoad(
					source,
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					M68kRuntimeAbi.DelegateFlagsOffset);
				_assembler.EmitWord(0x0280); // ANDI.L #multicast,D0
				_assembler.EmitLong(M68kRuntimeAbi.DelegateFlagMulticast);
				var appendSingle = UniqueLabel("delegate-combine-append-single");
				var appendDone = UniqueLabel("delegate-combine-append-done");
				_assembler.EmitBranch(M68kCondition.Equal, appendSingle);
				EmitAllocatedMove(source, M68kRegister.A5, M68kMachineValueWidth.Long);
				EmitAllocatedAddImmediate(M68kRegister.A5, M68kRuntimeAbi.DelegateInvocationTailOffset);
				var loop = UniqueLabel("delegate-combine-append-loop");
				_assembler.Mark(loop);
				EmitAllocatedBaseLoad(
					M68kRegister.A5,
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					0);
				EmitAllocatedBaseStore(
					M68kRegister.D0,
					M68kRegister.A4,
					M68kMachineValueWidth.Long,
					0);
				EmitAllocatedAddImmediate(M68kRegister.A5, 4);
				EmitAllocatedAddImmediate(M68kRegister.A4, 4);
				_assembler.EmitWord((ushort)(0x5380 | (int)count)); // SUBQ.L #1,Dn
				_assembler.EmitBranch(M68kCondition.NotEqual, loop);
				_assembler.EmitBranch(M68kCondition.True, appendDone);
				_assembler.Mark(appendSingle);
				EmitAllocatedBaseStore(
					source,
					M68kRegister.A4,
					M68kMachineValueWidth.Long,
					0);
				EmitAllocatedAddImmediate(M68kRegister.A4, 4);
				_assembler.Mark(appendDone);
			}

			EmitAppendInvocationList(left, M68kRegister.D2);
			EmitAppendInvocationList(right, M68kRegister.D3);
			EmitAllocatedMove(M68kRegister.A0, Definition(), M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.True, done);

			_assembler.Mark(returnRight);
			EmitAllocatedMove(right, Definition(), M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.True, done);
			_assembler.Mark(returnLeft);
			EmitAllocatedMove(left, Definition(), M68kMachineValueWidth.Long);
			_assembler.Mark(done);
			return;
		}
		if (name == "intrinsic:delegate-remove")
		{
			var sourceDelegate = Use(0);
			var valueDelegate = Use(1);
			var returnSource = UniqueLabel("delegate-remove-return-source");
			var returnNull = UniqueLabel("delegate-remove-return-null");
			var done = UniqueLabel("delegate-remove-done");

			EmitAllocatedTest(sourceDelegate, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, returnNull);
			EmitAllocatedTest(valueDelegate, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, returnSource);
			EmitAllocatedLoadDelegateTypeIdentity(sourceDelegate, M68kRegister.A4);
			EmitAllocatedLoadDelegateTypeIdentity(valueDelegate, M68kRegister.A5);
			EmitAllocatedCompare(M68kRegister.A4, M68kRegister.A5, M68kMachineValueWidth.Long);
			var matchingTypes = UniqueLabel("delegate-remove-matching-types");
			_assembler.EmitBranch(M68kCondition.Equal, matchingTypes);
			RegisterRuntimeTypeDescriptor("System.ArgumentException");
			EmitRuntimeObjectAddress(M68kRegister.A0, "System.ArgumentException");
			EmitExceptionRaise(reason: 0, hasException: true);
			_assembler.Mark(matchingTypes);

			void EmitInvocationCount(M68kRegister source, M68kRegister count)
			{
				EmitAllocatedBaseLoad(
					source,
					count,
					M68kMachineValueWidth.Long,
					M68kRuntimeAbi.DelegateFlagsOffset);
				EmitAllocatedMove(count, M68kRegister.D0, M68kMachineValueWidth.Long);
				_assembler.EmitWord(0x0280); // ANDI.L #multicast,D0
				_assembler.EmitLong(M68kRuntimeAbi.DelegateFlagMulticast);
				var multicast = UniqueLabel("delegate-remove-count-multicast");
				var countDone = UniqueLabel("delegate-remove-count-done");
				_assembler.EmitBranch(M68kCondition.NotEqual, multicast);
				EmitAllocatedImmediate(1, count);
				_assembler.EmitBranch(M68kCondition.True, countDone);
				_assembler.Mark(multicast);
				_assembler.EmitWord((ushort)(0x4840 | (int)count)); // SWAP Dn
				_assembler.EmitWord((ushort)(0x0280 | (int)count)); // ANDI.L #$ffff,Dn
				_assembler.EmitLong(0x0000FFFF);
				_assembler.Mark(countDone);
			}

			void EmitSingleDelegateComparison(
				M68kRegister leftEntry,
				M68kRegister rightEntry,
				string mismatch)
			{
				foreach (var offset in new short[]
				{
					M68kRuntimeAbi.ObjectDescriptorOffset,
					M68kRuntimeAbi.DelegateTargetOffset,
					M68kRuntimeAbi.DelegateThunkOffset,
					M68kRuntimeAbi.DelegateInvocationListOffset,
					M68kRuntimeAbi.DelegateFlagsOffset
				})
				{
					EmitAllocatedBaseLoad(
						leftEntry,
						M68kRegister.D0,
						M68kMachineValueWidth.Long,
						offset);
					EmitAllocatedBaseLoad(
						rightEntry,
						M68kRegister.D1,
						M68kMachineValueWidth.Long,
						offset);
					EmitAllocatedCompare(
						M68kRegister.D0,
						M68kRegister.D1,
						M68kMachineValueWidth.Long);
					_assembler.EmitBranch(M68kCondition.NotEqual, mismatch);
				}
			}

			EmitInvocationCount(sourceDelegate, M68kRegister.D2);
			EmitInvocationCount(valueDelegate, M68kRegister.D3);
			EmitAllocatedImmediate(1, M68kRegister.D0);
			EmitAllocatedCompare(M68kRegister.D2, M68kRegister.D0, M68kMachineValueWidth.Long);
			var sourceIsMulticast = UniqueLabel("delegate-remove-source-multicast");
			_assembler.EmitBranch(M68kCondition.NotEqual, sourceIsMulticast);
			EmitAllocatedCompare(M68kRegister.D3, M68kRegister.D0, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.NotEqual, returnSource);
			EmitSingleDelegateComparison(sourceDelegate, valueDelegate, returnSource);
			_assembler.EmitBranch(M68kCondition.True, returnNull);
			_assembler.Mark(sourceIsMulticast);
			EmitAllocatedMove(M68kRegister.D2, M68kRegister.D6, M68kMachineValueWidth.Long);
			EmitAllocatedBinaryInPlace(
				M68kMachineOperation.Subtract,
				M68kRegister.D3,
				M68kRegister.D6,
				M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.CarrySet, returnSource);

			var search = UniqueLabel("delegate-remove-search");
			var candidateMismatch = UniqueLabel("delegate-remove-candidate-mismatch");
			var found = UniqueLabel("delegate-remove-found");
			_assembler.Mark(search);
			EmitAllocatedMove(sourceDelegate, M68kRegister.A4, M68kMachineValueWidth.Long);
			EmitAllocatedAddImmediate(M68kRegister.A4, M68kRuntimeAbi.DelegateInvocationTailOffset);
			EmitAllocatedMove(M68kRegister.D6, M68kRegister.D0, M68kMachineValueWidth.Long);
			EmitAllocatedShiftImmediate(M68kRegister.D0, left: true);
			EmitAllocatedShiftImmediate(M68kRegister.D0, left: true);
			EmitAllocatedBinaryInPlace(
				M68kMachineOperation.Add,
				M68kRegister.D0,
				M68kRegister.A4,
				M68kMachineValueWidth.Long);

			EmitAllocatedImmediate(1, M68kRegister.D0);
			EmitAllocatedCompare(M68kRegister.D3, M68kRegister.D0, M68kMachineValueWidth.Long);
			var compareMulticastValue = UniqueLabel("delegate-remove-compare-list");
			_assembler.EmitBranch(M68kCondition.NotEqual, compareMulticastValue);
			EmitAllocatedBaseLoad(
				M68kRegister.A4,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				0);
			EmitSingleDelegateComparison(M68kRegister.A0, valueDelegate, candidateMismatch);
			_assembler.EmitBranch(M68kCondition.True, found);

			_assembler.Mark(compareMulticastValue);
			EmitAllocatedMove(valueDelegate, M68kRegister.A5, M68kMachineValueWidth.Long);
			EmitAllocatedAddImmediate(M68kRegister.A5, M68kRuntimeAbi.DelegateInvocationTailOffset);
			EmitAllocatedMove(M68kRegister.D3, M68kRegister.D7, M68kMachineValueWidth.Long);
			var compareLoop = UniqueLabel("delegate-remove-compare-loop");
			_assembler.Mark(compareLoop);
			EmitAllocatedBaseLoad(
				M68kRegister.A4,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				0);
			EmitAllocatedBaseLoad(
				M68kRegister.A5,
				M68kRegister.A1,
				M68kMachineValueWidth.Long,
				0);
			EmitSingleDelegateComparison(M68kRegister.A0, M68kRegister.A1, candidateMismatch);
			EmitAllocatedAddImmediate(M68kRegister.A4, 4);
			EmitAllocatedAddImmediate(M68kRegister.A5, 4);
			_assembler.EmitWord(0x5387); // SUBQ.L #1,D7
			_assembler.EmitBranch(M68kCondition.NotEqual, compareLoop);
			_assembler.EmitBranch(M68kCondition.True, found);

			_assembler.Mark(candidateMismatch);
			EmitAllocatedTest(M68kRegister.D6, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, returnSource);
			_assembler.EmitWord(0x5386); // SUBQ.L #1,D6
			_assembler.EmitBranch(M68kCondition.True, search);

			_assembler.Mark(found);
			EmitAllocatedMove(M68kRegister.D2, M68kRegister.D4, M68kMachineValueWidth.Long);
			EmitAllocatedBinaryInPlace(
				M68kMachineOperation.Subtract,
				M68kRegister.D3,
				M68kRegister.D4,
				M68kMachineValueWidth.Long);
			EmitAllocatedTest(M68kRegister.D4, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, returnNull);
			EmitAllocatedImmediate(1, M68kRegister.D0);
			EmitAllocatedCompare(M68kRegister.D4, M68kRegister.D0, M68kMachineValueWidth.Long);
			var allocateResult = UniqueLabel("delegate-remove-allocate-result");
			_assembler.EmitBranch(M68kCondition.NotEqual, allocateResult);
			EmitAllocatedMove(sourceDelegate, M68kRegister.A4, M68kMachineValueWidth.Long);
			EmitAllocatedAddImmediate(M68kRegister.A4, M68kRuntimeAbi.DelegateInvocationTailOffset);
			EmitAllocatedTest(M68kRegister.D6, M68kMachineValueWidth.Long);
			var returnRemaining = UniqueLabel("delegate-remove-return-remaining");
			_assembler.EmitBranch(M68kCondition.NotEqual, returnRemaining);
			EmitAllocatedMove(M68kRegister.D3, M68kRegister.D0, M68kMachineValueWidth.Long);
			EmitAllocatedShiftImmediate(M68kRegister.D0, left: true);
			EmitAllocatedShiftImmediate(M68kRegister.D0, left: true);
			EmitAllocatedBinaryInPlace(
				M68kMachineOperation.Add,
				M68kRegister.D0,
				M68kRegister.A4,
				M68kMachineValueWidth.Long);
			_assembler.Mark(returnRemaining);
			EmitAllocatedBaseLoad(
				M68kRegister.A4,
				Definition(),
				M68kMachineValueWidth.Long,
				0);
			_assembler.EmitBranch(M68kCondition.True, done);

			_assembler.Mark(allocateResult);
			EnsureManagedAllocationAllowed(
				caller,
				instruction.SourceInstruction ??
					throw new InvalidOperationException("Delegate removal has no source instruction."),
				"delegate removal");
			EmitAllocatedMove(M68kRegister.D4, M68kRegister.D0, M68kMachineValueWidth.Long);
			EmitAllocatedShiftImmediate(M68kRegister.D0, left: true);
			EmitAllocatedShiftImmediate(M68kRegister.D0, left: true);
			EmitAllocatedAddImmediate(M68kRegister.D0, M68kRuntimeAbi.DelegateInvocationTailOffset);
			EmitManagedAllocationFromD0();
			EmitAllocatedMove(M68kRegister.D0, M68kRegister.A0, M68kMachineValueWidth.Long);
			EmitAllocatedLoadDelegateTypeIdentity(sourceDelegate, M68kRegister.A4);
			EmitAllocatedAddress(DelegateMulticastDescriptorTableLabel, M68kRegister.A5);
			EmitAllocatedMove(M68kRegister.D4, M68kRegister.D0, M68kMachineValueWidth.Long);
			EmitAllocatedShiftImmediate(M68kRegister.D0, left: true);
			EmitAllocatedShiftImmediate(M68kRegister.D0, left: true);
			EmitAllocatedBinaryInPlace(
				M68kMachineOperation.Add,
				M68kRegister.D0,
				M68kRegister.A5,
				M68kMachineValueWidth.Long);
			EmitAllocatedBaseLoad(
				M68kRegister.A5,
				M68kRegister.D0,
				M68kMachineValueWidth.Long,
				0);
			EmitAllocatedBaseStore(
				M68kRegister.D0,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.ObjectDescriptorOffset);
			EmitAllocatedMove(M68kRegister.D4, M68kRegister.D1, M68kMachineValueWidth.Long);
			EmitAllocatedShiftImmediate(M68kRegister.D1, left: true);
			EmitAllocatedShiftImmediate(M68kRegister.D1, left: true);
			EmitAllocatedAddImmediate(M68kRegister.D1, M68kRuntimeAbi.DelegateInvocationTailOffset);
			EmitAllocatedBaseStore(
				M68kRegister.D1,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.ObjectSizeOffset);
			EmitAllocatedImmediate(0, M68kRegister.D1);
			foreach (var offset in new short[]
			{
				M68kRuntimeAbi.DelegateTargetOffset,
				M68kRuntimeAbi.DelegateInvocationListOffset
			})
			{
				EmitAllocatedBaseStore(
					M68kRegister.D1,
					M68kRegister.A0,
					M68kMachineValueWidth.Long,
					offset);
			}
			EmitAllocatedBaseStore(
				M68kRegister.A4,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.DelegateThunkOffset);
			EmitAllocatedMove(M68kRegister.D4, M68kRegister.D1, M68kMachineValueWidth.Long);
			_assembler.EmitWord(0x4841); // SWAP D1
			_assembler.EmitWord(0x0081); // ORI.L #multicast,D1
			_assembler.EmitLong(M68kRuntimeAbi.DelegateFlagMulticast);
			EmitAllocatedBaseStore(
				M68kRegister.D1,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.DelegateFlagsOffset);
			EmitAllocatedMove(sourceDelegate, M68kRegister.A5, M68kMachineValueWidth.Long);
			EmitAllocatedAddImmediate(M68kRegister.A5, M68kRuntimeAbi.DelegateInvocationTailOffset);
			EmitAllocatedMove(M68kRegister.A0, M68kRegister.A4, M68kMachineValueWidth.Long);
			EmitAllocatedAddImmediate(M68kRegister.A4, M68kRuntimeAbi.DelegateInvocationTailOffset);

			void EmitCopyEntries(M68kRegister count)
			{
				EmitAllocatedTest(count, M68kMachineValueWidth.Long);
				var copyDone = UniqueLabel("delegate-remove-copy-done");
				_assembler.EmitBranch(M68kCondition.Equal, copyDone);
				var copyLoop = UniqueLabel("delegate-remove-copy-loop");
				_assembler.Mark(copyLoop);
				EmitAllocatedBaseLoad(
					M68kRegister.A5,
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					0);
				EmitAllocatedBaseStore(
					M68kRegister.D0,
					M68kRegister.A4,
					M68kMachineValueWidth.Long,
					0);
				EmitAllocatedAddImmediate(M68kRegister.A5, 4);
				EmitAllocatedAddImmediate(M68kRegister.A4, 4);
				_assembler.EmitWord((ushort)(0x5380 | (int)count)); // SUBQ.L #1,Dn
				_assembler.EmitBranch(M68kCondition.NotEqual, copyLoop);
				_assembler.Mark(copyDone);
			}

			EmitAllocatedMove(M68kRegister.D6, M68kRegister.D7, M68kMachineValueWidth.Long);
			EmitCopyEntries(M68kRegister.D7);
			EmitAllocatedMove(M68kRegister.D3, M68kRegister.D0, M68kMachineValueWidth.Long);
			EmitAllocatedShiftImmediate(M68kRegister.D0, left: true);
			EmitAllocatedShiftImmediate(M68kRegister.D0, left: true);
			EmitAllocatedBinaryInPlace(
				M68kMachineOperation.Add,
				M68kRegister.D0,
				M68kRegister.A5,
				M68kMachineValueWidth.Long);
			EmitAllocatedMove(M68kRegister.D4, M68kRegister.D7, M68kMachineValueWidth.Long);
			EmitAllocatedBinaryInPlace(
				M68kMachineOperation.Subtract,
				M68kRegister.D6,
				M68kRegister.D7,
				M68kMachineValueWidth.Long);
			EmitCopyEntries(M68kRegister.D7);
			EmitAllocatedMove(M68kRegister.A0, Definition(), M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.True, done);

			_assembler.Mark(returnSource);
			EmitAllocatedMove(sourceDelegate, Definition(), M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.True, done);
			_assembler.Mark(returnNull);
			EmitAllocatedImmediate(0, M68kRegister.D0);
			EmitAllocatedMove(M68kRegister.D0, Definition(), M68kMachineValueWidth.Long);
			_assembler.Mark(done);
			return;
		}
		if (name == "intrinsic:delegate-invoke")
		{
			var delegateObject = Use(0);
			EmitAllocatedRequireNonNull(instruction.Uses[0], delegateObject);
			EmitAllocatedBaseLoad(
				delegateObject,
				M68kRegister.D2,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.DelegateFlagsOffset);
			EmitAllocatedMove(
				M68kRegister.D2,
				M68kRegister.D3,
				M68kMachineValueWidth.Long);
			_assembler.EmitWord(0x0283); // ANDI.L #multicast,D3
			_assembler.EmitLong(M68kRuntimeAbi.DelegateFlagMulticast);
			var singlecast = UniqueLabel("delegate-invoke-singlecast");
			var done = UniqueLabel("delegate-invoke-done");
			_assembler.EmitBranch(M68kCondition.Equal, singlecast);

			// D0/D1/A1 carry the register portion of the delegate arguments.
			// Every multicast target must observe the original values even though
			// those registers are caller-saved and may contain an earlier result.
			EmitAllocatedMove(M68kRegister.D0, M68kRegister.D4, M68kMachineValueWidth.Long);
			EmitAllocatedMove(M68kRegister.D1, M68kRegister.D5, M68kMachineValueWidth.Long);
			EmitAllocatedMove(M68kRegister.A1, M68kRegister.A4, M68kMachineValueWidth.Long);
			_assembler.EmitWord(0x4842); // SWAP D2: invocation count
			_assembler.EmitWord(0x0282); // ANDI.L #$ffff,D2
			_assembler.EmitLong(0x0000FFFF);
			EmitAllocatedMove(delegateObject, M68kRegister.A3, M68kMachineValueWidth.Long);
			EmitAllocatedAddImmediate(M68kRegister.A3, M68kRuntimeAbi.DelegateInvocationTailOffset);
			var loop = UniqueLabel("delegate-invoke-multicast-loop");
			_assembler.Mark(loop);
			EmitAllocatedBaseLoad(
				M68kRegister.A3,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				0);
			EmitAllocatedAddImmediate(M68kRegister.A3, 4);
			EmitAllocatedBaseLoad(
				M68kRegister.A0,
				M68kRegister.A2,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.DelegateThunkOffset);
			EmitAllocatedBaseLoad(
				M68kRegister.A0,
				M68kRegister.D3,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.DelegateFlagsOffset);
			_assembler.EmitWord(0x0283); // ANDI.L #closed-instance,D3
			_assembler.EmitLong(M68kRuntimeAbi.DelegateFlagClosedInstance);
			var multicastStatic = UniqueLabel("delegate-invoke-multicast-static");
			_assembler.EmitBranch(M68kCondition.Equal, multicastStatic);
			EmitAllocatedBaseLoad(
				M68kRegister.A0,
				M68kRegister.A0,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.DelegateTargetOffset);
			_assembler.Mark(multicastStatic);
			EmitAllocatedMove(M68kRegister.D4, M68kRegister.D0, M68kMachineValueWidth.Long);
			EmitAllocatedMove(M68kRegister.D5, M68kRegister.D1, M68kMachineValueWidth.Long);
			EmitAllocatedMove(M68kRegister.A4, M68kRegister.A1, M68kMachineValueWidth.Long);
			_assembler.EmitWord(0x4E92); // JSR (A2)
			RegisterCurrentUnwindSite(
				exception: true,
				gc: _emittingMachineInstruction?.IsSafepoint == true);
			_loadedPlatformBase = null;
			_assembler.EmitWord(0x5382); // SUBQ.L #1,D2
			_assembler.EmitBranch(M68kCondition.NotEqual, loop);
			_assembler.EmitBranch(M68kCondition.True, done);

			_assembler.Mark(singlecast);
			EmitAllocatedBaseLoad(
				delegateObject,
				M68kRegister.A1,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.DelegateTargetOffset);
			EmitAllocatedBaseLoad(
				delegateObject,
				M68kRegister.A2,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.DelegateThunkOffset);
			_assembler.EmitWord(0x0282); // ANDI.L #closed-instance,D2
			_assembler.EmitLong(M68kRuntimeAbi.DelegateFlagClosedInstance);
			var directStatic = UniqueLabel("delegate-direct-static");
			_assembler.EmitBranch(M68kCondition.Equal, directStatic);
			EmitAllocatedMove(M68kRegister.A1, M68kRegister.A0, M68kMachineValueWidth.Long);
			_assembler.Mark(directStatic);
			_assembler.EmitWord(0x4E92); // JSR (A2)
			RegisterCurrentUnwindSite(
				exception: true,
				gc: _emittingMachineInstruction?.IsSafepoint == true);
			_loadedPlatformBase = null;
			_assembler.Mark(done);
			return;
		}
		if (name is "intrinsic:delegate-equality" or "intrinsic:delegate-inequality")
		{
			var invert = name == "intrinsic:delegate-inequality";
			var left = Use(0);
			var right = Use(1);
			var equal = UniqueLabel("delegate-equality-equal");
			var unequal = UniqueLabel("delegate-equality-unequal");
			var done = UniqueLabel("delegate-equality-done");
			EmitAllocatedCompare(left, right, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, equal);
			EmitAllocatedTest(left, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, unequal);
			EmitAllocatedTest(right, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, unequal);
			foreach (var offset in new short[]
			{
				M68kRuntimeAbi.ObjectDescriptorOffset,
				M68kRuntimeAbi.DelegateFlagsOffset
			})
			{
				EmitAllocatedBaseLoad(
					left,
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					offset);
				EmitAllocatedBaseLoad(
					right,
					M68kRegister.D1,
					M68kMachineValueWidth.Long,
					offset);
				EmitAllocatedCompare(
					M68kRegister.D0,
					M68kRegister.D1,
					M68kMachineValueWidth.Long);
				_assembler.EmitBranch(M68kCondition.NotEqual, unequal);
			}
			EmitAllocatedBaseLoad(
				left,
				M68kRegister.D2,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.DelegateFlagsOffset);
			_assembler.EmitWord(0x0282); // ANDI.L #multicast,D2
			_assembler.EmitLong(M68kRuntimeAbi.DelegateFlagMulticast);
			var compareSinglecast = UniqueLabel("delegate-equality-singlecast");
			_assembler.EmitBranch(M68kCondition.Equal, compareSinglecast);

			EmitAllocatedBaseLoad(
				left,
				M68kRegister.D2,
				M68kMachineValueWidth.Long,
				M68kRuntimeAbi.DelegateFlagsOffset);
			_assembler.EmitWord(0x4842); // SWAP D2: invocation count
			_assembler.EmitWord(0x0282); // ANDI.L #$ffff,D2
			_assembler.EmitLong(0x0000FFFF);
			EmitAllocatedMove(left, M68kRegister.A2, M68kMachineValueWidth.Long);
			EmitAllocatedMove(right, M68kRegister.A3, M68kMachineValueWidth.Long);
			EmitAllocatedAddImmediate(M68kRegister.A2, M68kRuntimeAbi.DelegateInvocationTailOffset);
			EmitAllocatedAddImmediate(M68kRegister.A3, M68kRuntimeAbi.DelegateInvocationTailOffset);
			var compareMulticastLoop = UniqueLabel("delegate-equality-multicast-loop");
			_assembler.Mark(compareMulticastLoop);
			EmitAllocatedBaseLoad(
				M68kRegister.A2,
				M68kRegister.A4,
				M68kMachineValueWidth.Long,
				0);
			EmitAllocatedBaseLoad(
				M68kRegister.A3,
				M68kRegister.A5,
				M68kMachineValueWidth.Long,
				0);
			foreach (var offset in new short[]
			{
				M68kRuntimeAbi.ObjectDescriptorOffset,
				M68kRuntimeAbi.DelegateTargetOffset,
				M68kRuntimeAbi.DelegateThunkOffset,
				M68kRuntimeAbi.DelegateInvocationListOffset,
				M68kRuntimeAbi.DelegateFlagsOffset
			})
			{
				EmitAllocatedBaseLoad(
					M68kRegister.A4,
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					offset);
				EmitAllocatedBaseLoad(
					M68kRegister.A5,
					M68kRegister.D1,
					M68kMachineValueWidth.Long,
					offset);
				EmitAllocatedCompare(
					M68kRegister.D0,
					M68kRegister.D1,
					M68kMachineValueWidth.Long);
				_assembler.EmitBranch(M68kCondition.NotEqual, unequal);
			}
			EmitAllocatedAddImmediate(M68kRegister.A2, 4);
			EmitAllocatedAddImmediate(M68kRegister.A3, 4);
			_assembler.EmitWord(0x5382); // SUBQ.L #1,D2
			_assembler.EmitBranch(M68kCondition.NotEqual, compareMulticastLoop);
			_assembler.EmitBranch(M68kCondition.True, equal);

			_assembler.Mark(compareSinglecast);
			foreach (var offset in new short[]
			{
				M68kRuntimeAbi.DelegateTargetOffset,
				M68kRuntimeAbi.DelegateThunkOffset,
				M68kRuntimeAbi.DelegateInvocationListOffset
			})
			{
				EmitAllocatedBaseLoad(
					left,
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					offset);
				EmitAllocatedBaseLoad(
					right,
					M68kRegister.D1,
					M68kMachineValueWidth.Long,
					offset);
				EmitAllocatedCompare(
					M68kRegister.D0,
					M68kRegister.D1,
					M68kMachineValueWidth.Long);
				_assembler.EmitBranch(M68kCondition.NotEqual, unequal);
			}
			_assembler.Mark(equal);
			EmitAllocatedImmediate(invert ? 0 : 1, M68kRegister.D0);
			_assembler.EmitBranch(M68kCondition.True, done);
			_assembler.Mark(unequal);
			EmitAllocatedImmediate(invert ? 1 : 0, M68kRegister.D0);
			_assembler.Mark(done);
			if (instruction.Definitions.Length != 0)
			{
				EmitAllocatedMove(
					M68kRegister.D0,
					Definition(),
					M68kMachineValueWidth.Long);
			}
			return;
		}
			if (name == "intrinsic:runtime-throw-overflow")
			{
				EmitExceptionRaise(reason: 4, hasException: false);
				return;
			}
			if (name == "intrinsic:runtime-throw-arithmetic")
			{
				RegisterRuntimeTypeDescriptor("System.ArithmeticException");
				EmitExceptionRaise(reason: 19, hasException: false);
				return;
			}
			if (name == "intrinsic:list-enumerator-dispose")
			{
				return;
			}
			if (name == "intrinsic:dictionary-key-is-null:false")
			{
				EmitAllocatedImmediate(0, Definition());
				return;
			}
			if (name == "intrinsic:dictionary-key-is-null:reference")
			{
				var destination = Definition();
				EmitAllocatedTest(Use(0), M68kMachineValueWidth.Long);
				EmitAllocatedConditionResult(M68kCondition.Equal, destination);
				return;
			}
			if (name == "intrinsic:runtime-throw-format")
			{
				RegisterRuntimeTypeDescriptor("System.FormatException");
				EmitExceptionRaise(reason: 12, hasException: false);
				return;
			}
			if (name == "intrinsic:runtime-throw-argument")
			{
				RegisterRuntimeTypeDescriptor("System.ArgumentException");
				EmitExceptionRaise(reason: 9, hasException: false);
				return;
			}
			if (name == "intrinsic:runtime-throw-argument-null")
			{
				RegisterRuntimeTypeDescriptor("System.ArgumentNullException");
				EmitExceptionRaise(reason: 11, hasException: false);
				return;
			}
			if (name == "intrinsic:runtime-throw-argument-out-of-range")
			{
				RegisterRuntimeTypeDescriptor("System.ArgumentOutOfRangeException");
				EmitExceptionRaise(reason: 10, hasException: false);
				return;
			}
			if (name == "intrinsic:runtime-throw-invalid-operation")
			{
				RegisterRuntimeTypeDescriptor("System.InvalidOperationException");
				EmitExceptionRaise(reason: 13, hasException: false);
				return;
			}
			if (name == "intrinsic:runtime-throw-io")
			{
				RegisterRuntimeTypeDescriptor("System.IO.IOException");
				EmitExceptionRaise(reason: 15, hasException: false);
				return;
			}
			if (name == "intrinsic:runtime-throw-directory-not-found")
			{
				RegisterRuntimeTypeDescriptor("System.IO.DirectoryNotFoundException");
				EmitExceptionRaise(reason: 16, hasException: false);
				return;
			}
			if (name == "intrinsic:runtime-throw-file-not-found")
			{
				RegisterRuntimeTypeDescriptor("System.IO.FileNotFoundException");
				EmitExceptionRaise(reason: 18, hasException: false);
				return;
			}
			if (name == "intrinsic:runtime-throw-unauthorized-access")
			{
				RegisterRuntimeTypeDescriptor("System.UnauthorizedAccessException");
				EmitExceptionRaise(reason: 17, hasException: false);
				return;
			}
			if (name == "intrinsic:runtime-throw-key-not-found")
			{
				RegisterRuntimeTypeDescriptor("System.Collections.Generic.KeyNotFoundException");
				EmitExceptionRaise(reason: 14, hasException: false);
				return;
			}
			if (name == "intrinsic:runtime-throw-out-of-memory")
			{
				RegisterRuntimeTypeDescriptor("System.OutOfMemoryException");
				EmitExceptionRaise(reason: 6, hasException: false);
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
		if (name == "intrinsic:runtime-invoke-finalizer")
		{
			EmitAllocatedMove(
				Use(0),
				M68kRegister.D0,
				M68kMachineValueWidth.Long);
			EmitInvokeFinalizerFromD0();
			return;
		}
		if (name is
			"intrinsic:runtime-gc-suppress-finalize" or
			"intrinsic:runtime-gc-reregister-finalize")
		{
			EmitAllocatedMove(
				Use(0),
				M68kRegister.D0,
				M68kMachineValueWidth.Long);
			EmitFinalizerControlFromD0(
				name.EndsWith("reregister-finalize", StringComparison.Ordinal));
			return;
		}
		if (name is
			"intrinsic:object-finalize" or
			"intrinsic:runtime-gc-wait-finalizers" or
			"intrinsic:runtime-gc-keep-alive")
		{
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
			"intrinsic:runtime-bitcast-32" or
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
			EmitAllocatedLiteralAddress(caller, instruction, Definition());
			return;
		}
		if (name == "intrinsic:aptr-export-address")
		{
			var literal = AllocatedPrecedingStringLiteral(caller, instruction);
			var exportName = _module.GetUserString(
				literal,
				caller,
				instruction.IlOffset);
			if (!_exports.Any(export => export.Name == exportName))
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
		if (name is "intrinsic:aptr-read-uint8" or "intrinsic:aptr-read-uint16" or "intrinsic:aptr-read-uint32")
		{
			var baseRegister = Use(0);
			var destination = Definition();
			var foldedResultCopy = TryFoldAllocatedIntrinsicResultCopy(
					allocated,
					instruction,
					out var foldedDestination);
			if (foldedResultCopy)
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
			var width = name == "intrinsic:aptr-read-uint8" ? M68kMachineValueWidth.Byte :
				name == "intrinsic:aptr-read-uint16" ? M68kMachineValueWidth.Word : M68kMachineValueWidth.Long;
			EmitAllocatedBaseLoad(
				baseRegister,
				destination,
				width,
				displacement);
			if (width != M68kMachineValueWidth.Long)
			{
				// APTR reads publish canonical CLR byte/ushort values. A byte/word
				// memory move leaves the destination's upper bits unchanged, including
				// when result-copy folding places the load directly in its final register.
				// Emit the canonicalization at the producer; the raw optimizer can still
				// turn it into MOVEQ+partial MOVE or remove it after a proved low-only use.
				EmitAllocatedNormalize(
					destination,
					allocated.Function.Values[instruction.Definitions[0]].Kind);
			}
			return;
		}

		if (name?.StartsWith(
				"intrinsic:file-info-block-read-int32:",
				StringComparison.Ordinal) == true)
		{
			var displacement = name! switch
			{
				"intrinsic:file-info-block-read-int32:4" => (short)4,
				"intrinsic:file-info-block-read-int32:116" => (short)116,
				"intrinsic:file-info-block-read-int32:124" => (short)124,
				"intrinsic:file-info-block-read-int32:132" => (short)132,
				"intrinsic:file-info-block-read-int32:136" => (short)136,
				"intrinsic:file-info-block-read-int32:140" => (short)140,
				_ => throw new InvalidOperationException(
					$"Unsupported FileInfoBlock displacement intrinsic '{name}'.")
			};
			var destination = Definition();
			if (TryFoldAllocatedIntrinsicResultCopy(
					allocated,
					instruction,
					out var foldedDestination))
			{
				destination = foldedDestination;
			}
			EmitAllocatedBaseLoad(
				Use(0),
				destination,
				M68kMachineValueWidth.Long,
				displacement);
			return;
		}
		if (name is "intrinsic:aptr-write-uint8" or "intrinsic:aptr-write-uint16" or "intrinsic:aptr-write-uint32")
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
			var width = name == "intrinsic:aptr-write-uint8" ? M68kMachineValueWidth.Byte :
				name == "intrinsic:aptr-write-uint16" ? M68kMachineValueWidth.Word : M68kMachineValueWidth.Long;
			EmitAllocatedBaseStore(
				Use(instruction.Immediate is null ? 2 : 1),
				baseRegister,
				width,
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
		if (name!.StartsWith(
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
			StringComparison.Ordinal) ||
			name.StartsWith(
				"intrinsic:nullable-get-value-or-default-no-argument:",
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
			if (instruction.Immediate == 0 ||
				IsAllocatedAptrNullValue(
					caller,
					allocated.Function,
					instruction.Uses[0]))
			{
				EmitClearLabel(
					AmigaLibraryBaseSlotSymbol(libraryTypeName));
				_loadedPlatformBase = null;
				return;
			}
			var slotLabel = AmigaLibraryBaseSlotSymbol(libraryTypeName);
			if (TryGetResidentInvocationOffset(slotLabel, out var offset))
			{
				_assembler.EmitWord((ushort)(
					0x2B40 |
					AllocatedRegisterEa(Use(0)))); // MOVE.L reg,d16(A5)
				_assembler.EmitWord(unchecked((ushort)offset));
			}
			else
			{
				_assembler.EmitWord((ushort)(
					0x23C0 |
					AllocatedRegisterEa(Use(0))));
				_assembler.EmitAddress(slotLabel);
			}
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
			var slotLabel = AmigaLibraryBaseSlotSymbol(libraryTypeName);
			if (TryGetResidentInvocationOffset(slotLabel, out var offset))
			{
				_assembler.EmitWord((ushort)(0x202D | destinationEa)); // MOVE.L d16(A5),reg
				_assembler.EmitWord(unchecked((ushort)offset));
			}
			else
			{
				_assembler.EmitWord((ushort)(0x2039 | destinationEa));
				_assembler.EmitAddress(slotLabel);
			}
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
				EmitAllocatedRequireNonNull(instruction.Uses[0], stringBase);
			EmitAllocatedBaseLoad(
				stringBase,
				Definition(),
				M68kMachineValueWidth.Long,
				8);
				return;
			}
			if (name == "intrinsic:string-char")
			{
				var stringBase = Use(0);
				var index = Use(1);
				var destination = Definition();
				EmitAllocatedArrayBoundsCheck(
					instruction.Uses[0],
					stringBase,
					index);
				if (_request.Cpu == M68kCpuTarget.M68000)
				{
					EmitAllocatedShiftImmediate(index, left: true);
				}
				EmitAllocatedIndexedLoad(
					stringBase,
					index,
					destination,
					M68kMachineValueWidth.Word,
					2,
					checked((sbyte)M68kRuntimeAbi.StringDataOffset));
				_assembler.EmitWord((ushort)(0x0280 | (int)destination));
				_assembler.EmitLong(0xFFFF);
				return;
			}
			if (name.StartsWith(
					"intrinsic:memory-from-array",
					StringComparison.Ordinal) ||
				name.StartsWith(
					"intrinsic:readonly-memory-from-array",
					StringComparison.Ordinal))
			{
				var writesInstanceReceiver =
					target.Signature.Header.IsInstance &&
					instruction.SourceInstruction?.OpCode != OpCodes.Newobj;
				var destination = writesInstanceReceiver ? Use(0) : M68kRegister.A1;
				var firstParameter = writesInstanceReceiver ? 1 : 0;
				var array = Use(firstParameter);
				var hasRange = name.Contains("-range:", StringComparison.Ordinal);
				var start = hasRange ? Use(firstParameter + 1) : M68kRegister.D0;
				var length = hasRange ? Use(firstParameter + 2) : M68kRegister.D1;
				if (!writesInstanceReceiver)
				{
					EmitAllocatedStackLoad(destination, 0);
				}
				var nonNull = UniqueLabel("allocated_memory_array_nonnull");
				var complete = UniqueLabel("allocated_memory_array_complete");
				EmitAllocatedTest(array, M68kMachineValueWidth.Long);
				_assembler.EmitBranch(M68kCondition.NotEqual, nonNull);
				if (hasRange)
				{
					var nullRangeInvalid = UniqueLabel(
						"allocated_memory_null_range_invalid");
					EmitAllocatedTest(start, M68kMachineValueWidth.Long);
					_assembler.EmitBranch(M68kCondition.NotEqual, nullRangeInvalid);
					EmitAllocatedTest(length, M68kMachineValueWidth.Long);
					_assembler.EmitBranch(M68kCondition.NotEqual, nullRangeInvalid);
					EmitAllocatedBaseClearLong(destination, 0);
					EmitAllocatedBaseClearLong(destination, 4);
					EmitAllocatedBaseClearLong(destination, 8);
					_assembler.EmitBranch(M68kCondition.True, complete);
					_assembler.Mark(nullRangeInvalid);
					RegisterRuntimeTypeDescriptor("System.ArgumentOutOfRangeException");
					EmitExceptionRaise(reason: 10, hasException: false);
				}
				else
				{
					EmitAllocatedBaseClearLong(destination, 0);
					EmitAllocatedBaseClearLong(destination, 4);
					EmitAllocatedBaseClearLong(destination, 8);
					_assembler.EmitBranch(M68kCondition.True, complete);
				}

				_assembler.Mark(nonNull);
				if (hasRange)
				{
					RegisterRuntimeTypeDescriptor("System.ArgumentOutOfRangeException");
					EmitAllocatedBaseLoad(
						array,
						M68kRegister.D2,
						M68kMachineValueWidth.Long,
						8);
					var startValid = UniqueLabel("allocated_memory_start_valid");
					EmitAllocatedCompare(start, M68kRegister.D2, M68kMachineValueWidth.Long);
					_assembler.EmitBranch(M68kCondition.CarrySet, startValid);
					_assembler.EmitBranch(M68kCondition.Equal, startValid);
					EmitExceptionRaise(reason: 10, hasException: false);
					_assembler.Mark(startValid);
					EmitAllocatedBinaryInPlace(
						M68kMachineOperation.Subtract,
						start,
						M68kRegister.D2,
						M68kMachineValueWidth.Long);
					var lengthValid = UniqueLabel("allocated_memory_length_valid");
					EmitAllocatedCompare(length, M68kRegister.D2, M68kMachineValueWidth.Long);
					_assembler.EmitBranch(M68kCondition.CarrySet, lengthValid);
					_assembler.EmitBranch(M68kCondition.Equal, lengthValid);
					EmitExceptionRaise(reason: 10, hasException: false);
					_assembler.Mark(lengthValid);
				}
				else
				{
					EmitAllocatedImmediate(0, start);
					EmitAllocatedBaseLoad(
						array,
						length,
						M68kMachineValueWidth.Long,
						8);
				}
				EmitAllocatedBaseStore(
					array,
					destination,
					M68kMachineValueWidth.Long,
					0);
				EmitAllocatedBaseStore(
					start,
					destination,
					M68kMachineValueWidth.Long,
					4);
				EmitAllocatedBaseStore(
					length,
					destination,
					M68kMachineValueWidth.Long,
					8);
				_assembler.Mark(complete);
				return;
			}
			if (name.StartsWith(
					"intrinsic:readonly-memory-from-memory:",
					StringComparison.Ordinal))
			{
				var source = Use(0);
				EmitAllocatedStackLoad(M68kRegister.A1, 0);
				for (short offset = 0; offset < 12; offset += 4)
				{
					EmitAllocatedBaseLoad(
						source,
						M68kRegister.D0,
						M68kMachineValueWidth.Long,
						offset);
					EmitAllocatedBaseStore(
						M68kRegister.D0,
						M68kRegister.A1,
						M68kMachineValueWidth.Long,
						offset);
				}
				return;
			}
			if (name.StartsWith(
					"intrinsic:memory-slice-",
					StringComparison.Ordinal) ||
				name.StartsWith(
					"intrinsic:readonly-memory-slice-",
					StringComparison.Ordinal))
			{
				var source = Use(0);
				var start = Use(1);
				var hasExplicitLength = name.Contains("-range:", StringComparison.Ordinal);
				var requestedLength = hasExplicitLength ? Use(2) : M68kRegister.D1;
				EmitAllocatedStackLoad(M68kRegister.A1, 0);
				EmitAllocatedBaseLoad(
					source,
					M68kRegister.D2,
					M68kMachineValueWidth.Long,
					8);
				RegisterRuntimeTypeDescriptor("System.ArgumentOutOfRangeException");
				var startValid = UniqueLabel("allocated_memory_slice_start_valid");
				EmitAllocatedCompare(start, M68kRegister.D2, M68kMachineValueWidth.Long);
				_assembler.EmitBranch(M68kCondition.CarrySet, startValid);
				_assembler.EmitBranch(M68kCondition.Equal, startValid);
				EmitExceptionRaise(reason: 10, hasException: false);
				_assembler.Mark(startValid);
				EmitAllocatedBinaryInPlace(
					M68kMachineOperation.Subtract,
					start,
					M68kRegister.D2,
					M68kMachineValueWidth.Long);
				if (hasExplicitLength)
				{
					var lengthValid = UniqueLabel("allocated_memory_slice_length_valid");
					EmitAllocatedCompare(
						requestedLength,
						M68kRegister.D2,
						M68kMachineValueWidth.Long);
					_assembler.EmitBranch(M68kCondition.CarrySet, lengthValid);
					_assembler.EmitBranch(M68kCondition.Equal, lengthValid);
					EmitExceptionRaise(reason: 10, hasException: false);
					_assembler.Mark(lengthValid);
					EmitAllocatedMove(
						requestedLength,
						M68kRegister.D2,
						M68kMachineValueWidth.Long);
				}
				EmitAllocatedBaseLoad(
					source,
					M68kRegister.D3,
					M68kMachineValueWidth.Long,
					4);
				EmitAllocatedBinaryInPlace(
					M68kMachineOperation.Add,
					start,
					M68kRegister.D3,
					M68kMachineValueWidth.Long);
				EmitAllocatedBaseLoad(
					source,
					M68kRegister.A2,
					M68kMachineValueWidth.Long,
					0);
				EmitAllocatedBaseStore(
					M68kRegister.A2,
					M68kRegister.A1,
					M68kMachineValueWidth.Long,
					0);
				EmitAllocatedBaseStore(
					M68kRegister.D3,
					M68kRegister.A1,
					M68kMachineValueWidth.Long,
					4);
				EmitAllocatedBaseStore(
					M68kRegister.D2,
					M68kRegister.A1,
					M68kMachineValueWidth.Long,
					8);
				return;
			}
			if (name.StartsWith("intrinsic:memory-length:", StringComparison.Ordinal) ||
				name.StartsWith(
					"intrinsic:readonly-memory-length:",
					StringComparison.Ordinal))
			{
				EmitAllocatedBaseLoad(
					Use(0),
					Definition(),
					M68kMachineValueWidth.Long,
					8);
				return;
			}
			if (name.StartsWith("intrinsic:memory-is-empty:", StringComparison.Ordinal) ||
				name.StartsWith(
					"intrinsic:readonly-memory-is-empty:",
					StringComparison.Ordinal))
			{
				var materialize = instruction.Definitions.Length != 0;
				var destination = materialize ? Definition() : M68kRegister.D0;
				EmitAllocatedBaseLoad(
					Use(0),
					destination,
					M68kMachineValueWidth.Long,
					8);
				EmitAllocatedTest(destination, M68kMachineValueWidth.Long);
				if (materialize)
				{
					EmitAllocatedConditionResult(M68kCondition.Equal, destination);
				}
				return;
			}
			if (name.StartsWith("intrinsic:memory-copy-to:", StringComparison.Ordinal) ||
				name.StartsWith(
					"intrinsic:memory-try-copy-to:",
					StringComparison.Ordinal) ||
				name.StartsWith(
					"intrinsic:readonly-memory-copy-to:",
					StringComparison.Ordinal) ||
				name.StartsWith(
					"intrinsic:readonly-memory-try-copy-to:",
					StringComparison.Ordinal))
			{
				var source = Use(0);
				var destination = Use(1);
				var element = target.ConstructedDeclaringType?.GenericArguments[0] ??
					target.Signature.ParameterTypes[0].GenericArguments[0];
				var elementSize = GetAllocatedSpanElementSize(caller, element);
				if (source < M68kRegister.A0 ||
					destination < M68kRegister.A0 ||
					elementSize <= 0)
				{
					throw new InvalidOperationException(
						"Allocated Memory<T> copy has an invalid receiver or element shape.");
				}

				EmitAllocatedBaseLoad(
					source,
					M68kRegister.D2,
					M68kMachineValueWidth.Long,
					8);
				EmitAllocatedBaseLoad(
					destination,
					M68kRegister.D1,
					M68kMachineValueWidth.Long,
					8);
				EmitAllocatedBaseLoad(
					source,
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					4);
				EmitAllocatedBaseLoad(
					source,
					M68kRegister.A2,
					M68kMachineValueWidth.Long,
					0);
				EmitAllocatedAddImmediate(
					M68kRegister.A2,
					M68kRuntimeAbi.ArrayDataOffset);
				EmitAllocatedSpanElementAddress(
					M68kRegister.A2,
					M68kRegister.D0,
					M68kRegister.A2,
					elementSize,
					M68kRegister.D3);
				EmitAllocatedBaseLoad(
					destination,
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					4);
				EmitAllocatedBaseLoad(
					destination,
					M68kRegister.A1,
					M68kMachineValueWidth.Long,
					0);
				EmitAllocatedAddImmediate(
					M68kRegister.A1,
					M68kRuntimeAbi.ArrayDataOffset);
				EmitAllocatedSpanElementAddress(
					M68kRegister.A1,
					M68kRegister.D0,
					M68kRegister.A1,
					elementSize,
					M68kRegister.D3);

				var returnsSuccess = name.Contains("try-copy", StringComparison.Ordinal);
				EmitAllocatedCopyKernel(
					elementSize,
					returnsSuccess ? Definition() : null);
				return;
			}
			if (name.StartsWith("intrinsic:span-from-memory:", StringComparison.Ordinal) ||
				name.StartsWith(
					"intrinsic:readonly-span-from-memory:",
					StringComparison.Ordinal))
			{
				var source = Use(0);
				var elementSize = GetAllocatedSpanElementSize(
					caller,
					target.Signature.ReturnType.GenericArguments[0]);
				EmitAllocatedStackLoad(M68kRegister.A1, 0);
				EmitAllocatedBaseLoad(
					source,
					M68kRegister.A2,
					M68kMachineValueWidth.Long,
					0);
				var nonNull = UniqueLabel("allocated_memory_span_nonnull");
				var complete = UniqueLabel("allocated_memory_span_complete");
				EmitAllocatedTest(M68kRegister.A2, M68kMachineValueWidth.Long);
				_assembler.EmitBranch(M68kCondition.NotEqual, nonNull);
				EmitAllocatedBaseClearLong(M68kRegister.A1, 0);
				EmitAllocatedBaseClearLong(M68kRegister.A1, 4);
				EmitAllocatedBaseClearLong(M68kRegister.A1, 8);
				_assembler.EmitBranch(M68kCondition.True, complete);
				_assembler.Mark(nonNull);
				EmitAllocatedBaseLoad(
					source,
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					4);
				EmitAllocatedAddImmediate(
					M68kRegister.A2,
					M68kRuntimeAbi.ArrayDataOffset);
				EmitAllocatedSpanElementAddress(
					M68kRegister.A2,
					M68kRegister.D0,
					M68kRegister.A2,
					elementSize,
					M68kRegister.D3);
				EmitAllocatedBaseStore(
					M68kRegister.A2,
					M68kRegister.A1,
					M68kMachineValueWidth.Long,
					0);
				EmitAllocatedBaseLoad(
					source,
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					8);
				EmitAllocatedBaseStore(
					M68kRegister.D0,
					M68kRegister.A1,
					M68kMachineValueWidth.Long,
					4);
				EmitAllocatedBaseLoad(
					source,
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					0);
				EmitAllocatedBaseStore(
					M68kRegister.D0,
					M68kRegister.A1,
					M68kMachineValueWidth.Long,
					8);
				_assembler.Mark(complete);
				return;
			}
			if (name.StartsWith(
					"intrinsic:span-from-array:",
					StringComparison.Ordinal) ||
				name.StartsWith(
					"intrinsic:readonly-span-from-array:",
					StringComparison.Ordinal))
			{
				var array = Use(0);
				EmitAllocatedStackLoad(M68kRegister.A1, 0);
				var nonNull = UniqueLabel("allocated_span_array_nonnull");
				var complete = UniqueLabel("allocated_span_array_complete");
				EmitAllocatedTest(array, M68kMachineValueWidth.Long);
				_assembler.EmitBranch(M68kCondition.NotEqual, nonNull);
				EmitAllocatedBaseClearLong(M68kRegister.A1, 0);
				EmitAllocatedBaseClearLong(M68kRegister.A1, 4);
				EmitAllocatedBaseClearLong(M68kRegister.A1, 8);
				_assembler.EmitBranch(M68kCondition.True, complete);
				_assembler.Mark(nonNull);
				EmitAllocatedMove(array, M68kRegister.D0, M68kMachineValueWidth.Long);
				EmitAllocatedAddImmediate(M68kRegister.D0, M68kRuntimeAbi.ArrayDataOffset);
				EmitAllocatedBaseStore(
					M68kRegister.D0,
					M68kRegister.A1,
					M68kMachineValueWidth.Long,
					0);
				EmitAllocatedBaseLoad(
					array,
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					8);
				EmitAllocatedBaseStore(
					M68kRegister.D0,
					M68kRegister.A1,
					M68kMachineValueWidth.Long,
					4);
				EmitAllocatedBaseStore(
					array,
					M68kRegister.A1,
					M68kMachineValueWidth.Long,
					8);
				_assembler.Mark(complete);
				return;
			}
			if (name.StartsWith(
					"intrinsic:span-from-pointer:",
					StringComparison.Ordinal))
			{
				var pointer = Use(0);
				var length = Use(1);
				RegisterRuntimeTypeDescriptor("System.ArgumentOutOfRangeException");
				var lengthValid = UniqueLabel(
					"allocated_span_pointer_length_valid");
				EmitAllocatedTest(length, M68kMachineValueWidth.Long);
				_assembler.EmitBranch(M68kCondition.Plus, lengthValid);
				EmitExceptionRaise(reason: 10, hasException: false);
				_assembler.Mark(lengthValid);

				EmitAllocatedStackLoad(M68kRegister.A1, 0);
				EmitAllocatedBaseStore(
					pointer,
					M68kRegister.A1,
					M68kMachineValueWidth.Long,
					0);
				EmitAllocatedBaseStore(
					length,
					M68kRegister.A1,
					M68kMachineValueWidth.Long,
					4);
				EmitAllocatedBaseClearLong(M68kRegister.A1, 8);
				return;
			}
			if (name.StartsWith(
					"intrinsic:span-from-ref:",
					StringComparison.Ordinal) ||
				name.StartsWith(
					"intrinsic:readonly-span-from-ref:",
					StringComparison.Ordinal))
			{
				var reference = Use(0);
				var hasOwner = instruction.Uses.Length == 2;
				if (hasOwner)
				{
					EmitAllocatedMove(
						Use(1),
						M68kRegister.D0,
						M68kMachineValueWidth.Long);
				}
				EmitAllocatedStackLoad(M68kRegister.A1, 0);
				EmitAllocatedBaseStore(
					reference,
					M68kRegister.A1,
					M68kMachineValueWidth.Long,
					0);
				EmitAllocatedImmediate(1, M68kRegister.D1);
				EmitAllocatedBaseStore(
					M68kRegister.D1,
					M68kRegister.A1,
					M68kMachineValueWidth.Long,
					4);
				if (hasOwner)
				{
					EmitAllocatedBaseStore(
						M68kRegister.D0,
						M68kRegister.A1,
						M68kMachineValueWidth.Long,
						8);
				}
				else
				{
					EmitAllocatedBaseClearLong(M68kRegister.A1, 8);
				}
				return;
			}
			if (name == "intrinsic:readonly-span-from-string")
			{
				var source = Use(0);
				EmitAllocatedStackLoad(M68kRegister.A1, 0);
				var nonNull = UniqueLabel("allocated_readonly_span_string_nonnull");
				var complete = UniqueLabel("allocated_readonly_span_string_complete");
				EmitAllocatedTest(source, M68kMachineValueWidth.Long);
				_assembler.EmitBranch(M68kCondition.NotEqual, nonNull);
				EmitAllocatedBaseClearLong(M68kRegister.A1, 0);
				EmitAllocatedBaseClearLong(M68kRegister.A1, 4);
				EmitAllocatedBaseClearLong(M68kRegister.A1, 8);
				_assembler.EmitBranch(M68kCondition.True, complete);
				_assembler.Mark(nonNull);
				EmitAllocatedMove(
					source,
					M68kRegister.D0,
					M68kMachineValueWidth.Long);
				EmitAllocatedAddImmediate(
					M68kRegister.D0,
					M68kRuntimeAbi.StringDataOffset);
				EmitAllocatedBaseStore(
					M68kRegister.D0,
					M68kRegister.A1,
					M68kMachineValueWidth.Long,
					0);
				EmitAllocatedBaseLoad(
					source,
					M68kRegister.D0,
					M68kMachineValueWidth.Long,
					M68kRuntimeAbi.StringLengthOffset);
				EmitAllocatedBaseStore(
					M68kRegister.D0,
					M68kRegister.A1,
					M68kMachineValueWidth.Long,
					4);
				EmitAllocatedBaseStore(
					source,
					M68kRegister.A1,
					M68kMachineValueWidth.Long,
					8);
				_assembler.Mark(complete);
				return;
			}
			if (name.StartsWith(
				"intrinsic:readonly-span-from-span:",
				StringComparison.Ordinal))
			{
				var source = Use(0);
				EmitAllocatedStackLoad(M68kRegister.A1, 0);
				for (short offset = 0; offset < 12; offset += 4)
				{
					EmitAllocatedBaseLoad(
						source,
						M68kRegister.D0,
						M68kMachineValueWidth.Long,
						offset);
					EmitAllocatedBaseStore(
						M68kRegister.D0,
						M68kRegister.A1,
						M68kMachineValueWidth.Long,
						offset);
				}
				return;
			}
			if (name.StartsWith(
					"intrinsic:span-slice-",
					StringComparison.Ordinal) ||
				name.StartsWith(
					"intrinsic:readonly-span-slice-",
					StringComparison.Ordinal))
			{
				var span = Use(0);
				var start = Use(1);
				var hasExplicitLength = name.Contains(
					"span-slice-range:",
					StringComparison.Ordinal);
				var requestedLength = hasExplicitLength ? Use(2) : M68kRegister.D1;
				var elementSize = GetAllocatedSpanElementSize(
					caller,
					target.Signature.ReturnType.GenericArguments[0]);
				if (span < M68kRegister.A0 ||
					start > M68kRegister.D7 ||
					requestedLength > M68kRegister.D7 ||
					elementSize <= 0)
				{
					throw new InvalidOperationException(
						"Allocated Span<T> slice has an invalid register or element shape.");
				}

				EmitAllocatedStackLoad(M68kRegister.A1, 0);
				EmitAllocatedBaseLoad(
					span,
					M68kRegister.D2,
					M68kMachineValueWidth.Long,
					4);
				var startValid = UniqueLabel("allocated_span_slice_start_valid");
				EmitAllocatedCompare(start, M68kRegister.D2, M68kMachineValueWidth.Long);
				_assembler.EmitBranch(M68kCondition.CarrySet, startValid);
				_assembler.EmitBranch(M68kCondition.Equal, startValid);
				RegisterRuntimeTypeDescriptor("System.ArgumentOutOfRangeException");
				EmitExceptionRaise(reason: 10, hasException: false);
				_assembler.Mark(startValid);
				EmitAllocatedBinaryInPlace(
					M68kMachineOperation.Subtract,
					start,
					M68kRegister.D2,
					M68kMachineValueWidth.Long);

				if (hasExplicitLength)
				{
					var lengthValid = UniqueLabel("allocated_span_slice_length_valid");
					EmitAllocatedCompare(
						requestedLength,
						M68kRegister.D2,
						M68kMachineValueWidth.Long);
					_assembler.EmitBranch(M68kCondition.CarrySet, lengthValid);
					_assembler.EmitBranch(M68kCondition.Equal, lengthValid);
					EmitExceptionRaise(reason: 10, hasException: false);
					_assembler.Mark(lengthValid);
					EmitAllocatedMove(
						requestedLength,
						M68kRegister.D2,
						M68kMachineValueWidth.Long);
				}

				EmitAllocatedBaseLoad(
					span,
					M68kRegister.A2,
					M68kMachineValueWidth.Long,
					0);
				EmitAllocatedSpanElementAddress(
					M68kRegister.A2,
					start,
					M68kRegister.A2,
					elementSize,
					M68kRegister.D3);
				EmitAllocatedBaseStore(
					M68kRegister.A2,
					M68kRegister.A1,
					M68kMachineValueWidth.Long,
					0);
				EmitAllocatedBaseStore(
					M68kRegister.D2,
					M68kRegister.A1,
					M68kMachineValueWidth.Long,
					4);
				EmitAllocatedBaseLoad(
					span,
					M68kRegister.D3,
					M68kMachineValueWidth.Long,
					8);
				EmitAllocatedBaseStore(
					M68kRegister.D3,
					M68kRegister.A1,
					M68kMachineValueWidth.Long,
					8);
				return;
			}
			if (name.StartsWith(
					"intrinsic:span-length:",
					StringComparison.Ordinal) ||
				name.StartsWith(
					"intrinsic:readonly-span-length:",
					StringComparison.Ordinal))
			{
				EmitAllocatedBaseLoad(
					Use(0),
					Definition(),
					M68kMachineValueWidth.Long,
					4);
				return;
			}
			if (name.StartsWith(
					"intrinsic:span-is-empty:",
					StringComparison.Ordinal) ||
				name.StartsWith(
					"intrinsic:readonly-span-is-empty:",
					StringComparison.Ordinal))
			{
				var materialize = instruction.Definitions.Length != 0;
				var destination = materialize
					? Definition()
					: M68kRegister.D0;
				EmitAllocatedBaseLoad(
					Use(0),
					destination,
					M68kMachineValueWidth.Long,
					4);
				EmitAllocatedTest(destination, M68kMachineValueWidth.Long);
				if (materialize)
				{
					EmitAllocatedConditionResult(
						M68kCondition.Equal,
						destination);
				}
				return;
			}
			if (name == "intrinsic:readonly-span-sequence-equal:char")
			{
				EmitAllocatedStackLoad(M68kRegister.A0, 0);
				EmitAllocatedStackLoad(M68kRegister.D2, 4);
				EmitAllocatedStackLoad(M68kRegister.A1, 12);
				EmitAllocatedStackLoad(M68kRegister.D1, 16);

				var matchingLength = UniqueLabel(
					"allocated_readonly_span_sequence_equal_matching_length");
				var loop = UniqueLabel(
					"allocated_readonly_span_sequence_equal_loop");
				var equal = UniqueLabel(
					"allocated_readonly_span_sequence_equal_true");
				var notEqual = UniqueLabel(
					"allocated_readonly_span_sequence_equal_false");
				var complete = UniqueLabel(
					"allocated_readonly_span_sequence_equal_complete");

				EmitAllocatedCompare(
					M68kRegister.D1,
					M68kRegister.D2,
					M68kMachineValueWidth.Long);
				_assembler.EmitBranch(M68kCondition.Equal, matchingLength);
				_assembler.EmitBranch(M68kCondition.True, notEqual);

				_assembler.Mark(matchingLength);
				EmitAllocatedTest(M68kRegister.D2, M68kMachineValueWidth.Long);
				_assembler.EmitBranch(M68kCondition.Equal, equal);
				_assembler.Mark(loop);
				EmitAllocatedBaseLoad(
					M68kRegister.A0,
					M68kRegister.D0,
					M68kMachineValueWidth.Word,
					0);
				EmitAllocatedBaseLoad(
					M68kRegister.A1,
					M68kRegister.D1,
					M68kMachineValueWidth.Word,
					0);
				EmitAllocatedCompare(
					M68kRegister.D1,
					M68kRegister.D0,
					M68kMachineValueWidth.Word);
				_assembler.EmitBranch(M68kCondition.NotEqual, notEqual);
				EmitAllocatedAddImmediate(M68kRegister.A0, 2);
				EmitAllocatedAddImmediate(M68kRegister.A1, 2);
				_assembler.EmitWord(0x5382); // SUBQ.L #1,D2
				_assembler.EmitBranch(M68kCondition.NotEqual, loop);

				_assembler.Mark(equal);
				EmitAllocatedImmediate(1, Definition());
				_assembler.EmitBranch(M68kCondition.True, complete);
				_assembler.Mark(notEqual);
				EmitAllocatedImmediate(0, Definition());
				_assembler.Mark(complete);
				return;
			}
			if (name.StartsWith(
					"intrinsic:span-copy-to:",
					StringComparison.Ordinal) ||
				name.StartsWith(
					"intrinsic:readonly-span-copy-to:",
					StringComparison.Ordinal))
			{
				var source = Use(0);
				var destination = Use(1);
				var element = target.ConstructedDeclaringType?.GenericArguments[0] ??
					target.Signature.ParameterTypes[0].GenericArguments[0];
				var elementSize = GetAllocatedSpanElementSize(caller, element);
				if (source < M68kRegister.A0 ||
					destination < M68kRegister.A0 ||
					elementSize <= 0)
				{
					throw new InvalidOperationException(
						"Allocated Span<T>.CopyTo has an invalid receiver or element shape.");
				}

				EmitAllocatedBaseLoad(
					source,
					M68kRegister.D2,
					M68kMachineValueWidth.Long,
					4);
				EmitAllocatedBaseLoad(
					destination,
					M68kRegister.D1,
					M68kMachineValueWidth.Long,
					4);
				EmitAllocatedBaseLoad(
					source,
					M68kRegister.A2,
					M68kMachineValueWidth.Long,
					0);
				EmitAllocatedBaseLoad(
					destination,
					M68kRegister.A1,
					M68kMachineValueWidth.Long,
					0);
				EmitAllocatedCopyKernel(elementSize);
				return;
			}
			if (name.StartsWith(
					"intrinsic:span-get-item:",
					StringComparison.Ordinal) ||
				name.StartsWith(
					"intrinsic:readonly-span-get-item:",
					StringComparison.Ordinal))
			{
				var span = Use(0);
				var index = Use(1);
				var destination = Definition();
				var elementSize = target.Signature.ReturnType.ElementType is { } element
					? GetAllocatedSpanElementSize(caller, element)
					: 0;
				if (span < M68kRegister.A0 || index > M68kRegister.D7 ||
					destination < M68kRegister.A0 || elementSize <= 0)
				{
					throw new InvalidOperationException(
						"Allocated Span<T> indexer has an invalid register or element shape.");
				}
				var indexNonNegative = UniqueLabel("allocated_span_index_nonnegative");
				var indexValid = UniqueLabel("allocated_span_index_valid");
				_assembler.EmitWord((ushort)(0x4A80 | (int)index));
				_assembler.EmitBranch(M68kCondition.Plus, indexNonNegative);
				EmitExceptionRaise(reason: 2, hasException: false);
				_assembler.Mark(indexNonNegative);
				_assembler.EmitWord((ushort)(
					0xB0A8 |
					((int)index << 9) |
					((int)span - (int)M68kRegister.A0)));
				_assembler.EmitWord(0x0004);
				_assembler.EmitBranch(M68kCondition.CarrySet, indexValid);
				EmitExceptionRaise(reason: 2, hasException: false);
				_assembler.Mark(indexValid);
				EmitAllocatedBaseLoad(
					span,
					destination,
					M68kMachineValueWidth.Long,
					0);
				EmitAllocatedSpanElementAddress(
					destination,
					index,
					destination,
					elementSize,
					M68kRegister.D1);
				return;
			}
		if (name.StartsWith(
			"intrinsic:runtimehelpers-is-reference-or-contains-references:",
			StringComparison.Ordinal))
		{
			EmitAllocatedImmediate(
				name.EndsWith(":true", StringComparison.Ordinal) ? 1 : 0,
				Definition());
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
			if (!target.Signature.Header.IsInstance)
			{
				// Static ToAddress receives an already materialized BPTR value.
				// Its allocated register bank does not describe indirection.
				EmitAllocatedMove(
					Use(0),
					destination,
					M68kMachineValueWidth.Long);
			}
			else if (TryGetAllocatedFrameBackedAddressDisplacement(
				caller,
				allocated,
				instruction,
				out var frameDisplacement))
			{
				EmitAllocatedFrameLoad(
					destination,
					M68kMachineValueWidth.Long,
					frameDisplacement);
			}
			else if (IsAllocatedPromotedLocalAddress(
				caller,
				allocated,
				instruction))
			{
				EmitAllocatedMove(
					Use(0),
					destination,
					M68kMachineValueWidth.Long);
			}
			else
			{
				EmitAllocatedBaseLoad(
					Use(0),
					destination,
					M68kMachineValueWidth.Long,
					0);
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
			var producerValue = allocated.Function.Values[definition];
			if (producerValue.Width is
					M68kMachineValueWidth.Byte or M68kMachineValueWidth.Word &&
				destination >= M68kRegister.A0)
			{
				// MOVEA.W sign-extends and MOVEA.B is illegal. Let the ordinary Copy
				// path normalize in a data register before a canonical long transfer.
				return false;
			}
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
				if (sourceInstruction is not { Operand: int token })
				{
					continue;
				}
				var sourceReference = consumer.Operation == M68kMachineOperation.Call
					? ResolveAllocatedMachineMethod(method, consumer)
					: _module.ResolveMethodToken(
						token,
						method,
						sourceInstruction.Offset);
				if (!IsFrameAddressIntrinsic(sourceReference.ImportName) ||
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
			if (definition is { Operation: M68kMachineOperation.Constant })
			{
				if (definition.ConstantValue is { } constantValue &&
					constantValue.TryGetIntegral(out var integral))
				{
					return integral == 0;
				}
				return definition.SourceInstruction is { } constant &&
					GetAllocatedIntConstant(constant) == 0;
			}
			if (definition is
				{
					Operation: M68kMachineOperation.Call,
					SourceInstruction: { Operand: int token } call
				})
			{
				return ResolveAllocatedMachineMethod(caller, definition).ImportName ==
					"intrinsic:aptr-null";
			}
			return false;
		}
		return false;
	}

	private static bool IsAllocatedIntrinsic(string? name) =>
		name?.StartsWith(
			"intrinsic:runtime-integral-equals:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:runtime-integral-hash:",
			StringComparison.Ordinal) == true ||
		name is "intrinsic:runtime-int64-split" or "intrinsic:runtime-int64-combine" ||
		name?.StartsWith(
			"intrinsic:runtime-floating-hash:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:runtime-floating-equals:",
			StringComparison.Ordinal) == true ||
		name == "intrinsic:runtime-string-hash" ||
		name == "intrinsic:runtime-nullable-integral-hash:32" ||
		name?.StartsWith(
			"intrinsic:memory-",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:readonly-memory-",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:span-from-memory:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:readonly-span-from-memory:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:span-from-array:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:span-from-pointer:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:span-from-ref:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:readonly-span-from-",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:span-length:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:readonly-span-length:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:span-is-empty:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:readonly-span-is-empty:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:readonly-span-sequence-equal:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:span-copy-to:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:readonly-span-copy-to:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:span-slice-",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:readonly-span-slice-",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:span-get-item:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:readonly-span-get-item:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:runtimehelpers-is-reference-or-contains-references:",
			StringComparison.Ordinal) == true ||
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
			"intrinsic:nullable-get-value-or-default-no-argument:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:nullable-get-value-or-default:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:nullable-ctor:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:file-info-block-read-int32:",
			StringComparison.Ordinal) == true ||

		name is
			"intrinsic:runtime-allocate-string" or
			"intrinsic:runtime-set-string-char" or
			"intrinsic:string-concat-two" or
			"intrinsic:string-substring" or
			"intrinsic:string-copy-to-char-array" or
			"intrinsic:string-copy-to-span-char" or
			"intrinsic:string-to-char-array" or
			"intrinsic:string-starts-with-ordinal" or
			"intrinsic:string-ends-with-ordinal" or
			"intrinsic:string-contains-ordinal" or
			"intrinsic:string-index-of-ordinal" or
			"intrinsic:object-ctor" or
			"intrinsic:object-reference-equals" or
			"intrinsic:string-equality" or
			"intrinsic:string-inequality" or
			"intrinsic:delegate-combine" or
			"intrinsic:delegate-remove" or
			"intrinsic:delegate-invoke" or
			"intrinsic:delegate-equality" or
			"intrinsic:delegate-inequality" or
			"intrinsic:runtime-bitcast-32" or
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
			"intrinsic:aptr-read-uint8" or
			"intrinsic:aptr-read-uint16" or
			"intrinsic:aptr-read-uint32" or
			"intrinsic:aptr-write-uint8" or
			"intrinsic:aptr-write-uint16" or
			"intrinsic:aptr-write-uint32" or
			"intrinsic:aptr-raw" or
			"intrinsic:boopsi-instance-data" or
			"intrinsic:boopsi-do-method" or
			"intrinsic:boopsi-do-method-stack-varargs" or
			"intrinsic:aptr-null" or
			"intrinsic:string-char" or
			"intrinsic:string-length" or
			"intrinsic:file-info-block-file-name" or
			"intrinsic:bptr-address" or
			"intrinsic:bptr-from-address" or
			"intrinsic:aptr-is-null" or
			"intrinsic:aptr-is-not-null" or
			"intrinsic:dictionary-key-is-null:false" or
			"intrinsic:dictionary-key-is-null:reference" or
			"intrinsic:runtime-dispose" or
			"intrinsic:list-enumerator-dispose" or
			"intrinsic:runtime-throw-overflow" or
			"intrinsic:runtime-throw-arithmetic" or
			"intrinsic:runtime-throw-format" or
			"intrinsic:runtime-throw-argument" or
			"intrinsic:runtime-throw-argument-null" or
			"intrinsic:runtime-throw-argument-out-of-range" or
			"intrinsic:runtime-throw-invalid-operation" or
			"intrinsic:runtime-throw-io" or
			"intrinsic:runtime-throw-file-not-found" or
			"intrinsic:runtime-throw-directory-not-found" or
			"intrinsic:runtime-throw-unauthorized-access" or
			"intrinsic:runtime-throw-key-not-found" or
			"intrinsic:runtime-throw-out-of-memory" or
			"intrinsic:runtime-invoke-finalizer" or
			"intrinsic:runtime-gc-suppress-finalize" or
			"intrinsic:runtime-gc-reregister-finalize" or
			"intrinsic:object-finalize" or
			"intrinsic:runtime-gc-wait-finalizers" or
			"intrinsic:runtime-gc-keep-alive" or
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

	private void EmitAllocatedLiteralAddress(
		CilMethod caller,
		M68kMachineInstruction instruction,
		M68kRegister destination)
	{
		var literal = AllocatedPrecedingStringLiteral(caller, instruction);
		var identity = new CilUserStringIdentity(caller.ModuleName, literal);
		_cStringLiterals.TryAdd(
			identity,
			_module.GetUserString(literal, caller, instruction.IlOffset));
		EmitAllocatedAddress(CStringLabel(identity), destination);
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

	private void EmitAllocatedQuickImmediate(
		M68kRegister register,
		int delta,
		M68kMachineValueWidth width)
	{
		if (delta is 0 or < -8 or > 8)
		{
			throw new ArgumentOutOfRangeException(nameof(delta));
		}
		if (register >= M68kRegister.A0 && width != M68kMachineValueWidth.Long)
		{
			throw new InvalidOperationException(
				$"Quick {width} arithmetic cannot target {register}.");
		}
		var count = Math.Abs(delta);
		var encodedCount = count == 8 ? 0 : count;
		var baseOpcode = delta < 0 ? 0x5100 : 0x5000;
		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(
				baseOpcode |
				(encodedCount << 9) |
				AllocatedSizeBits(width) |
				(int)register));
			return;
		}
		_assembler.EmitWord((ushort)(
			baseOpcode |
			(encodedCount << 9) |
			0x88 |
			((int)register - (int)M68kRegister.A0)));
	}

	private void EmitAllocatedShiftImmediate(
		M68kRegister register,
		bool left,
		int count = 1)
	{
		if (register > M68kRegister.D7 || count is < 1 or > 8)
		{
			throw new InvalidOperationException(
				"Allocated shift requires a data register.");
		}
		_assembler.EmitWord((ushort)(
			(left ? 0xE188 : 0xE088) |
			((count & 7) << 9) |
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
		CilMethod caller,
		M68kMachineInstruction instruction,
		M68kAllocatedLocation destination)
	{
		if (destination.IsPair)
		{
			var value = instruction.ConstantValue is { } pairConstant
				? unchecked((long)pairConstant.Bits)
				: instruction.SourceInstruction is { } pairSource
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
		var scalar = instruction.ConstantValue is { } scalarConstant
			? unchecked((int)(uint)scalarConstant.Bits)
			: instruction.SourceInstruction is { } source
				? GetAllocatedScalarConstant(caller, source)
				: 0;
		EmitAllocatedImmediate(scalar, destination.Register);
	}

	private int GetAllocatedScalarConstant(CilMethod caller, CilInstruction instruction)
	{
		if ((instruction.OpCode == OpCodes.Call ||
			 instruction.OpCode == OpCodes.Callvirt) &&
			instruction.Operand is int token &&
			_module.ResolveMethodToken(token, caller, instruction.Offset).ImportName ==
				"intrinsic:aptr-null")
		{
			return 0;
		}

		return GetAllocatedIntConstant(instruction);
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
		if (operation == M68kMachineOperation.Add &&
			width == M68kMachineValueWidth.Long &&
			left >= M68kRegister.A0 &&
			right == destination &&
			destination < M68kRegister.A0)
		{
			// Address-register eligibility can place the left input in An while
			// coalescing the right input with a data-register result. Preserve An
			// and the original right operand with EXG/ADDA/EXG; MOVE An,Dn would
			// otherwise destroy the right operand before the addition.
			var dataIndex = (int)destination;
			var addressIndex = (int)left - (int)M68kRegister.A0;
			var exchange = (ushort)(0xC188 | (dataIndex << 9) | addressIndex);
			_assembler.EmitWord(exchange); // EXG Dd,An
			_assembler.EmitWord((ushort)(
				0xD1C0 | (addressIndex << 9) | dataIndex)); // ADDA.L Dd,An
			_assembler.EmitWord(exchange); // EXG Dd,An
			return;
		}
		if (destination == right && commutative)
		{
			(left, right) = (right, left);
		}
		if (width == M68kMachineValueWidth.LongPair)
		{
			if (operation is not (M68kMachineOperation.Add or M68kMachineOperation.Subtract) ||
				left > M68kRegister.D6 || right > M68kRegister.D6 || destination > M68kRegister.D6)
			{
				throw new InvalidOperationException(
					$"{operation} cannot use the allocated 64-bit register pair.");
			}
			if (destination != left)
			{
				EmitAllocatedMove(left, destination, M68kMachineValueWidth.LongPair);
			}
			var rightLow = (M68kRegister)((int)right + 1);
			var destinationLow = (M68kRegister)((int)destination + 1);
			EmitAllocatedBinaryInPlace(
				operation,
				rightLow,
				destinationLow,
				M68kMachineValueWidth.Long);
			var extendOpcode = operation == M68kMachineOperation.Add ? 0xD180 : 0x9180;
			_assembler.EmitWord((ushort)(
				extendOpcode |
				((int)destination << 9) |
				(int)right)); // ADDX.L/SUBX.L right-high,destination-high
			return;
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
		M68kMachineValueWidth sourceWidth,
		M68kRegister destination,
		M68kMachineValueWidth destinationWidth,
		bool normalize = true)
	{
		if (destinationWidth == M68kMachineValueWidth.LongPair)
		{
			if (sourceWidth == M68kMachineValueWidth.LongPair)
			{
				EmitAllocatedMove(source, destination, M68kMachineValueWidth.LongPair);
				return;
			}

			var lowDestination = (M68kRegister)((int)destination + 1);
			EmitAllocatedMove(source, lowDestination, M68kMachineValueWidth.Long);
			if (op == OpCodes.Conv_U8)
			{
				EmitAllocatedImmediate(0, destination);
				return;
			}
			if (op == OpCodes.Conv_I8)
			{
				EmitAllocatedImmediate(0, destination);
				EmitAllocatedTest(lowDestination, M68kMachineValueWidth.Long);
				var nonnegative = UniqueLabel("conv-i8-nonnegative");
				_assembler.EmitBranch(M68kCondition.Plus, nonnegative);
				EmitAllocatedImmediate(-1, destination);
				_assembler.Mark(nonnegative);
				return;
			}

			throw new InvalidOperationException(
				$"Conversion {op.Name} cannot produce a register pair.");
		}
		if (sourceWidth == M68kMachineValueWidth.LongPair &&
			destinationWidth != M68kMachineValueWidth.LongPair)
		{
			source = (M68kRegister)((int)source + 1);
		}
		EmitAllocatedMove(source, destination, M68kMachineValueWidth.Long);
		if (destination > M68kRegister.D7)
		{
			throw new InvalidOperationException(
				$"Conversion {op.Name} cannot target {destination}.");
		}
		var register = (int)destination;
		if (op == OpCodes.Conv_I1 && normalize)
		{
			EmitAllocatedSignExtendByte(destination);
		}
		else if (op == OpCodes.Conv_Ovf_I4_Un)
		{
			var inRange = UniqueLabel("checked-uint32-to-int32-in-range");
			EmitAllocatedTest(destination, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Plus, inRange);
			EmitExceptionRaise(reason: 4, hasException: false);
			_assembler.Mark(inRange);
		}
		else if (op == OpCodes.Conv_U1 && normalize)
		{
			_assembler.EmitWord((ushort)(0x0280 | register)); // ANDI.L #$FF,Dn
			_assembler.EmitLong(0xFF);
		}
		else if (op == OpCodes.Conv_I2 && normalize)
		{
			_assembler.EmitWord((ushort)(0x48C0 | register)); // EXT.L Dn
		}
		else if (op == OpCodes.Conv_U2 && normalize)
		{
			_assembler.EmitWord((ushort)(0x0280 | register)); // ANDI.L #$FFFF,Dn
			_assembler.EmitLong(0xFFFF);
		}
	}

	private void EmitAllocatedDefinitionNormalization(
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction,
		M68kRegister register,
		CilStackValueKind? kind = null)
	{
		if (_allocatedDeferredNormalizations.Contains(instruction.Id))
		{
			return;
		}

		EmitAllocatedNormalize(
			register,
			kind ?? allocated.Function.Values[instruction.Definitions.Single()].Kind);
	}

	private void EmitAllocatedNormalize(
		M68kRegister register,
		CilStackValueKind kind)
	{
		if (kind is CilStackValueKind.Int32 or
			CilStackValueKind.Int64 or
			CilStackValueKind.Float32 or
			CilStackValueKind.Reference or
			CilStackValueKind.ManagedPointer or
			CilStackValueKind.AggregateAddress)
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

	private bool TryEmitAllocatedZeroExtendedStackArgument(
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction,
		M68kRegister source,
		CilStackValueKind kind)
	{
		if (source > M68kRegister.D7 ||
			kind is not
				CilStackValueKind.BooleanByte and not
				CilStackValueKind.UnsignedByte and not
				CilStackValueKind.UnsignedWord ||
			!allocated.InstructionLiveness.LiveAfter.TryGetValue(
				instruction.Id,
				out var liveValues))
		{
			return false;
		}
		if (liveValues.Contains(instruction.Uses[0]))
		{
			return false;
		}

		var occupied = 0;
		foreach (var liveValue in liveValues)
		{
			if (!allocated.Allocation.Registers.TryGetValue(
					liveValue,
					out var location) ||
				location.Register > M68kRegister.D7)
			{
				continue;
			}
			occupied |= 1 << (int)location.Register;
			if (location.IsPair)
			{
				occupied |= 1 << ((int)location.Register + 1);
			}
		}

		var scratch = new[]
			{
				M68kRegister.D1,
				M68kRegister.D0,
				M68kRegister.D2,
				M68kRegister.D3,
				M68kRegister.D4,
				M68kRegister.D5,
				M68kRegister.D6,
				M68kRegister.D7
			}
			.FirstOrDefault(candidate =>
				candidate != source &&
				(candidate <= M68kRegister.D1 ||
				 allocated.Frame.CalleeSavedRegisters.Contains(candidate)) &&
				(occupied & (1 << (int)candidate)) == 0,
				(M68kRegister)(-1));
		if ((int)scratch < 0)
		{
			return false;
		}

		_assembler.EmitWord((ushort)(0x7000 | ((int)scratch << 9))); // MOVEQ #0,Dscratch
		var moveSize = kind is
			CilStackValueKind.BooleanByte or
			CilStackValueKind.UnsignedByte
			? 0x1000
			: 0x3000;
		_assembler.EmitWord((ushort)(
			moveSize |
			((int)scratch << 9) |
			(int)source)); // MOVE.B/W Dsource,Dscratch
		EmitAllocatedPush(scratch);
		return true;
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

	private void EmitAllocatedInt64Comparison(
		OpCode op,
		M68kRegister left,
		M68kRegister right,
		M68kRegister destination)
	{
		var isTrue = UniqueLabel("compare-i64-true");
		var isFalse = UniqueLabel("compare-i64-false");
		var complete = UniqueLabel("compare-i64-complete");

		EmitAllocatedCompare(left, right, M68kMachineValueWidth.Long);
		if (op == OpCodes.Ceq)
		{
			_assembler.EmitBranch(M68kCondition.NotEqual, isFalse);
			EmitAllocatedCompare(
				(M68kRegister)((int)left + 1),
				(M68kRegister)((int)right + 1),
				M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, isTrue);
			_assembler.EmitBranch(M68kCondition.True, isFalse);
		}
		else if (op == OpCodes.Clt || op == OpCodes.Clt_Un)
		{
			_assembler.EmitBranch(
				op == OpCodes.Clt ? M68kCondition.LessThan : M68kCondition.CarrySet,
				isTrue);
			_assembler.EmitBranch(
				op == OpCodes.Clt ? M68kCondition.GreaterThan : M68kCondition.Higher,
				isFalse);
			EmitAllocatedCompare(
				(M68kRegister)((int)left + 1),
				(M68kRegister)((int)right + 1),
				M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.CarrySet, isTrue);
			_assembler.EmitBranch(M68kCondition.True, isFalse);
		}
		else if (op == OpCodes.Cgt || op == OpCodes.Cgt_Un)
		{
			_assembler.EmitBranch(
				op == OpCodes.Cgt ? M68kCondition.GreaterThan : M68kCondition.Higher,
				isTrue);
			_assembler.EmitBranch(
				op == OpCodes.Cgt ? M68kCondition.LessThan : M68kCondition.CarrySet,
				isFalse);
			EmitAllocatedCompare(
				(M68kRegister)((int)left + 1),
				(M68kRegister)((int)right + 1),
				M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Higher, isTrue);
			_assembler.EmitBranch(M68kCondition.True, isFalse);
		}
		else
		{
			throw new InvalidOperationException($"Unsupported Int64 comparison {op.Name}.");
		}

		_assembler.Mark(isTrue);
		EmitAllocatedImmediate(1, destination);
		_assembler.EmitBranch(M68kCondition.True, complete);
		_assembler.Mark(isFalse);
		EmitAllocatedImmediate(0, destination);
		_assembler.Mark(complete);
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
					var producer = branchCondition.ProducerInstruction!;
					EmitAllocatedCall(
						method,
						allocated,
						instruction with
						{
							Operation = M68kMachineOperation.Call,
							IlOffset = producer.Offset,
							SourceInstruction = producer,
							Origin = instruction.Origin is { } origin
								? M68kMachineInstructionOrigin.Create(
									origin.SourceMethod, producer)
								: null,
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

	private void EmitAllocatedSwitch(
		CilMethod method,
		M68kAllocatedFunction allocated,
		M68kMachineBlock block,
		M68kMachineInstruction instruction)
	{
		if (_allocatedFunctionAddressSwitches.TryGetValue(
			instruction.Id,
			out var functionAddressPlan))
		{
			EmitAllocatedFunctionAddressSwitch(
				method,
				allocated,
				instruction,
				functionAddressPlan);
			return;
		}

		if (instruction.SourceInstruction is not
			{
				OpCode: var op,
				Operand: int[] targets
			} source || op != OpCodes.Switch || instruction.Uses.Length != 1)
		{
			throw new InvalidOperationException(
				$"Switch in '{method.DisplayName}' has an invalid machine-IR shape.");
		}

		var blocksById = allocated.Function.Blocks.ToDictionary(
			static candidate => candidate.Id);
		int SuccessorAtOffset(int offset)
		{
			var matches = block.Successors
				.Where(successor => blocksById[successor].StartIlOffset == offset)
				.ToArray();
			if (matches.Length != 1)
			{
				throw new InvalidOperationException(
					$"Switch in '{method.DisplayName}' resolves IL_{offset:X4} " +
					$"to {matches.Length} successor blocks.");
			}
			return matches[0];
		}
		var originalTargets = targets
			.Select(SuccessorAtOffset)
			.ToArray();
		var fallthrough = SuccessorAtOffset(source.NextOffset);

		var distinctTargets = originalTargets
			.Append(fallthrough)
			.Distinct()
			.ToArray();
		var edgeLabels = distinctTargets.ToDictionary(
			static target => target,
			_ => UniqueLabel("allocated-switch-edge"));
		var selector = allocated.Allocation.Registers[instruction.Uses[0]].Register;
		if (!TryEmitAllocatedSwitchAddressTable(allocated, instruction, selector,
			originalTargets, fallthrough, edgeLabels))
		{
			for (var index = 0; index < originalTargets.Length; index++)
			{
				// A CIL switch encodes sparse holes as explicit entries targeting the
				// fallthrough block. Comparing those indices is redundant: matching
				// and not matching both select the same eventual default edge.
				if (originalTargets[index] == fallthrough)
				{
					continue;
				}
				EmitCompareImmediateWithRegister(selector, index);
				_assembler.EmitBranch(
					M68kCondition.Equal,
					edgeLabels[originalTargets[index]]);
			}
			_assembler.EmitBranch(M68kCondition.True, edgeLabels[fallthrough]);
		}

		foreach (var originalTarget in distinctTargets)
		{
			_assembler.Mark(edgeLabels[originalTarget]);
			EmitAllocatedEdgeCopies(allocated, block.Id, originalTarget);
			_assembler.EmitBranch(
				M68kCondition.True,
				AllocatedBlockLabel(
					method,
					allocated.FinalDestinations.Resolve(originalTarget)));
		}
	}

	private void PrepareAllocatedFunctionAddressSwitches(
		CilMethod method,
		M68kAllocatedFunction allocated)
	{
		if (!IsInternalAddressReturn(method.Signature.ReturnType))
			return;

		var blocksById = allocated.Function.Blocks.ToDictionary(
			static block => block.Id);
		foreach (var sourceBlock in allocated.Function.Blocks)
		{
			foreach (var instruction in sourceBlock.Instructions)
			{
				if (instruction.Operation != M68kMachineOperation.Switch ||
					instruction.SourceInstruction is not
					{
						OpCode: var op,
						Operand: int[] cilTargets
					} source ||
					op != OpCodes.Switch ||
					cilTargets.Length is < 8 or > 8192 ||
					instruction.Uses.Length != 1 ||
					allocated.Allocation.Registers[instruction.Uses[0]].Register is
						> M68kRegister.D7 ||
					!allocated.InstructionLiveness.LiveAfter.TryGetValue(
						instruction.Id,
						out var liveValues) ||
					liveValues.Contains(instruction.Uses[0]))
				{
					continue;
				}

				if (!TryCreateAllocatedFunctionAddressSwitchPlan(
						method,
						allocated,
						blocksById,
						sourceBlock,
						instruction,
						source,
						cilTargets,
						out var plan,
						out var suppressedInstructions))
				{
					continue;
				}

				_allocatedFunctionAddressSwitches.Add(instruction.Id, plan);
				foreach (var suppressed in suppressedInstructions)
					_allocatedSuppressedInstructions.Add(suppressed);
			}
		}
	}

	private bool TryCreateAllocatedFunctionAddressSwitchPlan(
		CilMethod method,
		M68kAllocatedFunction allocated,
		IReadOnlyDictionary<int, M68kMachineBlock> blocksById,
		M68kMachineBlock sourceBlock,
		M68kMachineInstruction switchInstruction,
		CilInstruction source,
		IReadOnlyList<int> cilTargets,
		out AllocatedFunctionAddressSwitchPlan plan,
		out IReadOnlyList<int> suppressedInstructions)
	{
		plan = null!;
		suppressedInstructions = Array.Empty<int>();

		bool TrySuccessorAtOffset(int offset, out int successor)
		{
			var matches = sourceBlock.Successors
				.Where(candidate => blocksById[candidate].StartIlOffset == offset)
				.ToArray();
			if (matches.Length == 1)
			{
				successor = matches[0];
				return true;
			}
			successor = default;
			return false;
		}

		var originalTargets = new int[cilTargets.Count];
		for (var index = 0; index < cilTargets.Count; index++)
		{
			if (!TrySuccessorAtOffset(cilTargets[index], out originalTargets[index]))
				return false;
		}
		if (!TrySuccessorAtOffset(source.NextOffset, out var originalFallthrough))
			return false;

		var originalDestinations = originalTargets
			.Append(originalFallthrough)
			.Distinct()
			.ToArray();
		if (originalDestinations.Any(target =>
			allocated.ParallelCopies.EdgeCopies.ContainsKey((sourceBlock.Id, target))))
		{
			return false;
		}

		var resolvedDestinations = originalDestinations
			.Select(allocated.FinalDestinations.Resolve)
			.Distinct()
			.ToArray();
		var permittedPredecessors = new HashSet<int>(originalDestinations)
		{
			sourceBlock.Id
		};
		foreach (var destination in resolvedDestinations)
		{
			if (allocated.FinalDestinations.AliasesByDestination.TryGetValue(
				destination,
				out var aliases))
			{
				permittedPredecessors.UnionWith(aliases);
			}
		}

		var targetsByDestination =
			new Dictionary<int, AllocatedFunctionAddressTarget>();
		var instructionsToSuppress = new HashSet<int>();
		foreach (var destination in resolvedDestinations)
		{
			var targetBlock = blocksById[destination];
			if (targetBlock.ControlFlowPredecessors.Any(
					predecessor => !permittedPredecessors.Contains(predecessor)) ||
				!TryResolvePureFunctionAddressReturn(
					method,
					allocated,
					targetBlock,
					out var target,
					out _,
					out var targetInstructions))
			{
				return false;
			}

			targetsByDestination.Add(destination, target);
			instructionsToSuppress.UnionWith(targetInstructions);
		}
		var fallthroughTarget = targetsByDestination[
			allocated.FinalDestinations.Resolve(originalFallthrough)];
		// The fast default path materializes its address directly rather than
		// loading the table. Keep that path unchanged unless it has no tag.
		if (fallthroughTarget.Addend != 0)
			return false;

		var selector = allocated.Allocation.Registers[
			switchInstruction.Uses[0]].Register;
		// Every arm is replaced by the table's own return path. Its temporary
		// need not match the register independently allocated to each old arm
		// (tagged and untagged pointers can differ after identity-call folding).
		// The selector is already proven dead after this switch and can hold
		// the loaded address once indexed addressing has consumed its index.
		plan = new AllocatedFunctionAddressSwitchPlan(
			UniqueLabel("allocated-switch-function-address-table"),
			originalTargets.Select(target =>
				targetsByDestination[allocated.FinalDestinations.Resolve(target)])
				.ToArray(),
			fallthroughTarget.Label,
			selector,
			selector);
		suppressedInstructions = instructionsToSuppress.ToArray();
		return true;
	}

	private bool TryResolvePureFunctionAddressReturn(
		CilMethod method,
		M68kAllocatedFunction allocated,
		M68kMachineBlock block,
		out AllocatedFunctionAddressTarget target,
		out M68kRegister valueRegister,
		out IReadOnlyList<int> instructionIds)
	{
		target = default;
		valueRegister = default;
		instructionIds = Array.Empty<int>();
		if (block.IsExceptionEntry || block.Phis.Count != 0 ||
			block.Successors.Count != 0 || block.SuccessorEdges.Count != 0 ||
			block.Instructions.Count < 2 ||
			block.Instructions[^1] is not
			{
				Operation: M68kMachineOperation.Return,
				Uses: [var returnValue]
			})
		{
			return false;
		}

		var producers = new Dictionary<int, M68kMachineInstruction>();
		foreach (var instruction in block.Instructions)
		{
			foreach (var definition in instruction.Definitions)
			{
				if (!producers.TryAdd(definition, instruction))
					return false;
			}
		}
		var chain = new HashSet<int> { block.Instructions[^1].Id };

		bool TryResolveValue(
			int value,
			out AllocatedFunctionAddressTarget resolved)
		{
			resolved = default;
			if (!producers.TryGetValue(value, out var producer) ||
				producer.Definitions is not [_])
			{
				return false;
			}
			chain.Add(producer.Id);
			if (producer.Operation == M68kMachineOperation.FunctionAddress)
			{
				var sourceMethod = producer.Origin?.SourceMethod ?? method;
				var functionSource = producer.Origin?.SourceInstruction ??
					producer.SourceInstruction;
				if (functionSource is not { OpCode: var op, Operand: int token } ||
					op != OpCodes.Ldftn || producer.Uses.Length != 0)
				{
					return false;
				}
				var methodTarget = _module.ResolveMethodToken(
					token,
					sourceMethod,
					functionSource.Offset).Definition;
				if (methodTarget is null || methodTarget.IsImport)
					return false;

				resolved = new AllocatedFunctionAddressTarget(
					MethodLabel(methodTarget));
				return true;
			}

			if (producer.Uses is [var sourceValue] &&
				allocated.Function.Values[sourceValue].Width ==
					M68kMachineValueWidth.Long &&
				allocated.Function.Values[value].Width ==
					M68kMachineValueWidth.Long &&
				IsTransparentFunctionAddressTransport(method, producer))
			{
				return TryResolveValue(sourceValue, out resolved);
			}

			if (producer.Operation is not
				(M68kMachineOperation.Add or M68kMachineOperation.Or) ||
				producer.Uses is not [var left, var right] ||
				(producer.Operation == M68kMachineOperation.Add &&
				 producer.SourceInstruction?.OpCode != OpCodes.Add))
			{
				return false;
			}

			bool IsOne(int candidate)
			{
				if (!producers.TryGetValue(candidate, out var constant) ||
					constant.Operation != M68kMachineOperation.Constant ||
					constant.ConstantValue is not { } valueConstant ||
					!valueConstant.TryGetIntegral(out var integral) ||
					integral != 1)
				{
					return false;
				}
				chain.Add(constant.Id);
				return true;
			}

			var addressValue = IsOne(right)
				? left
				: IsOne(left)
					? right
					: -1;
			if (addressValue < 0 ||
				!TryResolveValue(addressValue, out var address) ||
				address.Addend != 0)
			{
				return false;
			}
			resolved = address with { Addend = 1 };
			return true;
		}

		if (!TryResolveValue(returnValue, out target) ||
			chain.Count != block.Instructions.Count)
		{
			return false;
		}
		valueRegister = allocated.Allocation.Registers[returnValue].Register;
		if (valueRegister > M68kRegister.D7)
			return false;
		instructionIds = chain.ToArray();
		return true;
	}

	private bool IsTransparentFunctionAddressTransport(
		CilMethod method,
		M68kMachineInstruction instruction)
	{
		if (instruction.Operation is
			M68kMachineOperation.Copy or M68kMachineOperation.Convert)
		{
			return true;
		}
		if (instruction.Operation != M68kMachineOperation.Call)
			return false;

		var target = ResolveAllocatedMachineMethod(method, instruction);
		return target.ImportName is
			"intrinsic:aptr-from-pointer" or
			"intrinsic:aptr-to-uint32";
	}

	private void EmitAllocatedFunctionAddressSwitch(
		CilMethod method,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction,
		AllocatedFunctionAddressSwitchPlan plan)
	{
		if (!allocated.InstructionLiveness.LiveAfter.TryGetValue(
			instruction.Id,
			out var liveValues))
		{
			throw new InvalidOperationException(
				"Function-address switch has no liveness information.");
		}

		var occupied = 1u << (int)M68kRegister.A0;
		foreach (var value in liveValues)
		{
			if (!allocated.Allocation.Registers.TryGetValue(value, out var location))
				continue;
			occupied |= 1u << (int)location.Register;
			if (location.IsPair)
				occupied |= 1u << ((int)location.Register + 1);
		}
		var scratch = new[]
			{
				M68kRegister.A1, M68kRegister.A2, M68kRegister.A3,
				M68kRegister.A4, M68kRegister.A5, M68kRegister.A6,
			}
			.FirstOrDefault(candidate =>
				!allocated.Function.ReservedRegisters.Contains(candidate) &&
				(candidate == M68kRegister.A1 ||
				 allocated.Frame.CalleeSavedRegisters.Contains(candidate)) &&
				(occupied & (1u << (int)candidate)) == 0,
				(M68kRegister)(-1));
		if ((int)scratch < 0)
		{
			throw new InvalidOperationException(
				"Prepared function-address switch has no address scratch register.");
		}

		var fallback = UniqueLabel("allocated-switch-function-address-default");
		var complete = UniqueLabel("allocated-switch-function-address-complete");
		var selector = (int)plan.Selector;
		var destination = (int)plan.ValueRegister;
		var address = (int)scratch - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x4A80 | selector)); // TST.L Dselector
		_assembler.EmitBranch(M68kCondition.Minus, fallback);
		_assembler.EmitWord((ushort)(0x0C80 | selector)); // CMPI.L #count,Dselector
		_assembler.EmitLong((uint)plan.Targets.Count);
		_assembler.EmitBranch(M68kCondition.CarryClear, fallback);
		_assembler.EmitWord((ushort)(0xD080 | (selector << 9) | selector));
		_assembler.EmitWord((ushort)(0xD080 | (selector << 9) | selector));
		_assembler.EmitWord((ushort)(0x41F9 | (address << 9))); // LEA table,Ascratch
		_assembler.EmitAddress(plan.TableLabel);
		_assembler.EmitWord((ushort)(0x2030 | (destination << 9) | address));
		_assembler.EmitWord((ushort)(selector << 12)); // MOVE.L (Ascratch,Dselector.W),Dvalue
		_assembler.EmitBranch(M68kCondition.True, complete);
		_assembler.Mark(fallback);
		EmitAllocatedAddress(plan.FallthroughTarget, plan.ValueRegister);
		_assembler.Mark(complete);
		EmitAllocatedMove(
			plan.ValueRegister,
			M68kRegister.A0,
			M68kMachineValueWidth.Long);
		EmitAllocatedFrameTeardown(method, allocated);
		EmitAllocatedReturn(method);
		_switchAddressTables.Add(new SwitchAddressTable(
			plan.TableLabel,
			plan.Targets.Select(static target => target.Label).ToArray(),
			plan.Targets.Select(static target => target.Addend).ToArray()));
	}

	private void EmitAllocatedReturn(CilMethod method)
	{
		const ushort calleeSavedData = 0x00FC; // D2-D7
		const ushort calleeSavedAddress = 0x00FC; // A2-A7
		var usesData = calleeSavedData;
		var usesAddress = calleeSavedAddress;
		var returnType = method.Signature.ReturnType;
		if (GetInternalCallAbi(method).ReturnBufferStackOffset is null &&
			!returnType.IsVoid)
		{
			if (IsInternalAddressReturn(returnType))
			{
				usesAddress |= 0x0001; // A0
			}
			else
			{
				usesData |= 1 << (int)M68kRegister.D0;
				if (returnType.IsSupportedScalar && returnType.Size == 8)
				{
					usesData |= 1 << (int)M68kRegister.D1;
				}
			}
		}
		var offset = _assembler.Offset;
		_assembler.EmitWord(0x4E75); // RTS
		_assembler.SetInstructionEffects(
			offset,
			new M68kInstructionEffects(
				usesData,
				0,
				usesAddress,
				0x0080,
				M68kConditionCodeSet.None,
				M68kConditionCodeSet.None,
				M68kMemorySet.Stack,
				M68kMemorySet.None,
				4,
				true,
				false));
	}

	private bool TryEmitAllocatedSwitchAddressTable(
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction,
		M68kRegister selector,
		IReadOnlyList<int> targets,
		int fallthrough,
		IReadOnlyDictionary<int, string> edgeLabels)
	{
		if (targets.Count is < 8 or > 8192 || selector > M68kRegister.D7 ||
			!allocated.InstructionLiveness.LiveAfter.TryGetValue(instruction.Id,
				out var liveValues) || liveValues.Contains(instruction.Uses[0]))
		{
			return false;
		}

		var occupied = 0u;
		foreach (var value in liveValues)
		{
			if (!allocated.Allocation.Registers.TryGetValue(value, out var location))
				continue;
			occupied |= 1u << (int)location.Register;
			if (location.IsPair)
				occupied |= 1u << ((int)location.Register + 1);
		}
		var scratch = new[]
			{
				M68kRegister.A0, M68kRegister.A1, M68kRegister.A2,
				M68kRegister.A3, M68kRegister.A4, M68kRegister.A5,
				M68kRegister.A6,
			}
			.FirstOrDefault(candidate =>
				!allocated.Function.ReservedRegisters.Contains(candidate) &&
				(candidate <= M68kRegister.A1 ||
				 allocated.Frame.CalleeSavedRegisters.Contains(candidate)) &&
				(occupied & (1u << (int)candidate)) == 0,
				(M68kRegister)(-1));
		if ((int)scratch < 0)
			return false;

		var table = UniqueLabel("allocated-switch-address-table");
		var data = (int)selector;
		var address = (int)scratch - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x4A80 | data)); // TST.L Dselector
		_assembler.EmitBranch(M68kCondition.Minus, edgeLabels[fallthrough]);
		_assembler.EmitWord((ushort)(0x0C80 | data)); // CMPI.L #count,Dselector
		_assembler.EmitLong((uint)targets.Count);
		_assembler.EmitBranch(M68kCondition.CarryClear,
			edgeLabels[fallthrough]);
		_assembler.EmitWord((ushort)(0xD080 | (data << 9) | data));
		_assembler.EmitWord((ushort)(0xD080 | (data << 9) | data));
		_assembler.EmitWord((ushort)(0x41F9 | (address << 9))); // LEA table,Ascratch
		_assembler.EmitAddress(table);
		_assembler.EmitWord((ushort)(0x2070 | (address << 9) | address));
		_assembler.EmitWord((ushort)(data << 12)); // (Ascratch,Dselector.W)
		_assembler.EmitWord((ushort)(0x4ED0 | address)); // JMP (Ascratch)
		_switchAddressTables.Add(new SwitchAddressTable(table,
			targets.Select(target => edgeLabels[target]).ToArray()));
		return true;
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
			var target = ResolveAllocatedMachineMethod(method, producer);
			if (target.ImportName == "intrinsic:aptr-is-null")
			{
				return M68kCondition.Equal;
			}
			if (target.ImportName?.StartsWith(
					"intrinsic:span-is-empty:",
					StringComparison.Ordinal) == true ||
				target.ImportName?.StartsWith(
					"intrinsic:readonly-span-is-empty:",
					StringComparison.Ordinal) == true ||
				target.ImportName?.StartsWith(
					"intrinsic:memory-is-empty:",
					StringComparison.Ordinal) == true ||
				target.ImportName?.StartsWith(
					"intrinsic:readonly-memory-is-empty:",
					StringComparison.Ordinal) == true)
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
		var frameBaseDelta = UsesAllocatedFrameAnchor
			? -2
			: 0;
		var baseOpcode = width switch
		{
			M68kMachineValueWidth.Byte => 0x102F + frameBaseDelta,
			M68kMachineValueWidth.Word => 0x302F + frameBaseDelta,
			M68kMachineValueWidth.Long => 0x202F + frameBaseDelta,
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
		int displacement,
		bool trackNonNull = true)
	{
		ValidateAllocatedFrameDisplacement(displacement);
		if (destination < M68kRegister.A0)
		{
			// LEA cannot target a data register. Building the address through A0
			// would overwrite a live fixed call argument when this value itself is
			// another argument (for example an out parameter after an A0 receiver).
			// Form the address directly in the allocated data register instead.
			if (UsesAllocatedFrameAnchor)
			{
				EmitAllocatedMove(
					M68kRegister.A5,
					destination,
					M68kMachineValueWidth.Long);
			}
			else
			{
				_assembler.EmitWord((ushort)(
					0x200F | ((int)destination << 9))); // MOVE.L A7,Dn
			}
			if (displacement != 0)
			{
				EmitAllocatedAddImmediate(destination, displacement);
			}
			if (trackNonNull)
			{
				_allocatedKnownNonNullRegisters.Add(destination);
				_allocatedFrameAddressesEmitted.Add(destination);
			}
			return;
		}
		_assembler.EmitWord((ushort)(
			(UsesAllocatedFrameAnchor ? 0x41ED : 0x41EF) |
			(((int)destination - (int)M68kRegister.A0) << 9)));
		_assembler.EmitWord(unchecked((ushort)(short)displacement));
		if (trackNonNull)
		{
			_allocatedKnownNonNullRegisters.Add(destination);
			_allocatedFrameAddressesEmitted.Add(destination);
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
		var frameBaseDelta = UsesAllocatedFrameAnchor
			? -0x0400
			: 0;
		var baseOpcode = width switch
		{
			M68kMachineValueWidth.Byte => 0x1F40 + frameBaseDelta,
			M68kMachineValueWidth.Word => 0x3F40 + frameBaseDelta,
			M68kMachineValueWidth.Long => 0x2F40 + frameBaseDelta,
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
		_assembler.EmitWord((ushort)(UsesAllocatedFrameAnchor
			? 0x2B6D // MOVE.L d16(A5),d16(A5)
			: 0x2F6F)); // MOVE.L d16(A7),d16(A7)
		_assembler.EmitWord(unchecked((ushort)(short)sourceDisplacement));
		_assembler.EmitWord(unchecked((ushort)(short)destinationDisplacement));
	}

	private void EmitAllocatedIncomingStackToFrame(
		int sourceDisplacement,
		int destinationDisplacement)
	{
		if (sourceDisplacement is < 0 or > short.MaxValue)
		{
			throw new InvalidOperationException(
				"Allocated stack argument exceeds d16(A7) range.");
		}
		ValidateAllocatedFrameDisplacement(destinationDisplacement);
		_assembler.EmitWord((ushort)(UsesAllocatedFrameAnchor
			? 0x2B6F // MOVE.L d16(A7),d16(A5)
			: 0x2F6F)); // MOVE.L d16(A7),d16(A7)
		_assembler.EmitWord(unchecked((ushort)(short)sourceDisplacement));
		_assembler.EmitWord(unchecked((ushort)(short)destinationDisplacement));
	}

	private void EmitAllocatedFrameClear(int displacement)
	{
		ValidateAllocatedFrameDisplacement(displacement);
		_assembler.EmitWord((ushort)(UsesAllocatedFrameAnchor
			? 0x42AD // CLR.L d16(A5), safe frame memory
			: 0x42AF)); // CLR.L d16(A7), safe frame memory
		_assembler.EmitWord(unchecked((ushort)(short)displacement));
	}

	private bool UsesAllocatedFrameAnchor =>
		_emittingAllocatedFunction?.Function.HasDynamicStackAllocation == true;

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
		if (width == M68kMachineValueWidth.Byte &&
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

	private void EmitAllocatedFloatingEquals(
		M68kRegister left,
		M68kRegister right,
		M68kRegister result,
		bool isDouble)
	{
		var checkSpecial = UniqueLabel("floating-equals-check-special");
		var classifyNaN = UniqueLabel("floating-equals-classify-nan");
		var equal = UniqueLabel("floating-equals-equal");
		var notEqual = UniqueLabel("floating-equals-not-equal");
		var done = UniqueLabel("floating-equals-done");

		EmitAllocatedCompare(left, right, M68kMachineValueWidth.Long);
		if (isDouble)
		{
			_assembler.EmitBranch(M68kCondition.NotEqual, checkSpecial);
			EmitAllocatedCompare(
				(M68kRegister)((int)left + 1),
				(M68kRegister)((int)right + 1),
				M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, equal);
		}
		else
		{
			_assembler.EmitBranch(M68kCondition.Equal, equal);
		}

		_assembler.Mark(checkSpecial);
		if (isDouble)
		{
			// Signed zero compares equal even though its raw representation differs.
			EmitAllocatedMaskedCopy(left, result, 0x7FFFFFFF);
			_assembler.EmitBranch(M68kCondition.NotEqual, classifyNaN);
			EmitAllocatedMove(
				(M68kRegister)((int)left + 1),
				result,
				M68kMachineValueWidth.Long);
			EmitAllocatedTest(result, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.NotEqual, classifyNaN);
			EmitAllocatedMaskedCopy(right, result, 0x7FFFFFFF);
			_assembler.EmitBranch(M68kCondition.NotEqual, notEqual);
			EmitAllocatedMove(
				(M68kRegister)((int)right + 1),
				result,
				M68kMachineValueWidth.Long);
			EmitAllocatedTest(result, M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, equal);
			_assembler.EmitBranch(M68kCondition.True, notEqual);

			_assembler.Mark(classifyNaN);
			EmitAllocatedDoubleNaNTest(left, result, notEqual);
			EmitAllocatedDoubleNaNTest(right, result, notEqual);
			_assembler.EmitBranch(M68kCondition.True, equal);
		}
		else
		{
			EmitAllocatedMaskedCopy(left, result, 0x7FFFFFFF);
			_assembler.EmitBranch(M68kCondition.NotEqual, classifyNaN);
			EmitAllocatedMaskedCopy(right, result, 0x7FFFFFFF);
			_assembler.EmitBranch(M68kCondition.Equal, equal);
			_assembler.EmitBranch(M68kCondition.True, notEqual);

			_assembler.Mark(classifyNaN);
			EmitAllocatedSingleNaNTest(left, result, notEqual);
			EmitAllocatedSingleNaNTest(right, result, notEqual);
			_assembler.EmitBranch(M68kCondition.True, equal);
		}

		_assembler.Mark(notEqual);
		EmitAllocatedImmediate(0, result);
		_assembler.EmitBranch(M68kCondition.True, done);
		_assembler.Mark(equal);
		EmitAllocatedImmediate(1, result);
		_assembler.Mark(done);
	}

	private void EmitAllocatedFloatingHash(
		M68kRegister source,
		M68kRegister result,
		bool isDouble)
	{
		var checkNaN = UniqueLabel("floating-hash-check-nan");
		var normal = UniqueLabel("floating-hash-normal");
		var zero = UniqueLabel("floating-hash-zero");
		var done = UniqueLabel("floating-hash-done");

		EmitAllocatedMaskedCopy(source, result, 0x7FFFFFFF);
		if (isDouble)
		{
			_assembler.EmitBranch(M68kCondition.NotEqual, checkNaN);
			EmitAllocatedTest(
				(M68kRegister)((int)source + 1),
				M68kMachineValueWidth.Long);
			_assembler.EmitBranch(M68kCondition.Equal, zero);
		}
		else
		{
			_assembler.EmitBranch(M68kCondition.Equal, zero);
		}

		_assembler.Mark(checkNaN);
		if (isDouble)
		{
			EmitAllocatedDoubleNaNTest(source, result, normal);
			EmitAllocatedImmediate(0x7FF00000, result);
		}
		else
		{
			EmitAllocatedSingleNaNTest(source, result, normal);
			EmitAllocatedImmediate(0x7F800000, result);
		}
		_assembler.EmitBranch(M68kCondition.True, done);

		_assembler.Mark(normal);
		EmitAllocatedMove(source, result, M68kMachineValueWidth.Long);
		if (isDouble)
		{
			EmitAllocatedBinaryInPlace(
				M68kMachineOperation.Xor,
				(M68kRegister)((int)source + 1),
				result,
				M68kMachineValueWidth.Long);
		}
		_assembler.EmitBranch(M68kCondition.True, done);

		_assembler.Mark(zero);
		EmitAllocatedImmediate(0, result);
		_assembler.Mark(done);
	}

	private void EmitAllocatedSingleNaNTest(
		M68kRegister value,
		M68kRegister scratch,
		string notNaN)
	{
		EmitAllocatedMaskedCopy(value, scratch, 0x7F800000);
		EmitAllocatedCompareImmediate(scratch, 0x7F800000);
		_assembler.EmitBranch(M68kCondition.NotEqual, notNaN);
		EmitAllocatedMaskedCopy(value, scratch, 0x007FFFFF);
		_assembler.EmitBranch(M68kCondition.Equal, notNaN);
	}

	private void EmitAllocatedDoubleNaNTest(
		M68kRegister value,
		M68kRegister scratch,
		string notNaN)
	{
		var isNaN = UniqueLabel("floating-equals-double-is-nan");
		EmitAllocatedMaskedCopy(value, scratch, 0x7FF00000);
		EmitAllocatedCompareImmediate(scratch, 0x7FF00000);
		_assembler.EmitBranch(M68kCondition.NotEqual, notNaN);
		EmitAllocatedMaskedCopy(value, scratch, 0x000FFFFF);
		_assembler.EmitBranch(M68kCondition.NotEqual, isNaN);
		EmitAllocatedMove(
			(M68kRegister)((int)value + 1),
			scratch,
			M68kMachineValueWidth.Long);
		EmitAllocatedTest(scratch, M68kMachineValueWidth.Long);
		_assembler.EmitBranch(M68kCondition.Equal, notNaN);
		_assembler.Mark(isNaN);
	}

	private void EmitAllocatedMaskedCopy(
		M68kRegister source,
		M68kRegister destination,
		int mask)
	{
		EmitAllocatedMove(source, destination, M68kMachineValueWidth.Long);
		_assembler.EmitWord((ushort)(0x0280 | (int)destination)); // ANDI.L #mask,Dn
		_assembler.EmitLong(unchecked((uint)mask));
	}

	private void EmitAllocatedCompareImmediate(M68kRegister register, int value)
	{
		_assembler.EmitWord((ushort)(0x0C80 | (int)register)); // CMPI.L #value,Dn
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

	private static bool IsConstructorReceiver(
		CilMethod method,
		M68kMachineFunction function,
		int value)
	{
		if (method.Name != ".ctor" || !method.Signature.Header.IsInstance)
		{
			return false;
		}
		var visited = new HashSet<int>();
		while (visited.Add(value))
		{
			var definition = function.Blocks
				.SelectMany(static block => block.Instructions)
				.SingleOrDefault(instruction =>
					instruction.Definitions.Contains(value));
			if (definition is
				{
					Operation: M68kMachineOperation.Argument,
					ArgumentIndex: 0
				})
			{
				return true;
			}
			if (definition is
				{
					Operation: M68kMachineOperation.Copy,
					Uses: [var source]
				})
			{
				value = source;
				continue;
			}
			break;
		}
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
			if (definition is { Operation: M68kMachineOperation.Constant })
			{
				if (definition.ConstantValue is { } constantValue &&
					constantValue.TryGetIntegral(out var integral) &&
					integral is >= int.MinValue and <= int.MaxValue)
				{
					constant = (int)integral;
					return true;
				}
				if (definition.SourceInstruction is { } source)
				{
					constant = GetAllocatedIntConstant(source);
					return true;
				}
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
			$"Unsupported allocated constant {op.Name} at IL_{instruction.Offset:X4} " +
			$"(operand {instruction.Operand ?? "<none>"}).");
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
