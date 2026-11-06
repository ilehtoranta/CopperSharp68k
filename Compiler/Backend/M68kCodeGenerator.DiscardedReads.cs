/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Metadata;
using System.Reflection.Emit;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
    private readonly Dictionary<int, (M68kMachineValueWidth Width, short Offset)> _allocatedDiscardedReads = new();
    private readonly HashSet<int> _allocatedDiscardedReadValues = new();

    private void PrepareAllocatedDiscardedReads(CilMethod method, M68kAllocatedFunction allocated)
    {
        _allocatedDiscardedReads.Clear();
        _allocatedDiscardedReadValues.Clear();
        if (method.InternalRegisterAbi is null || _request.ExceptionMode != M68kExceptionMode.Yolo ||
            _request.MemoryManagement != M68kMemoryManagement.None) return;
        var function = allocated.Function;
        var used = function.Blocks.SelectMany(b => b.Instructions.Where(i => i.Operation != M68kMachineOperation.Other || i.SourceInstruction?.OpCode != OpCodes.Pop).SelectMany(i => i.Uses)
            .Concat(b.Phis.SelectMany(p => p.Inputs.Values))).ToHashSet();
        foreach (var instruction in function.Blocks.SelectMany(b => b.Instructions))
        {
            if (instruction.Operation != M68kMachineOperation.Call || instruction.Definitions is not [var result] ||
                used.Contains(result) || instruction.ProducesConditionCodes || instruction.ConsumesConditionCodes ||
                _allocatedSuppressedInstructions.Contains(instruction.Id)) continue;
            var name = ResolveAllocatedMachineMethod(method, instruction).ImportName;
            var width = name switch {
                "intrinsic:aptr-read-uint8" => M68kMachineValueWidth.Byte,
                "intrinsic:aptr-read-uint16" => M68kMachineValueWidth.Word,
                "intrinsic:aptr-read-uint32" => M68kMachineValueWidth.Long,
                _ => (M68kMachineValueWidth)0
            };
            if (width == 0 || instruction.Uses.Length == 0 ||
                allocated.Allocation.Registers[instruction.Uses[0]].Register is < M68kRegister.A0 or > M68kRegister.A6) continue;
            int offset;
            if (instruction.Immediate is { } immediate) offset = immediate;
            else if (instruction.Uses.Length != 2 || !TryGetAllocatedConstant(function, instruction.Uses[1], out offset)) continue;
            if (offset is < short.MinValue or > short.MaxValue) continue;
            // TST performs the same sized read at the same point. Only the
            // unused data result and its normalization disappear, never MMIO.
            _allocatedDiscardedReads.Add(instruction.Id, (width, (short)offset));
            _allocatedDiscardedReadValues.Add(result);
        }
    }
}
