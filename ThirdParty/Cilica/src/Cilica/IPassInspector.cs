namespace Cilica;

using Cilica.Passes;

public interface IPassInspector
{
    void OnBeforePass(IMethodPass pass, MethodTransformContext ctx) { }
    void OnAfterPass(IMethodPass pass, MethodTransformContext ctx, MethodPassResult result) { }

    void OnPassFailed(IMethodPass pass, MethodTransformContext ctx, Exception error) { }

    void OnBegin(Compilation comp) { }
    void OnFinish(Compilation comp) { }
}

public class PassTimingInspector(bool logStatsOnFinish = true) : IPassInspector
{
    public Dictionary<Type, PassStatistics> Stats { get; } = new();

    long _startTs, _allocatedBefore, _buildsBefore, _hitsBefore;
    IMethodPass? _activePass;

    void IPassInspector.OnBeforePass(IMethodPass pass, MethodTransformContext ctx)
    {
        _activePass = pass;
        _startTs = Stopwatch.GetTimestamp();
        _allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        _buildsBefore = ctx.AnalysisBuilds;
        _hitsBefore = ctx.AnalysisCacheHits;
    }

    void IPassInspector.OnAfterPass(IMethodPass pass, MethodTransformContext ctx, MethodPassResult result)
    {
        if (_activePass == null) {
            throw new InvalidOperationException();
        }

        Finish(pass, ctx, result.Changes != MethodInvalidations.None, failed: false);
    }

    void IPassInspector.OnFinish(Compilation comp)
    {
        if (logStatsOnFinish) {
            LogStats(comp.Logger);
        }
    }

    void IPassInspector.OnPassFailed(IMethodPass pass, MethodTransformContext ctx, Exception error)
    {
        if (_activePass != null) {
            Finish(pass, ctx, changed: false, failed: true);
        }
    }

    public void IncrementStat(Type passType, long startTs, int numChanges, int invocations = 1, long allocatedBytes = 0)
    {
        var stat = Stats.GetOrAddRef(passType) ??= new();
        stat.TimeTaken += Stopwatch.GetElapsedTime(startTs);
        stat.ChangedInvocations += numChanges;
        stat.Invocations += invocations;
        stat.AllocatedBytes += allocatedBytes;
    }

    private void Finish(IMethodPass pass, MethodTransformContext ctx, bool changed, bool failed)
    {
        IncrementStat(pass.GetType(), _startTs, changed ? 1 : 0);
        var stat = Stats[pass.GetType()];
        stat.Failures += failed ? 1 : 0;
        stat.AllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - _allocatedBefore;
        stat.AnalysisBuilds += ctx.AnalysisBuilds - _buildsBefore;
        stat.AnalysisCacheHits += ctx.AnalysisCacheHits - _hitsBefore;
        _activePass = null;
    }

    public void LogStats(ICompilationLogger logger)
    {
        using var scope = logger.Push(new("Cilica.PassManager.ResultStats"), "Pass statistics");
        var totalTime = TimeSpan.Zero;

        foreach (var (key, stats) in Stats) {
            logger.Info($"{key.Name}: {stats.Invocations} runs, {stats.ChangedInvocations} changed, {stats.Failures} failed; " +
                $"{stats.TimeTaken.TotalMilliseconds:0.00} ms, {stats.AllocatedBytes / 1024.0:0.0} KiB; " +
                $"analyses {stats.AnalysisBuilds} built, {stats.AnalysisCacheHits} reused");
            totalTime += stats.TimeTaken;
        }

        logger.Info($"-- Total time: {totalTime.TotalSeconds:0.00}s");
    }
}

public sealed class PassStatistics
{
    public int Invocations { get; internal set; }

    public int ChangedInvocations { get; internal set; }

    public int Failures { get; internal set; }

    public TimeSpan TimeTaken { get; internal set; }

    public long AllocatedBytes { get; internal set; }

    public long AnalysisBuilds { get; internal set; }

    public long AnalysisCacheHits { get; internal set; }
}
