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
        if (_emittingVolatileMethod) return;
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
        PrepareSharedAbsoluteImmediateStores(method, allocated, instructions, phiInputs);
    }

    private void PrepareSharedAbsoluteImmediateStores(CilMethod method, M68kAllocatedFunction allocated,
        M68kMachineInstruction[] instructions, HashSet<int> phiInputs)
    {
        if (allocated.Function.HasExceptionHandlers) return;
        var users = instructions.SelectMany(i => i.Uses.Distinct().Select(v => (Value: v, Instruction: i)))
            .GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.Select(p => p.Instruction).ToArray());
        foreach (var root in instructions.Where(i => i.Operation == M68kMachineOperation.Constant && i.Definitions.Length == 1))
        {
            // Constant-copy forwarding may already have moved this load to an
            // identity transport. Inspect the whole use graph in either case.
            if (!IsAllocatedScalarIntegerConstant(allocated.Function, root.Definitions[0]) ||
                !TryGetAllocatedConstant(allocated.Function, root.Definitions[0], out var address)) continue;
            var transports = new HashSet<M68kMachineInstruction> { root };
            var stores = new HashSet<M68kMachineInstruction>();
            var values = new HashSet<int>();
            var pending = new Stack<int>(); pending.Push(root.Definitions[0]);
            var valid = true;
            while (pending.TryPop(out var value) && valid)
            {
                if (!values.Add(value)) continue;
                if (phiInputs.Contains(value)) { valid = false; break; }
                foreach (var user in users.GetValueOrDefault(value) ?? [])
                {
                    if (_allocatedImmediateStores.ContainsKey(user.Id) && user.Uses[0] == value &&
                        !user.Uses.Skip(1).Contains(value)) stores.Add(user);
                    else if (user.Uses is [var source] && user.Definitions is [var result] &&
                        allocated.Function.Values[source].Width == allocated.Function.Values[result].Width &&
                        (user.Operation == M68kMachineOperation.Copy || IsAllocatedIdentityIntrinsic(method, user)))
                    { transports.Add(user); pending.Push(result); }
                    else { valid = false; break; }
                }
            }
            // On 68000, replacing (An) with abs.L adds four cycles per write.
            // At most two writes cost less than materializing the address,
            // even before removing any callee-save traffic. Reject loops so
            // that repeated writes cannot amortize the retained register.
            if (!valid || stores.Count < 2 || !HasAtMostTwoStoresPerPath(allocated.Function, stores)) continue;
            foreach (var store in stores)
                _allocatedAbsoluteImmediateStores[store.Id] = unchecked((uint)(address + store.Immediate!.Value));
            foreach (var transport in transports)
            {
                _allocatedSuppressedInstructions.Add(transport.Id);
                _allocatedFoldedCopyConstants.Remove(transport.Id);
                _allocatedBitBranchDeadValues.UnionWith(transport.Definitions);
            }
        }
    }

    private static bool HasAtMostTwoStoresPerPath(M68kMachineFunction function, HashSet<M68kMachineInstruction> stores)
    {
        var blocks = function.Blocks.ToDictionary(b => b.Id);
        var reachable = new HashSet<int>();
        var pending = new Stack<int>(); pending.Push(function.EntryBlockId);
        while (pending.TryPop(out var id))
            if (reachable.Add(id)) foreach (var next in blocks[id].Successors) pending.Push(next);
        var incoming = reachable.ToDictionary(id => id, _ => 0);
        foreach (var id in reachable)
            foreach (var next in blocks[id].Successors) incoming[next]++;
        var ready = new Queue<int>(reachable.Where(id => incoming[id] == 0));
        var counts = reachable.ToDictionary(id => id, _ => 0);
        var visited = 0;
        while (ready.TryDequeue(out var id))
        {
            visited++;
            counts[id] += blocks[id].Instructions.Count(stores.Contains);
            if (counts[id] > 2) return false;
            foreach (var next in blocks[id].Successors)
            {
                counts[next] = Math.Max(counts[next], counts[id]);
                if (--incoming[next] == 0) ready.Enqueue(next);
            }
        }
        return visited == reachable.Count;
    }
}
