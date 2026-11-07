namespace Cilica.Analysis;

public class ProtectedRegionAnalysis : IMethodAnalysis
{
    public bool IsInvalidatedBy(bool controlFlowChanged) => controlFlowChanged;

    readonly Dictionary<BasicBlock, ProtectedRegion> _regions = new();

    public ProtectedRegion Root { get; }

    public ProtectedRegionAnalysis(MethodBody method) : this(method.EntryBlock) { }

    /// <summary> Analyzes a complete CFG, including a detached clone rooted at <paramref name="entry"/>. </summary>
    public ProtectedRegionAnalysis(BasicBlock entry)
    {
        Root = new ProtectedRegion() { StartBlock = entry };

        var visited = new RefSet<BasicBlock>();
        var worklist = new ArrayStack<(BasicBlock Block, ProtectedRegion Region)>();

        worklist.Push((entry, Root));
        visited.Add(entry);

        while (worklist.TryPop(out var node)) {
            var (block, region) = node;

            // Push sub regions
            // Note that handler/filters can't have guards, the second condition is fine.
            if (block.Guards().Any() && block != region.StartBlock) {
                visited.Remove(block);
                PushChild(block);

                foreach (var guard in block.Guards()) {
                    if (guard.HasFilter) {
                        PushChild(guard.FilterBlock);
                    }
                    PushChild(guard.HandlerBlock);
                }
                continue;
            }
            region.Blocks.Add(block);
            _regions.Add(block, region);

            // Resume edges are derived from leaves and do not establish reachability.
            if (block.Last is ResumeInst) continue;

            // Add succs to worklist
            var succRegion = block.Last is LeaveInst ? region.Parent! : region;
            foreach (var succ in block.Succs) {
                if (visited.Add(succ)) {
                    worklist.Push((succ, succRegion));
                }
            }

            void PushChild(BasicBlock entry)
            {
                if (!visited.Add(entry)) return;

                var child = new ProtectedRegion() {
                    StartBlock = entry,
                    Parent = region
                };
                region.Children.Add(child);
                worklist.Push((entry, child));
            }
        }
    }

    static IMethodAnalysis IMethodAnalysis.Create(IMethodAnalysisManager mgr)
        => new ProtectedRegionAnalysis(mgr.Method);

    public ProtectedRegion GetBlockRegion(BasicBlock block)
        => _regions.TryGetValue(block, out var region) ? region
            : throw new InvalidOperationException("Block is not part of this region analysis");

    /// <summary> Derives a handler's continuation targets from its reachable protected exits. </summary>
    public HashSet<BasicBlock> GetResumeTargets(ResumeInst resume)
    {
        var guard = Ensure.NotNull(GetBlockRegion(resume.Block).GetHandlerGuard());
        return GetBlockRegion(guard.Block).GetExitBlocks()
            .Select(block => ((LeaveInst)block.Last).Target).ToHashSet();
    }
}
public class ProtectedRegion
{
    public RefSet<BasicBlock> Blocks { get; } = new();
    /// <remarks> Guaranteed to be ordered as: [protectedRegion, filterRegion?, handlerRegion] pairs/triples for each guard in <see cref="StartBlock"/>. </remarks>
    public List<ProtectedRegion> Children { get; } = new();
    public ProtectedRegion? Parent { get; set; }

    public BasicBlock StartBlock { get; set; } = null!;

    public IEnumerable<BasicBlock> GetExitBlocks()
    {
        foreach (var block in Blocks) {
            if (block.Last is LeaveInst or ResumeInst) {
                yield return block;
            }
        }
    }

    public ProtectedRegion? FindInnermostParent(BasicBlock block)
    {
        if (Blocks.Contains(block)) {
            return this;
        }
        foreach (var child in Children) {
            if (child.FindInnermostParent(block) is { } region) {
                return region;
            }
        }
        return null;
    }

    /// <summary> If this is a handler region, returns the guard defining it. </summary>
    public GuardInst? GetHandlerGuard() => StartBlock.Users().OfType<GuardInst>().FirstOrDefault();
}
