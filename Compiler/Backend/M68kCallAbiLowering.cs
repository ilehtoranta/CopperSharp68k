/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

/// <summary>
/// Closes the logical-call phase. The current builder already materializes ABI
/// staging instructions; after whole-program rewrites finish, logical SSA ids
/// must not survive into spill rewriting or physical allocation.
/// </summary>
internal static class M68kCallAbiLowering
{
	public static void FinalizeLogicalCalls(M68kMachineFunction function)
	{
		M68kMachineIrVerifier.Verify(function);
		RematerializeFixedPlatformBaseLoads(function);
		// Stack arguments disappear from Call.Uses after ABI staging. Preserve
		// their frame-lifetime consequences while logical argument ids still
		// exist, including pointers returned into a later tail-position call.
		var frameAddresses = M68kFrameAddressLifetime.FindDependentValues(function);
		foreach (var block in function.Blocks)
		{
			for (var index = 0; index < block.Instructions.Count; index++)
			{
				var instruction = block.Instructions[index];
				if (instruction.LogicalCall is { } logicalCall)
				{
					block.Instructions[index] = instruction with
					{
						LogicalCall = null,
						RequiresLiveCallerFrame = instruction.RequiresLiveCallerFrame ||
							instruction.Operation == M68kMachineOperation.Call &&
							(instruction.Uses.Any(frameAddresses.Contains) ||
							 logicalCall.ArgumentValueIds.Any(frameAddresses.Contains))
					};
				}
			}
		}
		M68kMachineIrVerifier.Verify(function);
	}

	/// <summary>
	/// A platform-base load initially defines an unconstrained SSA value and the
	/// call ABI copies that value into its fixed base register. When every use of
	/// the unconstrained value is one of those fixed copies, load directly into
	/// the required register at each use. This avoids manufacturing a temporary
	/// register (and, in particular, a volatile A0 live range) solely to feed A6.
	/// </summary>
	private static void RematerializeFixedPlatformBaseLoads(
		M68kMachineFunction function)
	{
		var instructionUses = new Dictionary<
			int,
			List<M68kMachineInstruction>>();
		foreach (var instruction in function.Blocks.SelectMany(static block =>
			block.Instructions))
		{
			foreach (var use in instruction.Uses.Distinct())
			{
				if (!instructionUses.TryGetValue(use, out var users))
				{
					users = [];
					instructionUses.Add(use, users);
				}
				users.Add(instruction);
			}
		}

		var loads = function.Blocks
			.SelectMany(static block => block.Instructions)
			.Where(static instruction =>
				instruction.Operation == M68kMachineOperation.PlatformBaseLoad &&
				instruction.Definitions.Length == 1 &&
				instruction.PlatformBaseConvention is not null)
			.ToArray();
		foreach (var load in loads)
		{
			var source = load.Definitions[0];
			var convention = load.PlatformBaseConvention!;
			var fixedCopies = new List<M68kMachineInstruction>();
			var copyIdsToRemove = new HashSet<int>();
			var valuesToRemove = new HashSet<int> { source };
			var visited = new HashSet<int>();

			bool CollectFixedCopies(int value)
			{
				if (!visited.Add(value))
				{
					return true;
				}
				if (function.Blocks.Any(block => block.Phis.Any(phi =>
						phi.Inputs.Values.Contains(value))) ||
					!instructionUses.TryGetValue(value, out var users) ||
					users.Count == 0)
				{
					return false;
				}
				foreach (var user in users)
				{
					if (IsFixedPlatformBaseCopy(
							function,
							user,
							value,
							convention.BaseRegister))
					{
						fixedCopies.Add(user);
						continue;
					}
					if (user.Operation != M68kMachineOperation.Copy ||
						user.Uses is not [var copySource] ||
						copySource != value ||
						user.Definitions is not [var copyDestination] ||
						function.Values[copyDestination].PrecoloredRegister is not null)
					{
						return false;
					}
					copyIdsToRemove.Add(user.Id);
					valuesToRemove.Add(copyDestination);
					if (!CollectFixedCopies(copyDestination))
					{
						return false;
					}
				}
				return true;
			}

			if (!CollectFixedCopies(source) || fixedCopies.Count == 0 ||
				!CanMovePlatformBaseLoadToCopies(function, load, fixedCopies))
			{
				continue;
			}

			var userIds = fixedCopies.Select(static user => user.Id).ToHashSet();
			foreach (var block in function.Blocks)
			{
				for (var index = 0; index < block.Instructions.Count; index++)
				{
					var instruction = block.Instructions[index];
					if (!userIds.Contains(instruction.Id))
					{
						continue;
					}
					var destination = instruction.Definitions[0];
					block.Instructions[index] = instruction with
					{
						Operation = M68kMachineOperation.PlatformBaseLoad,
						Uses = [],
						Clobbers = M68kRegisterSet.None,
						MemoryEffect = load.MemoryEffect,
						IsSafepoint = false,
						MayThrow = false,
						ProducesConditionCodes = false,
						ConsumesConditionCodes = false,
						SourceInstruction = load.SourceInstruction,
						SpillSlotIndex = null,
						ArgumentIndex = null,
						StackVarargsRegister = null,
						Immediate = null,
						AllowCopyCoalescing = true,
						TransportsManagedByrefOwner = false,
						BranchCondition = null,
						RequiresLiveCallerFrame = false,
						ConstantValue = null,
						Origin = load.Origin,
						LogicalCall = null,
						ExactMemoryAccesses =
						[
							.. load.ExactMemoryAccesses.Select(access =>
								access with
								{
									ValueId = access.ValueId == source
										? destination
										: access.ValueId
								})
						],
						PlatformBaseConvention = convention,
						HasExplicitPlatformBase = false,
						MemoryOffset = 0,
						MemorySize = 0
					};
				}
			}

			foreach (var block in function.Blocks)
			{
				block.Instructions.RemoveAll(instruction =>
					instruction.Id == load.Id ||
					copyIdsToRemove.Contains(instruction.Id));
			}
			foreach (var value in valuesToRemove)
			{
				function.Values.Remove(value);
			}
		}
	}

	private static bool CanMovePlatformBaseLoadToCopies(
		M68kMachineFunction function,
		M68kMachineInstruction load,
		IReadOnlyList<M68kMachineInstruction> fixedCopies)
	{
		var block = function.Blocks.FirstOrDefault(block =>
			block.Instructions.Any(instruction => instruction.Id == load.Id));
		if (block is null) return false;
		var cacheRegister = load.PlatformBaseConvention is
			{ BaseSource: M68kExternalBaseSource.CachedPointer,
				CacheRegister: { } register }
				? (M68kRegister?)register
				: null;
		var loadIndex = block.Instructions.FindIndex(instruction =>
			instruction.Id == load.Id);
		foreach (var copy in fixedCopies)
		{
			var copyIndex = block.Instructions.FindIndex(instruction =>
				instruction.Id == copy.Id);
			if (copyIndex <= loadIndex) return false;
			for (var index = loadIndex + 1; index < copyIndex; index++)
			{
				var crossed = block.Instructions[index];
				if ((crossed.MemoryEffect &
						(M68kMachineMemoryEffect.Write |
						 M68kMachineMemoryEffect.Volatile)) != 0 ||
					crossed.Operation is M68kMachineOperation.Call or
						M68kMachineOperation.Branch or
						M68kMachineOperation.ConditionalBranch or
						M68kMachineOperation.Switch or
						M68kMachineOperation.Return or
						M68kMachineOperation.Throw ||
					crossed.MayThrow || crossed.IsSafepoint ||
					crossed.Clobbers != M68kRegisterSet.None ||
					cacheRegister is { } cache && crossed.Definitions.Any(
						definition => function.Values[definition].PrecoloredRegister == cache))
				{
					// A writable slot or cached register can change here. Keep the
					// original read before the barrier, including on EH/GC paths.
					return false;
				}
			}
		}
		return true;
	}

	private static bool IsFixedPlatformBaseCopy(
		M68kMachineFunction function,
		M68kMachineInstruction instruction,
		int source,
		M68kRegister baseRegister) =>
		instruction.Operation == M68kMachineOperation.Copy &&
		instruction.Uses is [var copySource] &&
		copySource == source &&
		instruction.Definitions is [var destination] &&
		function.Values[destination].PrecoloredRegister == baseRegister;
}
