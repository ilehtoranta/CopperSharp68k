using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
    private readonly Dictionary<int, (int Base, short Offset, int Constant, int SizeBits, M68kCondition Condition)>
        _allocatedMemoryImmediateBranches = new();

    private void PrepareAllocatedMemoryImmediateBranches(CilMethod method, M68kAllocatedFunction allocated)
    {
        _allocatedMemoryImmediateBranches.Clear();
        if (_emittingVolatileMethod) return;
        if (_request.ExceptionMode != M68kExceptionMode.Yolo ||
            _request.MemoryManagement != M68kMemoryManagement.None) return;
        var function = allocated.Function;
        var instructions = function.Blocks.SelectMany(b => b.Instructions).ToArray();
        var uses = instructions.SelectMany(i => i.Uses).Concat(function.Blocks.SelectMany(b => b.Phis)
            .SelectMany(p => p.Inputs.Values)).GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
        var definitions = instructions.Where(i => i.Definitions.Length == 1).ToDictionary(i => i.Definitions[0]);
        foreach (var block in function.Blocks)
        foreach (var branch in block.Instructions)
        {
            if (branch.Operation != M68kMachineOperation.ConditionalBranch || branch.ConsumesConditionCodes) continue;
            int value, immediate;
            M68kCondition condition;
            M68kMachineInstruction? constant = null;
            if (branch.Uses is [var compared, var literal] &&
                branch.BranchCondition is null or { SourceKind: M68kMachineConditionSourceKind.Compare })
            {
                if (uses.GetValueOrDefault(literal) != 1 || !definitions.TryGetValue(literal, out constant) ||
                    constant.Operation != M68kMachineOperation.Constant || !TryGetAllocatedConstant(function, literal, out immediate)) continue;
                value = compared;
                condition = branch.BranchCondition?.Condition ?? AllocatedRelationalBranchCondition(branch.SourceInstruction!.OpCode);
            }
            else if (branch.Uses is [var tested] &&
                branch.BranchCondition is null or { SourceKind: M68kMachineConditionSourceKind.Test })
            {
                value = tested; immediate = 0;
                if (branch.BranchCondition is { } test) condition = test.Condition;
                else if (branch.SourceInstruction is { } source && TryGetBooleanBranchCondition(source.OpCode, out var boolean)) condition = boolean;
                else continue;
            }
            else continue;
            if (condition is not (M68kCondition.Equal or M68kCondition.NotEqual)) continue;
            var copies = new List<M68kMachineInstruction>();
            var visited = new HashSet<int>();
            while (visited.Add(value) && uses.GetValueOrDefault(value) == 1 &&
                definitions.TryGetValue(value, out var copy) && copy.Operation == M68kMachineOperation.Copy && copy.Uses is [var source])
            { copies.Add(copy); value = source; }
            if (uses.GetValueOrDefault(value) != 1 || !definitions.TryGetValue(value, out var read) ||
                read.Operation != M68kMachineOperation.Call || read.Uses is not [var address] ||
                read.Immediate is not (>= short.MinValue and <= short.MaxValue) || _allocatedSuppressedInstructions.Contains(read.Id)) continue;
            var name = ResolveAllocatedMachineMethod(method, read).ImportName;
            int sizeBits;
            if (name == "intrinsic:aptr-read-uint8" && (uint)immediate <= byte.MaxValue) sizeBits = 0;
            else if (name == "intrinsic:aptr-read-uint16" && (uint)immediate <= ushort.MaxValue) sizeBits = 0x40;
            else continue;
            var register = allocated.Allocation.Registers[address].Register;
            if (register is < M68kRegister.A0 or > M68kRegister.A6) continue;
            int start = block.Instructions.IndexOf(read), end = block.Instructions.IndexOf(branch);
            if (start < 0 || start >= end || constant is not null && !block.Instructions.Contains(constant) ||
                copies.Any(c => !block.Instructions.Contains(c))) continue;
            // Move the read only across scalar copies/constants, with its base
            // register unchanged. A store, call or other effect is a barrier.
            if (block.Instructions.Skip(start + 1).Take(end - start - 1).Any(i =>
                i.Operation is not (M68kMachineOperation.Constant or M68kMachineOperation.Copy) ||
                i.Clobbers.Contains(register) || i.Definitions.Any(v =>
                    allocated.Allocation.Registers[v].OccupiedRegisters.Contains(register)))) continue;
            _allocatedImmediateBranches.Remove(branch.Id);
            _allocatedWordImmediateBranches.Remove(branch.Id);
            _allocatedMemoryImmediateBranches.Add(branch.Id, (address, (short)read.Immediate.Value, immediate, sizeBits, condition));
            copies.Add(read);
            if (constant is not null) copies.Add(constant);
            foreach (var removed in copies) {
                _allocatedSuppressedInstructions.Add(removed.Id);
                _allocatedBitBranchDeadValues.UnionWith(removed.Definitions);
            }
        }
    }
}
