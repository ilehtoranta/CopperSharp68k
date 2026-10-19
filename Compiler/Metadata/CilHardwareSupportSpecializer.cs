/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using CopperSharp.Compiler.Framework;

namespace CopperSharp.Compiler.Metadata;

internal static class CilHardwareSupportSpecializer
{
	public static CilMethod Specialize(CilMethod method, CompilationModule module)
	{
		if ((module.FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && !module.IsPinnedFloatingHardwareMethod(method)) || method.Instructions.Count == 0) return method;
		var instructions = method.Instructions.ToArray();
		var targets = instructions.SelectMany(instruction => instruction.Operand switch {
			int target when instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch => [target],
			int[] branches when instruction.OpCode == OpCodes.Switch => branches,
			_ => Array.Empty<int>()
		}).ToHashSet();
		var changed = false;
		for (var index = 0; index < instructions.Length; index++)
		{
			var call = instructions[index];
			if (call.OpCode != OpCodes.Call ||
				!FrameworkImplementationProfile.IsUnsupportedHardwareSupportGetter(module.DescribeFrameworkMethodToken((int)call.Operand!, method, call.Offset))) continue;
			instructions[index] = call with { OpCode = OpCodes.Ldc_I4_0, Operand = null };
			changed = true;
			// Preserve exception-region instruction ranges. A standalone getter or
			// a protected call still receives the correct target Boolean constant.
			if (method.ExceptionRegions.Count != 0 || index + 1 == instructions.Length || targets.Contains(instructions[index + 1].Offset)) continue;
			var branch = instructions[index + 1];
			if (branch.OpCode == OpCodes.Brtrue || branch.OpCode == OpCodes.Brtrue_S ||
				branch.OpCode == OpCodes.Brfalse || branch.OpCode == OpCodes.Brfalse_S)
			{
				instructions[index] = call with { OpCode = OpCodes.Nop, Operand = null };
				instructions[index + 1] = branch.OpCode == OpCodes.Brfalse || branch.OpCode == OpCodes.Brfalse_S
					? branch with { OpCode = OpCodes.Br } : branch with { OpCode = OpCodes.Nop, Operand = null };
			}
		}
		if (!changed) return method;
		if (method.ExceptionRegions.Count != 0) return method with { Instructions = instructions };
		var byOffset = instructions.ToDictionary(instruction => instruction.Offset);
		var reachable = new HashSet<int>(); var pending = new Stack<int>(); pending.Push(instructions[0].Offset);
		while (pending.TryPop(out var offset))
		{
			if (!reachable.Add(offset) || !byOffset.TryGetValue(offset, out var instruction)) continue;
			if (instruction.OpCode.FlowControl is FlowControl.Return or FlowControl.Throw) continue;
			if (instruction.OpCode == OpCodes.Switch)
				foreach (var target in (int[])instruction.Operand!) pending.Push(target);
			else if (instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch)
				pending.Push((int)instruction.Operand!);
			if (instruction.OpCode.FlowControl != FlowControl.Branch) pending.Push(instruction.NextOffset);
		}
		return method with { Instructions = instructions.Where(instruction => reachable.Contains(instruction.Offset)).ToArray() };
	}
}
