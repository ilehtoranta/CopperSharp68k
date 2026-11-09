/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using Cilica;
using Cilica.Passes;

namespace CopperSharp.Tools;

/// <summary>Explicitly selected scalar IL optimizations for CopperSharp inputs.</summary>
public static class RestrictedPipeline
{
    public const string SourceRevision = "a879c85f1608c764c7ab4f52b66b2b5db1ee60b6";

    public static CompilationSettings CreateSettings(bool enableInlining = true) => new() {
        VerifyEachPass = true,
        AssumeLittleEndian = false,
        AllowCrossAssemblyIPO = false,
        EnableInlining = enableInlining,
        EnableScalarReplacement = true
    };

    public static PassManager Create(Compilation compilation)
    {
        if (compilation.Settings.AssumeLittleEndian || compilation.Settings.AllowCrossAssemblyIPO ||
            !compilation.Settings.VerifyEachPass)
            throw new ArgumentException("Restricted optimization requires target-independent settings and verification after every pass.", nameof(compilation));

        // Construct an allowlist rather than removing positions from the default
        // pipeline. New upstream passes cannot silently enter this profile.
        var manager = new PassManager { Compilation = compilation };
        manager.AddPasses().Apply<SimplifyCFG>().Apply<SsaPromotion>().Apply<SimplifyInsts>();
        manager.AddPasses(applyIndependently: true).Apply<Devirtualize>().Apply<InlineMethods>();
        manager.AddPasses().Apply<SimplifyInsts>();
        manager.AddPasses(applyIndependently: true).Apply<Devirtualize>().Apply<InlineMethods>();
        var simplify = manager.AddPasses().Apply<SimplifyInsts>().Apply<SimplifyCFG>().Apply<DeadCodeElim>()
            .RepeatUntilFixedPoint(3);
        manager.AddPasses().Apply<ScalarReplacement>()
            .IfChanged(child => child.Apply<SsaPromotion>().Apply<InlineMethods>()).RepeatUntilFixedPoint(3);
        manager.AddPasses().Apply<ValueNumbering>().Apply<AssertionProp>().IfChanged(simplify);
        // No ExpandLinq, PresizeLists, LoopStrengthReduction or LoopVectorizer.
        return manager;
    }
}
