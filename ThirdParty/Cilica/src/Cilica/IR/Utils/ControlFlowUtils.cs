namespace Cilica.IR.Utils;

using Cilica.Analysis;

/// <summary> Maintains reachability, derived exception edges, and phi predecessors after CFG edits. </summary>
public static class ControlFlowUtils
{
    public static bool RemoveUnreachableBlocks(MethodBody method, bool foldBranches = false)
        => RemoveUnreachableBlocks(method.EntryBlock, [..method], foldBranches);

    /// <summary> Cleans a complete CFG, which may be a detached clone within another method. </summary>
    internal static bool RemoveUnreachableBlocks(BasicBlock entry, IEnumerable<BasicBlock> blocks, bool foldBranches = false)
    {
        bool changed = false;
        var reachable = new DiscreteStack<BasicBlock>(entry);
        var resumes = new List<ResumeInst>();

        while (reachable.TryPop(out var block)) {
            changed |= foldBranches && ConstFolding.FoldBlockBranch(block);

            if (block.Last is ResumeInst resume) {
                resumes.Add(resume);
                continue;
            }
            foreach (var successor in block.Succs) reachable.Push(successor);
        }

        // A resume is a summary of the protected region's leaves, not an
        // independent path. Rebuild it before removing blocks or repairing phis.
        if (resumes.Count != 0) {
            var regions = new ProtectedRegionAnalysis(entry);
            foreach (var resume in resumes) {
                var targets = regions.GetResumeTargets(resume);
                if (targets.SetEquals(resume.GetExitTargets())) continue;

                var replacement = new ResumeInst(targets, resume.FilterResult) { DebugLocation = resume.DebugLocation };
                resume.Block.ReplaceTerminator(replacement);
                changed = true;
            }
        }

        foreach (var block in blocks) {
            if (reachable.WasPushed(block)) continue;
            block.Remove();
            changed = true;
        }

        // Also handles phi arguments cloned from predecessors that were never
        // materialized. Their empty blocks have no successor edges to redirect.
        foreach (var block in reachable.EnumeratePushed()) {
            if (!block.HasPhis) continue;
            var predecessors = block.Preds.AsEnumerable().ToHashSet();
            foreach (var phi in block.Phis()) {
                for (int i = phi.NumArgs - 1; i >= 0; i--) {
                    if (predecessors.Contains(phi.GetBlock(i))) continue;
                    phi.RemoveArg(i, removeTrivialPhi: true);
                    changed = true;
                    if (phi.Block == null) break;
                }
            }
        }
        return changed;
    }
}
