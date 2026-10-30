# Compatibility and performance testing

## Native StringBuilder growth, formatting and GC

`StringBuilderNativeOsTests` compile the real pinned .NET 10.0.9 win-x64 CoreLib StringBuilder bodies through the normal admission profile, without enabling unlisted managed bodies. The optional runner test project restores that package and verifies SHA-256 `dc1945de746f94987ec705a1f27d512abf72a41a1da37e414d1951e4e823037a` before creating its implementation manifest. Five fixture entries cover presized append, chunk growth, invariant decimal/hex composite formatting, interpolated integer formatting, and temporary allocations plus explicit collection with a live builder and snapshot. They use an Exec-allocated 8 KiB managed heap, full exception support and the on-allocation-failure GC strategy. The expected reports are independently specified text, and every native return must be 42.

The first native selection passed presized/growing text but failed to link the other three entries: the entry stub's fixed BSR.W to `__c68k_gc_shutdown` exceeded its signed displacement range. `EmitRuntimeJsr` now emits an absolute internal JSR; final assembler layout shortens it when in range. The retained decimal/hex Hunk has opcode `4EB9` at code offset 82 and shutdown target 82126, a relative displacement of 82042 bytes (`runtime-call-evidence.json`). Native DOS relocation and execution verify that far call. The original fixture outputs, heap size and assertions were preserved.

SDK 10.0.301 passes all 37 runner cases with zero failures or skips (`TestResults/NativeStringBuilder/Verified/native-os.trx`). The 15 new StringBuilder theory rows perform 30 native executions: five scenarios, two optimizer modes and three backends. Ten runs use InterpreterM68000, ten InterpreterM68040 and ten JitM68040. The interpreters execute identical 68000 Hunks; JIT uses 68040 Hunks. The GC case preserves both the accumulated builder text and its snapshot, then clears and reuses the builder after collection. The existing 22 protocol/FileStats/native-launch cases also pass. All 55 selected managed-pool and branch-relaxation regressions pass (`RuntimeCallFix/runtime-call-regressions.trx`); that host selection additionally reports one explicit optional native integration method skip because its process had no ROM setting.

`Verified/StringBuilder` retains 30 Hunk images, maps and JSON records. `audit.ps1` and `audit.json` verify every output/return, actual backend, both optimizer modes, 30 fresh identifiers and executable hashes. Records also identify CoreLib, compiler, fixture, managed runtime, runner, Amiga engine, CPU engine and ROM inputs. `source-files.json` and `source-inputs-final.zip` retain and verify 725 source/build files; `RunnerBinaries` and `runner-files.json` retain 60 runner/dependency files. A separate CLI replay of the retained 68040 JIT composite-formatting image produces the exact output and result with a fresh identifier (`cli-replay.json`). ROM bytes are not archived.

This verification uses the primary checkout's accumulated, uncommitted BCL support, rather than a standalone commit on the pushed integration branch. The source archive makes that dependency explicit and reproducible. It establishes native StringBuilder behavior for the selected cases; it does not rerun the full BCL or compiler integration suite, validate native 68020 execution, or measure payload-only cycles/allocations.

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

## Versioned StringBuilder implementation input

The test project supplies `Microsoft.NETCore.App.Runtime.win-x64` exactly at `10.0.9` in a separate `PinnedCoreLib/10.0.9` output directory. This is CIL input; it does not replace the test process's CoreLib. `CoreLibPack.CreatePinned()` requires SHA-256 `dc1945de746f94987ec705a1f27d512abf72a41a1da37e414d1951e4e823037a`. The input MVID is `62eafe31-9268-4ab3-97e6-d291cf7a762b`. Synthetic pack tests verify loader controls, but do not prove released-package provenance. The validation SDK is exactly `10.0.301`, as required by `global.json`.

`StringBuilderPinnedPublicSignaturesMatchTheAuditedSurface` compares all 103 public instance constructor/method/accessor signatures with the released PE metadata. Structural identities include declaring assembly, class/value markers, method header, generic arity, required parameter count, arrays, pointers, byrefs and modifiers. Effect tests separately audit reached private helpers, chunk enumeration and interpolated handlers, rejecting adjacent signatures and application lookalikes. Callback effects remain separate reachable calls; declarations are not transitive purity promises.

Stable acceptance requires `EnableUnlistedManagedBodies = false`, the exact released input hash in native maps, full exceptions, separated image/heap/stack regions, all four CPU targets and both optimizer modes. Ownership/behavior tests use physical collection at every allocation. Fault-injection tests use an external accounting allocator and preserve their injected-failure assertions; that allocator does not prove physical GC retention. Cost tests use on-allocation-failure collection and exact allocation counts. M68060 uses the existing M68040 emulator model. Floating workloads use SoftFloat. A compatible dependency graph alone does not prove native correctness. Original native oracles, allocation-failure assertions, instruction ceilings and cost budgets must remain unchanged.

The retained suites cover text and editing, chunk/view lifetime, spans and memory, numeric/enum formatting, culture/provider callbacks, composite formatting, interpolated handlers, arrays/lists/custom enumeration, application values/references and nullable elements. Admission tests verify exact input, caller, signature, payload/layout, effects and root-path restrictions, including rejection of adjacent packs and fabricated metadata. The original stable allocation inventory contains 33 distinct families and is compared with the original fixture inventory; neither inventory may be reduced to obtain a passing result.

## Compiler and runtime invariants

Initialized primitive arrays verify constant length, immediate RVA identity, element width and blob size, reject interior control-flow entries, and convert PE little-endian elements to target byte order. Synchronous span borrowing retains its owner and does not escape its admitted call. Nested managed aggregates propagate reference bitmaps through container and iterator layouts. Successfully resolved constrained value calls execute directly; redispatch through an empty boxed virtual table is incorrect.

Join List fast paths require an exact List descriptor. Subclasses retain public enumeration, including explicit interface reimplementations. Verified List base storage precedes application subclass fields and contributes its GC bitmap and runtime base descriptor. Constructed GetType maps retain the actual layout module/metadata handle, keeping constructor, type-token and public-alias identities consistent. Ordinary inherited enumeration additionally requires inherited interface slots and registration of the boxed enumerator's actual metadata layout; synthetic value-storage layouts cannot supply interface dispatch.

Unwind tables are sorted by final native return address after optimization. Both managed root walkers use lower-bound binary search, preserving the first registered duplicate and termination on missing addresses. Boundary tests cover empty/single/multiple tables, both entry widths, unsigned extrema, duplicate ordering and physical caller-root/finalizer retention. This addresses the original nullable enum timeout without increasing its 500-million-instruction ceiling.

## Completed StringBuilder acceptance

The StringBuilder acceptance build is `TestResults/StringBuilderNullableRootsFinal`, produced with SDK `10.0.301`. Its fixture assembly SHA-256 is `C05AA405303D9DB4AE02CC9AF6BBE518FC0BB672B8C37598591F0466D2C95793`. All required StringBuilder acceptance runs completed successfully on 2026-10-08. The native record audit matches the complete source inventories, exact released CoreLib input, CPU/mode selections and required collection profiles. No compiler/runtime/test source changed between this build and its completion audit. Subsequent verification changes are recorded separately below.

All four `stringbuilder-completion-public-cost-CPU.trx` reports pass their two theory cases per CPU. They establish eight native executions of the unified fixture explicitly using all 103 public signatures, with unlisted bodies disabled, plus forty native cost measurements. The original image/loaded-image/cycle budgets, exact allocation counts and trimming assertions remain intact. All forty post-assertion benchmark records retain the pinned input and current fixture assembly hashes.

All four `stringbuilder-completion-full-corpus-CPU.trx` reports pass all eighty original cases per CPU, without failures or skips: 33 allocation, 29 nullable and 18 application cases. Both optimizer modes are verified. The 264 allocation images execute 1,776 selected cases with their original guarded regions, injected failures, instruction ceilings and result oracles. The 376 nullable/application executions use physical EveryAllocation collection, SoftFloat and the original 500-million-instruction ceiling. Together these establish 640 compiled fixture combinations and 2,152 native executions. Report display-name truncation is resolved only through unique source-inventory prefixes; full native record names independently match every original entry in both modes.

All four `stringbuilder-completion-protocols-CPU.trx` reports pass all forty protocol cases per CPU, without failures or skips. Their method inventories match the source selection, including all four floating cases. These cover text/editing/chunks, spans/memory, provider/handler/composite contracts, Decimal, floating-point, enum/integral/Boolean/character joins and concatenation. They establish 552 physical GC executions plus eight external-accounting concatenation executions across both optimizer modes. Each CPU has 136 distinct physical behavior records; its two boxed Decimal method selections independently prove both invocations despite their shared JSON filename. Concatenation preserves the original two injected failures, six guarded regions and 100-million-instruction ceiling.

The nullable embedded-reference regression verifies the boxed enumerator's actual 28-byte layout and bitmap 0x31. `stringbuilder-nullable-roots-final-nullable-retention.trx` passes all seven checks, including eight native executions across all CPUs and both modes retaining nested references after clearing the source List element, disposal and every-allocation collection. `stringbuilder-nullable-roots-final-gc.trx` passes all 39 selected root-lookup, caller-anchor, root-classification and finalizer checks.

`stringbuilder-nullable-roots-final-policy.trx` passes all 331 selected StringBuilder host, metadata, pinned-input, layout, effect and dependency-graph checks. These include precise boxed-layout and adjacent-pack rejection, host enumeration comparisons, public-signature auditing and original allocation-family inventory checks. This selection excludes CompilerExecutionTests and complements the completed native matrices. Historical and superseded reports are not used to claim acceptance of later source changes.

## CoreLib byte and text codecs

[CoreLibCodecs.md](CoreLibCodecs.md) documents the 41-signature UTF-8, Base64 and hexadecimal surface and its exact admission policy. The target implementations preserve status and consumed/written counts, malformed-input behavior, guarded destinations and explicit final-block semantics. The native matrix includes both optimizer modes for generated corpora on all four compiler CPU targets. The expanded gate contains nineteen roots, including a pipeline that round-trips Unicode through UTF-8, Base64 and hex across collection.

The UTF-8 tests exposed a compiler defect in promoted integer comparisons: an unsigned byte compared with `0x80` could emit a signed byte CMP/branch and misclassify ASCII. `M68kComparisonPromotion` keeps narrow comparisons only when the second operand fits the same domain, selects unsigned conditions for unsigned narrow domains, and otherwise sign- or zero-extends operands to Int32. This applies before condition fusion to materialized comparisons and direct/fused branches. Native regressions compare byte, signed-byte and character operands against signed Int32 thresholds, including 0x80, 0x8000, negative and wider values.

The allocating codec fixture also exposed a data-layout defect: an odd-length initialized byte blob could leave the following type descriptors and dispatch tables on odd addresses. A later physical GC then faulted while reading a descriptor on M68000. Data emission now restores word alignment after initialized blobs before emitting typed metadata. The native Base64 fixture retains its odd-length byte initializer and collection at every allocation, exercising this layout with real codec calls and exception dependencies.

The in-place Base64 encoder exposed a third defect in spilled phi inputs. A byte-valued branch could store only one byte into an Int32 phi slot, while the merge reloaded all four bytes. On the big-endian target the new byte occupied the high lane and the old padding character survived in the low lane. Spill rewriting now extends or truncates each incoming value to the phi's storage width before storing it, using the appropriate signedness. Critical-edge unit tests cover signed and unsigned byte/word extension and narrowing; native Base64 corpora compare guarded in-place operations for lengths 0–75 against the host.

Final qualification on 2026-10-23 passed all 450 selected regression cases, including the complete 64-case codec/comparison native matrix, with zero skips. The expanded gate passed 19/19 closure roots with zero gaps, emitted 19 compatible Hunk images, and passed all 19 mapped M68000 execution cases. `TestResults/CoreLibCodecQualification/qualification-audit.json` binds the final reports, host oracle, source and build hashes, and Hunk images to this run; it also verifies descriptor alignment in all 19 maps. [CoreLibCodecs.md](CoreLibCodecs.md) records the corpus checksums, exact supported signatures, host-versus-pinned runtime versions and rerun command.

## Deterministic Guid compatibility

`GuidSurface` admits exact signatures against the verified .NET 10.0.9 implementation input with unlisted managed bodies disabled. The component constructor `Guid(int, short, short, byte, byte, byte, byte, byte, byte, byte, byte)` and typed `CompareTo(Guid)` execute pinned CoreLib CIL. Its private `GetResult(uint, uint)` helper is admitted only from that exact pinned comparison caller. Byte storage, formatting and parsing use `ShadowGuid` target adapters because native field order is big-endian and CoreLib's raw-memory/vector helpers assume host runtime facilities. The reference semantics are the released [Guid.cs](https://github.com/dotnet/runtime/blob/v10.0.9/src/libraries/System.Private.CoreLib/src/System/Guid.cs).

The supported deterministic surface comprises the component constructor, `Guid(byte[])`, `Guid(ReadOnlySpan<byte>)`, `Guid(ReadOnlySpan<byte>, bool)`, typed equality and comparison, `==`/`!=`, `GetHashCode`, both `ToByteArray` forms, both `TryWriteBytes` forms, `ToString()`/`ToString(string)`, character-span `TryFormat`, and string `Parse`/`TryParse`/`ParseExact`/`TryParseExact`. All five standard text formats N/D/B/P/X accept upper- or lower-case format specifiers and produce lower-case hex. Parsing preserves the released implementation's outer Unicode whitespace handling, legacy D prefixes and X numeric/whitespace behavior. Failed TryParse calls clear their out value; invalid Parse calls throw the corresponding exception type. Short byte and character destinations are untouched and report zero written. Null/empty output format selects D; invalid output formats throw even with a short destination.

The Guid payload is exactly 16 bytes with no references: `_a` at 0, `_b` at 4, `_c` at 6 and `_d` through `_k` at 8–15. The compiler verifies the sequential eleven-field definition and matching runtime adapter before admitting the surface. Packing is restricted to those two owned definitions. Forwarded Guid metadata preserves value identity through locals, guarded object fields, arrays, arguments and returns; constrained Object.ToString/GetHashCode calls resolve only with the verified Guid receiver. Default byte serialization uses little-endian ordering for the first three logical fields; the explicit big-endian form uses network order. Native in-memory values remain target order. Hashes combine logical little-endian words, matching the host corpus independently of target byte order.

The eleven-argument constructor exposed scalar incoming-home spill pressure. Interference analysis and prologue emission now share the proof that a scalar argument's sole use is its adjacent spill store, directly or through its canonical entry copy. Such an argument can be stored from its original ABI location without another persistent register home. Byte/word values in address registers are staged on the stack without borrowing a live data register; stack-slot low-byte offsets and A5/A7 frame addressing are preserved. Pair-home constraints remain unchanged. Synthetic pressure/alias tests, the all-CPU narrow-argument fixture, and frame/callee-save regressions protect this compiler change.

`GuidCoreLibTests`, `GuidTextContractTests` and `GuidAdmissionTests` compare generated bytes, text, parsing, ordering, hashes, exceptions and buffers with host .NET, and reject altered signatures, unowned helper calls and unlisted public APIs. Four-CPU binary, text, generated and adversarial execution fixtures use full exceptions and physical collection at every allocation. The adversarial fixture mutates every character position in each text form and walks all 128 individual payload bits; native checksums must equal host checksums. The M68060 target uses the existing M68040 emulator model. `TestResults/GuidQualification/guid-and-frame-regressions.trx` records the combined semantic and compiler-regression run. The 15-root gate additionally compiles and executes `guid-binary-values` and `guid-text-roundtrips`; closure JSON, compatible reports and Hunk artifacts are under `TestResults/CoreLibAotQualification`.

NewGuid, CreateVersion7/randomness, COM/OS helpers, UTF-8 APIs, span-based parsing, provider overloads, string constructors, boxed/object equality and other unlisted overloads remain outside this surface. This milestone does not admit the complete Guid type or general packed-struct layout.

The Guid qualification run uses SDK 10.0.401 and host .NET 10.0.12, while the compiler input remains the hash-verified 10.0.9 CoreLib. SDK 10.0.301 from `global.json` is unavailable on this machine; `-DotNetWorkingDirectory ..` selects the installed SDK. The combined Guid and frame regression report passes all 188 cases without skips, including sixteen Guid executions and four incoming-scalar executions across the four CPU targets. The full closure gate passes all fifteen roots with zero gaps, valid Hunk images and fifteen passing emulator cases without skips. `TestResults/GuidQualification/qualification-audit.json` records input hashes, CPU coverage and each executed root/image; the compiler, fixture and runtime hashes remain unchanged through qualification. This run does not requalify the complete StringBuilder acceptance inventory above; the full closure gate protects its mapped acceptance root.

## Bounded TimeOnly compatibility

`TimeOnlyArithmeticAndInvariantFormattingExecuteOnTarget` exercises the pinned .NET 10.0.9 CoreLib identity on all four CPU targets. Its closed shadow surface covers 2–5 component and tick constructors, hour/minute/second/millisecond/microsecond/tick access, `Add(TimeSpan)` with positive, negative and multi-day wrapping, comparison/equality operators, and invariant `O`/`o` round-trip output. `TimeOnlyInvariantParsingExecutesOnTarget` covers canonical invariant `Parse`/`TryParse` forms (`HH:mm`, `HH:mm:ss`, and 1–7 fractional digits), including null, range, and malformed-input behavior. Parser arguments must be proven to originate from the exact `CultureInfo.InvariantCulture` getter by the shared provider analysis described below. The host contract tests compare these forms with .NET, while the full CoreLib gate checks closure and target execution for both TimeOnly roots.

## Bounded DateOnly parsing compatibility

`DateOnlyInvariantParsingExecutesOnTarget` exercises the pinned .NET 10.0.9 CoreLib `Parse(string, IFormatProvider)` and `TryParse(string, IFormatProvider, out DateOnly)` callsites across all four CPU targets. It accepts only canonical `yyyy-MM-dd` input and validates Gregorian leap years and date ranges. TryParse returns false with `DateOnly.MinValue` for invalid or null input; Parse throws for invalid or null input. Admission requires proof that the provider argument originates from the exact `CultureInfo.InvariantCulture` getter; safe local copies and invariant branch joins are supported. Host contract tests document that .NET accepts broader invariant forms outside this target grammar; the full CoreLib gate checks closure and M68000 target execution for the new root.

## Invariant parser provider flow

DateOnly and TimeOnly parser admission uses `InvariantParserProviderAnalysis`, shared by reachability analysis and executable method resolution. Its result is cached per method body, including the body of each specialization. The analysis reads call signatures without resolving executable methods, tracks evaluation-stack values and local copies, and computes a fixed point across branches, switches, and loop back edges. A join is invariant only if every incoming provider value is invariant. Getter-origin sets are retained even at unknown joins so later escapes cannot disappear from the use analysis.

The compiler replaces an invariant getter with its target placeholder only when its uses are confined to admitted parser provider arguments and safe copies. An unrelated call, field store, argument store, comparison, return, or explicit discard prevents replacement. If one getter at a provider join escapes, all getters feeding that dependent parser are withheld. Unused getters receive no parser authorization. Current-culture, null, and unknown providers do not authorize parsing. Method parameters and field reloads are unknown; this is a method-local proof, with no cross-method provider inference. Address-taken provider locals and methods with finally, fault, or filter regions fail closed. Catch-only regions propagate local origins along conservative exceptional edges, including uses in catch handlers.

`InvariantParserProviderTests` covers argument-slot confusion, discarded getters, local reassignment, aliases, unknown joins, branch merges, loops, catch escapes, unsupported exception flow, malformed stacks, and caching. `CachedInvariantParserProvidersExecuteAcrossBranchesAndLoops` compiles both Parse/TryParse pairs with shared cached providers, exercises both invariant branch outcomes and repeated loop uses, and executes on all four CPU targets. The canonical DateOnly and TimeOnly input grammars remain unchanged. The full 19-root CoreLib gate separately protects the existing inline-provider closure and execution corpus.

## Separate repository-wide limitations

`stringbuilder-completion-broad-regressions.trx` completes with 4,120 passes and 45 failures, complementing the separately passing 331 StringBuilder checks. Forty-three cases require the unavailable external CopperScreen headless CLI. The two remaining backend failures reproduce independently on an earlier build: `EveryReadWriteWidthWrapperIsRemovedWithoutInliningAttribute` and `ByValuePointerFieldCopyDoesNotLoadItsPointee`. The earlier broad report's missing-PAL Decimal capacity graph is corrected and passes in the current StringBuilder policy selection. Unavailable or failing checks are never counted as passes.

Subsequent pointer-lowering investigation resolves `ByValuePointerFieldCopyDoesNotLoadItsPointee`: the builder correctly extracts the transparent pointer field and propagates its intermediate copy away. The corrected test verifies that the record store consumes the original argument payload directly and that the field access performs no memory read. `pointer-lowering-verified.trx` passes this check and all four native CPU theory cases, each covering both optimizer modes and both scalar/pointer fixtures (sixteen native executions). No compiler/runtime change was needed. The earlier broad report remains a historical 45-failure snapshot; its pointer assertion failure is now resolved, while the wrapper failure, external-tool availability and general baseline remain outstanding.

Subsequent wrapper-elimination investigation resolves `EveryReadWriteWidthWrapperIsRemovedWithoutInliningAttribute`. Exact guest-memory folding now recognizes the verified APTR address operand as the same unowned 32-bit payload when the caller and wrapper use ManagedPointer and Int32 labels. This equivalence is restricted to that address operand; offset/value/result kinds, GC flags, widths, exact pass-through-body checks, null checks and NoInlining restrictions remain enforced. `wrapper-elimination-verified.trx` passes all 110 selected wrapper, machine-optimizer, pointer-identity, constructor-inlining and pointer-lowering checks on `WrapperEliminationInvestigation`. All six wrapper symbols are absent on all four CPUs; native differential cases preserve return values, memory and condition codes, and new negative cases reject GC-address and pointer-offset substitutions. This compiler change follows the earlier StringBuilder acceptance build; the retained StringBuilder reports continue to identify their original build. Both backend failures from the earlier broad snapshot have now been resolved, without rerunning or rewriting that historical report.

`stringbuilder-exact-sdk-baseline.trx` still fails the checked-in general baseline. All seven small fixtures match; six Amiga examples retain previously reproduced drift. The root-search helper adds one method and 216 bytes to IFFInspect compared with the earlier drift. The baseline is unchanged. Review this discrepancy separately; passing StringBuilder-specific budgets does not establish that the repository-wide baseline is green.

## Native OS launch through a host RAM-backed ADF

`Compiler.NativeOsRunner` now builds a standard OFS ADF in host memory and boots it in the current CopperScreen Lightweight engine. Kickstart runs `S/startup-sequence`; the compiled launcher uses native DOS `LoadSeg` and `RunCommand`, then writes a run-specific completion record through DOS. The host accepts only the matching 48-byte record, preserving all 32 return bits. Temporary incomplete-track exports during floppy DMA are retried; a persistent export failure remains visible and cannot create a successful completion.

`TestResults/native-os-final.trx` passes all eight tests with no failures or skips under SDK 10.0.301. Four native payload cases cover return 42, return 0xDEADBEEF, DOS file creation/write/flush/readback, and native argument delivery. Two negative native sessions cover a one-frame budget and DOS rejection of a truncated Hunk. Two host tests cover stale/truncated/version-mismatched result rejection and the RAM ADF's native boot block, OFS root checksum, startup sequence, and multi-sector binary round trip. Separate CLI records `native-os-answer.json`, `native-os-high-bit.json`, and `native-os-dos-probe.json` retain ROM/Hunk/initial-ADF hashes and boot-inclusive frame/cycle counts.

The validated machine is PAL OCS with 512 KiB Chip RAM, 512 KiB Slow RAM, a 68000 interpreter, and Kickstart 3.1 A500 (ROM SHA-256 `8C8A0CF04F91B88EAF0C4F1126041987067E2286A8EE590BDBAE447A8000C5EE`). The final runner and test build use Copper68k `1.5.4-detroit-dev.2` from the engine checkout's local feed; their CPU and engine assembly hashes match and are recorded in `TestResults/native-os-configuration.json`. ROMs are supplied locally and are not included in the source or disk artifacts. The optional project's [README](../../Compiler.NativeOsRunner/README.md) and `scripts/test-native-os.ps1` describe reproduction and bounds. Local user environment variables `COPPERSHARP_NATIVE_OS_RUNNER` and `COPPERSHARP_KICKSTART31_ROM` point to the verified runner and supplied ROM.

At this initial launch checkpoint, the old 43 external integration cases still targeted the retired headless CLI. The migration below supersedes that availability limitation without rewriting historical reports. No compiler or emulator behavior change was required for the launch implementation.

## Migrated compiler integration matrix

`CopperScreenHeadlessIntegrationTests` now invokes the native OS runner through `COPPERSHARP_NATIVE_OS_RUNNER`, or locates a built runner in this repository. The retired MedPlayer project reference is removed. Missing ROM configuration produces explicit discovery-time skips; configured but broken ROM or runner paths fail. Native runs have 1500-frame and 120-second host emulation bounds, with a 180-second process bound covering compilation and startup. JSON assertions validate native completion, launch status, return value, actual CPU/execution mode, fresh run identifier, and readable disk state.

All 42 original behavioral rows retain their expected results and compiler CPU targets. The 17 former 68000 JIT rows explicitly become 68040 interpreter rows executing 68000 images. This supplies different CPU coverage, not 68000 JIT parity. Existing 68040 JIT rows remain JIT, including the native floating-point case. Managed fixtures now compile through `AmigaM68kCompiler`; Exec allocates their GC arena instead of overlapping OS memory at $4000. Their original 88-byte, 0x400-byte, and 0x2000-byte stress arena sizes and behavior checks remain intact.

The first migrated matrix completed with 41 passes, one failure, and one skip (`native-os-migration-first.trx`). Native DOS supplies a terminating newline even for an otherwise empty command line. FileStats incorrectly opened this newline as a filename and returned 20. Its empty-input check now recognizes the newline-only form as well as direct callers' zero-length form, retaining expected usage result 10. The expected result was not changed.

The final SDK 10.0.301 build was verified in three disjoint groups under `TestResults/NativeOsMigrationFinal`: `native-os-execution.trx` passes 16 cases; `native-os-gc-exceptions.trx` passes 12; and `native-os-objects-dos.trx` passes 15 with one explicit skip. Together they pass all 42 migrated behavior cases plus the new FileStats native-launch budget check: 43 passes, zero failures, one skip. `audit.json` verifies 44 unique test cases, 43 unique successful native run identifiers, exact expected values and input-Hunk hashes, one compiler/fixture/ROM hash per input, and actual backend counts of 20 InterpreterM68000, 17 InterpreterM68040, and 6 JitM68040. The corresponding per-run JSON evidence is retained in `records`.

`FileStatsEmptyInputHasPinnedA500PalExecutionCost` retains its original 100,000-cycle and 10,000-instruction assertions but is explicitly skipped: native boot-inclusive cycles do not measure the retired loader's payload-only work, and the current runner has no payload instruction counter. `FileStatsEmptyInputCompletesWithinNativeBootBudget` separately checks a bounded PAL 68000 native launch. It does not claim to replace the cost baseline. `scripts/test-native-os.ps1 -IncludeCompilerIntegration` reproduces the complete native integration selection and records each run.

Related FileStats behavioral regression tests pass on all four compiler CPU targets. Without ROM configuration, all 12 integration test methods explicitly skip (`native-os-migration-host-and-no-rom.trx`). That report also records a separate assembly-shape failure in `FileStatsDefersWordNormalizationUntilWideningBoundary`: its adjacent ROL.W/ADD.W regex no longer matches. This reproduces with the original HEAD FileStats source rebuilt using the same SDK/compiler (`NativeOsFileStatsDiagnostic/original-filestats-shape.trx`), establishing that it was not introduced by the newline fix. Its assertions are unchanged; this code-shape issue and the general example baselines remain outstanding. No compiler or emulator source change was made in this migration.

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
