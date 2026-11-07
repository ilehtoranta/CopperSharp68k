/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Numerics;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
    private readonly Dictionary<int, (int Source, int Bit, M68kCondition Condition, short? MemoryOffset)> _allocatedBitBranches = new();
    private readonly HashSet<int> _allocatedBitBranchDeadValues = new();
    private readonly HashSet<int> _allocatedLowOnlyReads = new();

    private readonly Dictionary<int, (int Source, int Mask, M68kMachineValueWidth Width, bool Complement)> _allocatedImmediateAnds = new();
    private readonly HashSet<int> _allocatedWordImmediateBranches = new();
    private readonly Dictionary<int, (int Source, int Constant, M68kCondition Condition)> _allocatedImmediateBranches = new();

    private void PrepareAllocatedImmediatePredicates(CilMethod method, M68kAllocatedFunction allocated)
    {
        _allocatedImmediateAnds.Clear();
        _allocatedImmediateBranches.Clear();
        _allocatedWordImmediateBranches.Clear();
        if (_emittingVolatileMethod) return;
        if (method.InternalRegisterAbi is null || allocated.Frame.CalleeSavedRegisters.Count > 2 ||
            allocated.Function.Blocks.Any(b => b.LoopDepth != 0)) return;
        var function = allocated.Function;
        var instructions = function.Blocks.SelectMany(b => b.Instructions).ToArray();
        foreach (var constant in instructions.Where(i => i.Operation == M68kMachineOperation.Constant &&
            i.Definitions.Length == 1 && !_allocatedSuppressedInstructions.Contains(i.Id)))
        {
            int value = constant.Definitions[0];
            if (function.Values[value].Width != M68kMachineValueWidth.Long ||
                !TryGetAllocatedConstant(function, value, out int immediate) ||
                function.Blocks.Any(b => b.Phis.Any(p => p.Inputs.Values.Contains(value)))) continue;
            var consumers = instructions.Where(i => i.Uses.Contains(value)).ToArray();
            if (consumers.Length == 0) continue;
            var ands = new List<(int Id, int Source)>();
            var branches = new List<(int Id, int Source, M68kCondition Condition)>();
            bool valid = true;
            foreach (var use in consumers)
            {
                if (_allocatedSuppressedInstructions.Contains(use.Id) || use.Uses.Length != 2) { valid = false; break; }
                if (use.Operation == M68kMachineOperation.And && use.Definitions.Length == 1) {
                    int source = use.Uses[0] == value ? use.Uses[1] : use.Uses[0];
                    if (source == value || !(function.Values[source].Width == M68kMachineValueWidth.Long ||
                        function.Values[source].Kind == CilStackValueKind.UnsignedByte && (uint)immediate <= 255 ||
                        function.Values[source].Kind == CilStackValueKind.UnsignedWord && (uint)immediate <= 65535) ||
                        allocated.Allocation.Registers[source].Register > M68kRegister.D7 ||
                        allocated.Allocation.Registers[use.Definitions[0]].Register > M68kRegister.D7) { valid = false; break; }
                    ands.Add((use.Id, source));
                } else if (use.Operation == M68kMachineOperation.ConditionalBranch && (immediate is < -128 or > 127 || consumers.Length == 1 && allocated.Allocation.Registers[value].Register is >= M68kRegister.D2 and <= M68kRegister.D7) && use.Uses[1] == value &&
                    use.Uses[0] != value && function.Values[use.Uses[0]].Width == M68kMachineValueWidth.Long &&
                    allocated.Allocation.Registers[use.Uses[0]].Register <= M68kRegister.D7 &&
                    !use.ConsumesConditionCodes && use.BranchCondition is null or { SourceKind: M68kMachineConditionSourceKind.Compare }) {
                    var condition = use.BranchCondition?.Condition ?? AllocatedRelationalBranchCondition(use.SourceInstruction!.OpCode);
                    branches.Add((use.Id, use.Uses[0], condition));
                } else { valid = false; break; }
            }
            if (!valid) continue;
            foreach (var and in ands) _allocatedImmediateAnds.Add(and.Id, (and.Source, immediate, M68kMachineValueWidth.Long, false));
            foreach (var branch in branches) _allocatedImmediateBranches.Add(branch.Id, (branch.Source, immediate, branch.Condition));
            _allocatedBitBranchDeadValues.Add(value);
            _allocatedSuppressedInstructions.Add(constant.Id);
        }
        // A masked value used only for equality can stay narrow. No upper
        // register bits or signed comparison flags are observable in this form.
        foreach (var (id, selected) in _allocatedImmediateAnds.ToArray())
        {
            if ((uint)selected.Mask > ushort.MaxValue) continue;
            var and = instructions.Single(i => i.Id == id);
            int result = and.Definitions[0];
            if (function.Blocks.Any(b => b.Phis.Any(p => p.Inputs.Values.Contains(result)))) continue;
            var consumers = instructions.Where(i => i.Uses.Contains(result)).ToArray();
            if (consumers is not [var branch] ||
                !_allocatedImmediateBranches.TryGetValue(branch.Id, out var compare) ||
                compare.Source != result || (uint)compare.Constant > ushort.MaxValue ||
                compare.Condition is not (M68kCondition.Equal or M68kCondition.NotEqual)) continue;
            // The result has exactly one equality consumer. Test for missing
            // bits when the comparison asks whether every mask bit is set.
            bool allBits = !and.ProducesConditionCodes && selected.Mask != 0 && selected.Mask == compare.Constant;
            _allocatedImmediateAnds[id] = (selected.Source, selected.Mask, M68kMachineValueWidth.Word, allBits);
            if (allBits) _allocatedImmediateBranches[branch.Id] = (result, 0, compare.Condition);
            _allocatedWordImmediateBranches.Add(branch.Id);
        }
    }

    private void PrepareAllocatedAddressComparisons(M68kAllocatedFunction allocated)
    {
        var function = allocated.Function;
        var uses = function.Blocks.SelectMany(b => b.Instructions.SelectMany(i => i.Uses)
            .Concat(b.Phis.SelectMany(p => p.Inputs.Values))).GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
        foreach (var block in function.Blocks)
        for (int index = 0; index < block.Instructions.Count; index++)
        {
            var branch = block.Instructions[index];
            if (!_allocatedImmediateBranches.TryGetValue(branch.Id, out var compare) ||
                _allocatedWordImmediateBranches.Contains(branch.Id) || uses.GetValueOrDefault(compare.Source) != 1) continue;
            int producer = index - 1;
            while (producer >= 0 && _allocatedSuppressedInstructions.Contains(block.Instructions[producer].Id)) producer--;
            if (producer < 0 || block.Instructions[producer] is not { Operation: M68kMachineOperation.Copy,
                    Definitions: [var copied], Uses: [var source] } copy || copied != compare.Source ||
                function.Values[source].Width != M68kMachineValueWidth.Long ||
                function.Values[copied].Width != M68kMachineValueWidth.Long ||
                allocated.Allocation.Registers[source].Register is < M68kRegister.A0 or > M68kRegister.A6) continue;
            // CMPA.L has the same full-width comparison flags as CMP.L.
            // Only suppressed transport lies between this address copy and its
            // sole comparison, so the original address register is still live.
            _allocatedImmediateBranches[branch.Id] = (source, compare.Constant, compare.Condition);
            _allocatedSuppressedInstructions.Add(copy.Id);
            _allocatedBitBranchDeadValues.Add(copied);
        }
    }

    private void PrepareAllocatedNarrowImmediateBranches(CilMethod method, M68kAllocatedFunction allocated)
    {
        if (_emittingVolatileMethod) return;
        if (method.InternalRegisterAbi is null || _request.ExceptionMode != M68kExceptionMode.Yolo ||
            _request.MemoryManagement != M68kMemoryManagement.None) return;
        var function = allocated.Function;
        var uses = function.Blocks.SelectMany(b => b.Instructions.SelectMany(i => i.Uses)
            .Concat(b.Phis.SelectMany(p => p.Inputs.Values))).GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
        foreach (var block in function.Blocks)
        for (int index = 2; index < block.Instructions.Count; index++)
        {
            var convert = block.Instructions[index - 2];
            var constant = block.Instructions[index - 1];
            var branch = block.Instructions[index];
            if (convert.Operation != M68kMachineOperation.Convert || convert.Definitions is not [var value] ||
                convert.Uses is not [var source] || function.Values[value].Kind != CilStackValueKind.SignedWord ||
                function.Values[source].Kind is < CilStackValueKind.BooleanByte or > CilStackValueKind.Int32 ||
                function.Values[source].Width is not (M68kMachineValueWidth.Word or M68kMachineValueWidth.Long) ||
                allocated.Allocation.Registers[source].Register > M68kRegister.D7 || uses.GetValueOrDefault(value) != 1 ||
                constant.Operation != M68kMachineOperation.Constant || constant.Definitions is not [var immediateValue] ||
                uses.GetValueOrDefault(immediateValue) != 1 ||
                !TryGetAllocatedConstant(function, immediateValue, out int immediate) || immediate is < short.MinValue or > short.MaxValue ||
                branch.Operation != M68kMachineOperation.ConditionalBranch || branch.Uses.Length != 2 ||
                branch.Uses[0] != value || branch.Uses[1] != immediateValue || branch.ConsumesConditionCodes ||
                branch.BranchCondition is { SourceKind: not M68kMachineConditionSourceKind.Compare } ||
                _allocatedSuppressedInstructions.Contains(convert.Id) || _allocatedSuppressedInstructions.Contains(constant.Id)) continue;
            var condition = branch.BranchCondition?.Condition ?? AllocatedRelationalBranchCondition(branch.SourceInstruction!.OpCode);
            if (condition is not (M68kCondition.Equal or M68kCondition.NotEqual or M68kCondition.GreaterOrEqual or
                M68kCondition.GreaterThan or M68kCondition.LessOrEqual or M68kCondition.LessThan)) continue;
            // A signed-word conversion followed only by a signed-word-range
            // comparison needs no extended temporary or constant register.
            _allocatedImmediateBranches.Add(branch.Id, (source, immediate, condition));
            _allocatedWordImmediateBranches.Add(branch.Id);
            _allocatedSuppressedInstructions.Add(convert.Id);
            _allocatedSuppressedInstructions.Add(constant.Id);
            _allocatedBitBranchDeadValues.Add(value);
            _allocatedBitBranchDeadValues.Add(immediateValue);
        }
    }

    private void PrepareAllocatedBitBranches(CilMethod method, M68kAllocatedFunction allocated)
    {
        _allocatedBitBranches.Clear();
        _allocatedBitBranchDeadValues.Clear();
        if (_emittingVolatileMethod) return;
        if (method.InternalRegisterAbi is null || allocated.Frame.CalleeSavedRegisters.Count > 3) return;
        var function = allocated.Function;
        var uses = function.Blocks.SelectMany(b => b.Instructions.SelectMany(i => i.Uses)
            .Concat(b.Phis.SelectMany(p => p.Inputs.Values))).GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
        foreach (var block in function.Blocks)
        for (int index = 2; index < block.Instructions.Count; index++)
        {
            var constant = block.Instructions[index - 2];
            var and = block.Instructions[index - 1];
            var branch = block.Instructions[index];
            if (constant.Operation != M68kMachineOperation.Constant || constant.Definitions.Length != 1 ||
                and.Operation != M68kMachineOperation.And || and.Uses.Length != 2 || and.Definitions.Length != 1 ||
                branch.Operation != M68kMachineOperation.ConditionalBranch || branch.Uses.Length != 1 ||
                branch.Uses[0] != and.Definitions[0] || uses.GetValueOrDefault(and.Definitions[0]) != 1 ||
                uses.GetValueOrDefault(constant.Definitions[0]) != 1 ||
                _allocatedSuppressedInstructions.Contains(constant.Id) || _allocatedSuppressedInstructions.Contains(and.Id)) continue;
            int maskValue = constant.Definitions[0];
            int source = and.Uses[0] == maskValue ? and.Uses[1] : and.Uses[1] == maskValue ? and.Uses[0] : -1;
            if (source < 0 || function.Values[source].Width is not (M68kMachineValueWidth.Byte or M68kMachineValueWidth.Word or M68kMachineValueWidth.Long) ||
                allocated.Allocation.Registers[source].Register > M68kRegister.D7 ||
                !TryGetAllocatedConstant(function, maskValue, out int mask) ||
                !BitOperations.IsPow2(unchecked((uint)mask)) ||
                BitOperations.TrailingZeroCount(unchecked((uint)mask)) >= 8 * (int)function.Values[source].Width) continue;
            M68kCondition condition;
            if (branch.BranchCondition is { } test) {
                if (test.SourceKind != M68kMachineConditionSourceKind.Test ||
                    test.Condition is not (M68kCondition.Equal or M68kCondition.NotEqual)) continue;
                condition = test.Condition;
            } else if (branch.SourceInstruction is not { } cil ||
                !TryGetBooleanBranchCondition(cil.OpCode, out condition)) continue;
            // An adjacent single-use copy need not manufacture another register
            // home for the bit test. No intervening instruction survives the
            // selection, and testing a bit below the source width needs no
            // sign/zero-extension of the copied upper bits.
            bool forwardedCopy = false;
            if (index >= 3 && block.Instructions[index - 3] is { Operation: M68kMachineOperation.Copy, Uses: [var original], Definitions: [var copied] } copy &&
                copied == source && uses.GetValueOrDefault(copied) == 1 &&
                !_allocatedSuppressedInstructions.Contains(copy.Id) &&
                allocated.Allocation.Registers[original].Register <= M68kRegister.D7 &&
                function.Values[original].Kind is >= CilStackValueKind.BooleanByte and <= CilStackValueKind.Int32 &&
                function.Values[original].Width is M68kMachineValueWidth.Byte or M68kMachineValueWidth.Word or M68kMachineValueWidth.Long &&
                BitOperations.TrailingZeroCount(unchecked((uint)mask)) < 8 * (int)function.Values[original].Width)
            {
                source = original;
                forwardedCopy = true;
                _allocatedSuppressedInstructions.Add(copy.Id);
                _allocatedBitBranchDeadValues.Add(copied);
            }
            short? memoryOffset = null;
            int producerIndex = index - 3;
            if (forwardedCopy) producerIndex--;
            if (producerIndex >= 0 && _request.ExceptionMode == M68kExceptionMode.Yolo &&
                _request.MemoryManagement == M68kMemoryManagement.None &&
                block.Instructions[producerIndex] is { Operation: M68kMachineOperation.Call, Definitions: [var loaded] } read &&
                loaded == source && (uint)mask <= byte.MaxValue && uses.GetValueOrDefault(loaded) == 1 && read.Uses.Length >= 1 &&
                !_allocatedSuppressedInstructions.Contains(read.Id) &&
                ResolveAllocatedMachineMethod(method, read).ImportName == "intrinsic:aptr-read-uint8" &&
                read.Immediate is >= short.MinValue and <= short.MaxValue &&
                allocated.Allocation.Registers[read.Uses[0]].Register is >= M68kRegister.A0 and <= M68kRegister.A6)
            {
                // Byte BTST reads the same byte once, without manufacturing a
                // temporary. Never substitute it for a word or long MMIO read.
                source = read.Uses[0];
                memoryOffset = (short)read.Immediate.Value;
                _allocatedSuppressedInstructions.Add(read.Id);
                _allocatedBitBranchDeadValues.Add(loaded);
            }
            // Only a boolean zero/nonzero result is consumed. Select BTST before
            // preservation planning, so its vanished mask/result homes cannot
            // force a save. Selected memory predicates retain their byte access.
            _allocatedBitBranches.Add(branch.Id, (source, BitOperations.TrailingZeroCount(unchecked((uint)mask)), condition, memoryOffset));
            _allocatedBitBranchDeadValues.Add(maskValue);
            _allocatedBitBranchDeadValues.Add(and.Definitions[0]);
            _allocatedSuppressedInstructions.Add(constant.Id);
            _allocatedSuppressedInstructions.Add(and.Id);
        }
    }

    private void PrepareAllocatedLowOnlyReads(CilMethod method, M68kAllocatedFunction allocated)
    {
        _allocatedLowOnlyReads.Clear();
        if (_emittingVolatileMethod) return;
        if (method.InternalRegisterAbi is null || _request.ExceptionMode != M68kExceptionMode.Yolo ||
            _request.MemoryManagement != M68kMemoryManagement.None) return;
        var function = allocated.Function;
        // When every surviving consumer tests a low bit, canonical upper bits
        // have no observer. Follow copies and phi transports through a bounded
        // worklist; arithmetic, stores, calls or full-value returns reject it.
        var instructions = function.Blocks.SelectMany(b => b.Instructions).ToArray();
        bool LowOnly(int initial, int bits)
        {
            var pending = new Stack<int>();
            var visited = new HashSet<int>();
            pending.Push(initial);
            bool tested = false;
            while (pending.TryPop(out int value))
            {
                if (!visited.Add(value)) continue;
                foreach (var phi in function.Blocks.SelectMany(b => b.Phis))
                    if (phi.Inputs.Values.Contains(value)) pending.Push(phi.Definition);
                foreach (var use in instructions)
                {
                    if (_allocatedSuppressedInstructions.Contains(use.Id)) continue;
                    if (_allocatedBitBranches.TryGetValue(use.Id, out var bit)) {
                        if (bit.Source == value) { if (bit.MemoryOffset is not null || bit.Bit >= bits) return false; tested = true; }
                        continue;
                    }
                    if (_allocatedCounterBranches.TryGetValue(use.Id, out var counter)) {
                        if (counter.Base == value) return false; // an address needs every bit
                        continue;
                    }
                    if (_allocatedImmediateBranches.TryGetValue(use.Id, out var compare)) {
                        if (compare.Source == value) {
                            if (bits < 16 || !_allocatedWordImmediateBranches.Contains(use.Id)) return false;
                            tested = true;
                        }
                        continue;
                    }
                    if (_allocatedImmediateAnds.TryGetValue(use.Id, out var and)) {
                        if (and.Source == value) {
                            if (8 * (int)and.Width > bits) return false;
                            tested = true;
                        }
                        continue;
                    }
                    if (!use.Uses.Contains(value)) continue;
                    if (use.Operation != M68kMachineOperation.Copy || use.Uses is not [var source] || source != value ||
                        use.Definitions is not [var copied]) return false;
                    pending.Push(copied);
                }
            }
            return tested;
        }
        foreach (var read in instructions)
            if (read.Operation == M68kMachineOperation.Call && read.Definitions is [var value] &&
                !_allocatedSuppressedInstructions.Contains(read.Id) &&
                ResolveAllocatedMachineMethod(method, read).ImportName is { } name &&
                name is "intrinsic:aptr-read-uint8" or "intrinsic:aptr-read-uint16" &&
                LowOnly(value, name == "intrinsic:aptr-read-uint8" ? 8 : 16))
                _allocatedLowOnlyReads.Add(read.Id);
    }
}
