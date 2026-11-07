namespace Cilica.Analysis;

using Cilica.IR.Utils;

using VarSet = RefSet<Instruction>;

/// <summary>
/// Liveness analysis for SSA definitions. The current implementation is based on the 
/// path exploration method, which has a complexity of O(numGlobalVars * numCrossedBlocks).
/// 
/// See "Computing Liveness Sets for SSA-Form Programs" (https://hal.inria.fr/inria-00558509v2/document)
/// and section 7.4 of the SSA book.
/// </summary>
public class LivenessAnalysis : IMethodAnalysis, IPrintDecorator
{
    readonly Dictionary<BasicBlock, (VarSet? In, VarSet? Out)> _liveSets = new();

    public LivenessAnalysis(MethodBody method)
    {
        // TODO: This might be actually slower than an iterative data-flow analysis
        var worklist = new ArrayStack<BasicBlock>();
        ProtectedRegionAnalysis? regions = null;
        var protectedBlocks = new Dictionary<BasicBlock, BasicBlock[]>();

        // Visit all instructions defining a value
        foreach (var inst in method.Instructions()) {
            if (!inst.HasResult) continue;

            foreach (var user in inst.Users()) {
                if (user is PhiInst phi) {
                    // Enqueue predecessors for source blocks of this phi
                    foreach (var (pred, val) in phi) {
                        if (val == inst) {
                            // Phi inputs are consumed on the incoming edge.
                            // Marking the merge live-in here would also mark
                            // it visited, suppressing backward propagation for
                            // an ordinary use of this value in the same block.
                            worklist.Push(pred);
                            AddLiveOut(pred, inst);
                        }
                    }
                } else if (user.Block != inst.Block) {
                    worklist.Push(user.Block);
                }
                // Traverse the CFG backwards to propagate liveness
                while (worklist.TryPop(out var userBlock)) {
                    if (inst.Block == userBlock) continue; // Reached the defining block
                    if (!AddLiveIn(userBlock, inst)) continue; // Already propagated

                    foreach (var pred in userBlock.Preds) {
                        AddLiveOut(pred, inst);
                        worklist.Push(pred);
                    }
                    if (userBlock.IsHandlerEntry) {
                        // Guard edges summarize an entire protected region.
                        // A handler can run after any of its blocks, so values
                        // used there must survive the body and nested regions.
                        foreach (var pred in GetProtectedBlocks(userBlock)) {
                            AddLiveOut(pred, inst);
                            worklist.Push(pred);
                        }
                    }
                }
            }
        }

        // We could avoid set lookups by keeping the latest added block in _blockData,
        // but that's not a huge deal.
        bool AddLiveIn(BasicBlock block, Instruction inst)
            => (_liveSets.GetOrAddRef(block).In ??= new()).Add(inst);

        bool AddLiveOut(BasicBlock block, Instruction inst)
            => (_liveSets.GetOrAddRef(block).Out ??= new()).Add(inst);

        BasicBlock[] GetProtectedBlocks(BasicBlock handler)
        {
            var guard = handler.Users().OfType<GuardInst>().First();
            if (protectedBlocks.TryGetValue(handler, out var blocks)) return blocks;

            regions ??= new ProtectedRegionAnalysis(method);
            var pending = new Stack<ProtectedRegion>();
            var result = new List<BasicBlock>();
            pending.Push(regions.GetBlockRegion(guard.Block));
            // A successful filter resumes into its handler. Guard edges do
            // not express that transfer, but handler values must survive it.
            if (guard.HasFilter && handler == guard.HandlerBlock) {
                pending.Push(regions.GetBlockRegion(guard.FilterBlock));
            }
            while (pending.TryPop(out var region)) {
                foreach (var block in region.Blocks) result.Add(block);
                foreach (var child in region.Children) pending.Push(child);
            }
            return protectedBlocks[handler] = result.ToArray();
        }
    }

    static IMethodAnalysis IMethodAnalysis.Create(IMethodAnalysisManager mgr)
        => new LivenessAnalysis(mgr.Method);

    static readonly VarSet s_EmptySet = new(0);

    /// <summary> Returns the set of variables live when <paramref name="block"/> exits. </summary>
    /// <remarks> The returned set may be an empty singleton and it _should not_ be modified. </remarks>
    public VarSet GetLiveOut(BasicBlock block) => _liveSets.GetValueOrDefault(block).Out ?? s_EmptySet;

    /// <summary> Checks if <paramref name="inst"/> is live at the start of <paramref name="block"/>. </summary>
    public bool IsLiveIn(BasicBlock block, Instruction inst)
        => _liveSets.GetValueOrDefault(block).In?.Contains(inst) ?? false;

    /// <summary> Checks if <paramref name="inst"/> is live when <paramref name="block"/> exits. </summary>
    public bool IsLiveOut(BasicBlock block, Instruction inst)
        => _liveSets.GetValueOrDefault(block).Out?.Contains(inst) ?? false;

    /// <summary> Checks if <paramref name="inst"/> is live after <paramref name="pos"/> executes. </summary>
    public bool IsLiveAfter(Instruction inst, Instruction pos)
    {
        if (IsLiveOut(pos.Block, inst)) {
            return true;
        }
        // If `a` is defined or liveIn in the same block as `b`, we need to check if it is used after it
        if (inst.Block == pos.Block || IsLiveIn(pos.Block, inst)) {
            for (var curr = pos; (curr = curr.Next) != null;) {
                if (curr.Operands.ContainsRef(inst)) {
                    return true;
                }
            }
        }
        return false;
    }

    void IPrintDecorator.DecorateEdge(BasicBlock block, BasicBlock succ, ref GraphvizEdgeStyle style)
    {
        var liveOut = GetLiveOut(block);
        if (liveOut.Count == 0) return;

        var sw = new StringWriter();
        var pc = new PrintContext(sw, block.GetSymbolTable()!);

        foreach (var def in liveOut) {
            if (!IsLiveIn(succ, def)) continue;
            
            pc.PrintAsOperand(def);
            pc.Print(" ");
        }
        style.OutLabel = sw.ToString();
    }
}
