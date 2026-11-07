namespace Cilica.Analysis;

public interface IAnalysis
{
}
public interface IGlobalAnalysis : IAnalysis
{
    static virtual IGlobalAnalysis Create(Compilation comp) => throw new NotImplementedException();

    void Invalidate(MethodDef method) { }
}
public interface IMethodAnalysis : IAnalysis
{
    static virtual IMethodAnalysis Create(IMethodAnalysisManager mgr) => throw new NotImplementedException();

    /// <summary> Whether this analysis becomes stale after an instruction-only edit or a CFG edit. </summary>
    bool IsInvalidatedBy(bool controlFlowChanged) => true;
}
public interface IMethodAnalysisManager
{
    Compilation Compilation { get; }
    MethodBody Method { get; }

    A GetAnalysis<A>() where A : IMethodAnalysis;

    /// <summary> Invalidates all currently cached analyses. </summary>
    void InvalidateAll();
}
