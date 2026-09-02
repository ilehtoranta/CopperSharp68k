/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

/// <summary>
/// Finds private local longwords definitely written before their first possible
/// observation along the method's entry prefix. Address creation/copies alone do
/// not observe storage; all other unknown pointer uses do. Do not cross calls,
/// safepoints or control-flow splits, and retain initialization of GC/EH homes.
/// </summary>
internal static class M68kFrameInitializationAnalysis
{
	public static IReadOnlySet<(int Home, int Offset)> FindEntryOverwrites(
		M68kMachineFunction function,
		Func<M68kMachineInstruction, int> writtenBytes)
	{
		var result = new HashSet<(int Home, int Offset)>();
		if (function.HasExceptionHandlers)
		{
			return result;
		}
		var homes = function.LocalHomes.Values
			.Where(static home => home.Initialize && !home.HasGcReferences)
			.ToDictionary(static home => home.Index, static home => new byte[home.Size]);
		// 0 = not yet observed or written, 1 = overwritten first, 2 = observed first.
		var addresses = new Dictionary<int, int>();
		void Observe(int home)
		{
			if (homes.TryGetValue(home, out var bytes))
			{
				for (var index = 0; index < bytes.Length; index++)
				{
					if (bytes[index] == 0) bytes[index] = 2;
				}
			}
		}
		void Write(int home, int offset, int size)
		{
			if (homes.TryGetValue(home, out var bytes) && offset >= 0 &&
				size > 0 && (long)offset + size <= bytes.Length)
			{
				for (var index = offset; index < offset + size; index++)
				{
					if (bytes[index] == 0) bytes[index] = 1;
				}
			}
		}
		var entry = function.Blocks.Single(block => block.Id == function.EntryBlockId);
		foreach (var instruction in entry.Instructions)
		{
			if (instruction.IsSafepoint ||
				(instruction.MemoryEffect & M68kMachineMemoryEffect.Volatile) != 0 ||
				instruction.Operation is M68kMachineOperation.Call or
					M68kMachineOperation.TypeInitialize or M68kMachineOperation.Branch or
					M68kMachineOperation.ConditionalBranch or M68kMachineOperation.Switch or
					M68kMachineOperation.Return or M68kMachineOperation.Throw)
			{
				break;
			}
			foreach (var access in instruction.ExactMemoryAccesses)
			{
				if (access.Object.Kind == M68kMemoryObjectKind.FrameSlot &&
					access.Kind is M68kExactMemoryAccessKind.Read or M68kExactMemoryAccessKind.Escape &&
					int.TryParse(access.Object.Identity,
						System.Globalization.NumberStyles.Integer,
						System.Globalization.CultureInfo.InvariantCulture, out var observedHome))
				{
					Observe(observedHome);
				}
			}
			if (instruction.Operation == M68kMachineOperation.LocalAddress &&
				instruction.ArgumentIndex is { } addressHome &&
				instruction.Definitions is [var address])
			{
				addresses[address] = addressHome;
				continue;
			}
			if (instruction.Operation == M68kMachineOperation.Copy &&
				instruction.Uses is [var source] && instruction.Definitions is [var copy] &&
				function.Values[copy].Width == M68kMachineValueWidth.Long &&
				addresses.TryGetValue(source, out var copiedHome))
			{
				addresses[copy] = copiedHome;
				continue;
			}
			if (instruction.Operation == M68kMachineOperation.AggregateIndirectInitialize &&
				instruction.Uses is [var destination] &&
				addresses.TryGetValue(destination, out var initializedHome))
			{
				Write(initializedHome, 0, writtenBytes(instruction));
				continue;
			}
			if (instruction.BulkCopy is { } bulkCopy)
			{
				if (bulkCopy.Source is { Kind: M68kMemoryObjectKind.FrameSlot } sourceObject &&
					int.TryParse(sourceObject.Identity, out var sourceHome))
				{
					Observe(sourceHome);
				}
				if (bulkCopy.Destination is { Kind: M68kMemoryObjectKind.FrameSlot } destinationObject &&
					int.TryParse(destinationObject.Identity, out var destinationHome))
				{
					Write(destinationHome, destinationObject.Offset, bulkCopy.ByteCount);
				}
				continue;
			}
			// Observe sources before crediting a write: copying a local to itself
			// must not make its original zero initialization appear dead.
			foreach (var use in instruction.Uses)
			{
				if (addresses.TryGetValue(use, out var usedHome)) Observe(usedHome);
			}
			if (instruction.Operation == M68kMachineOperation.LocalLoad &&
				instruction.ArgumentIndex is { } readHome)
			{
				Observe(readHome);
			}
			if (instruction.Operation is M68kMachineOperation.LocalStore or
				M68kMachineOperation.AggregateFieldLoad or M68kMachineOperation.AggregateArrayLoad or
				M68kMachineOperation.AggregateIndirectLoad or M68kMachineOperation.AggregateIndirectCopy &&
				instruction.ArgumentIndex is { } writtenHome)
			{
				Write(writtenHome, instruction.MemoryOffset, writtenBytes(instruction));
				if (instruction.Operation != M68kMachineOperation.LocalStore &&
					instruction.Definitions is [var aggregateAddress])
				{
					addresses[aggregateAddress] = writtenHome;
				}
			}
		}
		foreach (var (home, bytes) in homes)
		{
			for (var offset = 0; offset + 4 <= bytes.Length; offset += 4)
			{
				if (bytes[offset] == 1 && bytes[offset + 1] == 1 &&
					bytes[offset + 2] == 1 && bytes[offset + 3] == 1)
				{
					result.Add((home, offset));
				}
			}
		}
		return result;
	}
}
