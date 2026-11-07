namespace Cilica.Passes;

using Cilica.Analysis;

using NameStack = ArrayStack<(BasicBlock B, Value V)>;

/// <summary> Promotes non-exposed local variables to SSA. </summary>
public class SsaPromotion : IMethodPass
{
    public MethodPassResult Run(MethodTransformContext ctx) => new Worker(ctx).Run();

    sealed class Worker(MethodTransformContext ctx)
    {
        Dictionary<PhiInst, LocalSlot> _phiDefs = new(); // phi -> variable
        Dictionary<LocalSlot, NameStack> _slotNameStacks = new();
        Dictionary<LocalSlot, Value> _initialValues = new();
        DominatorTree _domTree = null!;

        public MethodPassResult Run()
        {
            FindSlotsAndInsertPhis(ctx);

            if (_slotNameStacks.Count == 0) {
                Debug.Assert(_phiDefs.Count == 0);
                return MethodInvalidations.None;
            }

            _domTree = ctx.GetAnalysis<DominatorTree>();
            _domTree.Traverse(preVisit: RenameBlock, postVisit: LeaveBlock);

            RemoveDeadPhis();
            RemovePromotedSlots();
            ctx.Logger.Trace($"Promoted {_slotNameStacks.Count} variables to SSA.");

            return MethodInvalidations.DataFlow;
        }

        private void FindSlotsAndInsertPhis(MethodTransformContext ctx)
        {
            var worklist = new DiscreteStack<BasicBlock>();
            var phiAdded = new RefSet<BasicBlock>(); // blocks where a phi has been added
            var domFrontier = default(DominanceFrontier);

            foreach (var slot in ctx.Method.LocalVars()) {
                if (!CheckPromoteableAndFindStores(slot, worklist, out bool isGlobal)) continue;

                // Only need to insert phis for variables used across multiple blocks
                if (isGlobal) {
                    domFrontier ??= ctx.GetAnalysis<DominanceFrontier>();

                    // Recursively insert phis on the DF of each block in the worklist
                    while (worklist.TryPop(out var block)) {
                        foreach (var dom in domFrontier.Of(block)) {
                            if (!phiAdded.Add(dom)) continue;

                            var phi = dom.InsertPhi(slot.Type);
                            _phiDefs.Add(phi, slot);

                            worklist.Push(dom);
                        }
                    }
                    phiAdded.Clear();
                }
                _slotNameStacks.Add(slot, new());
            }
        }

        private bool CheckPromoteableAndFindStores(LocalSlot slot, DiscreteStack<BasicBlock> definingBlocks, out bool isGlobal)
        {
            isGlobal = false;

            if (slot.IsPinned || slot.IsHardExposed) {
                return false;
            }

            definingBlocks.Clear();

            foreach (var (user, operIdx) in slot.Uses()) {
                if (user is not MemoryInst { IsVolatile: false } access || access.ElemType != slot.Type) {
                    return false;
                }
                if (user is StoreInst && operIdx == 0) {
                    definingBlocks.Push(user.Block);
                } else if (user is LoadInst) {
                    isGlobal |= definingBlocks.Depth == 0 || definingBlocks.Top != user.Block;
                } else {
                    return false;
                }
            }
            isGlobal |= definingBlocks.UniqueCount >= 2;

            // Block dominance does not model the point at which an exception
            // interrupts a store sequence. Keep stores in one region and loads
            // within that region or its children; crossing out requires memory.
            // Decide from current IR, for imported and generated slots alike.
            if (isGlobal && definingBlocks.UniqueCount != 0) {
                var regions = ctx.GetAnalysis<ProtectedRegionAnalysis>();
                var storeRegion = regions.GetBlockRegion(definingBlocks.Top);

                foreach (var user in slot.Users()) {
                    var region = regions.GetBlockRegion(user.Block);

                    if (user is LoadInst) {
                        // Values stored in a parent region can flow into its children.
                        while (region != storeRegion && region.Parent != null) {
                            region = region.Parent;
                        }
                    }
                    if (region != storeRegion) return false;
                }
            }

            return true;
        }

        private void RenameBlock(BasicBlock block)
        {
            // Init phi defs
            foreach (var phi in block.Phis()) {
                if (_phiDefs.TryGetValue(phi, out var slot)) {
                    WriteDef(_slotNameStacks[slot], block, phi);
                }
            }
            // Promote load/stores
            foreach (var inst in block.NonPhis()) {
                if (inst is not MemoryInst { Address: LocalSlot slot }) continue;
                if (!_slotNameStacks.TryGetValue(slot, out var stack)) continue;

                // Update latest def
                if (inst is StoreInst store) {
                    var value = StoreInst.Coerce(store.ElemType, store.Value, insertBefore: inst);
                    WriteDef(stack, block, value);
                    inst.Remove();
                }
                // Replace load with latest def
                else {
                    Debug.Assert(inst is LoadInst);
                    inst.ReplaceWith(ReadDef(stack, block, slot));
                }
            }
            // Fill successors phis
            foreach (var succ in block.Succs) {
                foreach (var phi in succ.Phis()) {
                    if (_phiDefs.TryGetValue(phi, out var slot)) {
                        // TODO: AddArg() is O(n), maybe rewrite all phis in a final pass
                        phi.AddArg(block, ReadDef(_slotNameStacks[slot], block, slot));
                    }
                }
            }
        }

        private void LeaveBlock(BasicBlock block)
        {
            // Remove trivially useless phis
            // This must be done in post-dominance order, before
            // the materialization of useless phi-loops.
            foreach (var phi in block.Phis()) {
                if (!phi.Users().Any(u => u != phi)) {
                    phi.Remove();
                }
            }
        }

        private void RemovePromotedSlots()
        {
            // Remove promoted slots
            foreach (var slot in _slotNameStacks.Keys) {
                Debug.Assert(slot.NumUses == 0);
                slot.Remove();
            }
        }

        private void RemoveDeadPhis()
        {
            // Dominance frontiers can place unused phis on exceptional exits.
            // Remove entire dead phi webs, including cycles, before verification.
            var live = new DiscreteStack<PhiInst>();
            foreach (var phi in _phiDefs.Keys) {
                if (phi.Block != null && phi.Users().Any(user => user is not PhiInst other || !_phiDefs.ContainsKey(other))) {
                    live.Push(phi);
                }
            }
            while (live.TryPop(out var phi)) {
                foreach (var (_, value) in phi) {
                    if (value is PhiInst input && _phiDefs.ContainsKey(input)) live.Push(input);
                }
            }
            foreach (var phi in _phiDefs.Keys) {
                if (phi.Block != null && !live.WasPushed(phi)) phi.Remove();
            }
        }

        private void WriteDef(NameStack stack, BasicBlock block, Value value)
        {
            // Avoid pushing duplicate defs for the same block
            if (!stack.IsEmpty && stack.Top.B == block) stack.Pop();

            stack.Push((block, value));
        }

        private Value ReadDef(NameStack stack, BasicBlock block, LocalSlot slot)
        {
            // Discard definitions that do not dominate this block.
            while (!stack.IsEmpty) {
                if (_domTree.Dominates(stack.Top.B, block)) {
                    return stack.Top.V;
                }
                stack.Pop();
            }
            if (!_initialValues.TryGetValue(slot, out var initial)) {
                initial = ctx.Method.InitLocals
                    ? new IR.Utils.IRBuilder(ctx.Method.EntryBlock, IR.Utils.InsertionDir.Before).CreateDefaultOf(slot.Type)
                    : new Undef(slot.Type);
                _initialValues.Add(slot, initial);
            }
            return initial;
        }
        }
}
