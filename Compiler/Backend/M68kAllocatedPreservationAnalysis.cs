/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal static class M68kAllocatedPreservationAnalysis
{
	internal static IReadOnlyList<M68kRegister> RequiredRegisters(
		M68kMachineFunction function,
		M68kAllocationResult allocation,
		IReadOnlyList<M68kRegister> planned,
		IReadOnlySet<int> nonEmittedConstants,
		IReadOnlyDictionary<int, M68kRegisterSet> selectedClobbers,
		IEnumerable<M68kRegister> plannedScratchRegisters)
	{
		// A suppressed constant no longer writes its allocated location. Keep
		// every other physical value, including incoming arguments and phi
		// transports, even if it currently appears only as an instruction use.
		var values = function.Blocks.SelectMany(static block => block.Instructions)
			.SelectMany(static instruction => instruction.Uses.Concat(instruction.Definitions))
			.Where(value => !nonEmittedConstants.Contains(value))
			.Concat(function.Blocks.SelectMany(static block => block.Phis)
				.SelectMany(static phi => phi.Inputs.Values.Append(phi.Definition)))
			.ToHashSet();
		var required = allocation.Registers.Where(location => values.Contains(location.Key))
			.SelectMany(static location => location.Value.OccupiedRegisters.Enumerate())
			.Concat(function.Blocks.SelectMany(static block => block.Instructions)
				.SelectMany(instruction => selectedClobbers.GetValueOrDefault(
					instruction.Id, instruction.Clobbers).Enumerate()))
			.Concat(plannedScratchRegisters)
			.ToHashSet();
		if (function.HasDynamicStackAllocation) required.Add(M68kRegister.A5);
		return planned.Where(required.Contains).ToArray();
	}
}
