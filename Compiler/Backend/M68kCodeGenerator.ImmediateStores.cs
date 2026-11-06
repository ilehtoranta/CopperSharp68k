using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
    private readonly Dictionary<int, int> _allocatedImmediateStores = new();
    private readonly Dictionary<int, uint> _allocatedAbsoluteImmediateStores = new();

    private void PrepareAllocatedImmediateStores(CilMethod method, M68kAllocatedFunction allocated)
    {
        _allocatedImmediateStores.Clear();
        _allocatedAbsoluteImmediateStores.Clear();
        if (_request.ExceptionMode != M68kExceptionMode.Yolo ||
            _request.MemoryManagement != M68kMemoryManagement.None) return;
        var instructions = allocated.Function.Blocks.SelectMany(b => b.Instructions).ToArray();
        var phiInputs = allocated.Function.Blocks.SelectMany(b => b.Phis).SelectMany(p => p.Inputs.Values).ToHashSet();
        foreach (var store in instructions)
        {
            if (store.Operation != M68kMachineOperation.Call || store.Uses is not [_, var value] ||
                store.Immediate is not (>= short.MinValue and <= short.MaxValue) || phiInputs.Contains(value) ||
                instructions.Count(i => i.Uses.Contains(value)) != 1) continue;
            var name = ResolveAllocatedMachineMethod(method, store).ImportName;
            // Small long constants can use MOVEQ plus a register store faster
            // than MOVE.L immediate. Limit this fold to byte/word stores.
            if (name is not ("intrinsic:aptr-write-uint8" or "intrinsic:aptr-write-uint16")) continue;
            var producer = instructions.FirstOrDefault(i => i.Definitions is [var defined] && defined == value);
            if (producer is null || _allocatedSuppressedInstructions.Contains(producer.Id) ||
                (producer.Operation != M68kMachineOperation.Constant && !_allocatedFoldedCopyConstants.ContainsKey(producer.Id)) ||
                !TryGetAllocatedConstant(allocated.Function, value, out int immediate)) continue;
            // Only replace a private, single-use materialization. Reusing an
            // already-live constant register can be cheaper than an immediate.
            _allocatedImmediateStores.Add(store.Id, immediate);
            _allocatedSuppressedInstructions.Add(producer.Id);
            _allocatedFoldedCopyConstants.Remove(producer.Id);
            _allocatedBitBranchDeadValues.Add(value);
        }
        foreach (var store in instructions.Where(i => _allocatedImmediateStores.ContainsKey(i.Id)))
        {
            var value = store.Uses[0];
            var chain = new List<M68kMachineInstruction>();
            var visited = new HashSet<int>();
            while (visited.Add(value) && !phiInputs.Contains(value) &&
                instructions.Count(i => i.Uses.Contains(value)) == 1)
            {
                var producer = instructions.SingleOrDefault(i => i.Definitions.Contains(value));
                if (producer is null || producer.Definitions.Length != 1) break;
                chain.Add(producer);
                if (producer.Operation == M68kMachineOperation.Constant &&
                    TryGetAllocatedConstant(allocated.Function, value, out var address))
                {
                    // A single-use constant address needs no address register.
                    // Keep the write at its original position and size, including
                    // zero writes to MMIO. Only pure identity transports vanish.
                    _allocatedAbsoluteImmediateStores.Add(store.Id,
                        unchecked((uint)(address + store.Immediate!.Value)));
                    foreach (var removed in chain)
                    {
                        _allocatedSuppressedInstructions.Add(removed.Id);
                        _allocatedFoldedCopyConstants.Remove(removed.Id);
                        _allocatedBitBranchDeadValues.UnionWith(removed.Definitions);
                    }
                    break;
                }
                if (producer.Uses is not [var source] ||
                    producer.Operation != M68kMachineOperation.Copy && !IsAllocatedIdentityIntrinsic(method, producer)) break;
                value = source;
            }
        }
    }
}
