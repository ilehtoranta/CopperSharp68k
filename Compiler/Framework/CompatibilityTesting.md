# Compatibility and performance testing

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
