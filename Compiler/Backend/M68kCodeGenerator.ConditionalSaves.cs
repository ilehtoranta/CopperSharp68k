/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
    private sealed record ConditionalSavePlan(M68kRegister Register, HashSet<int> Active,
        HashSet<int> SaveAtEntry, M68kRegister[] Unconditional);

    private ConditionalSavePlan? _conditionalSave;
    private int _conditionalSaveBlock;
    private readonly Dictionary<(int From, int To), string> _conditionalSaveEdges = new();

    private M68kAllocatedFunction ReuseConditionalSaveScratch(CilMethod method, M68kAllocatedFunction allocated)
    {
        if (method.InternalRegisterAbi is null || allocated.Frame.CalleeSavedRegisters.Count != 2 ||
            allocated.Frame.FrameBytes != 0 || allocated.Function.Blocks.Any(b => b.LoopDepth != 0)) return allocated;
        var function = allocated.Function;
        var source = allocated.Frame.CalleeSavedRegisters[0];
        var target = allocated.Frame.CalleeSavedRegisters[1];
        if (source > M68kRegister.D7 || target > M68kRegister.D7 ||
            allocated.ParallelCopies.ReferencedRegisters.Contains(source) ||
            allocated.ParallelCopies.ReferencedRegisters.Contains(target)) return allocated;
        var registers = allocated.Allocation.Registers.ToDictionary();
        bool changed = false;
        foreach (var block in function.Blocks)
        {
            // A block-local scratch can reuse an already preserved register.
            // Keep fixed homes, implicit clobbers and all edge transports intact.
            if (block.Instructions.Any(i => i.Clobbers.Contains(source) ||
                i.Operation is not (M68kMachineOperation.Constant or M68kMachineOperation.Copy or
                    M68kMachineOperation.Shift or M68kMachineOperation.Multiply or
                    M68kMachineOperation.Convert or M68kMachineOperation.RotateImmediate or
                    M68kMachineOperation.ConditionalBranch or M68kMachineOperation.Branch) ||
                i.Operation == M68kMachineOperation.Multiply &&
                    (!_integerArithmeticPlans.TryGetValue(i.Id, out var plan) ||
                     plan.Kind is not (M68kIntegerArithmeticKind.SignedWordMultiply or M68kIntegerArithmeticKind.UnsignedWordMultiply)))) continue;
            var values = block.Instructions.SelectMany(i => i.Definitions.Concat(i.Uses))
                .Distinct().Where(v => registers[v].Register == source).ToArray();
            if (values.Length == 0 || values.Any(v =>
                !block.Instructions.Any(i => i.Definitions.Contains(v)) ||
                function.Values[v].Kind is < CilStackValueKind.BooleanByte or > CilStackValueKind.Int32 ||
                function.Values[v].IsRegisterPair || function.Values[v].PrecoloredRegister is not null ||
                !function.Values[v].AllowedRegisters.Contains(target) ||
                allocated.BlockLiveness.LiveIn[block.Id].Contains(v) ||
                allocated.BlockLiveness.LiveOut[block.Id].Contains(v) ||
                allocated.Interference.ForbiddenRegisters.GetValueOrDefault(v).Contains(target) ||
                allocated.Interference.Neighbors(v).Any(n => registers.TryGetValue(n, out var r) && r.OccupiedRegisters.Contains(target)) ||
                function.Blocks.Any(b => b.Phis.Any(p => p.Inputs.Values.Contains(v)) ||
                    b.Id != block.Id && b.Instructions.Any(i => i.Uses.Contains(v) || i.Definitions.Contains(v))))) continue;
            foreach (var value in values) registers[value] = new(target, false);
            changed = true;
        }
        if (!changed) return allocated;
        var allocation = allocated.Allocation with { Registers = registers };
        M68kGraphColoringAllocator.VerifyAllocation(function, allocated.Interference, allocation, allocated.InstructionLiveness);
        return allocated with { Allocation = allocation };
    }

    private void PrepareConditionalSave(CilMethod method, InternalCallAbi abi,
        M68kAllocatedFunction allocated)
    {
        _conditionalSave = null;
        _conditionalSaveEdges.Clear();
        var function = allocated.Function;
        // This first policy moves one small save only. Stack homes, unwind/GC
        // metadata and wide/switch lowering keep the established frame. Loops
        // are allowed only outside the relocated register's saved region.
        if (method.InternalRegisterAbi is null || _usesExceptionRuntime ||
            _request.ExceptionMode != M68kExceptionMode.Yolo ||
            _request.MemoryManagement != M68kMemoryManagement.None ||
            function.HasExceptionHandlers || function.HasDynamicStackAllocation ||
            allocated.Frame.FrameBytes != 0 || allocated.Frame.CalleeSavedRegisters.Count is < 1 or > 3 ||
            abi.ReturnBufferStackOffset is not null || abi.Arguments.Any(a => a.IsStack) ||
            function.Values.Values.Any(v => v.IsRegisterPair || v.IsGcReference) ||
            function.Blocks.Any(b => b.Instructions.Any(i =>
                i.Operation is M68kMachineOperation.Switch or M68kMachineOperation.Throw ||
                i.RequiresLiveCallerFrame))) return;

        var deadValues = function.Blocks.SelectMany(b => b.Instructions)
            .Where(i => _allocatedSuppressedInstructions.Contains(i.Id) &&
                i.Operation is M68kMachineOperation.Constant or M68kMachineOperation.Copy &&
                i.Definitions is [var value] && IsAllocatedScalarIntegerConstant(function, value))
            .Select(i => i.Definitions[0]).Concat(_allocatedBitBranchDeadValues).Concat(_allocatedDiscardedReadValues).ToHashSet();
        var emitted = allocated.FinalDestinations.EmittedBlockIds;
        var blocks = function.Blocks.Where(b => emitted.Contains(b.Id)).ToDictionary(b => b.Id);
        var entry = allocated.FinalDestinations.Resolve(function.EntryBlockId);
        var predecessors = blocks.Keys.ToDictionary(id => id, _ => new HashSet<int>());
        foreach (var block in blocks.Values)
            foreach (var target in block.Successors.Select(allocated.FinalDestinations.Resolve).Distinct())
                predecessors[target].Add(block.Id);

        foreach (var register in allocated.Frame.CalleeSavedRegisters)
        {
            // Parallel copies can have implicit scratch/stack effects. Do not
            // relocate their preservation until that edge contract is modeled.
            if (allocated.ParallelCopies.ReferencedRegisters.Contains(register)) continue;
            bool Uses(int value) => !deadValues.Contains(value) && allocated.Allocation.Registers.TryGetValue(value, out var location) &&
                location.OccupiedRegisters.Contains(register);
            var active = new HashSet<int>();
            foreach (var block in function.Blocks)
            {
                if (allocated.BlockLiveness.LiveIn[block.Id].Any(Uses) ||
                    allocated.BlockLiveness.LiveOut[block.Id].Any(Uses) ||
                    block.Instructions.Any(i => !_allocatedSuppressedInstructions.Contains(i.Id) &&
                        (i.Clobbers.Contains(register) || i.Uses.Any(Uses) || i.Definitions.Any(Uses) ||
                         _allocatedBitBranches.TryGetValue(i.Id, out var bit) && Uses(bit.Source) ||
                         _allocatedCounterBranches.TryGetValue(i.Id, out var counter) && Uses(counter.Base) ||
                         _allocatedMemoryImmediateBranches.TryGetValue(i.Id, out var memoryCompare) && Uses(memoryCompare.Base) ||
                         _allocatedImmediateBranches.TryGetValue(i.Id, out var immediate) && Uses(immediate.Source))))
                    active.Add(allocated.FinalDestinations.Resolve(block.Id));
            }
            // Entry argument transport is emitted in the prologue. Requiring
            // the entire entry block to be inactive also excludes such writes.
            if (active.Count == 0 || active.Contains(entry) || active.Count == blocks.Count) continue;
            // Carry an existing save through joins reached only from saved
            // predecessors. This avoids restoring through a separate return
            // stub when every incoming path already owns the same stack slot.
            bool changed;
            do {
                changed = false;
                foreach (var id in blocks.Keys)
                    if (!active.Contains(id) && predecessors[id].Count != 0 &&
                        predecessors[id].All(active.Contains))
                        changed |= active.Add(id);
            } while (changed);
            if (function.Blocks.Any(b => b.LoopDepth != 0 && active.Contains(allocated.FinalDestinations.Resolve(b.Id)))) continue;
            var saveAtEntry = active.Where(id => predecessors[id].Count != 0 &&
                predecessors[id].All(pred => !active.Contains(pred))).ToHashSet();
            // Keep transitions bounded: this is a code-size optimization as
            // well as a hot-path saving, not arbitrary CFG duplication.
            int edgeStubs = blocks.Values.Sum(block => block.Successors
                .Select(allocated.FinalDestinations.Resolve).Distinct().Count(target =>
                    active.Contains(block.Id) != active.Contains(target) &&
                    !(active.Contains(target) && saveAtEntry.Contains(target)) &&
                    !(active.Contains(block.Id) && block.Successors.Count == 1)));
            if (edgeStubs > 4) continue;
            _conditionalSave = new(register, active, saveAtEntry,
                allocated.Frame.CalleeSavedRegisters.Where(r => r != register).ToArray());
            return;
        }
    }

    private bool ConditionalSaveNeedsEdge(int from, int to) =>
        _conditionalSave is { } plan && plan.Active.Contains(from) != plan.Active.Contains(to) &&
        !(plan.Active.Contains(to) && plan.SaveAtEntry.Contains(to));

    private string AllocatedConditionalSaveTarget(CilMethod method, int from, int to)
    {
        if (!ConditionalSaveNeedsEdge(from, to)) return AllocatedBlockLabel(method, to);
        if (!_conditionalSaveEdges.TryGetValue((from, to), out var label))
            _conditionalSaveEdges.Add((from, to), label = UniqueLabel("conditional-save-edge"));
        return label;
    }

    private void EmitConditionalSaveBlockEntry(int block)
    {
        _conditionalSaveBlock = block;
        if (_conditionalSave is { } plan && plan.SaveAtEntry.Contains(block))
            EmitPushRegister(plan.Register);
    }

    private void EmitConditionalSaveSingleEdge(int from, int to)
    {
        if (_conditionalSave is not { } plan || plan.Active.Contains(from) == plan.Active.Contains(to)) return;
        if (plan.Active.Contains(from)) EmitPopRegister(plan.Register);
        else if (!plan.SaveAtEntry.Contains(to)) EmitPushRegister(plan.Register);
    }

    private void EmitConditionalSaveEdgeStubs(CilMethod method)
    {
        foreach (var (edge, label) in _conditionalSaveEdges)
        {
            _assembler.Mark(label);
            EmitConditionalSaveSingleEdge(edge.From, edge.To);
            _assembler.EmitBranch(M68kCondition.True, AllocatedBlockLabel(method, edge.To));
        }
    }
}
