/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed record M68kAggregateReturnForwardingStatistics(
	int ReturnBuffersForwarded,
	int LocalsForwarded,
	int TemporaryHomesRemoved,
	int TemporaryBytesRemoved)
{
	public static M68kAggregateReturnForwardingStatistics Empty { get; } = new(0, 0, 0, 0);
	public bool Changed => ReturnBuffersForwarded != 0 || LocalsForwarded != 0;
}

/// <summary>
/// Redirects an aggregate call's hidden return pointer to an immediately used
/// destination. Run after memory promotion and before bulk-copy selection.
/// Unknown aliases, EH observation points, and stack argument snapshots retain
/// the existing temporary-and-copy sequence.
/// </summary>
internal static class M68kAggregateReturnForwarding
{
	private sealed record Candidate(
		M68kMachineInstruction Call,
		M68kMachineInstruction Address,
		M68kMachineInstruction Consumer,
		IReadOnlyList<M68kMachineInstruction> ResultScaffolding,
		int TemporaryHome,
		int? DestinationHome);

	private sealed record BranchedReturnCandidate(
		M68kMachineBlock ProducerBlock,
		M68kMachineInstruction Call,
		M68kMachineInstruction Address,
		M68kMachineInstruction? Branch,
		M68kMachineBlock ReturnBlock,
		M68kMachineInstruction Return,
		int TemporaryHome);

	private sealed class UseIndex
	{
		public UseIndex(M68kMachineFunction function)
		{
			Instructions = function.Blocks.SelectMany(static block => block.Instructions).ToArray();
			Definitions = Instructions.SelectMany(instruction =>
				instruction.Definitions.Select(value => (Value: value, Instruction: instruction)))
				.ToDictionary(static item => item.Value, static item => item.Instruction);
			Uses = Instructions.SelectMany(instruction => instruction.Uses.Distinct()
				.Select(value => (Value: value, Instruction: instruction)))
				.ToLookup(static item => item.Value, static item => item.Instruction);
			LogicalArguments = Instructions.SelectMany(instruction =>
				(instruction.LogicalCall?.ArgumentValueIds ?? []).Distinct()
					.Select(value => (Value: value, Instruction: instruction)))
				.ToLookup(static item => item.Value, static item => item.Instruction);
			LogicalResults = Instructions.SelectMany(instruction =>
				(instruction.LogicalCall?.ResultValueIds ?? []).Distinct()
					.Select(value => (Value: value, Instruction: instruction)))
				.ToLookup(static item => item.Value, static item => item.Instruction);
			PhiValues = function.Blocks.SelectMany(static block => block.Phis)
				.SelectMany(static phi => phi.Inputs.Values.Append(phi.Definition)).ToHashSet();
		}

		public M68kMachineInstruction[] Instructions { get; }
		public Dictionary<int, M68kMachineInstruction> Definitions { get; }
		public ILookup<int, M68kMachineInstruction> Uses { get; }
		public ILookup<int, M68kMachineInstruction> LogicalArguments { get; }
		public ILookup<int, M68kMachineInstruction> LogicalResults { get; }
		public HashSet<int> PhiValues { get; }

		public bool HasOnlyUse(int value, int consumerId, int? logicalResultCallId = null) =>
			!PhiValues.Contains(value) &&
			Uses[value].Select(static instruction => instruction.Id).SequenceEqual([consumerId]) &&
			!LogicalArguments[value].Any() &&
			LogicalResults[value].All(instruction => instruction.Id == logicalResultCallId);
	}

	public static M68kAggregateReturnForwardingStatistics Run(
		M68kMachineFunction function,
		CompilationModule module,
		IReadOnlyDictionary<CilMethodIdentity, M68kMethodMemorySummary>?
			methodSummaries = null)
	{
		if (function.SourceMethod is not { } method || function.HasExceptionHandlers ||
			function.HasDynamicStackAllocation ||
			function.Blocks.Any(static block => block.IsExceptionEntry ||
				block.SuccessorEdges.Any(static edge => edge.Kind != M68kMachineEdgeKind.Normal)))
		{
			return M68kAggregateReturnForwardingStatistics.Empty;
		}

		var uses = new UseIndex(function);
		var candidates = new List<Candidate>();
		var branchedReturns = new List<BranchedReturnCandidate>();
		// Prove every destination against the original graph. Earlier accepted
		// rewrites must not look like newly escaping addresses during this proof.
		foreach (var block in function.Blocks)
		{
			for (var index = 0; index < block.Instructions.Count; index++)
			{
				if (TryFindCandidate(
						function,
						module,
						method,
						block,
						index,
						uses,
						methodSummaries,
						out var candidate))
				{
					candidates.Add(candidate);
				}
				else if (TryFindBranchedReturnCandidate(
						function,
						module,
						method,
						block,
						index,
						uses,
						out var branchedReturn))
				{
					branchedReturns.Add(branchedReturn);
				}
			}
		}
		if (candidates.Count == 0 && branchedReturns.Count == 0)
		{
			return M68kAggregateReturnForwardingStatistics.Empty;
		}

		var replacements = new Dictionary<int, M68kMachineInstruction>();
		var removedInstructions = new HashSet<int>();
		var removedValues = new HashSet<int>();
		foreach (var candidate in candidates)
		{
			var destination = candidate.DestinationHome;
			replacements[candidate.Address.Id] = candidate.Address with
			{
				Operation = destination is null ? M68kMachineOperation.ReturnBufferAddress : M68kMachineOperation.LocalAddress,
				ArgumentIndex = destination,
				MemoryEffect = destination is null ? M68kMachineMemoryEffect.Read : M68kMachineMemoryEffect.None,
				ExactMemoryAccesses = destination is { } local
					? [new M68kExactMemoryAccess(M68kMemoryModel.FrameObject(
						M68kMemoryObjectKind.FrameSlot, local, function.LocalHomes[local]), M68kExactMemoryAccessKind.Address)]
					: []
			};
			replacements[candidate.Call.Id] = candidate.Call with
			{
				LogicalCall = candidate.Call.LogicalCall! with { ResultValueIds = [] },
				// A partial exact-write set would incorrectly hide other call
				// effects. Keep the original conservative read/write call barrier.
				ExactMemoryAccesses = []
			};
			foreach (var instruction in candidate.ResultScaffolding)
			{
				removedInstructions.Add(instruction.Id);
				removedValues.UnionWith(instruction.Definitions);
			}
			if (destination is null)
			{
				replacements[candidate.Consumer.Id] = candidate.Consumer with
				{
					Uses = [],
					ReturnBufferWritten = true,
					ExactMemoryAccesses = []
				};
			}
			else
			{
				removedInstructions.Add(candidate.Consumer.Id);
			}
		}
		foreach (var candidate in branchedReturns)
		{
			replacements[candidate.Address.Id] = candidate.Address with
			{
				Operation = M68kMachineOperation.ReturnBufferAddress,
				ArgumentIndex = null,
				MemoryEffect = M68kMachineMemoryEffect.Read,
				ExactMemoryAccesses = []
			};
			replacements[candidate.Call.Id] = candidate.Call with
			{
				LogicalCall = candidate.Call.LogicalCall! with { ResultValueIds = [] },
				ExactMemoryAccesses = []
			};
			if (candidate.Branch is { } branch)
			{
				replacements[branch.Id] = candidate.Return with
				{
					Id = branch.Id,
					Uses = [],
					ReturnBufferWritten = true,
					ExactMemoryAccesses = []
				};
			}
			else
			{
				candidate.ProducerBlock.Instructions.Add(function.CreateInstruction(
					M68kMachineOperation.Return,
					candidate.Return.IlOffset,
					sourceInstruction: candidate.Return.SourceInstruction,
					origin: candidate.Return.Origin) with
				{
					ReturnBufferWritten = true
				});
			}
			candidate.ProducerBlock.Successors.Clear();
			candidate.ReturnBlock.Predecessors.Remove(candidate.ProducerBlock.Id);
		}
		foreach (var block in function.Blocks)
		{
			var rewritten = block.Instructions.Where(instruction => !removedInstructions.Contains(instruction.Id))
				.Select(instruction => replacements.GetValueOrDefault(instruction.Id, instruction)).ToArray();
			block.Instructions.Clear();
			block.Instructions.AddRange(rewritten);
		}
		var removedBlocks = branchedReturns.Select(static candidate => candidate.ReturnBlock)
			.Distinct()
			.Where(block => block.Id != function.EntryBlockId && block.Predecessors.Count == 0)
			.ToArray();
		foreach (var block in removedBlocks)
		{
			removedValues.UnionWith(block.Instructions.SelectMany(static instruction => instruction.Definitions));
			removedValues.UnionWith(block.Phis.SelectMany(static phi => phi.Inputs.Values.Append(phi.Definition)));
		}
		if (removedBlocks.Length != 0)
		{
			function.RemoveBlocks(removedBlocks.Select(static block => block.Id).ToHashSet());
		}
		else if (branchedReturns.Count != 0)
		{
			function.SynchronizeNormalEdges();
		}
		RemoveUnreferencedValues(function, removedValues);
		var removedHomes = 0;
		var removedBytes = 0;
		foreach (var homeIndex in candidates.Select(static candidate => candidate.TemporaryHome)
			.Concat(branchedReturns.Select(static candidate => candidate.TemporaryHome)).Distinct())
		{
			if (homeIndex < method.Locals.Length ||
				ReferencesHome(function, homeIndex) ||
				!function.LocalHomes.Remove(homeIndex, out var home))
			{
				continue;
			}
			removedHomes++;
			removedBytes += home.Size;
			foreach (var key in function.ReusableAggregateReturnHomes
				.Where(item => item.Value == homeIndex).Select(static item => item.Key).ToArray())
			{
				function.ReusableAggregateReturnHomes.Remove(key);
			}
		}
		M68kMachineIrVerifier.Verify(function);
		var statistics = new M68kAggregateReturnForwardingStatistics(
			candidates.Count(static candidate => candidate.DestinationHome is null) + branchedReturns.Count,
			candidates.Count(static candidate => candidate.DestinationHome is not null), removedHomes, removedBytes);
		if (candidates.Count == 0)
		{
			return statistics;
		}
		var followUp = Run(function, module, methodSummaries);
		return new(
			statistics.ReturnBuffersForwarded + followUp.ReturnBuffersForwarded,
			statistics.LocalsForwarded + followUp.LocalsForwarded,
			statistics.TemporaryHomesRemoved + followUp.TemporaryHomesRemoved,
			statistics.TemporaryBytesRemoved + followUp.TemporaryBytesRemoved);
	}

	private static bool TryFindCandidate(
		M68kMachineFunction function,
		CompilationModule module,
		CilMethod method,
		M68kMachineBlock block,
		int callIndex,
		UseIndex uses,
		IReadOnlyDictionary<CilMethodIdentity, M68kMethodMemorySummary>?
			methodSummaries,
		out Candidate candidate)
	{
		candidate = null!;
		var instructions = block.Instructions;
		var call = instructions[callIndex];
		if (!TryResolveDirectCall(module, method, call, out var target) ||
			call.Origin?.SourceMethod?.Identity != method.Identity ||
			call.Definitions.Length != 0 || call.StackVarargsRegister is not null ||
			(call.MemoryEffect & M68kMachineMemoryEffect.Volatile) != 0 ||
			!module.TryGetReferenceFreeStructLayout(target.Signature.ReturnType, target.ModuleName, out var layout) ||
			layout.Size <= 4 || layout.ReferenceBitmap != 0 ||
			callIndex + 3 >= instructions.Count ||
			instructions[callIndex + 1] is not { Operation: M68kMachineOperation.OutgoingArgumentCleanup, ArgumentIndex: 4 })
		{
			return false;
		}
		var resultAddress = instructions[callIndex + 2];
		if (resultAddress.Operation != M68kMachineOperation.LocalAddress ||
			resultAddress.ArgumentIndex is not { } temporaryHome || temporaryHome < method.Locals.Length ||
			resultAddress.Definitions is not [var result] || resultAddress.Uses.Length != 0 ||
			!function.LocalHomes.TryGetValue(temporaryHome, out var home) || home.HasGcReferences ||
			home.Size != layout.Size || function.Values[result].Kind != CilStackValueKind.AggregateAddress ||
			call.LogicalCall!.ResultValueIds.Any(value => value != result))
		{
			return false;
		}

		var scaffolding = new List<M68kMachineInstruction> { resultAddress };
		var consumerIndex = callIndex + 3;
		var previous = result;
		while (consumerIndex < instructions.Count && IsAddressCopy(instructions[consumerIndex], function, previous))
		{
			var copy = instructions[consumerIndex++];
			if (!uses.HasOnlyUse(previous, copy.Id, call.Id)) return false;
			scaffolding.Add(copy);
			previous = copy.Definitions[0];
		}
		if (consumerIndex >= instructions.Count) return false;
		var consumer = instructions[consumerIndex];
		if (consumer.Uses is not [var consumed] || consumed != previous ||
			!uses.HasOnlyUse(previous, consumer.Id, call.Id) ||
			(consumer.MemoryEffect & M68kMachineMemoryEffect.Volatile) != 0)
		{
			return false;
		}

		int? destinationHome = null;
		if (consumer.Operation == M68kMachineOperation.Return && !consumer.ReturnBufferWritten)
		{
			// Managed byrefs, raw/native pointers and transparent wrappers can
			// expose an alias supplied by an outer caller. A scalar ABI does not
			// establish disjointness for APTR (or another pointer wrapper).
			if (target.Signature.ParameterTypes.Any(parameter => HasUnprovenReturnAlias(module, parameter)) ||
				method.Signature.ReturnType.DisplayName != target.Signature.ReturnType.DisplayName ||
				!module.TryGetReferenceFreeStructLayout(method.Signature.ReturnType, method.ModuleName, out var returnLayout) ||
				returnLayout.Size != layout.Size || returnLayout.ReferenceBitmap != 0)
			{
				return false;
			}
		}
		else if (consumer.Operation == M68kMachineOperation.LocalStore &&
			consumer.ArgumentIndex is { } local && local >= 0 && local < method.Locals.Length &&
			function.LocalHomes.TryGetValue(local, out var destination) && !destination.HasGcReferences &&
			destination.Size == layout.Size && method.Locals[local].DisplayName == target.Signature.ReturnType.DisplayName &&
			IsPrivateLocal(
				function,
				module,
				method,
				local,
				layout.Size,
				call.Id,
				uses,
				methodSummaries))
		{
			destinationHome = local;
		}
		else
		{
			return false;
		}

		M68kMachineInstruction? hiddenAddress = null;
		for (var index = callIndex - 1; index >= 0; index--)
		{
			var instruction = instructions[index];
			if (instruction.IlOffset != call.IlOffset || instruction.Origin != call.Origin) break;
			if (instruction.Operation == M68kMachineOperation.OutgoingArgumentPush)
			{
				if (instruction.ArgumentIndex != 4 || instruction.Uses is not [var addressValue] ||
					!uses.HasOnlyUse(addressValue, instruction.Id) ||
					!uses.Definitions.TryGetValue(addressValue, out var address) ||
					address.Operation != M68kMachineOperation.LocalAddress || address.ArgumentIndex != temporaryHome ||
					index == 0 || instructions[index - 1].Id != address.Id)
				{
					return false;
				}
				hiddenAddress = address;
				break;
			}
			if (instruction.Operation != M68kMachineOperation.Copy || instruction.MemoryEffect != M68kMachineMemoryEffect.None ||
				instruction.MayThrow || instruction.IsSafepoint)
			{
				return false;
			}
		}
		if (hiddenAddress is null) return false;
		candidate = new(call, hiddenAddress, consumer, scaffolding, temporaryHome, destinationHome);
		return true;
	}

	private static bool TryFindBranchedReturnCandidate(
		M68kMachineFunction function,
		CompilationModule module,
		CilMethod method,
		M68kMachineBlock block,
		int callIndex,
		UseIndex uses,
		out BranchedReturnCandidate candidate)
	{
		candidate = null!;
		var instructions = block.Instructions;
		var call = instructions[callIndex];
		if (!TryResolveDirectCall(module, method, call, out var target) ||
			call.Origin?.SourceMethod?.Identity != method.Identity ||
			call.Definitions.Length != 0 || call.StackVarargsRegister is not null ||
			(call.MemoryEffect & M68kMachineMemoryEffect.Volatile) != 0 ||
			call.LogicalCall!.ResultValueIds.Length != 0 ||
			!module.TryGetReferenceFreeStructLayout(target.Signature.ReturnType, target.ModuleName, out var layout) ||
			layout.Size <= 4 || layout.ReferenceBitmap != 0 ||
			callIndex + 1 >= instructions.Count ||
			instructions[callIndex + 1] is not
				{ Operation: M68kMachineOperation.OutgoingArgumentCleanup, ArgumentIndex: 4 } ||
			block.Successors is not [var returnBlockId] ||
			block.SuccessorEdges.Any(static edge => edge.Kind != M68kMachineEdgeKind.Normal))
		{
			return false;
		}
		M68kMachineInstruction? branch = null;
		if (callIndex + 2 == instructions.Count)
		{
			// The sole normal successor is the physical fallthrough.
		}
		else if (callIndex + 2 == instructions.Count - 1 &&
			instructions[callIndex + 2] is
				{ Operation: M68kMachineOperation.Branch, Uses.Length: 0 } explicitBranch)
		{
			branch = explicitBranch;
		}
		else
		{
			return false;
		}

		var returnBlock = function.Blocks.SingleOrDefault(candidate => candidate.Id == returnBlockId);
		if (returnBlock is null || returnBlock.Id == function.EntryBlockId || returnBlock.IsExceptionEntry ||
			!returnBlock.Predecessors.Contains(block.Id) || returnBlock.Phis.Count != 0 ||
			returnBlock.Successors.Count != 0 ||
			returnBlock.SuccessorEdges.Any(static edge => edge.Kind != M68kMachineEdgeKind.Normal) ||
			!block.ActiveExceptionRegionIds.SequenceEqual(returnBlock.ActiveExceptionRegionIds) ||
			returnBlock.Instructions.Count < 2)
		{
			return false;
		}

		var resultAddress = returnBlock.Instructions[0];
		if (resultAddress.Operation != M68kMachineOperation.LocalAddress ||
			resultAddress.ArgumentIndex is not { } temporaryHome || temporaryHome < method.Locals.Length ||
			resultAddress.Definitions is not [var result] || resultAddress.Uses.Length != 0 ||
			!function.LocalHomes.TryGetValue(temporaryHome, out var home) || home.HasGcReferences ||
			home.Size != layout.Size || function.Values[result].Kind != CilStackValueKind.AggregateAddress)
		{
			return false;
		}

		var consumerIndex = 1;
		var previous = result;
		while (consumerIndex < returnBlock.Instructions.Count &&
			IsAddressCopy(returnBlock.Instructions[consumerIndex], function, previous))
		{
			var copy = returnBlock.Instructions[consumerIndex++];
			if (!uses.HasOnlyUse(previous, copy.Id)) return false;
			previous = copy.Definitions[0];
		}
		if (consumerIndex != returnBlock.Instructions.Count - 1 ||
			returnBlock.Instructions[consumerIndex] is not
				{ Operation: M68kMachineOperation.Return, ReturnBufferWritten: false } returned ||
			returned.Uses is not [var consumed] || consumed != previous ||
			!uses.HasOnlyUse(previous, returned.Id) ||
			(returned.MemoryEffect & M68kMachineMemoryEffect.Volatile) != 0 ||
			target.Signature.ParameterTypes.Any(parameter => HasUnprovenReturnAlias(module, parameter)) ||
			method.Signature.ReturnType.DisplayName != target.Signature.ReturnType.DisplayName ||
			!module.TryGetReferenceFreeStructLayout(method.Signature.ReturnType, method.ModuleName, out var returnLayout) ||
			returnLayout.Size != layout.Size || returnLayout.ReferenceBitmap != 0)
		{
			return false;
		}

		M68kMachineInstruction? hiddenAddress = null;
		for (var index = callIndex - 1; index >= 0; index--)
		{
			var instruction = instructions[index];
			if (instruction.IlOffset != call.IlOffset || instruction.Origin != call.Origin) break;
			if (instruction.Operation == M68kMachineOperation.OutgoingArgumentPush)
			{
				if (instruction.ArgumentIndex != 4 || instruction.Uses is not [var addressValue] ||
					!uses.HasOnlyUse(addressValue, instruction.Id) ||
					!uses.Definitions.TryGetValue(addressValue, out var address) ||
					address.Operation != M68kMachineOperation.LocalAddress || address.ArgumentIndex != temporaryHome ||
					index == 0 || instructions[index - 1].Id != address.Id)
				{
					return false;
				}
				hiddenAddress = address;
				break;
			}
			if (instruction.Operation != M68kMachineOperation.Copy ||
				instruction.MemoryEffect != M68kMachineMemoryEffect.None ||
				instruction.MayThrow || instruction.IsSafepoint)
			{
				return false;
			}
		}
		if (hiddenAddress is null) return false;
		candidate = new(block, call, hiddenAddress, branch, returnBlock, returned, temporaryHome);
		return true;
	}

	private static bool HasUnprovenReturnAlias(CompilationModule module, CilType parameter) =>
		parameter.IsReference || parameter.Kind is CilTypeKind.UnmanagedPointer or
			CilTypeKind.FunctionPointer or CilTypeKind.NativeInteger or
			CilTypeKind.GenericParameter or CilTypeKind.Unknown ||
		module.IsTransparentScalarType(parameter);

	private static bool IsPrivateLocal(
		M68kMachineFunction function,
		CompilationModule module,
		CilMethod method,
		int local,
		int size,
		int producerCallId,
		UseIndex uses,
		IReadOnlyDictionary<CilMethodIdentity, M68kMethodMemorySummary>?
			methodSummaries)
	{
		var localType = method.Locals[local].DisplayName;
		var aliases = uses.Instructions.Where(instruction => instruction.Operation == M68kMachineOperation.LocalAddress &&
			instruction.ArgumentIndex == local).SelectMany(static instruction => instruction.Definitions).ToHashSet();
		var pending = new Queue<int>(aliases);
		while (pending.TryDequeue(out var value))
		{
			if (uses.PhiValues.Contains(value) || uses.LogicalResults[value].Any()) return false;
			foreach (var instruction in uses.Uses[value])
			{
				if ((instruction.MemoryEffect & M68kMachineMemoryEffect.Volatile) != 0)
				{
					return false;
				}
				if (instruction.Operation == M68kMachineOperation.Call &&
					IsProvenReadOnlyAliasCall(
						module,
						method,
						instruction,
						aliases,
						size,
						localType,
						producerCallId,
						methodSummaries))
				{
					continue;
				}
				if (instruction.Operation == M68kMachineOperation.OutgoingArgumentPush &&
					instruction.ArgumentIndex == 4 &&
					uses.Instructions.Any(call =>
						call.Operation == M68kMachineOperation.Call &&
						call.Origin == instruction.Origin &&
						call.IlOffset == instruction.IlOffset &&
						IsProvenReadOnlyAliasCall(
							module,
							method,
							call,
							aliases,
							size,
							localType,
							producerCallId,
							methodSummaries)))
				{
					continue;
				}
				if (instruction.ExactMemoryAccesses.Any(static access =>
					access.Kind == M68kExactMemoryAccessKind.Escape))
				{
					return false;
				}
				if (IsPointerTransportCopy(instruction, function, value) ||
					instruction.Operation == M68kMachineOperation.Address && instruction.Uses is [var address] && address == value &&
					instruction.Definitions.Length == 1 &&
					instruction.SourceInstruction?.OpCode == OpCodes.Ldflda)
				{
					foreach (var definition in instruction.Definitions)
					{
						if (aliases.Add(definition)) pending.Enqueue(definition);
					}
					continue;
				}
				if (instruction.Operation == M68kMachineOperation.Load && instruction.Uses is [var loadedAddress] && loadedAddress == value ||
					instruction.Operation == M68kMachineOperation.Store && instruction.Uses.Length >= 2 &&
					instruction.Uses[0] == value && instruction.Uses.Skip(1).All(other => !aliases.Contains(other)))
				{
					continue;
				}
				if (instruction.Operation == M68kMachineOperation.OutgoingArgumentPush && instruction.ArgumentIndex == size &&
					function.Values[value].Kind == CilStackValueKind.AggregateAddress && size > 4)
				{
					continue;
				}
				if (instruction.Operation == M68kMachineOperation.Return && function.Values[value].Kind == CilStackValueKind.AggregateAddress &&
					module.TryGetReferenceFreeStructLayout(method.Signature.ReturnType, method.ModuleName, out var returned) &&
					returned.Size == size && returned.ReferenceBitmap == 0)
				{
					continue;
				}
				return false;
			}
			foreach (var call in uses.LogicalArguments[value])
			{
				if (!IsByValueAggregateArgument(module, method, call, value, size, uses) &&
					!IsReadOnlyByReferenceAggregateArgument(
						module,
						method,
						call,
						value,
						size,
						localType,
						producerCallId,
						methodSummaries))
				{
					return false;
				}
			}
		}
		return true;
	}

	private static bool IsByValueAggregateArgument(
		CompilationModule module,
		CilMethod method,
		M68kMachineInstruction call,
		int value,
		int size,
		UseIndex uses)
	{
		if (!TryResolveDirectCall(module, method, call, out var target) || call.Uses.Contains(value)) return false;
		var arguments = call.LogicalCall!.ArgumentValueIds;
		if (arguments.Length != target.Signature.ParameterTypes.Length) return false;
		for (var index = 0; index < arguments.Length; index++)
		{
			if (arguments[index] != value) continue;
			var parameter = target.Signature.ParameterTypes[index];
			if (parameter.IsReference || !module.TryGetReferenceFreeStructLayout(parameter, target.ModuleName, out var layout) ||
				layout.ReferenceBitmap != 0 || layout.Size != size || size <= 4)
			{
				return false;
			}
		}
		return uses.Uses[value].Any(instruction => instruction.Operation == M68kMachineOperation.OutgoingArgumentPush &&
			instruction.ArgumentIndex == size && instruction.Origin == call.Origin && instruction.IlOffset == call.IlOffset);
	}

	private static bool IsReadOnlyByReferenceAggregateArgument(
		CompilationModule module,
		CilMethod method,
		M68kMachineInstruction call,
		int value,
		int size,
		string aggregateType,
		int producerCallId,
		IReadOnlyDictionary<CilMethodIdentity, M68kMethodMemorySummary>?
			methodSummaries)
	{
		if (call.Id == producerCallId || methodSummaries is null ||
			!TryResolveDirectCall(module, method, call, out var target) ||
			!methodSummaries.TryGetValue(target.Identity, out var summary))
		{
			return false;
		}
		var arguments = call.LogicalCall!.ArgumentValueIds;
		if (arguments.Length != target.Signature.ParameterTypes.Length)
		{
			return false;
		}
		var matched = false;
		for (var index = 0; index < arguments.Length; index++)
		{
			if (arguments[index] != value) continue;
			matched = true;
			var parameter = target.Signature.ParameterTypes[index];
			if (!parameter.IsReference || !IsReadOnlyParameter(target, index, parameter) ||
				parameter.ElementType is not { } element ||
				element.DisplayName != aggregateType ||
				!module.TryGetReferenceFreeStructLayout(
					element,
					target.ModuleName,
					out var layout) ||
				layout.ReferenceBitmap != 0 || layout.Size != size || size <= 4 ||
				(summary.EffectForParameter(index) &
					~M68kParameterMemoryEffect.Read) != M68kParameterMemoryEffect.None)
			{
				return false;
			}
		}
		return matched;
	}

	private static bool IsProvenReadOnlyAliasCall(
		CompilationModule module,
		CilMethod method,
		M68kMachineInstruction call,
		IReadOnlySet<int> aliases,
		int size,
		string aggregateType,
		int producerCallId,
		IReadOnlyDictionary<CilMethodIdentity, M68kMethodMemorySummary>?
			methodSummaries)
	{
		var arguments = call.LogicalCall?.ArgumentValueIds
			.Where(aliases.Contains)
			.Distinct()
			.ToArray() ?? [];
		return arguments.Length != 0 && arguments.All(value =>
			IsReadOnlyByReferenceAggregateArgument(
				module,
				method,
				call,
				value,
				size,
				aggregateType,
				producerCallId,
				methodSummaries));
	}

	private static bool IsReadOnlyParameter(
		CilMethod method,
		int parameterIndex,
		CilType parameter) =>
		parameter.IsReadOnly || parameter.ElementType?.IsReadOnly == true ||
		!method.ParameterFlags.IsDefault &&
		parameterIndex >= 0 && parameterIndex < method.ParameterFlags.Length &&
		(method.ParameterFlags[parameterIndex] & ParameterAttributes.In) != 0;

	private static bool TryResolveDirectCall(
		CompilationModule module,
		CilMethod method,
		M68kMachineInstruction instruction,
		out CilMethod target)
	{
		target = null!;
		if (instruction.Operation != M68kMachineOperation.Call ||
			instruction.LogicalCall is not { DispatchKind: M68kMachineCallDispatchKind.Direct, RequiresNullCheck: false } logical ||
			logical.ResolvedTargets.Length != 1 ||
			instruction.SourceInstruction is not { Operand: int token } source || source.OpCode != OpCodes.Call)
		{
			return false;
		}
		var reference = module.ResolveMethodToken(token, instruction.Origin?.SourceMethod ?? method, source.Offset);
		if (reference.Definition is not { IsImport: false, IsAbstract: false } definition ||
			definition.Signature.Header.IsInstance || definition.Identity != logical.ResolvedTargets[0])
		{
			return false;
		}
		target = definition;
		return true;
	}

	private static bool IsAddressCopy(M68kMachineInstruction instruction, M68kMachineFunction function, int source) =>
		instruction.Operation == M68kMachineOperation.Copy && instruction.Uses is [var used] && used == source &&
		instruction.Definitions is [var defined] && function.Values[defined].Kind == CilStackValueKind.AggregateAddress &&
		instruction.MemoryEffect == M68kMachineMemoryEffect.None && !instruction.MayThrow && !instruction.IsSafepoint;

	private static bool IsPointerTransportCopy(M68kMachineInstruction instruction, M68kMachineFunction function, int source) =>
		instruction.Operation == M68kMachineOperation.Copy && instruction.Uses is [var used] && used == source &&
		instruction.Definitions is [var defined] && function.Values[source].Width == M68kMachineValueWidth.Long &&
		function.Values[defined].Width == M68kMachineValueWidth.Long &&
		instruction.MemoryEffect == M68kMachineMemoryEffect.None && !instruction.MayThrow && !instruction.IsSafepoint;

	private static void RemoveUnreferencedValues(M68kMachineFunction function, IReadOnlySet<int> candidates)
	{
		var referenced = function.Blocks.SelectMany(block => block.Instructions.SelectMany(static instruction =>
			instruction.Uses.Concat(instruction.Definitions)
				.Concat(instruction.LogicalCall?.ArgumentValueIds ?? []).Concat(instruction.LogicalCall?.ResultValueIds ?? [])
				.Concat(instruction.ExactMemoryAccesses.Where(static access => access.ValueId is not null)
					.Select(static access => access.ValueId!.Value)))
			.Concat(block.Phis.SelectMany(static phi => phi.Inputs.Values.Append(phi.Definition)))).ToHashSet();
		foreach (var value in candidates.Where(value => !referenced.Contains(value)))
		{
			function.Values.Remove(value);
			function.ManagedByrefTypes.Remove(value);
		}
	}

	private static bool ReferencesHome(M68kMachineFunction function, int home)
	{
		var identity = home.ToString(System.Globalization.CultureInfo.InvariantCulture);
		return function.Blocks.SelectMany(static block => block.Instructions).Any(instruction =>
			instruction.ArgumentIndex == home && instruction.Operation is not (
				M68kMachineOperation.OutgoingArgumentPush or M68kMachineOperation.OutgoingArgumentCleanup or
				M68kMachineOperation.OutgoingArgumentReserve or M68kMachineOperation.IncomingArgumentPush or
				M68kMachineOperation.Argument or M68kMachineOperation.ArgumentAddress or
				M68kMachineOperation.ArgumentLoad or M68kMachineOperation.ArgumentStore) ||
			instruction.ExactMemoryAccesses.Any(access => access.Object.Kind == M68kMemoryObjectKind.FrameSlot && access.Object.Identity == identity) ||
			instruction.BulkCopy?.Source is { Kind: M68kMemoryObjectKind.FrameSlot } source && source.Identity == identity ||
			instruction.BulkCopy?.Destination is { Kind: M68kMemoryObjectKind.FrameSlot } destination && destination.Identity == identity);
	}
}
