namespace Cilica.Passes;

using Cilica.IR.Utils;

/// <summary> Executes a method pass with change tracking, invalidation, verification, and inspection. </summary>
public static class PassRunner
{
    public static MethodPassResult Run(IMethodPass pass, MethodTransformContext ctx, ReadOnlySpan<IPassInspector> inspectors = default)
    {
        long revision = ctx.Method.Revision;
        long controlFlowRevision = ctx.Method.ControlFlowRevision;
        bool trace = ctx.Logger.IsEnabled(LogLevel.Trace);
        using var scope = trace
            ? ctx.Logger.Push(new("Cilica.Pass"), $"{pass.GetType().Name} on '{ctx.Definition}'")
            : default;

        try {
            foreach (var inspector in inspectors) {
                inspector.OnBeforePass(pass, ctx);
            }

            var result = pass.Run(ctx);
            result = ctx.Method.Revision == revision ? MethodInvalidations.None
                : result.Changes | (ctx.Method.ControlFlowRevision != controlFlowRevision
                    ? MethodInvalidations.ControlFlow : MethodInvalidations.DataFlow);
            result.InvalidateAffectedAnalyses(ctx);

            if (ctx.Compilation.Settings.VerifyEachPass) {
                IRVerifier.Verify(ctx.Method);
            }

            foreach (var inspector in inspectors) {
                inspector.OnAfterPass(pass, ctx, result);
            }

            return result;
        } catch (Exception error) {
            ctx.Logger.Trace($"Failed: {error.GetType().Name}: {error.Message}");
            ctx.InvalidateAll();
            ctx.Compilation.InvalidateAnalyses(ctx.Definition);

            foreach (var inspector in inspectors) {
                inspector.OnPassFailed(pass, ctx, error);
            }

            throw;
        }
    }
}
