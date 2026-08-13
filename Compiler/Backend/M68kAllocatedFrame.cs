/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal sealed record M68kAllocatedFramePlan(
	IReadOnlyDictionary<int, int> LocalOffsets,
	IReadOnlyDictionary<int, int> ArgumentHomeOffsets,
	IReadOnlyDictionary<int, int> SpillOffsets,
	IReadOnlyDictionary<int, int> RootOffsets,
	IReadOnlySet<int> GcHomeOffsets,
	int? ActiveExceptionOffset,
	int? PendingActionOffset,
	int? LeaveContinuationOffset,
	int? ParallelCopyTemporaryOffset,
	IReadOnlyList<M68kRegister> CalleeSavedRegisters,
	int FrameBytes);

internal static class M68kAllocatedFramePlanner
{
	public static M68kAllocatedFramePlan Create(
		M68kMachineFunction function,
		M68kAllocationResult allocation,
		M68kSpillLayout spills,
		M68kSafepointPlan safepoints,
		M68kParallelCopyPlan parallelCopies)
	{
		var nextOffset = 0;
		int? activeExceptionOffset = null;
		int? pendingActionOffset = null;
		int? leaveContinuationOffset = null;
		if (function.HasExceptionHandlers)
		{
			activeExceptionOffset = nextOffset;
			nextOffset += 4;
			pendingActionOffset = nextOffset;
			nextOffset += 4;
			leaveContinuationOffset = nextOffset;
			nextOffset += 4;
		}
		var localOffsets = new Dictionary<int, int>();
		var argumentHomeOffsets = new Dictionary<int, int>();
		var gcHomeOffsets = new HashSet<int>();
		foreach (var home in function.LocalHomes.Values.OrderBy(static home => home.Index))
		{
			nextOffset = Align(nextOffset, Math.Min(home.Size, 4));
			localOffsets.Add(home.Index, nextOffset);
			if (home.IsGcReference)
			{
				gcHomeOffsets.Add(nextOffset);
			}
			foreach (var referenceOffset in home.GcReferenceOffsets ?? [])
			{
				gcHomeOffsets.Add(checked(nextOffset + referenceOffset));
			}
			nextOffset = checked(nextOffset + home.Size);
		}
		foreach (var home in function.ArgumentHomes.Values.OrderBy(static home => home.Index))
		{
			nextOffset = Align(nextOffset, Math.Min(home.Size, 4));
			argumentHomeOffsets.Add(home.Index, nextOffset);
			if (home.IsGcReference)
			{
				gcHomeOffsets.Add(nextOffset);
			}
			foreach (var referenceOffset in home.GcReferenceOffsets ?? [])
			{
				gcHomeOffsets.Add(checked(nextOffset + referenceOffset));
			}
			nextOffset = checked(nextOffset + home.Size);
		}
		var spillBase = Align(nextOffset, 4);
		var spillOffsets = spills.Slots.Values
			.DistinctBy(static slot => slot.Index)
			.ToDictionary(
				static slot => slot.Index,
				slot => checked(spillBase + slot.Offset));
		nextOffset = checked(spillBase + Align(spills.FrameBytes, 4));
		var rootOffsets = new Dictionary<int, int>();
		foreach (var slot in safepoints.RootSlotByValue.Values
			.Distinct()
			.Order())
		{
			rootOffsets.Add(slot, nextOffset);
			nextOffset = checked(nextOffset + 4);
		}
		int? parallelCopyTemporaryOffset = null;
		if (parallelCopies.NeedsTemporarySlot)
		{
			parallelCopyTemporaryOffset = nextOffset;
			nextOffset = checked(nextOffset + 4);
		}
		// Allocation results may retain a location for a value removed by a late
		// machine-IR cleanup.  Do not let such stale locations enlarge the ABI
		// save mask; only values still referenced by the final function need to
		// contribute occupied registers.
		var referencedValues = function.Blocks
			.SelectMany(static block =>
				block.Phis.SelectMany(static phi =>
					phi.Inputs.Values.Append(phi.Definition))
				.Concat(block.Instructions.SelectMany(static instruction =>
					instruction.Uses.Concat(instruction.Definitions))))
			.ToHashSet();
		var calleeSaved = allocation.Registers
			.Where(location => referencedValues.Contains(location.Key))
			.Select(static location => location.Value)
			.SelectMany(static location =>
				location.OccupiedRegisters.Enumerate())
			.Concat(function.Blocks
				.SelectMany(static block => block.Instructions)
				.SelectMany(static instruction =>
					instruction.Clobbers.Enumerate()))
			.Where(static register =>
				register is >= M68kRegister.D2 and <= M68kRegister.D7 or
					>= M68kRegister.A2 and <= M68kRegister.A6)
			.Where(register => !function.ReservedRegisters.Contains(register))
			.Distinct()
			.Order()
			.ToArray();
		if (function.HasDynamicStackAllocation &&
			!calleeSaved.Contains(M68kRegister.A5))
		{
			calleeSaved = calleeSaved
				.Append(M68kRegister.A5)
				.Order()
				.ToArray();
		}
		if (!function.PreserveCalleeSavedRegisters &&
			!function.HasDynamicStackAllocation)
		{
			calleeSaved = [];
		}
		return new M68kAllocatedFramePlan(
			localOffsets,
			argumentHomeOffsets,
			spillOffsets,
			rootOffsets,
			gcHomeOffsets,
			activeExceptionOffset,
			pendingActionOffset,
			leaveContinuationOffset,
			parallelCopyTemporaryOffset,
			calleeSaved,
			Align(nextOffset, 4));
	}

	private static int Align(int value, int alignment) =>
		checked((value + alignment - 1) & -alignment);
}
