namespace Cilica.Analysis;

internal static class ControlFlowFacts
{
    public static bool HasCycle(MethodBody method)
    {
        var ancestors = new HashSet<BasicBlock>();
        bool cyclic = false;
        method.TraverseDepthFirst(preVisit: block => {
            ancestors.Add(block);
            cyclic |= block.Succs.Any(ancestors.Contains);
        }, postVisit: block => ancestors.Remove(block));

        return cyclic;
    }
}
