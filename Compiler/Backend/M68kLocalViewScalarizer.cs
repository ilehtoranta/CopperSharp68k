/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Collections.Immutable;
using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

/// <summary>
/// Exposes fields of nonescaping, reference-free frame values to memory SSA.
/// This runs after inlining so a borrowed view's constructor and accessors no
/// longer hide its address uses. Guest-memory accesses are never promoted here.
/// </summary>
internal static class M68kLocalViewScalarizer
{
    public static bool Run(M68kMachineFunction function, CompilationModule module)
    {
        if (function.HasExceptionHandlers || function.HasDynamicStackAllocation) return false;
        var instructions = function.Blocks.SelectMany(b => b.Instructions).ToArray();
        var candidates = function.LocalHomes.Values
            .Where(h => !h.HasGcReferences && h.Size is > 0 and <= 128 && (h.Size & 3) == 0)
            .Select(h => h.Index).ToHashSet();
        var addresses = new Dictionary<int, int>();
        foreach (var instruction in instructions)
            if (instruction.Operation == M68kMachineOperation.LocalAddress &&
                instruction.ArgumentIndex is { } local && instruction.Definitions is [var value])
                addresses[value] = local;
        bool changed;
        do
        {
            changed = false;
            foreach (var instruction in instructions)
                if (instruction.Operation == M68kMachineOperation.Copy && instruction.Uses is [var source] &&
                    instruction.Definitions is [var value] && addresses.TryGetValue(source, out var local) &&
                    addresses.TryAdd(value, local)) changed = true;
        } while (changed);

        var fields = new Dictionary<int, (int Local, int Offset, bool Address)>();
        var copies = new Dictionary<int, (int Source, int Destination)>();
        var clears = new Dictionary<int, int>();
        foreach (var instruction in instructions)
        {
            if (instruction.Operation is M68kMachineOperation.Load or M68kMachineOperation.Store &&
                instruction.Uses.Length != 0 && addresses.TryGetValue(instruction.Uses[0], out var local) &&
                TryFieldOffset(instruction, module, out var offset, out var addressField) &&
                offset >= 0 && offset <= function.LocalHomes[local].Size - 4)
                fields[instruction.Id] = (local, offset, addressField);
            if (instruction.Operation == M68kMachineOperation.LocalStore && instruction.ArgumentIndex is { } destination &&
                instruction.Uses is [var source] && function.Values[source].Kind == CilStackValueKind.AggregateAddress &&
                addresses.TryGetValue(source, out var sourceLocal) &&
                function.LocalHomes[sourceLocal].Size == function.LocalHomes[destination].Size)
                copies[instruction.Id] = (sourceLocal, destination);
            if (instruction.Operation == M68kMachineOperation.Other &&
                instruction.SourceInstruction?.OpCode == OpCodes.Initobj && instruction.Uses is [var clearAddress] &&
                addresses.TryGetValue(clearAddress, out var clearLocal) &&
                instruction.Origin is { SourceInstruction.Operand: int typeToken } origin &&
                module.TryGetReferenceFreeStructLayout(module.ResolveTypeToken(typeToken, origin.SourceMethod,
                    origin.SourceInstruction.Offset), origin.SourceMethod.ModuleName, out var layout) &&
                layout.Size == function.LocalHomes[clearLocal].Size)
                clears[instruction.Id] = clearLocal;
        }
        // A call, return, phi, address comparison, indirect access or a stored
        // pointer escapes the home. Copies between homes must be scalarizable
        // at both ends. Do not infer safety from an accessor's name alone.
        foreach (var phi in function.Blocks.SelectMany(b => b.Phis))
            foreach (var input in phi.Inputs.Values)
                if (addresses.TryGetValue(input, out var local)) candidates.Remove(local);
        foreach (var instruction in instructions)
        {
            foreach (var use in instruction.Uses.Concat(instruction.LogicalCall?.ArgumentValueIds ?? []))
            {
                if (!addresses.TryGetValue(use, out var local)) continue;
                if (instruction.Operation == M68kMachineOperation.Copy ||
                    fields.ContainsKey(instruction.Id) && use == instruction.Uses[0] &&
                        (instruction.Uses.Length == 1 || instruction.Uses[1] != use) ||
                    copies.ContainsKey(instruction.Id) || clears.ContainsKey(instruction.Id)) continue;
                candidates.Remove(local);
            }
        }
        do
        {
            changed = false;
            foreach (var copy in copies.Values)
                if (!candidates.Contains(copy.Source) || !candidates.Contains(copy.Destination))
                {
                    changed |= candidates.Remove(copy.Source);
                    changed |= candidates.Remove(copy.Destination);
                }
        } while (changed);

        changed = false;
        foreach (var block in function.Blocks)
        {
            for (var index = 0; index < block.Instructions.Count; index++)
            {
                var instruction = block.Instructions[index];
                if (fields.TryGetValue(instruction.Id, out var field) && candidates.Contains(field.Local))
                {
                    var load = instruction.Operation == M68kMachineOperation.Load;
                    if (load && field.Address && instruction.Definitions is [var loaded] &&
                        function.Values[loaded] is { Width: M68kMachineValueWidth.Long, IsGcReference: false,
                            PrecoloredRegister: null } value && value.AllowedRegisters == M68kRegisterSet.Data)
                        function.Values[loaded] = value with { AllowedRegisters = M68kRegisterSet.DataOrAddress };
                    block.Instructions[index] = instruction with
                    {
                        Operation = load ? M68kMachineOperation.LocalLoad : M68kMachineOperation.LocalStore,
                        Uses = load ? [] : [instruction.Uses[^1]],
                        ArgumentIndex = field.Local,
                        MemoryOffset = field.Offset,
                        MemorySize = 4,
                        MayThrow = false,
                        ExactMemoryAccesses = []
                    };
                    changed = true;
                }
                else if (copies.TryGetValue(instruction.Id, out var copy) && candidates.Contains(copy.Source))
                {
                    var replacement = new List<M68kMachineInstruction>();
                    for (var offset = 0; offset < function.LocalHomes[copy.Source].Size; offset += 4)
                    {
                        var lane = function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long, M68kRegisterSet.DataOrAddress);
                        replacement.Add(function.CreateInstruction(M68kMachineOperation.LocalLoad, instruction.IlOffset,
                            definitions: [lane.Id], memoryEffect: M68kMachineMemoryEffect.Read,
                            argumentIndex: copy.Source, memoryOffset: offset, memorySize: 4, origin: instruction.Origin));
                        replacement.Add(function.CreateInstruction(M68kMachineOperation.LocalStore, instruction.IlOffset,
                            uses: [lane.Id], memoryEffect: M68kMachineMemoryEffect.Write,
                            argumentIndex: copy.Destination, memoryOffset: offset, memorySize: 4, origin: instruction.Origin));
                    }
                    block.Instructions.RemoveAt(index);
                    block.Instructions.InsertRange(index, replacement);
                    index += replacement.Count - 1;
                    changed = true;
                }
                else if (clears.TryGetValue(instruction.Id, out var local) && candidates.Contains(local))
                {
                    var zero = function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long, M68kRegisterSet.Data);
                    var replacement = new List<M68kMachineInstruction>
                    {
                        function.CreateInstruction(M68kMachineOperation.Constant, instruction.IlOffset,
                            definitions: [zero.Id], constantValue: M68kMachineConstant.Int32(0))
                    };
                    for (var offset = 0; offset < function.LocalHomes[local].Size; offset += 4)
                        replacement.Add(function.CreateInstruction(M68kMachineOperation.LocalStore, instruction.IlOffset,
                            uses: [zero.Id], memoryEffect: M68kMachineMemoryEffect.Write, argumentIndex: local,
                            memoryOffset: offset, memorySize: 4, origin: instruction.Origin));
                    block.Instructions.RemoveAt(index);
                    block.Instructions.InsertRange(index, replacement);
                    index += replacement.Count - 1;
                    changed = true;
                }
            }
        }
        return changed;
    }

    private static bool TryFieldOffset(M68kMachineInstruction instruction, CompilationModule module, out int offset, out bool addressField)
    {
        offset = 0;
        addressField = false;
        if ((instruction.MemoryEffect & M68kMachineMemoryEffect.Volatile) != 0 ||
            instruction.MemorySize is not (0 or 4) || instruction.Origin is not { SourceInstruction.Operand: int token } origin ||
            origin.SourceInstruction.OpCode != OpCodes.Ldfld && origin.SourceInstruction.OpCode != OpCodes.Stfld)
            return false;
        var field = module.ResolveFieldToken(token, origin.SourceMethod, origin.SourceInstruction.Offset);
        if (field.IsStatic || field.Type.Kind == CilTypeKind.ManagedReference ||
            !(field.Type.IsSupportedScalar && field.Type.Size == 4 || module.IsTransparentScalarType(field.Type)))
            return false;
        offset = checked(module.GetTypeLayout(field).FieldOffsets[field.Handle] + instruction.MemoryOffset);
        // Scalar arithmetic still needs data registers (EOR, shifts, etc.).
        // Only pointer payloads can inherit the view's flexible address bank;
        // a later Raw conversion retains its own scalar operand constraints.
        addressField = field.Type.Kind is CilTypeKind.ManagedPointer or CilTypeKind.UnmanagedPointer ||
            module.IsTransparentScalarType(field.Type);
        return (offset & 3) == 0;
    }
}
