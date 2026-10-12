# Compatibility and performance testing

## Native StringBuilder growth, formatting and GC

The native runner tests use the verified Microsoft.NETCore.App.Runtime.win-x64 10.0.9 CoreLib with normal public admission. Six scenarios check presized append, chunk growth, invariant decimal/hex formatting, interpolated integer formatting, and explicit or allocation-triggered collection while a builder and its string snapshot remain live. Each scenario runs with Disabled and FixedPoint optimization on the 68000 interpreter, 68040 interpreter, and 68040 JIT, checking exact stdout, return value 42, and fresh native completion identifiers. The managed heap is 8 KiB, allocated through Exec. This is semantic and GC coverage; boot-inclusive cycles do not measure StringBuilder performance.

The compiler/runtime prerequisites include the pinned StringBuilder surface and dependency rules, formatting helpers, aggregate/byref lowering and GC root handling. Built-in runtime calls use relocatable absolute JSR until final layout proves that a short call is in range, allowing large formatting bodies to reach GC shutdown. The native test script retains Hunk images, maps, backend settings and input hashes under the StringBuilder results directory. The broader BCL corpus contains additional gates; these native scenarios do not establish general CoreLib support.

The allocation-failure scenario makes no explicit collection or disposal calls and uses `OnAllocationFailure`. It allocates at least 64 KiB of discarded character-array data, plus object headers and temporary strings, on the 8 KiB heap and requires at least two decreases in `ManagedPool.GetStaleBytes()`: ordinary allocation increases this counter and a sweep resets it. Each pressure round checks the retained snapshot and growing builder length, then the final check verifies all 64 characters before clearing and reusing the builder. All six backend/optimizer combinations returned 42 with exact output. This verifies repeated automatic reclamation and successful allocation retry with live caller-frame roots using the existing telemetry; no compiler or runtime changes were needed.

The isolated branch was verified with SDK 10.0.301: 775 selected admission, pinned-input, surface/ABI, register/dataflow, branch-relaxation and managed-runtime/GC host tests passed without skips; all 37 native runner tests passed, including 30 StringBuilder executions; and 43 native compiler integration tests passed. The one existing payload-only cycle benchmark remains explicitly deferred. The retained StringBuilder records were audited against executable/map hashes, exact output and return values, actual backends, optimizer pairs and 30 distinct run identifiers. The 68000 and 68040 interpreters executed identical images for each scenario/mode, and all 726 source/build inputs remained unchanged during verification. These checks do not claim a complete run of the broader BCL corpus.

The automatic-GC extension was verified with all 40 native runner tests passing without skips, including 36 StringBuilder executions. The retained maps and executable hashes, all 36 distinct run identifiers, exact stdout/returns and backend/optimizer pairs were audited. Each interpreter pair executed the same 68000 image. The recorded compiler, fixture, runtime, runner and engine hashes matched the build, and the 726 source/build inputs remained unchanged during execution.

The broader host check completed with 4,473 passing cases and one baseline failure in `Net10ProfileBaselineTests.CurrentBaselineCanBeReproduced`, excluding the large `CompilerExecutionTests` CPU matrix and native integration. Its sixteen profile samples reproduced the same measurements in two independent runs. The subsequent baseline review reproduced the checked-in snapshot at revision `ac67839` under the same SDK and compared generated assembly with the current compiler before updating the snapshot. These differences predate the automatic-GC fixture; no compiler or runtime implementation changes were needed for that fixture or the snapshot update.

The reviewed changes are:

| Workload | Code bytes | Reason established by source and generated assembly review |
| --- | --- | --- |
| FileStats | 632 → 932 | Native DOS argument handling adds `ReportCommandLine`, its allocation, parsing and cleanup calls. |
| IFFInspect | 5,244 → 5,624 | Collector support adds the root-site binary search and aggregate-field tracing helpers, increasing the compiled method count from 24 to 26. |
| ConsoleIO | 6,084 → 6,108 | Four exception-slot clears add 8 bytes; four retained address null comparisons add 16 bytes. The clears prevent stale frame data, and the comparisons replace unsafe condition reuse across calls. |
| StopwatchBenchmark | 11,140 → 11,168 | Five exception-slot clears add 10 bytes, the same four null comparisons add 16 bytes, and alignment adds 2 bytes. |
| MUITaskList | 2,128 → 2,036 | Cleanup no longer propagates unused zero values through repeated register copies and branch paths; pointer register assignments also change. |
| CopperBars | 1,216 → 1,216 | Pointer register assignments and address formation change. Instruction count rises by one and explicit stack operations by two, with unchanged code and image sizes. |

All seven fixture samples, including their recorded MC68000 cycle counts, remain unchanged. DOS, MUISunflower and Polymorphism samples also remain unchanged. The snapshot continues to compare exact measurements; no assertion or size budget was relaxed. The isolated baseline test passes after the update, reproducing all sixteen samples under SDK 10.0.301. The earlier 4,473-case focused host result and the separate passing baseline rerun provide the verification scope; the full focused suite was not repeated for this JSON and documentation change.

An initial full host run was intentionally stopped after 99 minutes, with 4,609 passes (including 394 StringBuilder cases), one identical baseline failure, and no skips. Its TRX records an aborted run because the owned test host was terminated. That run does not establish a passing or complete full CPU matrix. The subsequent focused check and the complete native runner check provide the stated verification scope; the remaining full execution matrix is outstanding.

## Generic metadata lookup cost

Profiling the slowest retained host cases identified repeated generic definition lookup during interface reachability. In an isolated `StringBuilderInheritedGenericListDispatchEntry` analysis, `TryFindConstructedGenericDefinition` occupied 94.22% of inclusive sampled time. Each lookup scanned the module's complete type table and decoded signature names, including enum metadata. Repeated CoreLib searches produced 68.16 GB of cumulative temporary managed allocation; this is allocation traffic, not peak memory usage.

The lookup now builds one ordinal signature-name index per `CompilationModule`, retaining the first matching definition and its interface flag. Each result still carries the caller's constructed type and arguments. Missing names use the same fallback as before. The index is separate from runtime primitive aliases and is neither global nor shared across compilations.

Single-process analysis measurements under Windows, SDK 10.0.301 and the pinned CoreLib 10.0.9 were:

| Entry | Before / after seconds | Before / after allocated GB |
| --- | --- | --- |
| `StringBuilderInheritedGenericListDispatchEntry` | 63.57 / 2.69 | 68.16 / 1.65 |
| `CoreLibFloatingHandlerFormatProbeEntry` | 29.39 / 2.06 | 29.90 / 1.28 |
| `CoreLibGenericDecimalNullableJoinsEntry` | 5.52 / 1.16 | 6.04 / 0.66 |

All three complete analysis JSON outputs were byte-identical before and after the change. The first pair used sampled-thread tracing; the other pairs ran without tracing. These are individual local measurements, not statistical performance thresholds or estimates for the complete CPU suite. Twenty targeted type identity, interface dispatch and profile-baseline checks passed, followed by all ninety selected allocation, floating, nullable and integral/enum graph checks, without failures or skips.

Paired full compilation of the inherited generic-list entry with optimization disabled produced byte-identical Hunk images and analysis JSON. Elapsed time decreased from 161.69 to 49.63 seconds, with cumulative allocation decreasing from 175.71 to 22.21 GB. The before measurement partially overlapped other profiling/check processes, so this timing ratio is indicative rather than a controlled benchmark; the image equality is independent of that scheduling difference. No native OS suite or complete CPU matrix was rerun for this change.

Full fixed-point compilation of the inherited generic-list entry with the index still took 107.48 seconds and allocated 60.68 GB cumulatively. Its separate sampled profile attributed 53.47% of inclusive time to optimization and 17.79% to graph-coloring allocation. Hot paths include repeated instruction decoding/rendering and `CanFoldAcrossUnreferencedIlLabels`, which scans labels for each replacement candidate. This establishes remaining backend costs; the metadata speedups above must not be reported as full-compilation or full-suite speedups. The optimizer and allocator were not changed in this investigation.

## Backend cost and resumable CPU verification

The label guard indexes hard boundary offsets once per optimizer round and uses a binary search for the strict interior of each replacement. Hard aliases at the same offset are retained, while only valid, unreferenced source markers can move. The snapshot is cleared at round start and end; queries outside a round observe current labels and scope. Register simplification maintains effective degrees as neighbors are removed, with ordered candidate sets preserving the original spill-temporary, spill-priority and group-ID decisions. Fixed-register neighbors remain counted.

The inherited generic-list example produced byte-identical Hunk images in FixedPoint and Disabled modes compared with the compiler at `b3552e4`. Individual local FixedPoint time changed from 107.48 to 59.56 seconds, and Disabled time from 49.63 to 20.80 seconds. Cumulative allocation changed from 60.68 to 57.28 GB and 22.21 to 18.50 GB respectively. The earlier FixedPoint measurement used sampling and the current measurement did not; these are indicative observations rather than controlled statistical ratios. All 154 selected allocator, boundary, control-flow and unchanged profile-baseline cases passed without skips.

`scripts/run-cpu-matrix.ps1` freezes the discovered `CompilerExecutionTests` method assignments, SDK and all test-directory DLL hashes in a manifest. It runs all theory rows for each assigned method, retains separate attempts and TRX files, and checks method coverage, failures, skips and aborts before accepting a batch. Coverage comes from actual execution results rather than test definitions alone. Each result must pass, the result count must agree with the summary, and every explicitly discovered theory row must appear. Retained discovery must agree with all manifest assignments and counts; new manifests also hash the discovery file. `-Resume` re-verifies existing successful TRX results rather than trusting a saved completion flag. `-ShardNumbers` allows disjoint workers to use the same manifest. `-AuditOnly` checks every batch against the manifest and writes `audit.json`; an incomplete audit exits with code 2. The observed case count must also meet the discovery count, while deferred theories may expand to additional cases. A changed compiler build requires a new results directory.

Initialize after building with the pinned SDK, then run or resume selected batches:

```powershell
./scripts/run-cpu-matrix.ps1 -ResultsDirectory TestResults/CpuMatrix -InitializeOnly
./scripts/run-cpu-matrix.ps1 -ResultsDirectory TestResults/CpuMatrix -ShardNumbers @(1, 2, 3) -Resume
./scripts/run-cpu-matrix.ps1 -ResultsDirectory TestResults/CpuMatrix -AuditOnly
```

`scripts/test-cpu-matrix-audit.ps1 -TrxPath <passing-trx>` checks the verifier against real TRX structure and deliberately corrupted copies in a separate results directory. Its input must contain a fact and matching 68040/68060 theory rows. Six checks cover a passing result, a missing CPU row hidden by unchanged totals, an unexecuted definition, a result/count disagreement, a failed row with a passing summary, and deferred theory expansion. These verifier checks do not count toward CPU matrix execution coverage.

The current matrix manifest assigns all 1,100 discovered execution-test methods to 32 batches. The complete matrix remains unverified until its final audit succeeds; the targeted checks and identical sample images above do not substitute for that audit.

The frozen matrix exposed two pre-existing failures, also reproduced with the compiler at `b3552e4`: invariant culture operations reached host locale storage, and a boxed enum failed `is System.Enum`. Experimental culture identity adapters now read target-owned invariant storage while preserving the official virtual slots. Exact `CultureInfo.ReadOnly` and its nested `TextInfo.ReadOnly` call retain their CoreLib bodies with target-owned initialization. These bindings require the existing experimental or pinned-input admission rules. At revision `3f14873`, `CultureInfo.ReadOnly` execution still failed because `DateTimeFormatInfo` reference fields exceeded the 32-word descriptor bitmap; compatible reachability alone did not establish execution support.

The enum correction treats CoreLib `System.Enum` as a reference base class and makes `System.Enum`/`System.ValueType` type tests use the same canonical identities as boxed descriptors. This lookup requires the CoreLib module identity. The running matrix retains its original frozen binaries, so its results do not validate these later corrections; a corrected build requires a separate matrix manifest and results directory.

With SDK 10.0.301, all four enum-contract CPU rows passed, each exercising FixedPoint and Disabled modes. A separate 26-case check passed the culture binding/initializer regressions, invariant culture execution on all four CPU targets in both modes, value-type predicate specialization, and the unchanged sixteen-sample profile baseline. Neither run skipped cases. The six audit-verifier checks also passed, and manifest initialization revalidated the existing frozen binary inventory and all 1,100 method assignments. These targeted results do not cover the remaining ReadOnly execution failure or establish a complete CPU matrix.

Class descriptors now retain the original bitmap at offset 4 and append additional bitmap words at offset 24 when the fixed class payload exceeds 128 bytes. Offset 20 remains the finalizer slot, emitted as zero when finalization is unused. Fixed size determines the number of appended words; even large reference-free boxed payloads include zero padding. The collector scans each word at the matching 128-byte payload offset. Inherited and constructed class layouts preserve these additional words, while reference-bearing value-type transport and aggregate-array elements still reject references beyond their existing bitmap limit.

The large-class regression keeps its only child references in payload words 32, 63, 64 and 65, creates discarded arrays beyond its heap capacity, and checks the retained values after collection on all four CPU targets in both optimizer modes. Its low bitmap is zero. The metadata and execution checks, culture binding tests and frame-root checks passed in a 27-case run; a separate 24-case managed-pool regression run passed. The broader culture/enum recheck passed all 32 rows, eight on each CPU target, with each row executing both optimizer modes. Matching exact TRX names confirms that this run covers all sixteen failures in the original first shard. That recheck used the GC-extension build before the condition-state allocation change; the corrected full matrix freezes the later compiler and remains a separate completion gate.

The GC extension intentionally changes only IFFInspect in the sixteen-sample baseline: code bytes increase from 5,624 to 5,748 and image bytes from 6,692 to 6,832. Two captures produced identical measurements. Comparing emitted assembly attributes the code growth to `ManagedPool.TraceObject` retaining the descriptor and iterating its additional bitmaps; two added call sites also expand unwind metadata. The other fifteen samples are unchanged. The refreshed exact baseline and selected branch/label/large-class checks passed together in 102 cases without skips.

Condition provenance now clones an edge state only when branch refinement changes its zero/nonzero facts. Initial stored states remain copied and joins only read their incoming states, preserving successor isolation. A sequential inherited generic-list FixedPoint comparison produced identical Hunk images while reducing cumulative allocations from 57.71 to 54.78 GB (2.94 GB). Times were 59.74 and 60.76 seconds under concurrent matrix load; this observation establishes an allocation reduction, not a timing speedup.

Completed diagnostic shard timings identify the floating left-alignment allocation corpus among the longest execution rows (up to 307 seconds on 68020). A compiler-only probe using the stable helper's 68020/ExternalAllocator request took 64.20 seconds and 53.70 GB cumulative allocations in FixedPoint mode, then 25.15 seconds and 19.48 GB in Disabled mode. These are single measurements under concurrent matrix load. The full row also executes 64 independent fault-injection cases per mode, so its duration cannot be treated as compilation time. The measurements and images are retained separately from matrix execution results; the probe does not count toward CPU coverage.

The matrix runner accepts `-ArtifactsPath` for builds created with `dotnet build --artifacts-path`, freezes that path in the manifest, and uses it for discovery and execution. A separate one-method/four-CPU smoke matrix executed the isolated build and passed its final audit; it is not the full matrix. The corrected full manifest assigns 1,101 methods, including the new large-class test, to 32 shards. The original frozen matrix and corrected matrix must remain separate, and completion still requires the corrected matrix's full audit.

The corrected GC/optimizer build passed its first two full shards: 148 and 132 cases, with no failures or skips and no missing methods or explicit rows in the strict audit. Exact TRX names match passing results for all sixteen original first-shard failures. These 280 passing cases do not establish the remaining thirty shards.

The diagnostic fifth shard exposed eight stale fixture expectations: unsigned and 64-bit formatting helpers expected the custom format `000.0` to throw after custom-format support had already been implemented. A focused recheck reproduced the unsigned expectation failure on all four CPU targets. The fixtures now verify the supported result `042.0`, written lengths and untouched destination suffixes, resetting the buffer before their separate default-format checks. All eight revised helper rows passed on the four CPU targets with no skips; exact TRX names cover all eight diagnostic failures. The changed fixture assembly requires a separate frozen full matrix, retained in `MatrixFormatContracts`; its 1,101 methods and 32 shards remain the completion gate. Earlier matrix results and this focused check cannot substitute for that final audit.

The currency-provider diagnostic exposed an invalid test memory layout: the latest 68040 image is 203,708 bytes, so loading it at 0x10000 ends at 0x41BBC and overlaps the former 0x40000 managed heap. A probe executing the unchanged fixture with a disjoint 0x100000 heap returned 42. The currency execution row now uses that heap and checks that its image remains below the test stack. All four currency CPU rows passed with both optimizer modes and no skips. The mixed verification run also contained the separately investigated enum dependency assertion, which failed on this earlier build. This fixture-assembly change requires a fresh final 32-shard matrix; MatrixFormatContracts predates it and remains diagnostic evidence only.

The enum dependency assertion also reproduced against the corrected build. Its shortest reachability path entered System.Int32::ToString through the Object.ToString declaration emitted for a sealed StringBuilder receiver. The existing exact StringBuilder binding had been restricted to stable admission, so experimental graphs walked unrelated allocated overrides and acquired numeric culture dependencies. The same binding now applies when the pack supports the StringBuilder protocol ABI, retaining its receiver, CoreLib identity, sealed-type and signature checks. Nine focused checks passed, including the unchanged enum dependency assertion, stable admission checks and new experimental guards that reject unknown object receivers. The enum execution rows passed on all four CPU targets in both optimizer modes, with no skips. MatrixSealedDispatch freezes the build containing both this compiler correction and the currency heap correction for the required final 32-shard audit.

A controlled sealed-dispatch probe compiled the same frozen enum fixture, CPU, optimizer mode, heap and CoreLib input before and after the binding correction. Compiler allocations fell from 3,429,189,448 to 2,390,663,432 bytes (30.3%) and generated code from 66,204 to 43,584 bytes (34.2%); both images executed to the return sentinel with result 42. The maps and metrics are retained in sealed-dispatch-profile. Elapsed time was 11.85 versus 10.20 seconds under concurrent matrix load, so these single timings are indicative rather than an established speedup. The code-size change is intentional removal of unrelated virtual-dispatch dependencies; the final full CPU matrix still validates the resulting compiler.

The percent-provider row exposed the same invalid heap layout on the latest sealed-dispatch build. Its 68060 provider-contract image is 205,876 bytes and ends at 0x42434 when loaded at 0x10000, overlapping the 0x40000 heap. The exact CPU row and a retained probe reproduced an invalid read; the unchanged fixture and compiler returned 42 with a 0x100000 heap. The percent execution row now uses that disjoint heap and checks its image remains below the test stack. All four percent CPU rows passed in both optimizer modes, exactly matching the four earlier failing rows. The focused run passed all 13 checks, including sealed-receiver and bounded-admission guards, with no skips. MatrixFormatLayout freezes the build containing both formatting heap corrections and the sealed StringBuilder binding; it remains diagnostic because the later console benchmark correction is absent. MatrixSealedDispatch also predates the percent correction. Final completion still requires all 32 shards of a frozen build containing every correction, with every assigned method and explicit discovered row passing and no skips.

The console input/EOF benchmark retained costs from before two compiler correctness fixes. Compiler-only probes reproduce its original values through ecd6687. Controlled external builds removing call-barrier alias invalidation reduce input cost by 700 cycles and 70 instructions and EOF cost by 490 cycles and 49 instructions; removing active-exception slot initialization accounts for the remaining 160/8 and 60/3. Both fixes remain enabled: calls must invalidate stale register facts, and normal finally paths must not publish stale exception roots. The benchmark now pins 30,992 cycles/3,190 instructions for input and 17,156/1,849 for EOF. The candidate build passed that exact benchmark and all four normal-finally collection CPU rows with no skips. The measurements, historical source snapshots and assembly comparisons are retained in console-cost-investigation.

The constrained-ToString rejection fixture predated experimental Boolean, Char and enum support. Boolean text can compile without an allocator, while Char and enum formatting require the external allocation import. Two negative rows now assert the exact unresolved __c68k_alloc diagnostic. A host oracle and four CPU rows verify the original three text entries plus false and a non-ASCII character with a managed heap collecting on every allocation. All 12 focused checks passed, including the console benchmark and normal-finally CPU rows, with no skips; each CPU row exercises both optimizer modes. MatrixConstrainedText freezes this build with 1,103 assigned methods across 32 shards, but predates the grouped-provider layout correction and cannot establish final completion. The actual TRX rows and probe outcomes are retained in constrained-text-investigation.

The grouped-provider CPU rows exposed another code/heap overlap: the frozen 68040 image is 197,196 bytes, ending at 0x4024C when loaded at 0x10000, which occupies the first 588 bytes of the former 0x40000 heap. With the same frozen compiler and fixture it returned 6 at checkpoint 100; changing only the heap to 0x100000 returned 42 and reached all 123 checkpoints, ending at 1002. The grouped execution test now uses the disjoint heap without changing its semantic or checkpoint assertions. The shared Hunk test loader reads the emitted GC configuration and rejects static heaps overlapping the loaded image before execution; dynamically supplied OS arenas have a zero start and are excluded. A dedicated regression checks that rejection. All 17 focused checks passed with no skips, including all four grouped CPU rows in both optimizer modes and the loader regression. Probe images, state, metrics and the actual TRX rows are retained in grouped-provider-investigation.

The shared loader guard also identifies the parsed-composite provider overlap directly: its image ends at 0x407C4, occupying 1,988 bytes of the former 0x40000 heap. The candidate recheck reproduced that diagnostic before execution on 68040 and 68060. The parsed-composite validation, provider and capacity fixture family now uses a 0x100000 heap. All 12 corrected parsed-composite rows passed with no skips across four CPU targets, each exercising both optimizer modes. The overlap diagnostics and corrected-build results are retained in parsed-provider-investigation. MatrixParsedLayout freezes this corrected build with 1,104 assigned methods across 32 shards. Its full execution and strict audit remain pending; focused checks and earlier builds cannot establish final completion.

The diagnostic IFF sample check also pinned incidental register assignments and rejected live address copies inside the expanded managed GC tracer. The configuration pointer now resides in d3 while a0 still serves both adjacent configuration loads. The no-copy assertion remains enforced on the SDK methods, where incoming addresses should be used directly; runtime tracing can retain a base address across branches while advancing another cursor. Generated probe assemblies and temporary rewritten fixture copies now use a separate output root. The matrix runner retains those outputs beside each shard attempt and continues auditing every DLL recursively in the frozen input directory. Rewritten fixture requests explicitly supply the original compiler and managed runtime assembly paths instead of relying on sibling DLL resolution. All 34 focused checks passed with no skips, including IFF execution and cleanup, managed-byref diagnostics, copied-struct and box-interior execution on every CPU, and five representative generated-probe rows. The recursive frozen inventory remained unchanged after probe execution. MatrixRetainedProbes runs 1,104 methods across 32 shards, with eight workers assigned disjoint shard lists. Shard 5 exposed a stale rejection expectation: the compiler already transports the selected GC owner alongside a byref merge between two objects, so IncompatibleOwnerByrefAtSafepointHasStableDiagnostic compiles successfully. This run therefore remains diagnostic. The next correctness task is to replace that stale expectation with execution checks for both runtime choices under allocation pressure and preserve diagnostics for genuinely unsupported provenance. Full completion then requires every assigned method and explicit discovered case to pass from one corrected frozen build, no skips or aborts, and the strict final TRX and input-inventory audit. Earlier builds and focused checks cannot be combined into final coverage.

A 30-second sampled trace from the frozen MatrixRetainedProbes build identifies remaining compiler work while the full matrix runs. On the selected active test-worker thread, Compile accounts for 97.2% of sampled thread time; overlapping inclusive portions include peephole optimization at 48.9%, instruction dataflow at 28.3%, register allocation at 22.7%, and instruction-stream construction at 8.7%. The sampled row is CustomFloatingEnumerableAllocationFailuresPreservePrefixesAndDispose. The report selects that worker thread rather than aggregating waiting test-runner threads and excludes synthetic CPU_TIME and UNMANAGED_CODE_TIME markers from exclusive method attribution. These are sampled thread-time proportions for part of one row, not CPU percentages, additive phases, a controlled whole-compile benchmark, or evidence of a new speedup. The trace, frozen DLL hashes, raw stack samples, report source and findings are retained in backend-v14-live-findings.json and its referenced artifacts. The current frozen matrix build remains unchanged while its diagnostic run continues.

The stale byref rejection is replaced by runtime-selected object-owner execution on all four CPUs in both optimizer modes. After collection, 128 pairs of simultaneously live, consumed allocation-pressure objects exercise storage reuse; both owner choices retain their original values. A separate stack/object merge still produces the Unknown provenance diagnostic in both modes. Two reference-free struct dictionary fixtures also used an 0x4000..0x18000 heap overlapping their images; moving their heaps to 0x100000 preserves the original capacity and behavior assertions. The v17 focused run passes all 14 rows without skips. The diagnostic matrix also exposed an actual compiler binding issue: Object.Equals on a verified sealed StringBuilder reached unrelated CultureInfo.Equals, its culture dictionary and HashHelpers.IsPrime, ending at native Math.Sqrt. The compiler now binds the exact inherited equality operation to reference comparison after the callvirt null check. Admission requires the verified pack, exact CoreLib identity, sealed type with Object base and no Equals(object) override, exact declaration/signature, directly loaded receiver and a suffix without control-flow ingress. The v20 focused run passes 23 rows without skips: all 12 existing equality CPU rows and four null/identity CPU rows execute both optimizer modes; receiver/pack/signature/control-flow guards, four static ReferenceEquals rows and the existing sealed ToString guards also pass. Root-path and build-hash evidence is retained in stringbuilder-equals-investigation. These corrections require a new frozen full matrix; the existing diagnostic run cannot establish final completion.

The next backend candidate avoids discarded assembly text for simple one-word instruction analysis. Its opcode table is derived directly from the existing renderer rules; complex instructions retain the original renderer path. A synthetic 45,056-byte stream contains 16,384 decoded instructions. With tiered compilation disabled for measurement, each of seven runs scanning it 20 times allocates 68,168,200 bytes before and 59,648,520 bytes after, a 12.498% reduction. This includes returned instruction-list allocations and does not prove a whole-compilation speedup. Exhaustive before/after comparisons retain identical first-instruction records for all 65,536 opcodes with five extension patterns and six bounded buffer lengths: 720,896 records with digest 9311DA9FCB93EABD382C0A79064BF7F910AE2666E9F0DE6B51CF3AA4BFF798E4. The candidate passes 487 instruction-dataflow/register-allocation regressions and 142 peephole, instruction-boundary, branch/fixup and MOVEC cases with zero failures or skips. Benchmark source, raw runs, compiler hashes and record audits are retained in instruction-stream-probe. A paired full compilation of StringBuilderInheritedGenericListDispatchEntry uses 107 matching input DLLs with only the compiler changed. Allocation falls from 54,529,875,320 to 54,474,025,984 bytes, saving 55,849,336 bytes (0.1024%). Hunk bytes, map and framework analysis remain identical, including image digest ECD372186645B4B2063BE75EBA82A7086D1BAA4475C97EE6B1DB4985997A4067. Elapsed times of 93.52 and 98.60 seconds under concurrent matrix load do not establish a speedup. The paired records and full input hashes are retained in stream-full-compile/comparison.json. Three added repository regression rows also pass without skips, comparing every opcode against the text renderer at zero, one and five extension-word boundaries. The mandatory final corrected-build CPU matrix remains pending.

Dominator analysis now intersects predecessor sets in one reusable scratch set and exchanges it with a block's previous result only when that result changes. Stored sets remain distinct and are not mutated as scratch. The v23 focused run passes 130 register-allocation, machine-optimizer and dominator cases without skips. A separate removal-and-reachability oracle verifies 192 generated cyclic graphs with sparse block IDs, forward/reversed block order, normal and exceptional edges, and one to three entry roots; unreachable-block diagnostics remain checked. On StringBuilderInheritedGenericListDispatchEntry, allocation falls from 54,474,025,984 bytes in the preceding v21 run to 49,953,670,504 bytes, saving 4,520,355,480 bytes (8.298%). The 107 paired DLLs differ only in the compiler, and Hunk bytes, map and framework analysis remain identical. The existing v21 baseline is reused explicitly; the single 98.60/95.91-second pair under concurrent matrix load does not establish an elapsed speedup. Full inputs and comparison are retained in dominator-full-compile. The corrected-build CPU matrix remains mandatory.

The diagnostic Console.ReadLine row retained a stale count of 27 compiled-method allocation-statistics entries. Both the original profiling compiler and the frozen diagnostic build report the same 29 methods, including the separately compiled FindRootSite and TraceFields GC helpers introduced in 111a064. The assertion now pins 29 and explicitly requires both helpers. Image size, code size, MC68000 cycle, zero-spill, native-buffer lifetime and managed-arena lifetime budgets remain enforced. The v24 focused run passes all eight Console.ReadLine CPU/GC-strategy rows and seven dominator rows, with no failures or skips; its TRX and verification record are retained in console-method-count-investigation. This focused verification does not replace the pending corrected-build full CPU matrix.

## Testing model

Compatibility is executable. The prose specifications explain intent, while manifests, ledgers, compiler diagnostics, semantic tests, target execution, map assertions, and package integration tests enforce it.

The suite is divided by responsibility so a profile change can identify which contract moved:

- contract-manifest and executable-registry agreement;
- implementation-pack identity and hash validation;
- CIL analysis, lowering, register allocation, linking, and target execution;
- object layout, descriptor identity, boxing, delegates, generics, byrefs, GC maps, and exceptions;
- managed shadow semantic tests;
- PAL behavior and native-resource cleanup;
- headless MC68000/020/040/060 execution;
- compatibility corpus and shortest-path diagnostics;
- SDK restore/build/publish, incrementality, and provenance;
- image size, code size, allocations, spills, frame size, stack-memory traffic, feature reachability, and cycle estimates.

## Machine-readable artifacts

The exact current contract is [`net10.0-10.0.9.json`](net10.0-10.0.9.json).

The profile baseline and metric format are retained in [`net10.0-profile-baseline.json`](../../Compiler.Tests/Baselines/net10.0-profile-baseline.json).

The compatibility corpus is recorded in [`net10.0-compatibility-ledger.json`](../../Compiler.Tests/Baselines/net10.0-compatibility-ledger.json). It contains application roots, reachable framework occurrences, expected acceptance, and diagnostic evidence.

## Baseline metrics

Each representative fixture records, where applicable:

- final image bytes;
- emitted code bytes and reachable method count;
- allocation-site count;
- spill count and maximum frame size;
- stack-memory operations;
- selected framework, runtime, and PAL feature groups;
- estimated target cycles for a defined execution path.

Metrics are compared per CPU because instruction selection and timing differ. A baseline update requires a reason; a successful functional test does not automatically authorize a size or cycle regression.

Cycle estimates are deterministic compiler-side models used for regression detection, not claims about every Amiga configuration. Hardware or emulator measurements may supplement them but must identify CPU, memory, caches, and system setup.

## Corpus policy

Corpus tests compile realistic roots rather than isolated member calls. They verify that closed-world analysis finds transitive framework dependencies, preserves provenance through admitted generic and LINQ flows, and rejects the first unsupported boundary with an actionable shortest root path.

The current ledger includes both compatible and intentionally incompatible roots. Representative negative boundaries include object-based string concatenation, unsupported `Memory<T>` materialization, mixed-provenance LINQ materialization, directory creation, and a dependency that reaches an unsupported four-string concatenation overload.

Adding a member may turn an expected negative root positive. That change is reviewed deliberately and updates the ledger together with the binding and semantic tests.

## Semantic comparison

Managed bodies and shadows are tested against ordinary host .NET where the platform-independent contract permits it. Target-side execution then verifies the compiler/runtime ABI and big-endian 68k behavior. Platform members use PAL-specific expected behavior rather than pretending the host filesystem or console is AmigaOS.

Important edge classes include:

- null, empty, minimum, maximum, overflow, and exception cases;
- UTF-16 surrogate code units and embedded zero characters;
- Latin-1 native conversion failures;
- type and interface identity across boxes and generics;
- delegate equality, multicast ordering, and exception interruption;
- byref lifetime across safepoints and tail calls;
- collection mutation during enumeration;
- deferred LINQ callback order and unknown provenance;
- partial PAL initialization and cleanup-before-throw;
- big-endian array, value-layout, and bit-conversion behavior.

## Pay-for-play assertions

Map tests compile nearby programs with and without a feature and compare their reachable graph. They assert that unused implementation bodies, descriptors, helpers, static data, PAL groups, library handles, and startup/shutdown hooks are absent.

This is especially important for collection families, console input versus output, filesystem versus clock support, managed shadows, generic instantiations, and exception helpers.

## Performance policy

The compiler may choose a verified official body, private shadow, or intrinsic based on semantic correctness and measured target cost. Selection is not based on implementation ideology.

Hot operations should have explicit budgets for allocations and code/cycle growth. Fused target implementations are preferred when they remove observable intermediate allocations without changing semantics. Optimizations must be validated on every supported CPU and must not weaken GC, exception, or byref correctness.

### Pinned `Stopwatch` implementation comparison

The following retained measurement snapshot compares the private shadow fallback with the verified pinned CoreLib implementation. It is dated 2026-08-10 and should be regenerated when the relevant compiler, runtime body, or timing model changes.

| CPU | Shadow image/code/cycles | Pinned image/code/cycles |
|---|---:|---:|
| MC68000 | 10,216 / 7,448 / 19,398 | 9,028 / 6,678 / 15,822 |
| MC68020 | 10,196 / 7,428 / 1,664 | 9,012 / 6,662 / 1,328 |
| MC68040 | 10,200 / 7,430 / 1,663 | 9,012 / 6,664 / 1,327 |
| MC68060 | 10,200 / 7,430 / 1,663 | 9,012 / 6,664 / 1,327 |

The pinned body reduced image size by about 11.6%, code by about 10.3%, and estimated cycles by about 18–20%, with unchanged allocation and spill counts. The profile therefore prefers that verified body and retains the shadow as a controlled fallback.

## Validation commands

Use the solution and focused test projects as defined by the repository. For noisy output, repository guidance prefers `repowise distill` around the normal command. Typical release checks include:

```powershell
dotnet build CopperSharp68k.slnx
dotnet test Compiler.Tests/CopperSharp.Compiler.Tests.csproj
```

Package integration tests should restore into isolated temporary outputs and verify both a successful publish and representative failures. Tests must not depend on an installed unpinned runtime or a previously populated global package cache.

## Evidence retention

Keep durable baselines, manifests, ledgers, and regression tests in the repository. Keep dated measurements only when they explain a current design choice. Remove transient checkpoint status, local lock failures, task-resumption instructions, and copied console transcripts once the corresponding test or decision is permanent.

## FileStats rotate-fold investigation

The subsequent investigation found an instruction-effects defect in `M68kInstructionDataflow.ClassifyArithmetic`: EOR decoded its destination from the source-register and operation-mode fields instead of the opcode's low six effective-address bits. In particular, `EOR.L D4,D2` incorrectly appeared to read D0 and indexed memory, and did not define D2. This falsely kept the rotate sequence's D0 scratch live. The destination decoding is corrected, together with the B-family condition effects: supported CMP/CMPA/EOR forms preserve X. Unsupported memory EOR and CMPM forms still use the existing conservative unknown-instruction fallback; this change does not add those forms to the decoder.

SDK 10.0.301 verification passes all 476 selected instruction-dataflow, machine-optimizer and FileStats report-execution cases, with zero skips (`TestResults/FileStatsShapeInvestigation/compiler-regressions.trx`). Seven new cases cover byte/word/long EOR effects, comparison X preservation, a rotate fold followed by EOR and full scratch overwrites, and the retained conservative behavior for a partial scratch overwrite. The four FileStats CPU cases execute their original three input sequences and check every printed report argument. No full BCL or native OS integration matrix was rerun for this compiler change.

The original FileStats shape assertion still fails (`remaining-shape.trx`), and remains unchanged. After correcting EOR, D0 is dead before its next overwrite, but D1 still fails the whole-register scratch check: the newline path loads the line count with `MOVE.W 8(A7),D1`, which preserves and therefore models a use of D1's upper word. The removed rotate sequence changes only the scratch registers' lower words. A safe follow-up is to track which register bits the rewrite changes across both control-flow paths, rather than treating every partial write as a full overwrite or weakening the assertion. The optimized Report loop remains 122 instruction bytes against its original less-than-128-byte budget. Its behavioral tests pass; restoring the requested ROL.W/ADD.W shape remains outstanding.

The follow-up implements that tracking specifically for the deferred word-rotate rewrite. Both scratch checks walk the control-flow graph and track only the low words modified by the removed sequence. A word overwrite can end a path if its source does not read that scratch; byte overwrites, self-copies, indexed operands, unknown instructions/targets and reads on either successor remain conservative. Whole-register checks used by other rewrites are unchanged.

The existing rewrite also copied the result's upper word from the source rather than from the original combined scratch. The replacement now preserves the combined scratch's upper word explicitly unless matching upper words are proven or the result is masked. When the result overwrites the source, it first saves the source's low word in the other scratch. Updated unit expectations check these preserving copies instead of the previous unsafe full-register copy.

`TestResults/FileStatsShapeInvestigation/word-fold-final.trx` passes all 487 selected cases with zero failures or skips under SDK 10.0.301. This includes the unchanged FileStats shape test, its four CPU report-execution cases, six new overwrite/branch guards, and four CPU theories performing 192 optimized/unoptimized emulator executions of the rotate sequence. The latter vary source words (zero, high-bit and overflow), distinct nonzero upper words, both OR destinations, result/source aliasing and both successors; they check the full result and both full scratch registers. The FileStats loop now occupies 108 instruction bytes (previously 122), within the original less-than-128-byte budget. Its adjacent ROL.W/ADD.W assertion and all subsequent original assertions pass. The full BCL and native OS integration matrices were not rerun for this change; the unrelated general example baselines and payload-only native cost measurement remain outstanding.

Before committing, an isolated worktree based on origin/main at 37ef093 verified the exact six-file change, including the test class's one-line partial prerequisite. All 506 selected instruction-dataflow, machine-optimizer, register-allocation, FileStats shape/behavior and rotate-execution cases pass with zero skips (TestResults/WordRotateCommit/results/commit-on-latest-main.trx). This verifies the commit independently of the larger uncommitted BCL and native OS work in the primary checkout.

## FileStats native file-report coverage

The native OS session now accepts root input files through `inputFiles`; the CLI exposes a single file through `--input-file`. Files are copied into the host RAM-backed ADF before boot and contribute to its image hash. Case-insensitive duplicate names, staging escapes, host-invalid names and names reserved by the runner are rejected. No host-directory mount or fake DOS gateway is used.

The native launcher captures the payload's standard output using DOS Open/SelectOutput, restores the previous stream, and flushes/closes `STDOUT.TXT` before writing the completion record. `NativeOsResult.StandardOutput` contains that file's Latin-1 text; the separate `PayloadOutput` probe retains its meaning. Output-open and flush/close failures are launch statuses 2 and 3. Polling now also defers incomplete filesystem directory snapshots within the existing frame/time bounds. A persistent read error remains visible; disk contents are never repaired to make a run pass.

FileStats requests DOS v37 and parses newline-terminated command lines with native `ReadArgs("FILE/A")`, a supplied CSource and NoPrompt. Successful parser allocations are released with FreeArgs, and the caller-owned object with FreeDosObject. This follows the [AmigaOS command-line parser documentation](https://wiki.amigaos.net/wiki/Basic_Input_and_Output_Programming). Direct callers supplying a filename without the DOS newline retain their existing filename-pointer behavior. Its report algorithm and original code-shape assertions are unchanged.

The final SDK 10.0.301 verification is `TestResults/NativeFileStatsVerified`: `native-os-report.trx` passes all 22 runner cases without skips, and `host-file-stats.trx` passes all 21 selected FileStats/rotate cases without skips, including all four compiler CPU targets. Seven real-OS FileStats scenarios each execute Disabled and FixedPoint images on the 68000 interpreter: empty file, high-bit byte, binary/newline data, quoted filename with spaces, missing file, extra argument and empty command line. They verify complete output and return values against independent host calculations, including signed LONG rendering of the hash. All 14 executions have fresh identifiers, matching expected reports and different Hunk hashes between modes. `records` retains input/compiler/fixture hashes, executable images, expected output and native results; `audit.json` cross-checks the images and the seven mode pairs. The existing six native launcher cases also pass, including invalid Hunk and frame-limit failures. The CLI's new input-file option independently produces the exact six-byte binary report (`cli/result.json` and `cli/report.txt`). Native report runs finish in 550–560 boot-inclusive frames; these are not payload performance measurements. The full BCL and migrated 68040/JIT integration matrices were not rerun for this change.

Before committing, the isolated codex/word-rotate-fix worktree verified this exact 15-file change independently of the primary checkout's uncommitted BCL and integration changes. SDK 10.0.301 passes all 22 native runner cases and all 21 selected FileStats/rotate compiler regressions with zero failures or skips (TestResults/NativeFileStatsCommit/native-os.trx and host-file-stats.trx). The script itself performs native dependency restore and execution. FileStats contains all 14 Hunk images and per-run records; audit.json confirms seven optimizer-mode pairs, fresh identifiers, exact reports/returns and matching executable hashes. Native coverage remains the 68000 interpreter profile.

## Native compiler integration migration

`CopperScreenHeadlessIntegrationTests` now invoke the native AmigaDOS runner instead of the retired headless CLI. The old optional MedPlayer project reference is removed; the host test suite's Copper68k package pin stays unchanged. Native applications use OS-allocated heaps instead of the retired loader's fixed guest address. Expected return values remain unchanged. The current engine's 68000 interpreter and 68040 interpreter replace the retired AccurateM68000/JitM68000 selections; 68040 JIT coverage remains explicit. The 68040 interpreter cases execute 68000 payloads, while the JIT cases execute 68040 payloads.

`scripts/test-native-os.ps1 -IncludeCompilerIntegration` builds and runs both suites, configures the locally built runner, and retains per-run JSON and executable images. Records identify the compiler, fixture, runner and Hunk hashes, requested backend, expected return value and actual native result. Missing ROM configuration explicitly skips optional integration methods. Once configured, missing ROM or runner paths fail rather than silently skipping.

The isolated worktree at parent 5d7385f verifies the migration independently of the uncommitted BCL work. SDK 10.0.301 passes all 22 runner cases without skips and all 43 native compiler behavior/budget cases with zero failures (`TestResults/NativeIntegrationCommit/native-os.trx` and `native-os-compiler-integration.trx`). This covers managed GC roots, exception unwinding, Int64 ABI, virtual/interface dispatch, closures, static initialization, arrays/strings, 68040 floating point, and DOS examples. The native matrix has 20 InterpreterM68000, 17 InterpreterM68040 and 6 JitM68040 executions. `audit.ps1` and `audit.json` verify all 43 fresh identifiers, exact returns, actual backends and retained Hunk hashes against the native records. Separate configuration reports verify 12 explicit method skips without a ROM and expected failures for configured missing ROM/runner paths.

`FileStatsEmptyInputHasPinnedA500PalExecutionCost` is the sole deferred test in the configured matrix. Its original 100,000-cycle and 10,000-instruction limits remain present, but boot-inclusive cycles and the absence of a payload instruction counter prevent equivalent measurement. `FileStatsEmptyInputCompletesWithinNativeBootBudget` independently verifies bounded native launch and result 10; it does not replace the payload benchmark. No compiler, runtime or emulator source changes were required for this migration. The full BCL suite, native 68020 execution and payload-only instrumentation remain outside this verification.
