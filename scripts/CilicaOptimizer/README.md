# Restricted CILica preprocessing

The CopperSharp SDK runs this preprocessing step by default during Amiga native
build and publish. Set `CopperSharpCilica=false` to opt out. The standalone
wrapper below is also available when driving `copper68kc` directly. The core
compiler does not reference CILica.

```powershell
./scripts/optimize-il.ps1 `
  -InputAssembly ./MyApp/bin/Release/net10.0/MyApp.dll `
  -OutputAssembly ./artifacts/optimized/MyApp.dll
```

The wrapper builds the standalone tool against `Cilica.Core` 0.20.2-preview
and writes an optimization report alongside the output DLL
(`MyApp.dll.cilica.json`). It uses the repository's pinned SDK by default;
`-SdkVersion 10.0.401` explicitly selects the SDK used for the retained audit.
`-ResolverPaths <directory[]>` supplies additional dependency locations.
Use `-Inlining off` for a ROM build; scalar replacement remains enabled.

Use the original dependency assemblies when compiling the rewritten application.
Only the selected DLL is rewritten; dependencies are not copied to the output
directory. Supply them through the compiler's `--managed-assemblies` manifest
when they are not beside the rewritten DLL. Preserve the original DLL filename.
Input and output must be separate files. Debug symbols are not emitted.
Signed/ReadyToRun assemblies are subject to CILica's default writer restrictions;
this tool does not strip signatures or native images automatically.

The CopperSharp SDK NuGet package includes this standalone optimizer. The tool
can also be invoked directly after building its project:

```text
dotnet CilicaOptimizer.dll input/MyApp.dll output/MyApp.dll --resolver dependencies
```

## Pipeline

`RestrictedPipeline` constructs an explicit pass allowlist. It does not inherit
new passes from the upstream default pipeline. The enabled transforms are CFG
and scalar simplification, SSA promotion, local inlining, devirtualization,
scalar replacement, dead-code elimination, value numbering and assertion
propagation. Existing fixed-point and inlining budgets remain bounded.

The configuration sets:

```csharp
AssumeLittleEndian = false;
AllowCrossAssemblyIPO = false;
VerifyEachPass = true;
```

LINQ expansion, loop strength reduction, list presizing and vectorization are
excluded. This preserves the compiler's original LINQ/framework call shapes
and avoids the new array-data/exception-helper dependencies found by the audit.
List-presizing speedups from the broader pipeline are intentionally absent.

Rewriting selects methods in the input module only. Any CILica import,
optimization or emission failure aborts the rewrite. The output DLL is replaced
only after a complete successful save; the input is never rewritten in place.

This is an experimental restricted profile, enabled by default for Amiga native
builds through the CopperSharp SDK. The normal compiler still validates its
reachable framework surface and rejects unsupported IL or direct RVA storage.
Passing these focused benchmarks does not establish compatibility with
arbitrary .NET applications.

## Benchmark

The audit uses this same `RestrictedPipeline` implementation, rather than a
separate approximation. Run the baseline/portable/restricted comparison with:

```powershell
./scripts/CilicaAudit/run.ps1 -Restricted `
  -ResultsDirectory TestResults/CilicaRestrictedRepeat -SdkVersion 10.0.401
```

The run checks target results, repeated image/cycle stability, compatibility
against every baseline-supported kernel, and scalar-replacement/devirtualization
speedups against both baseline and the earlier portable profile. Raw artifacts
and `kernels/restricted-summary.json` are retained in the requested directory.
The two bundled applications are compilation/size probes only.
