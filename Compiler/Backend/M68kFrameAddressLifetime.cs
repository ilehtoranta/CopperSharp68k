/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

/// <summary>
/// A may-analysis for addresses backed by the current invocation. Unlike GC
/// byref provenance, this follows raw pointer/integer conversions too: converting
/// an argument-home address to byte* does not make its stack storage disposable.
/// </summary>
internal static class M68kFrameAddressLifetime
{
	public static IReadOnlySet<int> FindDependentValues(M68kMachineFunction function)
	{
		var instructions = function.Blocks.SelectMany(static block => block.Instructions).ToArray();
		var dependent = new HashSet<int>();
		var localPointerHomes = new HashSet<int>();
		var argumentPointerHomes = new HashSet<int>();
		foreach (var instruction in instructions)
		{
			if (instruction.Operation is M68kMachineOperation.LocalAddress or
				M68kMachineOperation.ArgumentAddress or M68kMachineOperation.DynamicStackAllocate or
				M68kMachineOperation.OutgoingArgumentReserve ||
				instruction.Operation is M68kMachineOperation.AggregateFieldLoad or
					M68kMachineOperation.AggregateArrayLoad or M68kMachineOperation.AggregateIndirectLoad)
			{
				dependent.UnionWith(instruction.Definitions);
			}
		}

		bool changed;
		do
		{
			changed = false;
			foreach (var phi in function.Blocks.SelectMany(static block => block.Phis))
			{
				if (phi.Inputs.Values.Any(dependent.Contains)) changed |= dependent.Add(phi.Definition);
			}
			foreach (var instruction in instructions)
			{
				var usesFrame = instruction.Uses.Any(dependent.Contains) ||
					instruction.Operation == M68kMachineOperation.Call &&
					instruction.LogicalCall?.ArgumentValueIds.Any(dependent.Contains) == true;
				if (usesFrame && instruction.ArgumentIndex is { } home)
				{
					if (instruction.Operation == M68kMachineOperation.LocalStore)
						changed |= localPointerHomes.Add(home);
					if (instruction.Operation == M68kMachineOperation.ArgumentStore)
						changed |= argumentPointerHomes.Add(home);
				}
				var loadsPointer = instruction.ArgumentIndex is { } loadedHome &&
					(instruction.Operation == M68kMachineOperation.LocalLoad && localPointerHomes.Contains(loadedHome) ||
					 instruction.Operation == M68kMachineOperation.ArgumentLoad && argumentPointerHomes.Contains(loadedHome));
				var transportsPointer = instruction.Operation is M68kMachineOperation.Copy or
					M68kMachineOperation.Convert or M68kMachineOperation.Address or
					M68kMachineOperation.Add or M68kMachineOperation.Subtract or
					M68kMachineOperation.And or M68kMachineOperation.Or or M68kMachineOperation.Xor or
					M68kMachineOperation.Shift or M68kMachineOperation.Multiply or
					M68kMachineOperation.Divide or M68kMachineOperation.Remainder or
					M68kMachineOperation.Negate or M68kMachineOperation.Not or M68kMachineOperation.Call;
				if (loadsPointer || usesFrame && transportsPointer)
				{
					foreach (var definition in instruction.Definitions)
					{
						if (function.Values[definition].Width == M68kMachineValueWidth.Long)
							changed |= dependent.Add(definition);
					}
				}
			}
		}
		while (changed);
		return dependent;
	}
}
