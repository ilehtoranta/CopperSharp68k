/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

/// <summary>
/// Reclaims argument homes whose address stopped escaping after inlining. Run
/// before allocation so register liveness, frame offsets and unwind information
/// all describe the same promoted representation.
/// </summary>
internal static class M68kArgumentHomeOptimizer
{
	public static void Run(M68kMachineFunction function,
		IReadOnlyList<M68kRegister?>? argumentRegisters = null)
	{
		if (function.HasExceptionHandlers || function.ArgumentHomes.Count == 0)
		{
			return;
		}
		var entry = function.Blocks.Single(block => block.Id == function.EntryBlockId);
		foreach (var home in function.ArgumentHomes.Values.ToArray())
		{
			if (home.HasGcReferences)
			{
				continue;
			}
			var accesses = function.Blocks.SelectMany(static block => block.Instructions)
				.Where(instruction =>
					instruction.ArgumentIndex == home.Index && instruction.Operation is
						(M68kMachineOperation.ArgumentLoad or
						 M68kMachineOperation.ArgumentStore or
						 M68kMachineOperation.ArgumentAddress) ||
					instruction.ExactMemoryAccesses.Any(access =>
						access.Object.Kind == M68kMemoryObjectKind.ArgumentHome &&
						access.Object.Identity == home.Index.ToString(
							System.Globalization.CultureInfo.InvariantCulture)))
				.ToArray();
			if (accesses.Length == 0)
			{
				function.ArgumentHomes.Remove(home.Index);
				continue;
			}
			if (home.Size != 4 || accesses.Any(instruction =>
				instruction.Operation != M68kMachineOperation.ArgumentLoad ||
				instruction.ArgumentIndex != home.Index ||
				instruction.Definitions.Length != 1 ||
				instruction.MemoryOffset != 0 || instruction.MemorySize is not (0 or 4) ||
				(instruction.MemoryEffect & M68kMachineMemoryEffect.Volatile) != 0 ||
				function.Values[instruction.Definitions[0]].Width != M68kMachineValueWidth.Long ||
				function.ManagedByrefTypes.ContainsKey(instruction.Definitions[0])))
			{
				continue;
			}
			var prototype = function.Values[accesses[0].Definitions[0]];
			if (accesses.Any(instruction =>
				function.Values[instruction.Definitions[0]].Kind != prototype.Kind))
			{
				continue;
			}
			var argument = entry.Instructions.FirstOrDefault(instruction =>
				instruction.Operation == M68kMachineOperation.Argument &&
				instruction.ArgumentIndex == home.Index &&
				instruction.Definitions is [var definition] &&
				function.Values[definition].Kind == prototype.Kind &&
				function.Values[definition].Width == prototype.Width);
			int value;
			if (argument is not null)
			{
				value = argument.Definitions[0];
				if (function.Values[value].PrecoloredRegister is not null)
				{
					var copies = entry.Instructions.Where(instruction =>
						instruction.Operation == M68kMachineOperation.Copy &&
						instruction.IlOffset == entry.StartIlOffset &&
						instruction.Uses is [var source] && source == value &&
						instruction.Definitions.Length == 1).ToArray();
					if (copies.Length != 1) continue;
					var copied = function.Values[copies[0].Definitions[0]];
					if (copied.Kind != prototype.Kind || copied.Width != prototype.Width)
						continue;
					// Use the SSA home after the entry transfer, not the fixed ABI
					// register whose lifetime ends at that canonical copy.
					value = copies[0].Definitions[0];
				}
				else if (argumentRegisters is not null &&
					home.Index < argumentRegisters.Count && argumentRegisters[home.Index] is not null)
				{
					continue; // An unexpected ABI shape must keep its memory home.
				}
			}
			else
			{
				value = function.CreateValue(prototype.Kind, prototype.Width,
					prototype.AllowedRegisters).Id;
				if (argumentRegisters is not null && home.Index < argumentRegisters.Count &&
					argumentRegisters[home.Index] is { } register)
				{
					var incoming = function.CreateValue(prototype.Kind, prototype.Width,
						M68kRegisterSet.From(register), precoloredRegister: register).Id;
					// Match the builder's ABI contract: incoming fixed value followed
					// by exactly one canonical entry copy. The prologue schedules all
					// these transfers together, even when the first real read is later.
					entry.Instructions.InsertRange(0,
					[
						function.CreateInstruction(M68kMachineOperation.Argument,
							entry.StartIlOffset, definitions: [incoming], argumentIndex: home.Index),
						function.CreateInstruction(M68kMachineOperation.Copy,
							entry.StartIlOffset, uses: [incoming], definitions: [value])
					]);
				}
				else
				{
					entry.Instructions.Insert(0, function.CreateInstruction(
						M68kMachineOperation.Argument, entry.StartIlOffset,
						definitions: [value], argumentIndex: home.Index));
				}
			}
			var ids = accesses.Select(static instruction => instruction.Id).ToHashSet();
			foreach (var block in function.Blocks)
			{
				for (var index = 0; index < block.Instructions.Count; index++)
				{
					var instruction = block.Instructions[index];
					if (ids.Contains(instruction.Id))
					{
						block.Instructions[index] = instruction with
						{
							Operation = M68kMachineOperation.Copy,
							Uses = [value],
							MemoryEffect = M68kMachineMemoryEffect.None,
							ArgumentIndex = null,
							ExactMemoryAccesses = [],
							MemoryOffset = 0,
							MemorySize = 0
						};
					}
				}
			}
			function.ArgumentHomes.Remove(home.Index);
		}
	}
}
