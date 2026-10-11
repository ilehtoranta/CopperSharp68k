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
