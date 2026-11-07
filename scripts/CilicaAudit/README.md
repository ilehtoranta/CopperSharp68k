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

## Restricted benchmark rerun with volatile — 2026-11-07

A complete fresh run from clean compiler commit
`561bc80c22fff8d2217c7fa43f7422822263869b` used the same pinned CILica revision,
SDK 10.0.401, three recorded samples after warmup, and baseline/portable/restricted
variants on MC68000/020/040. The
[new machine-readable summary](restricted-volatile-results-summary.json)
retains compiler/input hashes, coverage, failures, loaded sizes and cycle counts.
The earlier summaries above remain historical.

Restricted now compiles and passes **all 15 baseline-supported kernels on all
three CPUs**, including volatile. All **756 restricted executions** passed;
**2,196 executions** passed across the three variants. All 129 successful
kernel/CPU/variant groups have stable image hashes and per-input results/cycles.
There are **no restricted compatibility regressions or incorrect results**.
Range.Where.Select.Sum and checked multiply remain unsupported in baseline and
restricted output. Portable retains its existing ArrayLoop and Range.Select.Sum
compilation failures.

All six scalar-replacement/devirtualization cycle counts and speedups in the
earlier table are unchanged, and still equal portable output. All **42 earlier
baseline/restricted kernel/CPU comparisons** retain exactly the same cycle counts
and loaded sizes. Both bundled applications compile with unchanged loaded sizes;
they remain compilation/size probes only.

The volatile kernel passed all four inputs (1, 7, 13, 31), three repetitions and
three CPUs in every variant: **108 checked executions**, including 36 restricted.
Both rewritten variants preserve all four `volatile.` prefixes. CILica expresses
the static accesses using `ldsflda` plus volatile indirect loads/stores. This
retains correctness but increases cycles and loaded size for this kernel:

| CPU | Baseline cycles at input 31 | Restricted cycles | Cycle increase | Baseline → restricted loaded bytes |
| --- | ---: | ---: | ---: | ---: |
| MC68000 | 824 | 922 | 11.9% | 3,024 → 3,164 |
| MC68020 | 175 | 201 | 14.9% | 3,072 → 3,208 |
| MC68040 | 67 | 77 | 14.9% | 2,972 → 3,112 |

Portable matches restricted for volatile. These are cold-invocation interpreter
cycles, including startup, rather than wall-clock or physical-hardware timings.
The profile preserves the requested scalar gains but does not improve every
kernel.

All **111 volatile regression tests** also passed again, covering changing bus
reads, polling, repeated/discarded accesses and acquire/release boundaries through
calls. These separately test the native backend's volatile implementation; the
benchmark executes the rewritten volatile kernel. See
[volatile memory support](../../Compiler/VolatileMemory.md) for semantics and
hardware/model limits.

Reproduce with:

```powershell
./scripts/CilicaAudit/run.ps1 -Restricted -SdkVersion 10.0.401 -Repetitions 3 `
  -ResultsDirectory TestResults/CilicaRestrictedVolatileRepeat
```

Raw evidence is retained in `TestResults/CilicaRestrictedVolatileVerified`, with
the run log at `TestResults/CilicaRestrictedVolatileVerified-run.log` and the
historical coverage/performance cross-check in `kernels/followup-verification.json`.
The separate regression log is
`TestResults/VolatileSupport/volatile-benchmark-followup-tests.log`.

## Removing the volatile preprocessing overhead — 2026-11-07

The native output isolated two avoidable costs in CILica's volatile kernel:
four null checks on `ldsflda` results, and repeated materialization of the same
linked address into an unchanged address register. The compiler now recognizes
static storage addresses as non-null after type initialization and remembers
their exact labels within a basic block. Full-width byref copies propagate the
fact; register definitions, call clobbers and block boundaries invalidate it.
The address-register restriction avoids changing implicit data-register
condition-code writes. Static field contents keep their existing nullability.

No volatile load, store, width, or synchronization instruction was removed.
The kernel still has two volatile reads, two volatile writes and eight `NOP`
synchronization instructions. The original implementation's four redundant
null checks disappear; two repeated address reloads also disappear. A staged
probe with only the null-check fix measured 826/177/69 cycles, identifying the
last two-cycle overhead before address reuse.

| CPU | Baseline cycles | Restricted before | Restricted after | Restricted loaded bytes, before → after |
| --- | ---: | ---: | ---: | ---: |
| MC68000 | 824 | 922 | 802 | 3,164 → 2,988 |
| MC68020 | 175 | 201 | 171 | 3,208 → 3,032 |
| MC68040 | 67 | 77 | 67 | 3,112 → 2,936 |

These are input-31 cold-invocation interpreter cycles including startup. The
12–15% slowdown is eliminated on all three CPUs; portable matches restricted.
Each rewritten volatile image is 176 loaded bytes smaller than before.

A fresh three-sample baseline/portable/restricted run again passed **all 15
baseline-supported kernels** on all three CPUs: **2,196 correct executions**,
including **756 restricted** and **108 volatile** executions across variants.
All 129 successful groups have stable images, results and cycles. Restricted
introduced no compatibility regressions. All 42 nonvolatile baseline/restricted
kernel/CPU comparisons have unchanged cycles and loaded sizes, including all
six scalar-replacement/devirtualization gains. The existing unsupported cases
remain compilation failures.
Both applications still compile in every variant/CPU combination. Polymorphism
keeps its earlier loaded sizes; StopwatchBenchmark is 76 loaded bytes smaller in
every combination. These remain compilation/size checks, without native OS
execution.

The [optimized summary](restricted-volatile-optimized-results-summary.json)
records the compiler binary hash, base commit `561bc80c2`, and SHA-256 of the
uncommitted source patch used for this build. Raw evidence and that patch are
under `TestResults/CilicaVolatileOptimizedVerified`; the run log is
`TestResults/CilicaVolatileOptimizedVerified-run.log`. The historical cross-check
is in `kernels/optimization-verification.json`. Staged assembly and cycle probes
are under `TestResults/VolatileOptimization`.

All **123 volatile tests** pass, including static-address access traces through
clobbers, different fields, managed calls and both branch paths, with assembly
checks for synchronization and address reuse. Another **232 related tests** and
**12 type-initialization tests** pass. Logs are in
`TestResults/VolatileSupport/static-address-reuse-final-tests.log`,
`static-address-related-tests.log` and `static-address-type-init-tests.log`.

## Signed checked Int32 support — 2026-11-08

Signed Int32 `mul.ovf` now lowers to the registered managed numeric helper
`Integer32Arithmetic.MultiplyCheckedSigned`. It widens the operands before the
multiply, checks both Int32 bounds and raises the canonical OverflowException.
The replacement call retains the original IL offset and EH boundaries, so
discarded checked results, catches and finally blocks remain observable. It uses
the existing integer backend with floating point disabled; checked Int64
multiplication remains explicitly unsupported.

The benchmark oracle is now shared directly with the test project in
[`ReferenceResults.cs`](ReferenceResults.cs). Its checked case uses host C#
`checked` multiplication and catches OverflowException, fixing the previous
one-sided threshold. The benchmark now executes **12 checked inputs**:
0, 1, -1, 7, 13, 21, 22, -21, -22, 31, Int32.MinValue and Int32.MaxValue.
Thus both successful ±21 products and overflowing ±22 products are checked.

The new narrowed-result test also exposed a separate frame-store error: storing
a byte-converted result into a declared Int32 local emitted a byte write, then
reloaded the whole slot. Big-endian output returned `0xFF000000` for 255. Scalar
local/argument stores now use the declared home width and normalize narrower
sources before wider stores, including when producer normalization was deferred.
Aggregate lane stores retain their original widths.
Independent plain-narrow-local and address-taken-argument cases cover this fix.

All **37 new tests** pass. They include 28 operand pairs over four output targets
and all three peephole modes, covering signs, zero, Int32 limits, MinValue times
-1, 46340/46341 square boundaries, narrowed operands/results, constant overflow,
discarded results, calls and catch/finally unwinding. Their target tests perform
**2,700 executions**. Host helper checks also cover the boundaries and 10,000
deterministic random pairs; nine test rows exercise the actual benchmark oracle.
Another **598 related tests** pass, including arithmetic selection, Int64,
floating conversions, narrow ABI/frame storage, struct-return and frame-transfer
coverage, volatile, byref and optimizers. MC68060 output uses the emulator's
MC68040 core for execution.

A fresh three-sample restricted run passes **all 16 baseline-supported kernels
on MC68000/020/040**. It records **2,520 correct executions** across baseline,
portable and restricted, including **864 restricted** and **324 checked-overflow**
executions. All 138 successful kernel/CPU/variant groups have stable images,
per-input results and cycles. There are no restricted compatibility regressions
or incorrect results. The original Range.Where.Select.Sum case is now the only
baseline/restricted failure. Portable retains its separate ArrayLoop and
Range.Select.Sum failures.

| CheckedOverflow at input 31 | Baseline cycles | Restricted cycles | Baseline → restricted loaded bytes |
| --- | ---: | ---: | ---: |
| MC68000 | 7,380 | 7,222 | 3,376 → 3,332 |
| MC68020 | 1,905 | 1,868 | 3,416 → 3,372 |
| MC68040 | 734 | 718 | 3,320 → 3,276 |

All **45 earlier baseline/restricted kernel/CPU comparisons** retain their
cycles and loaded sizes, including scalar replacement, devirtualization and the
optimized volatile path. Both application compilation/size probes also pass.
These remain cold-invocation interpreter cycles including startup, rather than
physical hardware timings or host compilation-time claims.

The [checked-Int32 summary](checked-int32-results-summary.json) records compiler,
managed-runtime and input hashes. This run uses base commit `c156ad864` plus the
recorded worktree snapshot, which includes other in-flight backend edits. Raw
evidence is under `TestResults/CilicaCheckedInt32Final`, with its run log at
`TestResults/CilicaCheckedInt32Final-run.log`. The historical/matrix cross-check
is `kernels/checked-support-verification.json`. New source files and source hashes
are retained in `source-inputs/` and `source-inputs.json`; test logs are
`TestResults/CheckedInt32Support/checked-final-tests.log` (the expanded final set),
`checked-tests-4.log` and `related-tests.log` (the earlier focused runs).

Reproduce with:

```powershell
./scripts/CilicaAudit/run.ps1 -Restricted -SdkVersion 10.0.401 -Repetitions 3 `
  -ResultsDirectory TestResults/CilicaCheckedInt32Repeat
```

## Remaining unsupported cases: feasibility investigation — 2026-11-07

This retained investigation predates the signed checked Int32 support described
above; that implementation supersedes the multiplication findings below.

Both remaining failures also occur in baseline IL. They are CopperSharp support
gaps, independent of the restricted CILica pass selection. This investigation
leaves the production compiler unchanged. The retained
[probe summary](unsupported-cases-investigation.json) records the clean base
commit `c156ad864`, compiler/input hashes and execution coverage.

### Range.Where.Select.Sum

The exact missing stage is `Select<int,int>` after `Range.Where`.
[`CompilationModule`](../../Compiler/Metadata/CompilationModule.cs) deliberately
requires exact `Range` provenance at the Select call. This matches the runtime:
[`ShadowEnumerable.SelectInt32`](../../Runtime.Managed/FrameworkShadows.cs)
casts its source to `ShadowRangeIterator`. Removing the provenance guard would
therefore select the wrong runtime representation.

The existing compiler already supports the benchmark-equivalent expression:

```csharp
Enumerable.Range(0, n).Where(x => (x & 1) == 0).Sum(x => x * 3)
```

It resolves to `RangeWhereInt32SumSelector`, whose existing filtered loop invokes
the selector only on accepted elements and uses checked accumulation. This proves
the required predicate/selector delegates, checked Sum and exception machinery
already work. It is an application-level alternative for this direct, pure
benchmark expression, rather than a general justification for rewriting deferred
Select queries into Sum calls.

A focused implementation of the original syntax would:

1. Add a distinct `RangeWhereSelect` provenance state and its transfer through
   stack/local/control-flow joins in `EnumerableSourceProvenance.cs`. Unknown or
   mixed iterator families must continue to fail closed.
2. Select a dedicated factory for `Select<int,int>` on an exact `RangeWhere`
   source. Add immutable deferred state holding that source and selector, with
   immediate argument validation and no callback evaluation at construction.
3. Bind Sum on the new family to its exact terminal, reusing the existing
   filtered selector-sum loop. Keep callback order, checked accumulation and
   repeatability. Keep the factory's source/selector rooted across GC.
4. Add host and native tests for empty/all/none matches, null selector timing,
   captured delegates, callback exceptions, repeated terminals, explicit
   collection, and provenance merges. Unknown sources and indexed/generic
   overloads remain outside this narrow slice.

`Range.Select.Where` is an existing, different family; it cannot stand in for
filter-then-project. Extending `Take`, `Any`, `ToArray` and public enumeration to
the new family is additional work and should be explicitly admitted/tested.
For a first Sum-only Int32 slice, estimate **1–2 engineering days**, including
tests and audit rerun. A matching set of additional terminals is a larger follow-up.

### Signed checked Int32 multiplication

The benchmark fails at `mul.ovf`. Typed stack analysis accepts its Int32 form,
but `CilMachineIrBuilder` maps only `mul` and `mul.ovf.un` to Multiply and only
constrains those forms to the arithmetic registers. Signed `mul.ovf` becomes
`Other` and later produces the internal "allocated emission is incomplete"
failure. Unsigned checked multiplication and canonical OverflowException
raising already exist; signed checked Int64 arithmetic is a separate rejected
scope.

The smallest correctness-first route is to extend the existing numeric-helper
lowering in `CompilationModule.Numeric.cs`: replace signed Int32 `mul.ovf` with
a registered runtime helper call at the same IL offset. The helper computes:

```csharp
long product = (long)left * right;
if (product < int.MinValue || product > int.MaxValue)
    M68kRuntime.ThrowOverflowException();
return (int)product;
```

Every Int32-by-Int32 product fits in Int64, and the compiler already emits this
unchecked Int64 multiply and comparison sequence on all output targets. The
registered call path already carries throwing behavior, call clobbers and EH
edges, so it avoids prematurely routing the opcode into unchecked native
multiply or constant-strength-reduction paths. It requires the managed numeric
runtime to be resolvable, as existing numeric-helper operations do. This is a
portable starting point; CPU-specific faster lowering can follow separately.

A direct native implementation would additionally need signed opcode mapping,
fixed-register constraints, scratch clobbers, `MayThrow`, correct overflow
detection, and exclusion from unchecked constant-multiply transforms. Changing
the opcode classifier alone would not provide checked semantics.

Tests should cover zero, all sign combinations, Int32 bounds, MinValue times
-1, 46340/46341 square boundaries, discarded results, catches/finally, and all
peephole modes. The audit's current checked-overflow oracle only handles its
positive inputs: before adding negative edge inputs, replace its one-sided
threshold with a host `checked` multiply/catch oracle. The benchmark's
100,000,000 factor needs both +21/+22 and -21/-22 cases.

Estimate **half a day to one engineering day** for the helper-based Int32 slice,
including integration, regression tests, stable diagnostics for remaining
unsupported forms and the benchmark rerun. Full checked arithmetic, especially
64-bit multiplication, is outside that estimate.

### Probe evidence and recommended order

On the unchanged compiler, **1,704 alternative executions passed** across
baseline/restricted IL, all four output targets and disabled/fixed-point/bounded
peepholes. The original two forms still fail in all **48 expected unsupported
groups**; the alternatives pass in 120 groups:

| Alternative | Checked executions |
| --- | ---: |
| Where.Sum(selector), including empty input | 168 |
| Where.Sum(selector), catching accumulation overflow | 168 |
| Explicit widened checked product | 456 |
| Widened checked product with discarded result | 456 |
| Widened checked product through catch/finally | 456 |

These probes validate the reusable building blocks, not an implementation of
the original unsupported opcodes/query shape. MC68060 output executes on the
emulator's MC68040 model. The full logs, fixture/runner sources, rewritten DLLs
and maps are under `TestResults/UnsupportedPipelineInvestigation`.

Implement the signed Int32 helper route first, then the deferred Range.Where.Select
family with an exact Sum terminal. Keep the restricted CILica pass allowlist
unchanged. The effort ranges above are engineering estimates for these focused
slices, not for general LINQ or every checked CIL operation.

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
