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
		// every other physical value conservatively, then prune only proven
		// read-only incoming registers below.
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
		// Incoming callee-saved arguments are reads, not writes. Prune a save
		// only when every definition in that location is an incoming argument
		// or a same-location copy, with no phi transfer, scratch or clobber.
		var instructions = function.Blocks.SelectMany(block => block.Instructions).ToArray();
		foreach (var register in planned)
		{
			if (plannedScratchRegisters.Contains(register) ||
				instructions.Any(instruction => selectedClobbers.GetValueOrDefault(
					instruction.Id, instruction.Clobbers).Contains(register)) ||
				function.Blocks.Any(block => block.Phis.Any(phi =>
					allocation.Registers[phi.Definition].OccupiedRegisters.Contains(register)))) continue;
			bool incoming = false, written = false;
			foreach (var instruction in instructions)
			{
				foreach (var definition in instruction.Definitions)
				{
					if (!allocation.Registers[definition].OccupiedRegisters.Contains(register)) continue;
					if (instruction.Operation == M68kMachineOperation.Argument &&
						function.Values[definition].PrecoloredRegister == register) { incoming = true; continue; }
					if (instruction.Operation == M68kMachineOperation.Copy && instruction.Uses is [var source] &&
						allocation.Registers[source] == allocation.Registers[definition] &&
						function.Values[source].Kind == function.Values[definition].Kind &&
						function.Values[source].Width == function.Values[definition].Width) continue;
					written = true;
				}
			}
			if (incoming && !written) required.Remove(register);
		}
		if (function.HasDynamicStackAllocation) required.Add(M68kRegister.A5);
		return planned.Where(required.Contains).ToArray();
	}
}
