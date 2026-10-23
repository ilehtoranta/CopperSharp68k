/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal sealed record M68kIncomingScalarSpill(M68kMachineInstruction? Copy, M68kMachineInstruction Store, M68kMachineValueWidth Width);

/// <summary>
/// Scalar argument homes written directly by the prologue need no persistent
/// register home. Share this proof between interference and actual emission.
/// Pair homes retain their existing simultaneous-register constraints.
/// </summary>
internal static class M68kIncomingArgumentSpills
{
	private static readonly IReadOnlyDictionary<int, M68kIncomingScalarSpill> Empty = new Dictionary<int, M68kIncomingScalarSpill>();
	public static IReadOnlyDictionary<int, M68kIncomingScalarSpill> FindScalarSpills(M68kMachineFunction function)
	{
		var entry = function.Blocks.Single(b => b.Id == function.EntryBlockId);
		if (!entry.Instructions.Any(i => i.Operation is M68kMachineOperation.Argument or M68kMachineOperation.Copy &&
			i.Definitions is [var value] && function.Values[value].IsSpillTemporary &&
			function.Values[value].Width is M68kMachineValueWidth.Byte or M68kMachineValueWidth.Word or M68kMachineValueWidth.Long)) return Empty;
		var users = new Dictionary<int, List<M68kMachineInstruction>>();
		foreach (var instruction in function.Blocks.SelectMany(b => b.Instructions))
			foreach (var use in instruction.Uses.Distinct())
			{
				if (!users.TryGetValue(use, out var list)) users.Add(use, list = []);
				list.Add(instruction);
			}
		var phiUses = function.Blocks.SelectMany(b => b.Phis).SelectMany(p => p.Inputs.Values).ToHashSet();
		var positions = entry.Instructions.Select((instruction, index) => (instruction.Id, index)).ToDictionary(p => p.Id, p => p.index);
		var result = new Dictionary<int, M68kIncomingScalarSpill>();
		foreach (var argument in entry.Instructions)
		{
			if (argument.Operation != M68kMachineOperation.Argument || argument.Definitions is not [var incoming]) continue;
			var value = incoming;
			var position = positions[argument.Id];
			M68kMachineInstruction? copy = null;
			if (function.Values[incoming].PrecoloredRegister is not null)
			{
				if (phiUses.Contains(incoming) || !users.TryGetValue(incoming, out var copies) || copies is not [var candidate] ||
					candidate.Operation != M68kMachineOperation.Copy || candidate.Uses is not [var source] || source != incoming ||
					candidate.IlOffset != entry.StartIlOffset || candidate.Definitions is not [var copied] ||
					!positions.TryGetValue(candidate.Id, out position)) continue;
				copy = candidate;
				value = copied;
			}
			var machineValue = function.Values[value];
			if (!machineValue.IsSpillTemporary || machineValue.Width is not
				(M68kMachineValueWidth.Byte or M68kMachineValueWidth.Word or M68kMachineValueWidth.Long) ||
				phiUses.Contains(value) || position + 1 >= entry.Instructions.Count ||
				entry.Instructions[position + 1] is not { Operation: M68kMachineOperation.SpillStore, Uses: [var stored] } store || stored != value ||
				!users.TryGetValue(value, out var stores) || stores is not [var only] || only.Id != store.Id) continue;
			result.Add(argument.Id, new(copy, store, machineValue.Width));
		}
		return result;
	}
}
