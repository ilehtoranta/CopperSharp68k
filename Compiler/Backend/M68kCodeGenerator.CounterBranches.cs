/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Metadata;
using System.Reflection.Emit;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
    private readonly Dictionary<int, (int Base, short Offset, bool Increment, int SizeBits, M68kCondition Condition)> _allocatedCounterBranches = new();
    private readonly HashSet<int> _allocatedDiscardedCounterResults = new();

    private void PrepareAllocatedCounterBranches(CilMethod method, M68kAllocatedFunction allocated)
    {
        _allocatedCounterBranches.Clear();
        _allocatedDiscardedCounterResults.Clear();
        if (_request.ExceptionMode != M68kExceptionMode.Yolo ||
            _request.MemoryManagement != M68kMemoryManagement.None) return;
        var function = allocated.Function;
        var uses = function.Blocks.SelectMany(b => b.Instructions
            .Where(i => i.Operation != M68kMachineOperation.Other || i.SourceInstruction?.OpCode != OpCodes.Pop)
            .SelectMany(i => i.Uses)
            .Concat(b.Phis.SelectMany(p => p.Inputs.Values))).GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
        foreach (var call in function.Blocks.SelectMany(b => b.Instructions))
        {
            if (call.Operation != M68kMachineOperation.Call || call.Definitions is not [var result] ||
                uses.ContainsKey(result)) continue;
            var name = ResolveAllocatedMachineMethod(method, call).ImportName;
            if (name is not ("intrinsic:aptr-increment-word-zero" or "intrinsic:aptr-decrement-word-negative" or
                "intrinsic:aptr-increment-byte-zero" or "intrinsic:aptr-decrement-byte-negative")) continue;
            // Retain the atomic memory effect, but do not manufacture an unused boolean.
            _allocatedDiscardedCounterResults.Add(call.Id);
            _allocatedBitBranchDeadValues.Add(result);
        }
        foreach (var block in function.Blocks)
        for (int index = 1; index < block.Instructions.Count; index++)
        {
            var call = block.Instructions[index - 1];
            var branch = block.Instructions[index];
            if (call.Operation != M68kMachineOperation.Call || call.Definitions is not [var result] ||
                call.Uses is not [var address] || call.Immediate is not (>= short.MinValue and <= short.MaxValue) ||
                branch.Operation != M68kMachineOperation.ConditionalBranch || branch.Uses is not [var tested] ||
                tested != result || uses.GetValueOrDefault(result) != 1 ||
                _allocatedSuppressedInstructions.Contains(call.Id) ||
                allocated.Allocation.Registers[address].Register is < M68kRegister.A0 or > M68kRegister.A6) continue;
            var name = ResolveAllocatedMachineMethod(method, call).ImportName;
            if (name is not ("intrinsic:aptr-increment-word-zero" or "intrinsic:aptr-decrement-word-negative" or "intrinsic:aptr-increment-byte-zero" or "intrinsic:aptr-decrement-byte-negative")) continue;
            M68kCondition testCondition;
            if (branch.BranchCondition is { } test) {
                if (test.SourceKind != M68kMachineConditionSourceKind.Test ||
                    test.Condition is not (M68kCondition.Equal or M68kCondition.NotEqual)) continue;
                testCondition = test.Condition;
            } else if (branch.SourceInstruction is not { } cil ||
                !TryGetBooleanBranchCondition(cil.OpCode, out testCondition)) continue;
            bool increment = name is "intrinsic:aptr-increment-word-zero" or "intrinsic:aptr-increment-byte-zero";
            int sizeBits = name.Contains("-byte-", StringComparison.Ordinal) ? 0 : 0x40;
            var condition = increment ? M68kCondition.Equal : M68kCondition.Minus;
            if (testCondition == M68kCondition.Equal) condition = InvertCondition(condition);
            // Consume the atomic counter update's flags directly. Its boolean
            // has exactly one adjacent branch consumer, so no scalar result,
            // extension or retest needs to be emitted or preserved.
            _allocatedCounterBranches.Add(branch.Id, (address, (short)call.Immediate.Value, increment, sizeBits, condition));
            _allocatedSuppressedInstructions.Add(call.Id);
            _allocatedBitBranchDeadValues.Add(result);
        }
    }
}
