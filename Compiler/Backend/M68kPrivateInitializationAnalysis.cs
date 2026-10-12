/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/
namespace CopperSharp.Compiler.Backend;

/// <summary>Least fixed point of writes-before-observation for private local bytes.
/// Unknown uses escape a home; later unknown effects can observe escaped homes.
/// An unexposed home cannot be reached by a call without a derived address argument.
/// Cycles without a definite write conservatively retain the implicit clear.</summary>
internal static class M68kPrivateInitializationAnalysis
{
    internal static IReadOnlySet<(int Home, int Offset)> FindOverwrites(
        M68kMachineFunction function, Func<M68kMachineInstruction, int> writtenBytes)
    {
        var result = new HashSet<(int Home, int Offset)>();
        if (function.HasExceptionHandlers || function.HasDynamicStackAllocation ||
            function.Values.Values.Any(v => v.IsGcReference) || function.LocalHomes.Values.Any(h => h.HasGcReferences) ||
            function.ArgumentHomes.Values.Any(h => h.HasGcReferences) || function.Blocks.SelectMany(b => b.Instructions)
                .Any(i => (i.MemoryEffect & M68kMachineMemoryEffect.Volatile) != 0)) return result;
        var homes = function.LocalHomes.Values.Where(h => h.Initialize && !h.HasGcReferences).ToArray();
        var addresses = new Dictionary<int, int>();
        var instructions = function.Blocks.SelectMany(b => b.Instructions).ToArray();
        foreach (var instruction in instructions)
            if (instruction.Operation == M68kMachineOperation.LocalAddress && instruction.ArgumentIndex is { } home &&
                instruction.Definitions is [var definition]) addresses[definition] = home;
        bool changed;
        do {
            changed = false;
            foreach (var instruction in instructions)
                if (instruction.Operation == M68kMachineOperation.Copy && instruction.Uses is [var source] &&
                    instruction.Definitions is [var destination] && addresses.TryGetValue(source, out var home) &&
                    !addresses.ContainsKey(destination)) { addresses[destination] = home; changed = true; }
        } while (changed);
        // Unknown derived pointers/phis cannot be silently treated as private.
        // Direct address users are observations unless recognized as writes.
        foreach (var home in homes) {
            bool IsAddressUse(M68kMachineInstruction instruction) => instruction.Uses.Any(v => addresses.GetValueOrDefault(v, -1) == home.Index);
            bool IsAddressOnly(M68kMachineInstruction instruction) => instruction.Operation == M68kMachineOperation.LocalAddress ||
                instruction.Operation == M68kMachineOperation.Copy && instruction.Uses is [var source] && addresses.ContainsKey(source);
            bool IsInitialize(M68kMachineInstruction instruction) => instruction.Operation == M68kMachineOperation.AggregateIndirectInitialize &&
                instruction.Uses is [var destination] && addresses.GetValueOrDefault(destination, -1) == home.Index;
            var escapedIn = function.Blocks.ToDictionary(b => b.Id, _ => false);
            var escapedOut = function.Blocks.ToDictionary(b => b.Id, _ => false);
            do {
                changed = false;
                foreach (var block in function.Blocks) {
                    var incoming = block.ControlFlowPredecessors.Any(id => escapedOut.GetValueOrDefault(id));
                    var outgoing = incoming || block.Instructions.Any(i => IsAddressUse(i) && !IsAddressOnly(i) && !IsInitialize(i)) ||
                        block.Phis.Any(phi => phi.Inputs.Values.Any(v => addresses.GetValueOrDefault(v, -1) == home.Index));
                    if (incoming != escapedIn[block.Id] || outgoing != escapedOut[block.Id]) {
                        escapedIn[block.Id] = incoming; escapedOut[block.Id] = outgoing; changed = true;
                    }
                }
            } while (changed);
            var observed = new Dictionary<int, bool>();
            foreach (var block in function.Blocks) {
                var escaped = escapedIn[block.Id] || block.Phis.Any(phi => phi.Inputs.Values.Any(v => addresses.GetValueOrDefault(v, -1) == home.Index));
                foreach (var instruction in block.Instructions) {
                    observed[instruction.Id] = IsAddressUse(instruction) && !IsAddressOnly(instruction) && !IsInitialize(instruction) ||
                        instruction.Operation == M68kMachineOperation.LocalLoad && instruction.ArgumentIndex == home.Index ||
                        escaped && (instruction.MemoryEffect != M68kMachineMemoryEffect.None || instruction.IsSafepoint);
                    if (IsAddressUse(instruction) && !IsAddressOnly(instruction) && !IsInitialize(instruction)) escaped = true;
                }
            }
            var entryWrites = function.Blocks.ToDictionary(b => b.Id, _ => new bool[home.Size]);
            do {
                changed = false;
                foreach (var block in function.Blocks.AsEnumerable().Reverse()) {
                    var successors = block.ControlFlowSuccessors.ToArray();
                    var state = Enumerable.Range(0, home.Size).Select(index => successors.Length == 0
                        ? !escapedOut[block.Id]
                        : successors.All(id => entryWrites.TryGetValue(id, out var next) && next[index])).ToArray();
                    foreach (var instruction in block.Instructions.AsEnumerable().Reverse()) {
                        var offset = instruction.MemoryOffset;
                        var size = 0;
                        if (IsInitialize(instruction)) { offset = 0; size = writtenBytes(instruction); }
                        else if (instruction.Operation == M68kMachineOperation.LocalStore && instruction.ArgumentIndex == home.Index)
                            size = writtenBytes(instruction);
                        // Exact reads and escapes are authoritative, even when an
                        // instruction also writes. Reads occur before its write.
                        if (offset >= 0 && size > 0 && (long)offset + size <= home.Size)
                            Array.Fill(state, true, offset, size);
                        if (observed[instruction.Id]) Array.Fill(state, false);
                        foreach (var access in instruction.ExactMemoryAccesses)
                            if (access.Object.Kind == M68kMemoryObjectKind.FrameSlot && access.Object.Identity == home.Index.ToString() &&
                                access.Kind is M68kExactMemoryAccessKind.Read or M68kExactMemoryAccessKind.Escape) Array.Fill(state, false);
                    }
                    if (!state.SequenceEqual(entryWrites[block.Id])) { entryWrites[block.Id] = state; changed = true; }
                }
            } while (changed);
            var entry = entryWrites[function.EntryBlockId];
            for (var offset = 0; offset + 4 <= home.Size; offset += 4)
                if (Enumerable.Range(offset, 4).All(index => entry[index])) result.Add((home.Index, offset));
        }
        return result;
    }
}
