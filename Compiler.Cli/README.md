# CopperSharp.Compiler.Cli

Command-line driver for the optional `CopperSharp.Compiler` CIL-to-Motorola 68k
ahead-of-time compiler.

```text
dotnet tool install --global CopperSharp.Compiler.Cli --version 0.1.0-preview.1
copper68kc Firmware.dll --entry Firmware.Boot::Main --cpu 68000 \
  --format hunk --output Firmware.hunk
copper68kc Application.dll --entry Application.Program::Main \
  --platform amiga --format hunk --output Application
copper68kc Firmware.dll --entry Firmware.Boot::Main \
  --format asm --runtime application --output Firmware.s
copper68kc Firmware.dll --entry Firmware.Boot::Main \
  --framework-report framework-compatibility.json
```

`--framework-report <file|->` performs closed-world analysis without generating
target code. It writes the exact reachable .NET framework members, shortest
rooting paths, call sites, compatibility status, binding, effects, required
runtime features, and pinned reference-pack contract as JSON. Use `-` for
standard output. The command returns a non-zero exit code when
the reachable graph contains a `deferred` or `unsupported` member.

`--platform amiga` enables Amiga SDK library-vector resolution and automatic
library-base handling. The default `generic` platform remains freestanding.
`--managed-assemblies <file>` adds resolved dependency assemblies from one
UTF-8 path per line. `@response-manifest` accepts the versioned SDK `key=value`
format, including repeated `managed-assembly` entries, so dependency paths and
publish settings do not expand the shell command.
`--compatibility-report` writes the exact framework analysis already produced
by a successful compilation; it does not run a second reachability pass. Its
schema-version 2 `NativeCompatibility` object also records the selected
exception and effective memory modes, final exception-region and fatal-machine-
fault-site counts, linked runtime features and helpers, every external native
assembler target, and exact identities of the managed assemblies that contributed
to the reachable graph. Fatal-machine-fault sites are decoded only from the final
executable section ending at the first data boundary; read-only, writable, and BSS
data words are excluded.

`--framework-implementation-manifest <file>` opts into compiler-approved bodies
from an explicitly pinned framework implementation pack. The same setting is
available as `framework-implementation-manifest=<file>` in a response manifest.
The compiler does not inspect installed host runtimes: it verifies the assembly
identity, MVID, and SHA-256 declared by the supplied manifest, and records that
provenance in the map and compatibility JSON. Implementation assemblies remain
separate from `--managed-assembly` dependency inputs.

For the supported Stopwatch instance slice, pinned CoreLib is preferred when a
verified manifest is available: it provides the official implementation and
currently beats the shadow implementation in image size, generated code size,
and measured execution cycles. Omitting the option keeps `ShadowStopwatch` as
the deterministic fallback; the CLI never discovers a host runtime on its own.

Run `copper68kc --help` for ROM layout and fixed-address import options.
Use `--fpu disabled|040|68882|soft` to select floating-point generation. `040`
requires `--cpu 68040` or `--cpu 68060`; `68882` requires `--cpu 68020`.
Floating point is disabled by default.
`--clr auto` enables `CLR` for known-readable frame and stack slots and
automatically for all memory on 68020/040/060 output; use `--clr always` to opt in
for other 68000-compatible memory targets.
`--peephole fixed-point` preserves historical whole-image convergence.
`--peephole bounded` applies a deterministic rewrite budget for very large
closed-world graphs; skipped optional rewrites affect code quality, not program
semantics. `--peephole disabled` skips optional post-allocation condition-code
and peephole rewrites while retaining lowering, allocation, verification, and
linking.
HUNK output defaults to the terminating `application` lifetime. Assembly
output defaults to `freestanding`; pass `--runtime application` only when the
entry is invoked once and private image storage becomes unobservable after it
returns. ROM output is persistent and never receives terminal-store removal.
Use `--runtime resident` for an Amiga image that remains loaded and may run
concurrently in multiple tasks. Its compiler-owned writable library bases live
in a per-invocation record: records up to 512 bytes use the task stack, while
larger records use `Exec.AllocMem` and are freed before return. It emits no
`Exec.Forbid`/`Permit` guard. This initial profile rejects mutable C# static fields, type
initialization, `localloc`, and managed object allocation. Keep small state in
registers or stack locals, and use explicit `Exec.AllocMem` blocks for larger
per-invocation state. It requires `--memory external` or `--memory none`, does
not support managed lifecycle hooks, and requires the Amiga target.
This is a code-generation lifetime contract, not a loader: the host is still
responsible for keeping the image loaded and invoking its entry point again.
HUNK output includes method symbols by default. Pass `--symbols off` to omit
`HUNK_SYMBOL` records from release executables. The equivalent response
manifest entry is `symbols=off`.

## Executable size investigation (2026-10-19)

Omitting embedded HUNK symbols exceeded the 10% executable-file-size target
for every bundled example measured: **48,520 -> 38,928 bytes in total (19.77%)**.
This is a reduction in the executable container; generated instructions and
runtime data do not shrink. The compiler still writes a separate `.map` with
symbol names and addresses. Embedded debugger symbol lookup is unavailable in
the stripped executable.

| Example | Symbols on (bytes) | Symbols off (bytes) | Reduction |
| --- | ---: | ---: | ---: |
| DOS | 876 | 708 | 19.18% |
| FileStats | 1,280 | 1,048 | 18.12% |
| CopperBars | 2,244 | 1,324 | 41.00% |
| ConsoleIO | 8,624 | 7,268 | 15.72% |
| StopwatchBenchmark | 15,396 | 12,776 | 17.02% |
| Polymorphism | 7,332 | 5,720 | 21.99% |
| IFFInspect | 8,168 | 6,692 | 18.07% |
| MUISunflower | 1,488 | 1,012 | 31.99% |
| MUITaskList | 3,112 | 2,380 | 23.52% |

These paired measurements use the same Release IL, MC68000, application
lifetime, fixed-point peepholes, and unchanged exception/memory settings.
ConsoleIO and StopwatchBenchmark use full exceptions and external allocation;
Polymorphism and IFFInspect use full exceptions and a 32 KiB managed pool;
the remaining examples use YOLO exceptions and external allocation. Both MUI
examples use the existing baseline's fixed test import addresses and are size
fixtures, not deployable images. No pinned CoreLib implementation is selected.
Measurements are from the current working tree, built with SDK 10.0.401; the
repository's SDK 10.0.301 pin was not changed. Exact sizes can change with source
or toolchain changes.

Reproduce with the repository's SDK available:

```powershell
.\scripts\measure-executable-size.ps1
```

The script builds the CLI and examples, writes three executables per example
under `artifacts/executable-size/Release`, and records CSV/JSON results and
compiler/input fingerprints. `-NoBuild` reuses existing outputs;
`-Configuration Debug` measures Debug IL separately. It parses the serialized
HUNK records and verifies that removing only `HUNK_SYMBOL` leaves identical
code/data, relocations, hunk layout and allocation sizes for each pair.

Use `--symbols off` with the CLI, or
`dotnet publish -c Release -p:CopperSharpSymbols=off` with the updated SDK.
The SDK property also applies to `CopperSharpCompileOnBuild`; its default
remains `on`.

### Application code generation

With the same example sources and settings above, the initial application passes reduced the
already stripped executables from **38,928 to 34,032 bytes (12.58%)**. This
exceeds the 10% target across the suite; individual reductions range from
5.21% to 16.53%, so the target is not achieved by every example.
All nine freshly built stripped baselines are byte-identical to the original
baseline executables. Each comparison uses one unchanged Release input assembly.

| Example | Stripped baseline (bytes) | Optimized (bytes) | Reduction |
| --- | ---: | ---: | ---: |
| DOS | 708 | 616 | 12.99% |
| FileStats | 1,048 | 988 | 5.73% |
| CopperBars | 1,324 | 1,252 | 5.44% |
| ConsoleIO | 7,268 | 6,232 | 14.25% |
| StopwatchBenchmark | 12,776 | 10,664 | 16.53% |
| Polymorphism | 5,720 | 5,036 | 11.96% |
| IFFInspect | 6,692 | 6,100 | 8.85% |
| MUISunflower | 1,012 | 888 | 12.25% |
| MUITaskList | 2,380 | 2,256 | 5.21% |

Method-level register-move cleanup and return sharing further reduce this suite
from **34,032 to 33,936 bytes: 96 bytes (0.28%)**. This falls short of the additional
10% target. Against the original stripped baseline, the combined reduction is
**12.82%**. These measurements predate the further metadata changes below.

| Example | Previous policy (bytes) | With method passes (bytes) | Saved (bytes) |
| --- | ---: | ---: | ---: |
| DOS | 616 | 612 | 4 |
| FileStats | 988 | 980 | 8 |
| CopperBars | 1,252 | 1,248 | 4 |
| ConsoleIO | 6,232 | 6,216 | 16 |
| StopwatchBenchmark | 10,664 | 10,632 | 32 |
| Polymorphism | 5,036 | 5,024 | 12 |
| IFFInspect | 6,100 | 6,088 | 12 |
| MUISunflower | 888 | 888 | 0 |
| MUITaskList | 2,256 | 2,248 | 8 |

The next investigation found **20,692 bytes of instructions**, **9,122 bytes
classified as read-only data**, and **820 relocations** in the 33,936-byte suite.
Read-only data includes the emitted unwind restore thunks. Console support
accounts for 4,636 instruction bytes across the examples, integer formatting
for 2,862, and the managed-pool runtime for 2,624. The largest individual bodies
include MUITaskList's `Run` (742 bytes), console `Read` (680), and decimal packing
(528). Unwind metadata offered savings without rewriting these algorithms.

Removing unused unwind records, narrowing eligible private exception tables and
sharing more restore routines reduces **33,936 to 32,192 bytes**, saving **1,744
bytes (5.14%)**. The additional 10% suite target remains unmet. The cumulative
reduction from the original stripped 38,928-byte suite is **17.30%**.

| Affected example | Previous policy (bytes) | Current policy (bytes) | Additional reduction |
| --- | ---: | ---: | ---: |
| ConsoleIO | 6,216 | 5,824 | 6.31% |
| StopwatchBenchmark | 10,632 | 9,504 | 10.61% |
| Polymorphism | 5,024 | 4,932 | 1.83% |
| IFFInspect | 6,088 | 5,956 | 2.17% |

The other five examples retain their previous sizes. Run
`scripts/measure-executable-size.ps1 -CompareApplicationMetadata` to compare the
current policy with the five-pass 33,936-byte baseline. `-CompareApplicationMethods`
instead compares with the older three-pass policy. The reports record executable
and input hashes; each comparison uses one unchanged input assembly. `-NoBuild`
reuses existing builds. With `-NoBuild`, `-CompilerPath` can select a separately
built compiler DLL, avoiding interference with another compiler process.

`--symbols off --code-size-optimizations on` enables these application
passes for MC68000 HUNK output. The default remains off; the API equivalent is
`CodeSizeOptimizations = new M68kCodeSizeOptions()`. SDK build and publish accept
`-p:CopperSharpSymbols=off -p:CopperSharpCodeSizeOptimizations=on`.

- `CompactApplicationAddresses` replaces local immediate address loads with
  PC-relative LEA and permits PC-relative loads from initialized writable data
  in the same hunk. Final displacement and alignment checks retain absolute
  forms when necessary. BSS, external imports and address addends retain their
  existing forms.
- `ShareApplicationRuntimeMetadata` aliases identical immutable GC root maps
  and unwind descriptors, shares restore routines, and uses MOVEM for eligible
  register restores. Frame sizes, exact saved-register order, dynamic-stack
  flags and frame-anchor offsets participate in the sharing decision.
- `CompactApplicationExceptionSites` omits unused root-map columns and, when
  every state is null, the state column. It encodes table pointers as signed
  32-bit relative offsets, removing their loader relocations. This applies only
  when there is no built-in collector, external GC import or nonempty root map.
  This pass leaves GC tables in their existing 20/24-byte format. Relative offsets
  add a few instructions to exception dispatch; this is a size tradeoff, not a
  claim of faster exception handling.
- `RemoveApplicationRegisterMoves` removes dead register moves using control-flow
  liveness and forwards redundant register copies into eligible consumers. Live
  condition codes, partial-register values, call effects and metadata boundaries
  remain protected. It does not enable private-stack or guest-memory rewrites.
- `ShareReturnSequences` reuses exact terminal sequences and suffixes within each
  eligible method. Application sharing never pools returns across methods and
  retains unwind resume addresses. Exception-handler and dynamic-stack methods
  are excluded from both method passes. If a built-in or external collector can
  run, methods with GC references, root spills or managed byrefs are also excluded.
  The collector's root-map ABI and frame layout remain unchanged.
- `PruneApplicationUnwindMetadata` omits descriptors and restore routines with
  no remaining references. Site records, root maps and directly referenced
  descriptors remain available, including descriptors used by finally/resume paths.
- `NarrowApplicationExceptionSites` uses signed 16-bit fields in private compact
  tables with at least eight sites and representable stack adjustments. Final
  pointer offsets are checked after layout. An overflow recompiles with this
  speculative narrow table passes disabled. The lookup rejects out-of-range
  PCs before comparing 16-bit keys.
- `CompactApplicationGcSites` selects matching private readers in the built-in
  collector and emits 10/12-byte site rows with signed relative word fields.
  Root maps, descriptors, frame anchors and static roots retain their layouts.
  The final linker checks every displacement and retries with legacy readers
  on overflow. External collectors always keep the public 20/24-byte format.
- `PruneApplicationLeafSites` omits sites only for direct callees whose complete
  call graph cannot throw or collect. Opaque calls, indirect dispatch, handlers,
  dynamic frames and collector entry points remain barriers. The six raw
  address read/write intrinsics are recognized by their exact lowering.
  Instructions, safepoint liveness and root spills remain unchanged.
- `CompactApplicationExceptionRuntime` shares compiler-generated fault entries
  and repeated catch/finally entry sequences. Fault stubs tail-jump into cursor
  creation, preserving the original caller return address and unwind metadata.
  Images with no exception states omit resume/state dispatch, while retaining
  the full stack walk, register restoration and unhandled-exception lifecycle.
- `ShareApplicationUnwindRestores`, together with `ShareApplicationRuntimeMetadata`,
  shares restore routines across different frame sizes when the saved-register
  order matches. It reads the frame size from the existing exception descriptor.
  Descriptor fields, GC roots, frame anchors and register snapshot locations are
  unchanged. This adds instructions during exceptional unwinding only.

Select passes individually with `--code-size-passes` and a comma-separated list
of these names. Exception mode, memory management and application lifetime are
unchanged. Existing ROM/resident/freestanding transformations retain their
original eligibility checks.

The focused execution tests compare baseline, individual passes and combined
passes with peepholes enabled and disabled. Serialized HUNKs are loaded at two
addresses with separately placed BSS and executed on 68000/68020/68040 models.
Coverage includes nested exceptions, rethrows, finally blocks, saved registers,
dynamic stack allocation, forced collection, interior byrefs and finalizers.
ConsoleIO output and shutdown are compared on input and EOF paths. Additional
checks cover all condition codes for address loads, relocation fallbacks,
long relative offsets, and the external collector's unchanged table format.
The initial application passes passed 26 focused execution/relocation tests plus 1,294 selected
branch-relaxation, generated/resident-size, root-lookup, frame-preservation and
external-collector regression tests. SDK build and publish both produced the
expected 616-byte stripped, optimized DOS executable before the method passes.

With the method passes implemented, validation passed **1,407 selected tests**:
23 application execution/policy cases, 1,380 optimizer/relocation regressions,
and four root-lookup cases. New coverage checks live flags, partial writes,
values live on only one branch, protected resume addresses, method ownership,
independent pass controls and unchanged output when peepholes are disabled.
All nine previous-policy executables and input assemblies match the frozen
first-stage baseline hashes.

The subsequent metadata changes passed **1,419 selected tests**: 39 application,
GC/root-lookup and metadata boundary cases plus the 1,380 optimizer/relocation
regressions. Coverage includes all eight combinations of the three new passes,
forced collection, nested exceptions and dynamic frames, every saved-register
snapshot, signed 16-bit boundaries, rejected PCs with matching low bits, and
byte-identical fallback for oversized tables. All nine previous-policy images
and input assemblies match the frozen 33,936-byte baseline hashes. Measurement
and tests used the same compiler binary, built in an isolated output directory
because another compiler process held the usual CLI build files open.

### Console I/O and packed integer formatting

Sharing DOS acquisition/restoration and the native byte writer removes repeated
console exception paths. `Write` and `WriteLine` share string handling; packed
32/64-bit values use private stack buffers and the same checked writer. Empty
`Write` remains a no-op, while null `WriteLine` writes a newline. Output handles
are acquired once per call; newline writes remain separate and only occur after
the preceding write succeeds. Every installed DOS base is restored in `finally`.

Integer packing now prepends bytes with fixed shifts. The 32-bit path uses an
exact shift/add division by ten with a remainder correction, avoiding a software
division loop. The 64-bit path retains base-10000 division, trims only the final
chunk and no longer links the general digit counter or byte-position helper.
No managed or native allocations are added. These runtime improvements apply
without requiring the optional compiler size policy.

With the same nine Release inputs, MC68000, stripped HUNKs and the existing
application size policy, the additional runtime reduction against main at
`d94cb27` is:

| Example | Previous runtime | Updated runtime | Reduction |
| --- | ---: | ---: | ---: |
| ConsoleIO | 5,824 | 4,888 | 16.07% |
| StopwatchBenchmark | 9,504 | 7,144 | 24.83% |
| Seven other examples combined | 17,204 | 17,204 | 0% |
| **Total** | **32,532** | **29,236** | **10.13%** |

The method inventory attributes 1,216 saved instruction bytes to console code
and 1,462 to packing; the remaining savings come from smaller unwind metadata
and relocation records. Existing cycle, spill and allocation budgets are
retained. Only expected linked-method counts change to reflect shared helpers.

`measure-executable-size.ps1 -NoBuild -CompilerPath <compiler.dll>
-RuntimeDirectory <updated-runtime-directory> -CompareRuntimeDirectory
<previous-runtime-directory>` compares both runtime pairs with one compiler
and unchanged sample assemblies. Both directories must contain
`CopperSharp.Runtime.Managed.dll` and `CopperSharp.Runtime.AmigaPal.dll`.
The report includes runtime hashes and previous-runtime executable sizes/hashes.

Regression coverage includes invariant formatting and zero padding at decimal
boundaries and 10,000 deterministic random values, allocation-free packing,
generated execution on 68000/68020/68040 with two load bases and peepholes on/off,
every write failure in mixed console output, application and scoped DOS
ownership, buffered input/EOF, and existing exception/forced-GC cases.

`--code-size-optimizations on --code-size-passes SimplifyLoopPhis` selects the
default-off trivial-loop-phi cleanup. Append `SimplifyLoopPhis` to an existing
comma-separated pass selection to preserve that selection. The pass applies
to MC68000 resident HUNKs and ROM-profile ROM/assembly output with YOLO exceptions
and `--memory none`. The other opt-in generated size passes use the same gate;
`--rom-size-optimizations on` retains its existing policy.
It removes phis with one distinct non-self input before register allocation;
type, register, GC, and managed-byref constraints still apply. It does not
enable loop copy substitution or loop memory optimization. Other targets and
runtime profiles retain their existing lowering. API clients select
`M68kCodeSizeOptions.SimplifyLoopPhis = true`.

### Compact collector tables and proven-safe call sites

Against main at `439c1e8`, the next pass keeps the same nine Release inputs,
MC68000 target, stripped HUNK format, exception modes and heap settings:

| Example | Previous policy | Updated policy | Reduction |
| --- | ---: | ---: | ---: |
| ConsoleIO | 4,888 | 4,872 | 0.33% |
| StopwatchBenchmark | 7,144 | 7,104 | 0.56% |
| Polymorphism | 5,100 | 4,028 | 21.02% |
| IFFInspect | 6,128 | 4,972 | 18.86% |
| Five other examples combined | 5,976 | 5,976 | 0% |
| **Total** | **29,236** | **26,952** | **7.81%** |

Compact GC tables alone save 1,372 bytes. Removing proven-safe call sites and
their now-unused metadata saves another 912 bytes. This round saves 2,284 bytes,
short of the additional 10% target. The two collector examples contain the
largest remaining avoidable metadata, so they receive most of the reduction.
Bypassing unwinding in programs without handlers was considered but not used:
unwinding also restores registers observable when the error requester returns.

Reproduce with `scripts/measure-executable-size.ps1 -NoBuild -CompilerPath
<compiler.dll> -RuntimeDirectory <matching-runtime-directory>
-CompareApplicationGcSites`. The previous-policy variant disables only
`CompactApplicationGcSites` and `PruneApplicationLeafSites`; the report records
both executable hashes, unchanged input hashes, compiler/runtime hashes,
instruction/data bytes and relocations. The updated compiler and managed runtime
must be used together for the private compact readers.

Coverage includes both optimizer modes, relocated serialized HUNKs on
68000/68020/68040, signed lookup boundaries and duplicate keys, 64-KiB PC aliases,
automatic wide-table fallback, external collector ABI preservation,
type-initializer cleanup, recursive safe/throwing call graphs, opaque native
calls, dynamic frames, interior/byref roots, finalizers, forced collection and
console output/failure paths.

The wider console regression run passes its output checks but exposes an
existing pinned-cost failure in `ConsoleIoInputAndEofPathsHavePinnedMc68000Costs`:
32,262 input cycles versus the expected 30,992. The same test reproduces the
identical failure with the frozen compiler and runtime from `439c1e8`, with size
optimizations disabled. This round leaves that benchmark expectation unchanged.

### Smaller runtime code

The next investigation starts at `3dafa84` (26,952 bytes). Across the nine
examples, emitted named runtime methods account for 3,420 bytes in ConsolePal,
2,924 in ManagedPool, 1,400 in integer formatting and 748 in ClockPal. The largest
individual routines are console Read (600), PackDecimal64 (364), string output
(300 per using image), DivideUInt64By10000 (282) and object tracing (196 per
collector image). Generated exception handling also repeats across these images.

The retained changes share fault and catch/finally entry code, omit unused
exception-state dispatch, read console bytes directly, and share the GC loop
that marks slots from static and compact frame root maps. Static slots use a
zero base; signed frame offsets use the original frame base. Root-map formats,
collection triggers, exception identities and unwind register restoration are
unchanged.

| Example | Previous policy | Updated policy | Reduction |
| --- | ---: | ---: | ---: |
| ConsoleIO | 4,872 | 4,628 | 5.01% |
| StopwatchBenchmark | 7,104 | 6,984 | 1.69% |
| Polymorphism | 4,028 | 3,864 | 4.07% |
| IFFInspect | 4,972 | 4,828 | 2.90% |
| Five other examples combined | 5,976 | 5,976 | 0% |
| **Total** | **26,952** | **26,280** | **2.49%** |

This is below the additional 10% target. A shared string/native output owner
was rejected because it pulled the string writer into boolean and character
only programs. Splitting console input saved little after helper metadata.
Smaller decimal-division loops and word helpers exceeded the existing
four-byte register-spill budget, so integer formatting remains unchanged.

Reproduce with `scripts/measure-executable-size.ps1 -NoBuild -CompilerPath
<compiler.dll> -RuntimeDirectory <matching-runtime-directory>
-CompareApplicationRuntime -CompareRuntimeDirectory <3dafa84-runtime-directory>`.
The previous-policy variant disables `CompactApplicationExceptionRuntime`.
Together with the comparison runtime directory, the previous-runtime variant
reconstructs the previous complete baseline. All nine reconstructed images
are byte-identical to the frozen `3dafa84` images.

Coverage adds all supported explicit runtime fault reasons and null throws,
comparing the unhandled hook's preserved registers against the previous
dispatcher at two load addresses, on three CPU models, with both optimizer
modes. Existing catch/finally, dynamic frame, finalizer, forced-GC, output,
short-write and resource-ownership checks remain in the regression set.
The pre-existing pinned console-input timing failure remains: direct byte
loads improve its measured path from 32,262 to 31,758 cycles, still above
the unchanged 30,992 expectation.

### Dead arithmetic, narrow values and memory operands

Three independently selectable passes now default on within the opt-in MC68000
application HUNK size policy:

- `RemoveApplicationDeadArithmetic` deletes non-trapping computations whose
  register results and condition codes are dead on every successor. Memory
  accesses, division, stack changes, explicit effects, metadata entries and
  unresolved control-flow exits remain barriers.
- `TrackApplicationNarrowValues` follows the bits changed by masks, extensions
  and zero-clears through branches, loops and partial writes. It also narrows
  proven zero-extended comparisons when signed flags are unobserved. Upper bits
  may pass through arithmetic only when they cannot affect the consumed low
  bits and all potentially different flags are dead. Calls and unknown uses
  reject the proof; bounded analysis falls back to the original instructions.
- `FoldApplicationMemoryOperands` combines byte, word and long loads with
  ADD/SUB/CMP/AND/OR, including short gaps of independent register computations.
  The memory read keeps its position, width and address. No access is moved
  across another access or call, and observable flags and temporary values are
  preserved.

These passes keep the existing method eligibility rules: no handlers or dynamic
frames, and no GC-reference/byref/root-bearing frames when a collector can run.
They do not alter root maps, unwind layouts, collection triggers or native ABIs.
Policy-off, unsupported CPU targets and disabled peepholes retain the old code.

Measured against `d7f359f`, using the same nine Release inputs and stripped HUNKs:

| Example | Previous policy | Updated policy | Bytes saved |
| --- | ---: | ---: | ---: |
| Polymorphism | 3,864 | 3,852 | 12 |
| IFFInspect | 4,828 | 4,820 | 8 |
| Seven other examples combined | 17,588 | 17,588 | 0 |
| **Total** | **26,280** | **26,260** | **20 (0.08%)** |

Instruction bytes fall from 17,540 to 17,516 (24 bytes, 0.14%). Alignment hides
the two-byte instruction savings in each of FileStats and CopperBars. Narrow
tracking produces the sample savings; dead arithmetic and broader memory folding
are exercised by the instruction-level regressions but find no additional sites
in these nine images. The additional 10% target was not reached. Existing earlier
passes already remove most of the simple patterns; broadening exception/GC gates
or removing required checks to inflate the result is outside these proofs.

Reproduce with `scripts/measure-executable-size.ps1 -NoBuild -CompilerPath
<compiler.dll> -RuntimeDirectory <matching-runtime-directory>
-CompareApplicationArithmetic`. The previous-policy variant disables all three
new passes. Its nine images were checked byte-for-byte against the frozen
`d7f359f` baseline. The report records compiler/runtime/input/image hashes and
instruction, data and relocation counts.

Validation: 703 checks passed; the sole failure is the pre-existing console
timing assertion (31,758 measured versus 30,992 expected). The 57 new regression
cases compare registers, flags, stack, memory and access order/width across input
boundaries and all 32 initial CCR combinations. They execute on 68000/68020/68040,
except OR.B/W memory-source cases, which run on 68000/68040 because Copper68k
1.5.1 lacks their exact 68020 timing entries. Coverage also includes signed and
unsigned comparisons, live upper bits, arithmetic carry, calls, metadata entries,
relocation addends and policy gates. Existing tests exercise relocated HUNKs,
forced collection, interior/byref roots, finalizers, catch/finally/rethrow, output,
short writes and resource cleanup with the new passes enabled and independently
disabled.
### Register allocation and copies across blocks

`OptimizeApplicationRegisterCopies` now defaults on within the opt-in MC68000
application HUNK size policy. It enables three changes:

- Copy substitution and trivial-phi cleanup also run in methods containing loops.
  Substitution still requires equal value/register constraints and a dead source;
  successor uses, phi inputs, exact-memory identities and logical-call arguments
  are updated together.
- Allocation preferences count static copy sites, including repeated phi inputs.
  A bounded recoloring step accepts only strict copy-score improvements, respects
  interference, fixed registers, call clobbers and register-pair overlaps, and
  cannot introduce spills or expand the existing callee-save mask.
- A plain 32-bit integer copy between values already assigned the same spill slot
  omits its redundant reload/store pair. GC references, managed byrefs, owner
  transport, explicit effects and call metadata retain the original path.

The existing application method eligibility checks remain: no exception handlers
or dynamic stack frames, and no GC references, byrefs or root-bearing frame homes
when a collector can run. Root maps, unwind layouts and the native ABI are
unchanged. This is a machine-IR/allocation policy, so it also operates when
post-emission peepholes are disabled. Other CPU/profile combinations and the
policy-off path retain their previous allocation.

Measured against `012220e`, with identical Release inputs and stripped HUNKs:

| Example | Previous policy | Updated policy | Bytes saved |
| --- | ---: | ---: | ---: |
| DOS | 612 | 612 | 0 |
| FileStats | 980 | 944 | 36 |
| CopperBars | 1,248 | 1,248 | 0 |
| ConsoleIO | 4,628 | 4,620 | 8 |
| StopwatchBenchmark | 6,984 | 6,964 | 20 |
| Polymorphism | 3,852 | 3,848 | 4 |
| IFFInspect | 4,820 | 4,816 | 4 |
| MUISunflower | 888 | 892 | -4 |
| MUITaskList | 2,248 | 2,248 | 0 |
| **Total** | **26,260** | **26,192** | **68 (0.26%)** |

Instruction bytes fall from 17,516 to 17,448 (0.39%). MUISunflower keeps the same
instruction-byte count but gains one four-byte relocation: a string address moves
from an address register/PC-relative LEA into a data register/absolute immediate.
The allocation score estimates copies, so it does not guarantee a smaller final
HUNK for every input. The same-slot spill cleanup has focused regression coverage
but adds no further savings in this nine-image corpus.

The 10% target was not reached. The baseline contains 1,762 bytes of register moves
across these images (6.7% of total serialized size), including necessary ABI moves.
Stack MOVE instructions account for another 2,478 bytes, including required argument,
frame and GC traffic. Further substantial savings need to address lowering and ABI
staging costs as well as register copies; eliminating all of these instructions
would not preserve program behavior.

Reproduce with `scripts/measure-executable-size.ps1 -NoBuild -CompilerPath
<compiler.dll> -RuntimeDirectory <matching-runtime-directory>
-CompareApplicationRegisterCopies`. This disables only the new allocation policy
in the previous-policy variant. All nine previous-policy images were verified
byte-for-byte against the frozen `012220e` images. Reports include compiler,
runtime, input and output SHA-256 hashes, instruction bytes and relocation counts.
The measured compiler SHA-256 is
`D18561FEF18E9C79C538259C5ACC036C09A8F5C02AA75F8F0061815A2C18F3DF`.

Validation: 422 focused checks passed. Coverage includes repeated phi-edge
preferences, pair-overlap rejection, live-across-call clobbers, scalar/GC spill
handling, successor metadata substitution, loop-carried initializers, dense
switch joins, CRC results and FileStats/console output. The application matrix
also covers relocated HUNKs, full exceptions, forced collection, interior/byref
roots, finalizers, dynamic frames and both peephole modes. Existing pinned cycle
budgets were not changed.
### Call-argument staging and ABI copies

`OptimizeApplicationCallArguments` defaults on within the opt-in MC68000
application HUNK size policy. It retains formal argument locations, stack layout,
argument evaluation order and call effects while reducing redundant transport:

- Exact direct callees with unused scalar register formals no longer require the
  caller to stage those values. Stack arguments and logical argument identities
  stay intact; effectful and throwing argument expressions still execute. This
  application control is independent of the older ROM/resident
  `ElideUnusedRegisterArguments` switch.
- A destructive staging copy can prefer its source register when it is the last
  use of an equal-width scalar source. The copy is not declared equivalent, so
  normal interference and clobber constraints remain in force.
- A bounded search can move one flexible scalar blocker to free a fixed argument
  register. It requires a strict reduction in static copy cost, preserves fixed
  and pair locations, and cannot introduce spills or new callee-saved registers.
- Adjacent single-use large integer materializations, including constants folded
  through copies, can push the immediate directly. Zero uses `CLR.L -(A7)` only
  when the selected CLR policy permits it; the default MC68000 policy retains
  the original zero transport. Forwarded stack arguments can be pushed directly
  from their original stack slot without going through D0.

Eligibility retains the existing exception/GC gates: no local handlers or dynamic
frames, and no GC references, managed byrefs or root-bearing frames when a collector
can run. Unused-argument analysis additionally excludes managed byref functions and
GC-bearing callers/callees. No runtime table format, collection trigger or native
ABI is changed. The policy operates before or during allocation/emission, including
when post-emission peepholes are disabled; other targets retain their old lowering.

The exception regression also exposed an existing checked-add bug: the small
constant arithmetic shortcut emitted ADDQ and returned before the overflow check.
Throwing arithmetic now uses the normal checked emitter on every target,
independently of the call-argument option.

Measured against `384f977`, using the same Release inputs and runtime DLLs for
both policy variants, with stripped HUNKs:

| Example | Previous policy | Updated policy | Bytes saved |
| --- | ---: | ---: | ---: |
| DOS | 612 | 612 | 0 |
| FileStats | 944 | 944 | 0 |
| CopperBars | 1,248 | 1,248 | 0 |
| ConsoleIO | 4,620 | 4,604 | 16 |
| StopwatchBenchmark | 6,964 | 6,928 | 36 |
| Polymorphism | 3,848 | 3,848 | 0 |
| IFFInspect | 4,816 | 4,816 | 0 |
| MUISunflower | 892 | 888 | 4 |
| MUITaskList | 2,240 | 2,236 | 4 |
| **Total** | **26,184** | **26,124** | **60 (0.23%)** |

Instruction bytes fall from 17,440 to 17,396 (0.25%). The 10% target was not
reached. The audit found 28 adjacent constant-to-stack staging pairs, with at
most 56 bytes of direct instruction savings before eligibility and CLR-policy
checks. Larger remaining call costs include 66 platform-base loads (396 bytes),
but their writable base slots and external callbacks prevent simply hoisting
them without an additional stability contract. Existing caller-supplied cached
base-register APIs provide that contract where appropriate.

Reproduce with `scripts/measure-executable-size.ps1 -NoBuild -CompilerPath
<compiler.dll> -RuntimeDirectory <matching-runtime-directory>
-CompareApplicationCallArguments`. The previous-policy variant disables only this
new policy while retaining the earlier allocation and arithmetic improvements.
All nine previous-policy images were verified byte-for-byte against the frozen
`384f977` images. Reports include compiler, runtime, input and output SHA-256
hashes, instruction bytes and relocation counts. The measured compiler SHA-256 is
`303C87A78214A723F2608B9F94192A4DFCD482D034489DC53CF7DF1E163534FD`.

Validation: 447 focused checks passed. Coverage includes fixed-register affinity,
live sources, bounded reassignment, direct push encodings, CLR policy, forwarded
stack slots, argument side-effect order, unused throwing arguments and checked
arithmetic. The application matrix covers relocated HUNKs, full exception
unwinding, forced collection, interior/byref roots, finalizers, dynamic frames,
console output and both peephole modes. Direct push and exception regressions
execute on 68000/68020/68040. Existing pinned cycle budgets were not changed.

### Repeated platform-base loads

`CompactApplicationPlatformBases` defaults on within the opt-in MC68000
application HUNK size policy and requires `CompactApplicationAddresses`.
Frequently read, zero-initialized writable base slots are placed alongside
initialized data when at least two exact longword reads can reach them with a
signed 16-bit PC displacement. Each relaxed read saves two instruction bytes
and removes a four-byte absolute relocation, covering the four initialized
bytes and alignment cost. Infrequent slots remain eligible for BSS; the HUNK
writer still chooses the smaller container layout. Other CPUs, ROM/resident
output, and caller-supplied cached-register conventions retain their layout.

This reduces the encoding cost of the reads while preserving their values and
ordering. The audit found 66 absolute slot-to-A6 loads in the nine previous
images, but no safe repeated same-slot reads within a straight-line region
without intervening calls, writes or clobbers. Caching across native calls would
need a stronger stability contract: a callee can retain a callback from an
earlier call and invoke it later, changing a writable base slot.

The investigation also corrected two existing assumptions independently of the
size option. Memory promotion now treats opaque calls as possible observers and
writers of platform slots, even when the current call has no callback argument.
Known local value intrinsics remain eligible for promotion and resolve metadata
against their original method after inlining. ABI rematerialization retains the
original base snapshot across writes, calls, control flow, exception/GC boundaries
and register clobbers. Cached-register definitions are barriers too. These fixes
retain stores and reloads where re-entry or mutation can make them observable.

Measured against `5aea929`, using a frozen previous compiler, identical Release
inputs and runtime DLLs for both variants, and stripped HUNKs:

| Example | Previous compiler | Updated compiler | Bytes saved |
| --- | ---: | ---: | ---: |
| DOS | 612 | 612 | 0 |
| FileStats | 944 | 956 | -12 |
| CopperBars | 1,248 | 1,272 | -24 |
| ConsoleIO | 4,604 | 4,480 | 124 |
| StopwatchBenchmark | 6,928 | 6,712 | 216 |
| Polymorphism | 3,848 | 3,848 | 0 |
| IFFInspect | 4,816 | 4,700 | 116 |
| MUISunflower | 888 | 888 | 0 |
| MUITaskList | 2,236 | 2,096 | 140 |
| **Total** | **26,124** | **25,564** | **560 (2.14%)** |

Instruction bytes fall from 17,396 to 17,280 and absolute relocations from 512 to
436. The net total includes the additional state preservation in FileStats and
CopperBars. The continuing 10% target was not reached.

Reproduce with `scripts/measure-executable-size.ps1 -NoBuild -CompilerPath
<updated-cli.dll> -CompareCompilerPath <frozen-previous-cli.dll> -RuntimeDirectory
<matching-runtime-directory> -CompareApplicationPlatformBases`. Without
`-CompareCompilerPath`, both variants use the current compiler and isolate only
the placement option; a frozen compiler is necessary to include the independent
correctness fixes in the comparison. Reports record both compiler hashes plus
runtime, input and output hashes. All nine baseline images were checked against
the frozen `5aea929` images.

The measured compiler SHA-256 is
`98C701ED3A475E63A51CB59064B9FF9507FACFFD9C89AF06AC7081A968AC3A85`;
the previous compiler is
`303C87A78214A723F2608B9F94192A4DFCD482D034489DC53CF7DF1E163534FD`.

Validation: 720 focused checks passed. New coverage checks exact read selection,
signed PC-displacement limits, CPU and option gating, zero initialization,
mutable slots across retained callbacks and explicit writes, A6 clobbers, and
base-load rematerialization across writes, exceptions, safepoints and control
flow. The inlining regression resolves a real SDK intrinsic from a different
module. Execution coverage includes 68000/68020/68040, relocated HUNKs, full
exception unwinding, forced collection, interior/byref roots, finalizers,
dynamic frames, console output and both peephole modes.
