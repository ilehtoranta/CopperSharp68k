namespace Cilica.Passes;

using Cilica.Analysis;

public interface IMethodPass
{
    MethodPassResult Run(MethodTransformContext ctx);

    /// <summary> Creates the pass instance through the default constructor of <typeparamref name="TSelf"/>. </summary>
    /// <remarks> Derived classes that don't have a default constructor _must_ implement this method. </remarks>
    static virtual IMethodPass Create<TSelf>(Compilation comp) where TSelf : IMethodPass
        => Activator.CreateInstance<TSelf>();
}

public readonly struct MethodPassResult
{
    public MethodInvalidations Changes { get; init; }

    public void InvalidateAffectedAnalyses(MethodTransformContext ctx)
    {
        ctx.Invalidate(Changes);
    }

    public static implicit operator MethodPassResult(MethodInvalidations changes)
        => new() { Changes = changes };
}
[Flags]
public enum MethodInvalidations
{
    None                = 0,
    Everything          = ~0,

    DataFlow            = 1 << 0,
    ControlFlow         = (1 << 1) | DataFlow,
    Loops               = (1 << 2) | ControlFlow,  // ControlFlow changes may imply loop changes, this is mostly for granularity. 
    ExceptionRegions    = (1 << 3) | ControlFlow,  // Likewise, ControlFlow changes may imply EHReg changes.
}

public class MethodTransformContext : IMethodAnalysisManager
{
    public Compilation Compilation { get; }
    public MethodBody Method { get; }

    public MethodDef Definition => Method.Definition;
    public ModuleDef Module => Method.Definition.Module;

    public ICompilationLogger Logger => Compilation.Logger;

    readonly AnalysisCache<IMethodAnalysis> _analyses = new();
    long _revision, _controlFlowRevision;

    public long AnalysisCacheHits => _analyses.Hits;

    public long AnalysisBuilds => _analyses.Builds;

    public MethodTransformContext(Compilation comp, MethodBody method)
    {
        Compilation = comp;
        Method = method;
        _revision = method.Revision;
        _controlFlowRevision = method.ControlFlowRevision;
    }

    public A GetAnalysis<A>() where A : IMethodAnalysis
    {
        if (_revision != Method.Revision) {
            Invalidate(_controlFlowRevision != Method.ControlFlowRevision
                ? MethodInvalidations.ControlFlow : MethodInvalidations.DataFlow);
        }

        return (A)_analyses.Get(typeof(A), () => A.Create(this));
    }

    public void Invalidate<A>() where A : IMethodAnalysis
    {
        _analyses.Invalidate(typeof(A));
    }

    public void InvalidateAll()
    {
        _analyses.Clear();
    }

    internal void Invalidate(MethodInvalidations changes)
    {
        if (changes == MethodInvalidations.None) {
            return;
        }

        bool controlFlow = changes.HasFlag(MethodInvalidations.ControlFlow);
        _analyses.Invalidate((_, analysis) => analysis.IsInvalidatedBy(controlFlow));
        Compilation.InvalidateAnalyses(Definition);
        _revision = Method.Revision;
        _controlFlowRevision = Method.ControlFlowRevision;
    }
}
