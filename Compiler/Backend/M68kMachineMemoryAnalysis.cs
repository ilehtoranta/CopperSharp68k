/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;

namespace CopperSharp.Compiler.Backend;

/// <summary>
/// Exact frame-memory analysis shared by forwarding and dead-store passes.
/// Heap and externally visible memory remain conservative unknown aliases.
/// </summary>
internal static class M68kMachineMemoryAnalysis
{
	public static bool Optimize(
		M68kMachineFunction function,
		M68kMachineOptimizer.MutableStatistics statistics)
	{
		var changed = false;
		foreach (var block in function.Blocks)
		{
			var available = new Dictionary<M68kMemoryObject, int>();
			var escaped = new HashSet<M68kMemoryObject>();
			for (var index = 0; index < block.Instructions.Count; index++)
			{
				var instruction = block.Instructions[index];
				if (TryGetExactFrameRead(instruction, out var read))
				{
					if (!escaped.Contains(read) &&
						available.TryGetValue(read, out var value) &&
						CanForward(function, instruction, value))
					{
						block.Instructions[index] = instruction with
						{
							Operation = M68kMachineOperation.Copy,
							Uses = ImmutableArray.Create(value),
							MemoryEffect = M68kMachineMemoryEffect.None,
							MayThrow = false,
							SourceInstruction = null,
							ArgumentIndex = null,
							ConstantValue = null
						};
						statistics.LoadsForwarded++;
						changed = true;
					}
					else if (instruction.Definitions is [var definition])
					{
						available[read] = definition;
					}
					continue;
				}
				if (TryGetExactFrameWrite(instruction, out var write) &&
					instruction.Uses is [var stored])
				{
					available[write] = stored;
					continue;
				}
				if (TryGetEscapedFrameObject(instruction, out var escapedObject))
				{
					escaped.Add(escapedObject);
					available.Remove(escapedObject);
					continue;
				}
				if (IsBarrier(instruction))
				{
					available.Clear();
				}
			}

			var overwritten = new HashSet<M68kMemoryObject>();
			escaped.Clear();
			for (var index = block.Instructions.Count - 1; index >= 0; index--)
			{
				var instruction = block.Instructions[index];
				if (TryGetExactFrameWrite(instruction, out var write))
				{
					if (!escaped.Contains(write) && overwritten.Contains(write))
					{
						block.Instructions.RemoveAt(index);
						statistics.StoresRemoved++;
						changed = true;
						continue;
					}
					overwritten.Add(write);
					continue;
				}
				if (TryGetExactFrameRead(instruction, out var read))
				{
					overwritten.Remove(read);
					continue;
				}
				if (TryGetEscapedFrameObject(instruction, out var escapedObject))
				{
					escaped.Add(escapedObject);
					overwritten.Remove(escapedObject);
					continue;
				}
				if (IsBarrier(instruction))
				{
					overwritten.Clear();
				}
			}
		}
		return changed;
	}

	private static bool TryGetExactFrameRead(
		M68kMachineInstruction instruction,
		out M68kMemoryObject location)
	{
		if (instruction.ArgumentIndex is { } index &&
			instruction.Operation is M68kMachineOperation.LocalLoad or
				M68kMachineOperation.ArgumentLoad)
		{
			location = new M68kMemoryObject(
				instruction.Operation == M68kMachineOperation.LocalLoad
					? M68kMemoryObjectKind.FrameSlot
					: M68kMemoryObjectKind.ArgumentHome,
				index.ToString(System.Globalization.CultureInfo.InvariantCulture));
			return true;
		}
		location = default;
		return false;
	}

	private static bool TryGetExactFrameWrite(
		M68kMachineInstruction instruction,
		out M68kMemoryObject location)
	{
		if (instruction.ArgumentIndex is { } index &&
			instruction.Operation is M68kMachineOperation.LocalStore or
				M68kMachineOperation.ArgumentStore)
		{
			location = new M68kMemoryObject(
				instruction.Operation == M68kMachineOperation.LocalStore
					? M68kMemoryObjectKind.FrameSlot
					: M68kMemoryObjectKind.ArgumentHome,
				index.ToString(System.Globalization.CultureInfo.InvariantCulture));
			return true;
		}
		location = default;
		return false;
	}

	private static bool TryGetEscapedFrameObject(
		M68kMachineInstruction instruction,
		out M68kMemoryObject location)
	{
		if (instruction.ArgumentIndex is { } index &&
			instruction.Operation is M68kMachineOperation.LocalAddress or
				M68kMachineOperation.ArgumentAddress)
		{
			location = new M68kMemoryObject(
				instruction.Operation == M68kMachineOperation.LocalAddress
					? M68kMemoryObjectKind.FrameSlot
					: M68kMemoryObjectKind.ArgumentHome,
				index.ToString(System.Globalization.CultureInfo.InvariantCulture));
			return true;
		}
		location = default;
		return false;
	}

	private static bool CanForward(
		M68kMachineFunction function,
		M68kMachineInstruction load,
		int storedValue)
	{
		if (load.Definitions is not [var loadedValue] ||
			!function.Values.TryGetValue(storedValue, out var source) ||
			!function.Values.TryGetValue(loadedValue, out var destination))
		{
			return false;
		}
		return source.Kind == destination.Kind &&
			source.Width == destination.Width &&
			source.IsGcReference == destination.IsGcReference;
	}

	private static bool IsBarrier(M68kMachineInstruction instruction) =>
		instruction.IsSafepoint || instruction.MayThrow ||
		(instruction.MemoryEffect & M68kMachineMemoryEffect.Volatile) != 0 ||
		instruction.Operation == M68kMachineOperation.Call ||
		(instruction.MemoryEffect & M68kMachineMemoryEffect.Write) != 0 &&
			!TryGetExactFrameWrite(instruction, out _);
}
