/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal enum M68kFrameClearLoopKind
{
	Scratch,
	PreserveData,
	PreserveDataAndAddress
}

internal readonly record struct M68kFrameClearRun(int Start, int Count, bool Loop);

internal sealed record M68kFrameClearRunPlan(
	IReadOnlyList<M68kFrameClearRun> Runs,
	int OriginalBytes,
	int PlannedBytes,
	long OriginalCycles,
	long PlannedCycles);

internal static class M68kFrameClearRunPlanner
{
	internal static M68kFrameClearRunPlan? Create(
		IReadOnlyList<int> displacements,
		bool hasUnrolledZeroRegister,
		M68kFrameClearLoopKind loopKind)
	{
		if (displacements.Count < 8) return null;
		var groups = new List<M68kFrameClearRun>();
		for (var start = 0; start < displacements.Count;)
		{
			var end = start + 1;
			while (end < displacements.Count &&
				(long)displacements[end - 1] + 4 == displacements[end]) end++;
			groups.Add(new(start, end - start, false));
			start = end;
		}
		// Preserve the existing single-run selection and its exact encoding.
		if (groups.Count == 1) return null;
		var minimum = loopKind switch
		{
			M68kFrameClearLoopKind.Scratch => 8,
			M68kFrameClearLoopKind.PreserveData => 13,
			_ => 17
		};
		var storeCycles = hasUnrolledZeroRegister ? 16 : 24;
		var originalBytes = checked(displacements.Count * 4 + (hasUnrolledZeroRegister ? 2 : 0));
		var originalCycles = (long)displacements.Count * storeCycles + (hasUnrolledZeroRegister ? 4 : 0);
		var plannedBytes = 0;
		long plannedCycles = 0;
		var runs = new List<M68kFrameClearRun>();
		var anyLoop = false;
		foreach (var group in groups)
		{
			var cost = LoopCost(group.Count, loopKind);
			var loop = group.Count >= minimum &&
				cost.Bytes < (long)group.Count * 4 && cost.Cycles <= (long)group.Count * storeCycles;
			if (loop)
			{
				runs.Add(group with { Loop = true });
				plannedBytes = checked(plannedBytes + cost.Bytes);
				plannedCycles += cost.Cycles;
				anyLoop = true;
			}
			else
			{
				if (runs.Count != 0 && !runs[^1].Loop)
					runs[^1] = runs[^1] with { Count = runs[^1].Count + group.Count };
				else
				{
					runs.Add(group);
					if (hasUnrolledZeroRegister) { plannedBytes += 2; plannedCycles += 4; }
				}
				plannedBytes = checked(plannedBytes + group.Count * 4);
				plannedCycles += (long)group.Count * storeCycles;
			}
		}
		// Every short segment after a loop pays for its own MOVEQ. A data
		// register used by that loop's counter cannot be assumed still zero.
		return anyLoop && plannedBytes < originalBytes && plannedCycles <= originalCycles
			? new(runs, originalBytes, plannedBytes, originalCycles, plannedCycles) : null;
	}

	private static (int Bytes, long Cycles) LoopCost(int count, M68kFrameClearLoopKind kind)
	{
		var groups = count / 4;
		if (groups == 0) return (int.MaxValue, long.MaxValue);
		var counterIsQuick = groups - 1 <= 127;
		var preserveBytes = kind == M68kFrameClearLoopKind.Scratch ? 0 : 8;
		var preserveCycles = kind switch
		{
			M68kFrameClearLoopKind.Scratch => 0,
			M68kFrameClearLoopKind.PreserveData => 48,
			_ => 68
		};
		// MC68000 LEA d16, MOVEQ zero, optional remainder stores, immediate
		// counter, four postincrement stores and DBRA. Include the final DBRA
		// (14 cycles) and either two scalar saves/restores or a three-reg MOVEM.
		return (4 + 2 + (count % 4) * 2 + (counterIsQuick ? 2 : 6) + 8 + 4 + preserveBytes,
			8 + 4 + (counterIsQuick ? 4 : 12) + (long)count * 12 +
			(groups - 1L) * 10 + 14 + preserveCycles);
	}
}
