namespace Cilica;

using Cilica.IR.Utils;
using Cilica.Passes;

/// <summary> Canonical normalization and optimization pipelines for managed IL. </summary>
public static class CompilationPipeline
{
    public static PassManager CreateDefault(Compilation compilation)
    {
        var manager = new PassManager() { Compilation = compilation };
        manager.AddPasses().Apply<SimplifyCFG>().Apply<SsaPromotion>()
            .Apply(new ExpandLinq(compilation.Resolver, deferEnumeratorSources: true)).Apply<SimplifyInsts>();
        manager.AddPasses(applyIndependently: true).Apply<Devirtualize>().Apply<InlineMethods>();
        manager.AddPasses().Apply<ExpandLinq>().Apply<SimplifyInsts>();
        manager.AddPasses(applyIndependently: true).Apply<Devirtualize>().Apply<InlineMethods>();
        var simplify = manager.AddPasses().Apply<SimplifyInsts>().Apply<SimplifyCFG>().Apply<DeadCodeElim>()
            .RepeatUntilFixedPoint(3);
        manager.AddPasses().Apply<ScalarReplacement>()
            .IfChanged(child => child.Apply<SsaPromotion>().Apply<InlineMethods>()).RepeatUntilFixedPoint(3);
        manager.AddPasses().Apply<ValueNumbering>().Apply<AssertionProp>().Apply<PresizeLists>().Apply<LoopStrengthReduction>().IfChanged(simplify);
        manager.AddPasses().Apply<LoopVectorizer>().IfChanged(simplify);

        return manager;
    }

    /// <summary> Produces reachable SSA IR suitable for interprocedural analysis and inlining. </summary>
    public static void Normalize(MethodTransformContext context, bool inlineConstructors = false)
    {
        DeadCodeElim.RemoveUnreachableBlocks(context.Method);
        PassRunner.Run(new SimplifyCFG(), context);
        PassRunner.Run(new SsaPromotion(), context);

        if (inlineConstructors) {
            PassRunner.Run(new InlineMethods(), context);
        }

        IRVerifier.Verify(context.Method);
    }
}
