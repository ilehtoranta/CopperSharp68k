/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal readonly record struct M68kTargetCost(
	long Bytes,
	long Cycles,
	long AddedLiveValuePressure)
{
	public static M68kTargetCost operator +(
		M68kTargetCost left,
		M68kTargetCost right) =>
		new(
			left.Bytes + right.Bytes,
			left.Cycles + right.Cycles,
			left.AddedLiveValuePressure + right.AddedLiveValuePressure);
}

internal static class M68kTargetCostModel
{
	public static M68kTargetCost Estimate(
		IEnumerable<M68kMachineInstruction> instructions,
		M68kCpuTarget cpu,
		int loopDepth = 0)
	{
		var cost = new M68kTargetCost();
		var values = new HashSet<int>();
		foreach (var instruction in instructions)
		{
			var bytes = EstimatedBytes(instruction);
			var cycles = EstimatedCycles(instruction, cpu);
			var loopWeight = 1L;
			for (var depth = 0; depth < loopDepth && loopWeight < 1_000_000; depth++)
			{
				loopWeight *= 10;
			}
			values.UnionWith(instruction.Uses);
			values.UnionWith(instruction.Definitions);
			cost += new M68kTargetCost(bytes, cycles * loopWeight, 0);
		}
		return cost with
		{
			AddedLiveValuePressure = Math.Max(0, values.Count - 4)
		};
	}

	public static bool Accept(
		M68kTargetCost before,
		M68kTargetCost after,
		M68kCpuTarget cpu)
	{
		if (after.Bytes <= before.Bytes && after.Cycles <= before.Cycles &&
			after.AddedLiveValuePressure <= before.AddedLiveValuePressure)
		{
			return true;
		}
		var sizeWeight = cpu switch
		{
			M68kCpuTarget.M68000 => 2.0,
			M68kCpuTarget.M68020 => 1.0,
			_ => 0.5
		};
		return Score(after, sizeWeight) < Score(before, sizeWeight);
	}

	public static long Score(M68kTargetCost cost, M68kCpuTarget cpu)
	{
		var sizeWeight = cpu switch
		{
			M68kCpuTarget.M68000 => 2.0,
			M68kCpuTarget.M68020 => 1.0,
			_ => 0.5
		};
		return checked((long)Math.Ceiling(Score(cost, sizeWeight)));
	}

	private static double Score(M68kTargetCost cost, double sizeWeight) =>
		cost.Cycles + sizeWeight * cost.Bytes +
		cost.AddedLiveValuePressure * 16.0;

	private static int EstimatedBytes(M68kMachineInstruction instruction) =>
		instruction.Operation switch
		{
			M68kMachineOperation.Copy => 2,
			M68kMachineOperation.Constant =>
				instruction.ConstantValue is { } constant &&
				constant.TryGetIntegral(out var value) && value is >= -128 and <= 127
					? 2
					: 6,
			M68kMachineOperation.Call => 4,
			M68kMachineOperation.Multiply or M68kMachineOperation.Divide or
				M68kMachineOperation.Remainder => 4,
			M68kMachineOperation.Branch or M68kMachineOperation.ConditionalBranch => 2,
			M68kMachineOperation.Switch => 8,
			M68kMachineOperation.Return => 2,
			_ => 2
		};

	private static int EstimatedCycles(
		M68kMachineInstruction instruction,
		M68kCpuTarget cpu)
	{
		var classic = instruction.Operation switch
		{
			M68kMachineOperation.Copy => 4,
			M68kMachineOperation.Constant => 8,
			M68kMachineOperation.Multiply => 42,
			M68kMachineOperation.Divide or M68kMachineOperation.Remainder => 140,
			M68kMachineOperation.Call => 32,
			M68kMachineOperation.Branch => 10,
			M68kMachineOperation.ConditionalBranch => 10,
			M68kMachineOperation.Return => 16,
			_ => 8
		};
		return cpu == M68kCpuTarget.M68000
			? classic
			: cpu == M68kCpuTarget.M68020
				? Math.Max(2, classic / 2)
				: Math.Max(1, classic / 3);
	}
}
