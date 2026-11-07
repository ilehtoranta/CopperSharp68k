# CILica codegen audit — 2026-11-07

**CILica can materially improve selected CopperSharp programs, but should not be
enabled globally.** Local scalar replacement and devirtualization are the most
promising improvements to bring into our compiler. Even a constrained CILica
pipeline regressed compatibility for two previously supported kernels.

This directory contains the executable audit, its fixtures, and the retained
[machine-readable summary](results-summary.json). Production projects do not
reference CILica.

## Restricted opt-in follow-up — 2026-11-07

The standalone [restricted preprocessor](../CilicaOptimizer/README.md) is now
available through `scripts/optimize-il.ps1`. Its explicit pass allowlist omits
LINQ expansion, loop strength reduction, list presizing and vectorization, with
cross-assembly IPO and little-endian assumptions disabled. It is never invoked
by ordinary application builds. The audit and standalone tool share the same
pipeline implementation.

A fresh run against compiler source commit `4fedfc9b71ffc3b8e36822bb68cd740574508fd8`
compared baseline, the earlier portable profile, and restricted output. Three
recorded samples followed a warmup for each kernel/CPU/variant; tiered host
compilation was disabled. The retained
[restricted summary](restricted-results-summary.json) includes input/compiler
hashes, compatibility outcomes, loaded sizes and cycle comparisons.

| Kernel | CPU | Baseline cycles | Restricted cycles | Speedup |
| --- | --- | ---: | ---: | ---: |
| Scalar replacement | MC68000 | 2,092 | 704 | 2.97× |
| Scalar replacement | MC68020 | 470 | 162 | 2.90× |
| Scalar replacement | MC68040 | 181 | 55 | 3.29× |
| Devirtualization | MC68000 | 2,650 | 716 | 3.70× |
| Devirtualization | MC68020 | 608 | 160 | 3.80× |
| Devirtualization | MC68040 | 219 | 55 | 3.98× |

These are complete cold-invocation interpreter cycles at input 31, including
startup. Both kernels have exactly the same measured cycles as portable output
on every CPU. Their restricted IL contains arithmetic with no allocation or
virtual calls. The standalone wrapper's output was byte-identical to the
benchmarked rewrite: SHA-256
`93CA8925601BB5C7954F23E23CCF43F4DB766A88E62E6B308D04D96B42D687D5`.
An attempted in-place rewrite was rejected and left the input hash unchanged.

Restricted compiled and passed **all 14 baseline-supported kernels on all three
CPUs**, recovering the ArrayLoop and Range.Select.Sum cases rejected by portable.
Its **720 checked executions** include all three recorded samples and scalar
edge inputs. Across the three variants, 2,088 executions passed; all 120
successful kernel/CPU/variant groups had stable image hashes and cycle results.
Restricted introduced no compatibility regressions, and no successful variant
produced a wrong result. The original three
unsupported kernels (Range.Where.Select.Sum, volatile IL and checked multiply)
remain unsupported in baseline and restricted output.

Disabling list presizing deliberately loses its earlier speedup: MC68000
ListGrowth at input 31 uses 37,632 cycles restricted versus 37,690 baseline and
23,668 portable. Struct promotion still trades 1,188 baseline cycles for 1,214
restricted cycles on MC68000. This profile preserves the requested gains, but
does not promise every method becomes faster.

Both bundled applications compile under restricted preprocessing. Their loaded
sizes match portable output: Polymorphism is 4,872/4,872/4,776 bytes and
StopwatchBenchmark is 12,028/11,872/11,724 bytes for MC68000/020/040. These remain
compilation/size probes, not application execution benchmarks.

Reproduce with:

```powershell
./scripts/CilicaAudit/run.ps1 -Restricted -SdkVersion 10.0.401 `
  -ResultsDirectory TestResults/CilicaRestrictedRepeat
```

`summarize-restricted.ps1` checks stability, compatibility against baseline and
retention of both gains against portable, and fails the run if any check fails.
Raw evidence for this run is in `TestResults/CilicaRestrictedVerified`. The
original broader-pipeline audit below is retained as historical evidence.

## Volatile support follow-up — 2026-11-07

CopperSharp now supports scalar/reference CIL `volatile.` accesses; the earlier
volatile compilation failures below are historical. With this implementation,
the original `Volatile` kernel returns the expected **3** at input 1 in both
baseline and restricted assemblies on MC68000/020/040 (six executions). This
follow-up checks compatibility and result correctness; the retained benchmark
summaries and cycle measurements have not been regenerated.

See [volatile memory support](../../Compiler/VolatileMemory.md) for semantics,
scope, optimization tradeoffs and execution-test coverage. Local probe output
is retained under `TestResults/VolatileSupport`.

## Inputs and measurement

Upstream: [hez2010/Cilica](https://github.com/hez2010/Cilica), commit
[`a879c85f1608c764c7ab4f52b66b2b5db1ee60b6`](https://github.com/hez2010/Cilica/tree/a879c85f1608c764c7ab4f52b66b2b5db1ee60b6).
This audits that source revision, not an unspecified NuGet preview.

The local compiler was built once from the working checkout and held fixed as
the checkout changed concurrently. Compiler DLL SHA-256:
`75DD8033D197E70FBB4FFF2E653FD03C8B22BCB8A2011D1584C8E9A89CCAA7FD`.
The source checkout had uncommitted codegen work; these are measurements of that
binary, not a claim about a clean Git revision. SDK 10.0.401 and Windows x64
runtime 10.0.12 were available; the repository's pinned SDK 10.0.301 was absent.
A private `global.json` selected the available SDK without modifying the root.

Seventeen Release-built kernels were compiled for MC68000, MC68020 and MC68040,
with four input variants:

| Variant | Configuration |
| --- | --- |
| baseline | Original Roslyn IL |
| default | Upstream `CompilationPipeline.CreateDefault` |
| scalar | Default pipeline with its final vectorization segment removed |
| portable | Scalar pipeline; `AssumeLittleEndian=false`, `AllowCrossAssemblyIPO=false` |

The Core API driver uses the CLI's main-module candidate filter. Its default
output was also compared against a real `Cilica.Cli --strict --verify-each
--no-pdb-gen` invocation and was byte-for-byte identical (SHA-256
`6C764D6A09E511F7367BB708D918DBEB16B8E2FA69E84FB1427B8EE67EE414D7`).

Every rewrite used `VerifyEachPass=true` and rejected partial optimization
failures. Those checks verify CILica IR consistency; they do not prove target
semantics. No upstream source modifications were needed. Removing the final
pipeline segment depends on the pinned revision's pipeline layout.

Each kernel/CPU/variant received one unrecorded compile warmup and three recorded
compiles, reversing variant order on alternate rounds. Every recorded image was
executed on the Copper68k 1.5.4 interpreter with runtime inputs from guest RAM,
checking independent expected results and stack restoration. Inputs were
1, 7, 13 and 31, plus -1, `int.MinValue`, and `int.MaxValue` for eight scalar
kernels. Every execution began from a fresh bus/CPU. Full exceptions and a real
managed pool were enabled, including startup costs in cycle counts.

There were **2,484 successful, checked target executions**, including repeated
samples, and six recorded wrong-result cases (default/scalar switch table on
three CPUs). All 135 successful kernel/CPU/variant groups had identical image
hashes and execution metrics across their three samples. Failed combinations
are retained in the summary rather than excluded from the compatibility result.
This is a focused audit, not a complete compiler regression suite or a hardware
timing measurement.

## Measured benefit

The following compares baseline against **portable**, at input 31. Loaded bytes
include linked code, runtime and data, rather than just the kernel. Cycles are
emulator measurements of the complete cold invocation.

| Kernel | MC68000 cycles | Speedup | MC68000 loaded bytes | MC68020 speedup | MC68040 speedup |
| --- | ---: | ---: | ---: | ---: | ---: |
| Local object scalar replacement | 2,092 → 704 | 2.97× | 3,244 → 2,932 | 2.90× | 3.29× |
| Known interface implementation | 2,650 → 716 | 3.70× | 3,508 → 2,936 | 3.80× | 3.98× |
| Repeated small helper inlining | 1,324 → 836 | 1.58× | 3,116 → 2,980 | 1.13× | 1.10× |
| List growth | 37,690 → 23,668 | 1.59× | 4,944 → 5,156 | 1.60× | 1.57× |
| Struct promotion | 1,188 → 1,214 | 0.98× | 3,084 → 3,052 | 1.02× | 1.03× |

IL inspection confirms that scalar replacement removes the local object's
allocation and constructor entirely, and list presizing changes the default
constructor to the capacity constructor. The devirtualization case combines
several optimizations; its speedup cannot be attributed to dispatch removal
alone. List presizing trades roughly 4% more loaded bytes for fewer cycles.
Struct promotion demonstrates that smaller output can run slower on MC68000.

Preprocessing the small fixture assembly took about 0.38 s default, 0.37 s scalar,
and 0.33 s portable in individual runs. Compile-only timings are retained per
sample, but exclude preprocessing. They are descriptive: host tiering was left
at its ambient setting, the three samples do not establish significance, and
the audit does not establish a compiler throughput improvement. The reproduction
script disables tiering for future runs, so its timing conditions differ.

## Compatibility and correctness findings

| Probe | Baseline | Default | Scalar | Portable |
| --- | --- | --- | --- | --- |
| Successful kernel cases, out of 17, on each CPU | 14 | 9 | 10 | 12 |
| Integer loop | Pass | Vector call rejected | Pass | Pass |
| Array loop | Pass | Rejected | Rejected | Rejected |
| Range.Select.Sum | Pass | Rejected | Rejected | Rejected |
| Catch/finally | Pass | External field rejected | External field rejected | Pass |
| Switch table | Pass | **Wrong result** | **Wrong result** | Pass |
| Range.Where.Select.Sum | Rejected | Rejected | Rejected | Rejected |
| CIL volatile prefix | Rejected | Rejected | Rejected | Rejected |
| Checked multiply | Emission fails | Emission fails | Emission fails | Emission fails |

* **Endianness and initialized static data:** upstream defaults
  `AssumeLittleEndian` to true. `SimplifyCFG.ConvertSwitchToLut` writes a
  little-endian RVA lookup table and emits an indirect load. On our pipeline,
  `SwitchTable(1)` then returns **0 instead of 9,029** on all three CPUs. This
  demonstrates an integration correctness failure; the exact zero result is
  not explained by byte order alone. RVA initialization/lowering would need
  investigation as well. Disabling little-endian assumptions avoids this
  transformation and the portable case passes.
* **Host framework inlining:** default/scalar inline host framework bodies
  before CopperSharp can bind them to its target runtime. In the bundled
  `StopwatchBenchmark`, this exposes Windows `QueryPerformanceCounter`; native
  compilation rejects it. The local exception constructor also acquires
  unsupported framework fields. Disabling cross-assembly IPO fixes both probes.
* **Vectorization:** a supported scalar loop gains `Vector256`/`Vector128`
  operations. Our closed-world analysis does not admit those calls; a runtime
  hardware guard does not make the transformed graph compile. The standard CLI
  has no vector-pass selection option at this revision; the audit uses Core APIs.
* **Other passes introduce framework surface too:** even portable ArrayLoop
  adds `MemoryMarshal.GetArrayDataReference<int>`, and LINQ expansion adds
  `ArgumentOutOfRangeException.ThrowIfNegative<int>` and related helpers. They
  lie outside the compilation profile used here. Disabling cross-assembly IPO
  does not prevent passes from explicitly introducing these calls.
* **Existing backend limitations remain separate:** the volatile and checked
  multiply failures also occur in baseline. CILica retains all four `volatile.`
  prefixes in the volatile probe; we cannot claim target execution validation
  for them. No floating-point, native-call ABI, GC stress, interrupt/MMIO, or
  broad aliasing correctness conclusion follows from this suite.

The source evidence is in upstream
[CompilationSettings](https://github.com/hez2010/Cilica/blob/a879c85f1608c764c7ab4f52b66b2b5db1ee60b6/src/Cilica/Compilation.cs),
[pipeline construction](https://github.com/hez2010/Cilica/blob/a879c85f1608c764c7ab4f52b66b2b5db1ee60b6/src/Cilica/CompilationPipeline.cs), and
[switch lookup lowering](https://github.com/hez2010/Cilica/blob/a879c85f1608c764c7ab4f52b66b2b5db1ee60b6/src/Cilica/Passes/SimplifyCFG.cs).

## Silent wrong-code follow-up

The zero result was traced to CopperSharp's ordinary static-field emission.
`CompilationModule.GetField` discarded the `HasFieldRVA` attribute, and
`EmitZeroInitializedStatic` emitted zeros for every materialized static field,
including CILica's initialized lookup table. This was not a peephole rewrite.
A minimal independent probe reads one byte at offset two of the same table;
before the safeguard it returned 0 instead of `0x45` on MC68000/020/040.
That isolates lost initialization from multi-byte endianness.

The compiler now retains the RVA-initializer flag and reports `C68K0004` if an
RVA field reaches ordinary zero-initialized static storage. It does not guess
an element size or silently swap arbitrary blobs. Existing initialized-array,
initialized-span and verified byte-span paths keep their explicit data handling.
General direct RVA-field support remains unimplemented.

The original default/scalar switch assemblies now fail compilation with the
named RVA field; baseline and portable assemblies still return 9,029 on all
three CPUs. CILica's little-endian lookup lowering remains independently
unsuitable for this target. The original benchmark measurements above and in
`results-summary.json` are retained as historical evidence.

Regression coverage is in
[`InitializedStaticFieldTests`](../../Compiler.Tests/InitializedStaticFieldTests.cs),
including local and cross-assembly field references, optimized and unoptimized
output, and HUNK/assembly output. Investigation logs are retained locally in
`TestResults/RvaStaticInvestigation-*.log`.

## Bundled applications

These were **compiled and sized only**, since they use the native Amiga startup
and OS contract. They were not executed on the simple kernel bus.

| Application | MC68000 loaded bytes, baseline → portable | MC68020 | MC68040 |
| --- | ---: | ---: | ---: |
| Polymorphism | 4,896 → 4,872 | 4,912 → 4,872 | 4,816 → 4,776 |
| StopwatchBenchmark | 12,016 → 12,028 | 11,860 → 11,872 | 11,716 → 11,724 |

The tiny gains/regressions in these applications do not support extrapolating
the large microkernel speedups to complete applications.

## Recommended next work

1. Use the local allocation and interface fixtures to guide native escape
   analysis, scalar replacement and concrete-type propagation. These gave the
   strongest target gains while avoiding dependencies on host framework IL.
2. Improve profitable local inlining for repeated small callees. Retain a 68k
   cost model: helper inlining helped, while struct promotion slowed MC68000.
3. Consider list presizing in the admitted framework path, with explicit
   allocation-failure semantics and a size/cycle tradeoff policy.
4. If external preprocessing is pursued, keep it opt-in and method-scoped; add
   target-safe pass selection, dependency validation, big-endian RVA handling,
   and full native regression coverage first. The portable variant is a useful
   experiment, not a supported or certified portable profile.

No production CILica integration is justified by this audit yet.

## Reproduce

From the repository root, with the pinned SDK installed:

```powershell
./scripts/CilicaAudit/run.ps1 -ResultsDirectory TestResults/CilicaAuditRepeat
```

To use the SDK available on the audited machine explicitly:

```powershell
./scripts/CilicaAudit/run.ps1 -ResultsDirectory TestResults/CilicaAuditRepeat401 -SdkVersion 10.0.401
```

The script requires a fresh directory, pins the upstream commit, builds separate
artifacts, and retains compiler state information. It writes rewritten DLLs,
rewrite status for every selected/imported method, IL listings, input hashes,
sample JSON, HUNK images, and maps. Compile/execute incompatibilities are data
in `samples.json`; they do not make this investigative driver exit nonzero.
A rewrite failure stops the run instead of accepting partial optimization.

The original local evidence is under `TestResults/CilicaAudit/verified-results`,
`verified-example-Polymorphism`, and `verified-example-StopwatchBenchmark`; IL listings
are in their parent directory. These raw artifacts are ignored by Git. The
summary here preserves the measured comparisons and diagnostics.
