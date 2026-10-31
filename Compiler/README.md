# CopperSharp.Compiler

`CopperSharp.Compiler` is an optional closed-world CIL-to-68k ahead-of-time
compiler. It emits Amiga loadable HUNK executables and 256/512 KiB Kickstart ROM
images, plus portable assembler text, for MC68000, MC68020, MC68040, and MC68060 targets.

The compiler package is deliberately separate from the `Copper68k` emulator.
Installing or using the emulator does not pull in the compiler.

This README describes the current `main` source build, reviewed on 31 October
2026. Published preview packages may expose an earlier subset; use the restored
package versions and the compatibility/provenance report from your own build.

## Floating point

Floating point is opt-in through `M68kCompilationRequest.FloatingPoint`; the
default `Disabled` mode preserves the integer-only behavior. `M68040` targets
the integrated FPU on 68040/68060, while `M68882` targets an external
coprocessor with a 68020 host. The native backend supports IEEE single/double
constants and native add, subtract, multiply and divide. Admitted comparisons,
negation and numeric conversions use shared integer-runtime lowering in these
modes. Native floating-point remainder remains unsupported.
For MC68040, double-precision operations reuse one 8-byte `(a7)` scratch slot
for both operands and the result, avoiding repeated stack writeback chains.
For MC68060, double-precision operations use one shared stack scratch window so
operand loads and result stores do not repeatedly adjust A7; single-precision
operations remain direct D-register-to-FPU transfers.

`SoftFloat` implements single/double add, subtract, multiply, divide, remainder,
negation, IEEE comparisons, and admitted conversions between integer and
floating-point representations through the bundled integer-only runtime. It
requires no FPU. Both software and native modes transport IEEE bits through the
private D-register/stack ABI: one longword for `float`, a high:low pair for
`double`. Helpers are linked only when reachable. Selecting a floating-point
mode does not admit every floating framework member or checked conversion.

The implementation boundary is recorded in
[`CompilationModule.Numeric.cs`](Metadata/CompilationModule.Numeric.cs), with
arithmetic and conversion kernels in
[`FloatingPointArithmetic.cs`](../Runtime.Managed/FloatingPointArithmetic.cs)
and [`FloatingPointConversions.cs`](../Runtime.Managed/FloatingPointConversions.cs).

## Library API

```csharp
var result = M68kCompiler.Compile(new M68kCompilationRequest
{
    AssemblyPath = "Firmware.dll",
    EntryPoint = "Firmware.Boot::Main",
    Cpu = M68kCpuTarget.M68000,
    OutputFormat = M68kOutputFormat.KickstartRom,
    RuntimeProfile = M68kRuntimeProfile.Rom,
    Rom = new KickstartRomOutputOptions
    {
        Size = 512 * 1024,
        InitialStackPointer = 0x0008_0000
    }
});

File.WriteAllBytes("kick.rom", result.Image);
```

`M68kClrPolicy.Auto` uses `CLR` for known-readable frame and stack slots, and
for general memory on MC68020/040/060 output. MC68000-compatible output uses
move-based clears for arbitrary memory unless `ClrPolicy` is set to
`M68kClrPolicy.Always`.

`M68kPeepholeOptimizationMode.FixedPoint` preserves the historical global
peephole fixed point. Large closed-world images with thousands of independent
rewrite candidates may select `Bounded`; it applies one deterministic global
cycle with eight optional rewrites. This bounds compile work without changing
program semantics, although the resulting image can retain optimization
opportunities.

The compiler accepts a deliberately bounded, freestanding subset of CIL. Any
reachable unsupported instruction or type is reported with a stable `C68Kxxxx`
diagnostic rather than silently changing its semantics.

MC68020, MC68040, and MC68060 have separate optimization profiles. MC68020
selection uses the processor manual's cache-case timings and favors compact
`MOVEQ` plus register operations over long immediate forms where both time and
size improve. MC68040 selection accounts for its single-issue integer pipeline,
while MC68060 can retain short register forms that benefit its dual execution pipelines.
Instruction reordering is deliberately deferred until the backend has
dependency- and alias-aware scheduling.

Every compilation result also exposes `LoopFootprints`, measured after final
branch relaxation and linking. Each natural-loop report includes its header IL
offset and linked address, emitted instruction bytes, physical span, four-byte
instruction-cache lines touched, and whether those lines fit without index
conflicts in the 256-byte MC68020/MC68030 instruction-cache model. The same
information is written under the `LOOPS` section of the map output. For
MC68020 output, natural-loop headers are aligned to four-byte boundaries with
padding outside the reported loop range, while other CPU profiles retain their
existing alignment policy. Block layout also keeps transitive
exception/throw-only chains out of hot fallthrough paths. Loops initially
measured within 32 bytes of the 256-byte cache limit additionally use a
size-first MC68020 profitability mode. This permits `PEA (xxx).W` to replace a
larger immediate stack push when flags are dead. Exact, relocation-free
signed-word immediates are narrowed globally for `MOVEA`, `CMPA`, `ADDA`, and
`SUBA`; data-register immediate moves are also narrowed when value analysis
proves that retaining the destination's upper word is equivalent. Remaining
zero-displacement effective addresses are canonicalized from `0(An)` to
`(An)` after higher-level peephole rewrites have reached a fixed point.

## Command line

Install `CopperSharp.Compiler.Cli`, then compile an assembly:

```text
copper68kc Firmware.dll --entry Firmware.Boot::Main --output Firmware.hunk
copper68kc Firmware.dll --entry Firmware.Boot::Main --format asm \
  --runtime application --output Firmware.s
copper68kc Firmware.dll --entry Firmware.Boot::Main --cpu 68040 \
  --format rom --rom-size 524288 --output kick.rom
```

External symbols are fixed at link time. Freestanding and application profiles
default to `ExternalAllocator`, so managed `new` and arrays call
`__c68k_alloc`. ROM profile defaults to `None`, so managed allocation is
rejected unless the request explicitly selects another memory policy.
The application profile also promises that the selected entry is invoked once
and its private image storage is unobservable after return. That contract lets
the compiler remove proven terminal stores. Assembly output otherwise defaults
to the conservative freestanding profile; HUNK output defaults to application.
The Amiga-only `resident` profile is for persistent HUNK or assembly images
whose entry may run concurrently in multiple tasks. Compiler-owned writable
library bases are allocated in a per-invocation record, rather than in the
image, so one invocation cannot clear or replace another's library base. The
default 512-byte stack budget keeps small records on the task stack; larger
records use `Exec.AllocMem` and are freed before return. Set
`M68kCompilationRequest.ResidentStackContextThresholdBytes` to tune that
budget, or to zero to always use Exec memory. It emits no task-wide
`Forbid`/`Permit` guard. Mutable C# static fields, type initialization,
`localloc`, and managed object allocation are rejected in this initial profile;
keep small state in registers or stack locals, or manage larger state explicitly
with `Exec.AllocMem`. Managed heap and managed lifecycle support remain
intentionally unavailable until their state can also be made per-invocation.
It is a code-generation lifetime contract rather than a resident-module
installer; the host must retain the image and invoke its entry again.

```text
--import platform.service=0x00F81234 --import __c68k_alloc=0x00002800
```

For new applications, use the `CopperSharp.Sdk` project SDK: normal build emits
managed IL and publish uses the compiler carried by the restored package. Its
optional `CopperSharpCompileOnBuild=true` hook requires no global tool. The
compiler package's older `Copper68kCompile=true` build target remains only for
projects that reference this package directly and still requires `copper68kc`
on `PATH`; under `CopperSharp.Sdk` those properties are deprecated aliases.

## ABI

Compiled methods use a hybrid private ABI. 32-bit scalar arguments use D0/D1,
address-class arguments (including `this`) use A0/A1, and only excess
arguments use a compact caller-owned stack block. A 64-bit scalar argument uses
D0:D1 when both data registers are free; otherwise the whole value goes on the
stack, high longword first. Scalar results return in D0,
reference and pointer results in A0, and 64-bit results in D0:D1. D0/D1/A0/A1
are volatile; D2-D7/A2-A6 are callee-saved. See
[InternalAbi.md](InternalAbi.md) for the complete classification, overflow,
GC-root, preservation, and adapter contracts.

`[M68kExport]` creates a native-callable adapter. Every exported parameter is
mapped with `[M68kRegister]`; D2-D7 and A2-A6 are preserved. `[M68kImport]`
supports either the private stack ABI or an explicit register ABI:

```csharp
[M68kImport("exec.DoIO")]
[return: M68kRegister(M68kRegister.D0)]
static extern int DoIO(
    [M68kRegister(M68kRegister.A1)] uint request);
```

## Platform extensions

Target-specific external-call metadata is supplied through
`IM68kExternalCallResolver`. The compiler core only understands generic
base-source, register, displacement, and cache descriptions. Amiga library
attributes and LVO semantics live in the optional `CopperSharp.Targets.Amiga`
and `CopperSharp.Sdk.Amiga` packages.

## Supported subset

This overview covers the compiler's broader CIL and output capabilities. The
exact admitted .NET 10 framework surface and its limitations are defined by the
[compatibility profile](Framework/Net10Profile.md) and its machine-readable
manifest. The current JSON manifest declares 305 exact bindings: 158
`Implemented`, 90 `Intrinsic`, and 57 `Platform`, spanning 47 declaring-type
patterns and 23 feature groups. These are manifest-entry counts, not the entire
admitted CoreLib surface; see the
[inventory and admission boundary](Framework/Net10Profile.md#manifest-inventory).

- Signed and unsigned 32-bit integer arithmetic, comparisons, conversions,
  branches, calls, locals, and arguments.
- `Int64`/`UInt64` constants, locals, arguments and returns; register-pair
  managed/import/platform ABIs; widening conversions; unchecked add, subtract
  and multiply; full-width signed/unsigned division and remainder; bitwise
  operations, masked shifts and comparisons; and admitted formatting. Checked
  forms and other conversions remain opcode-specific. See
  [numeric support](Framework/Net10Profile.md#numeric-support).
- Exact .NET 10 `Console.Write`/`WriteLine` bindings for strings, 32/64-bit
  integers, booleans, and characters, plus direct `Console.Read`/`ReadLine` on
  the Amiga application PAL. Input/output use an explicit Latin-1 policy and
  buffered DOS handles; `Console.In`/`Out`/`Error` and encoding objects remain
  unsupported rather than exposing partial reader/writer contracts.
- Exact .NET 10 `File.Exists(string)`, `Directory.Exists(string)`,
  `File.Delete(string)`, non-recursive `Directory.Delete(string)`, and
  `Directory.Move(string,string)`, plus `File.GetAttributes(string)` and
  `File.SetAttributes(string,FileAttributes)` bindings on the independently
  linked Amiga filesystem PAL. Paths use the declared Latin-1 policy;
  existence, deletion, and attributes use AmigaDOS lock/examine semantics,
  while move uses same-volume `Rename` with typed failure translation.
  Attributes map directory/link entry types and Amiga write-denied/archive
  protection to `Directory`, `ReparsePoint`, `ReadOnly`, and `Archive`;
  mutation preserves unmapped Amiga protection bits and ignores defined flags
  that cannot be represented. `File.Move`, recursive deletion, streams,
  enumeration, and other adjacent `System.IO` APIs remain unsupported.
- Exact .NET 10 `Environment.NewLine` and `Environment.ProcessorCount`
  bindings in an independently linked, allocation-free Amiga environment
  group. The declared target values are LF and one available processor;
  adjacent environment/process/OS objects remain unsupported.
- Exact .NET 10 `Stopwatch.GetTimestamp`, `Stopwatch.Frequency`, and
  `Stopwatch.IsHighResolution` bindings backed by the raw Amiga
  `timer.device` E-clock, plus the instance constructor, `Start`, `Stop`,
  `Reset`, `Restart`, `StartNew`, `IsRunning`, `ElapsedTicks`,
  `ElapsedMilliseconds`, `Elapsed`, and the one-/two-timestamp `GetElapsedTime`
  overloads. `ElapsedTicks` retains raw E-clock ticks; elapsed millisecond and
  `TimeSpan` results use explicit target-side scaling. Listed `TimeSpan(long)`,
  `FromTicks`, tick/component/total properties and comparison operators are
  admitted. A verified implementation pack selects pinned CoreLib Stopwatch
  bodies, with `ShadowStopwatch` retained as a fallback. This is not the full
  `TimeSpan` or date/time framework surface.
- `Nullable<T>` locals for 32-bit scalar values and transparent 32-bit structs,
  including null initialization, construction, `HasValue`, `Value`, and
  `GetValueOrDefault`.
- Fields, one-dimensional arrays and object construction for admitted scalar,
  reference and value layouts, including the managed-reference maps required
  by those layouts; hybrid register/stack arguments and UTF-16 string literals.
  Multidimensional arrays and incompatible layouts remain unsupported.
- Classes with single inheritance, statically direct instance calls, and
  vtable-based class virtual dispatch. Base-class instance fields retain their
  inherited offsets, inherited reference fields remain in GC descriptor
  bitmaps, and overrides replace the inherited slot without renumbering later
  slots. Virtual calls use the same hybrid ABI as direct calls.
- Closed-world interface dispatch through per-type interface maps, including
  interface inheritance, implementations inherited from base classes, multiple
  interfaces, and explicit interface implementations. Interface calls use the
  same hybrid ABI as direct calls.
- Closed generic specialization and representation-compatible sharing where
  calling convention, GC shape and observable type identity remain compatible;
  see the [managed runtime](Framework/ManagedRuntime.md#generics).
- MC68000-compatible integer arithmetic helpers, with CPU-specific long
  instructions selected where supported and profitable. Full-width 64-bit
  division/remainder use the bundled integer runtime.
- The audited 103-signature public `StringBuilder` surface for the verified
  Microsoft CoreLib 10.0.9 input, including admitted editing, copying/chunks,
  joins, formatting and handler paths. Exact dependency, caller, callback and
  layout rules apply; general CoreLib ingestion and Decimal arithmetic are not
  implied. See [StringBuilder admission](Framework/FrameworkBinding.md#stringbuilder-implementation-and-admission).
- Table-driven managed exceptions with catch, finally, rethrow, leave, and
  callee-saved restoration during unwind; see
  [ExceptionRuntime.md](ExceptionRuntime.md).
- HUNK executable output with relocations, symbols, and automatic `HUNK_BSS`
  storage for safe zero-initialized static suffixes when the fully serialized
  two-hunk image is strictly smaller; and 256/512 KiB Kickstart ROM output
  with reset vectors and checksum.

Framework identities without an exact binding or applicable audited CoreLib
admission rule, general reflection, arbitrary P/Invoke, and unsupported CIL
opcodes remain fail-closed. Floating point,
boxing, and delegates are available only through their explicitly admitted
compiler/runtime profiles; they are not general CoreCLR-compatible services. Allocation
is supplied by the `__c68k_alloc` import. Optional explicit release can be
supplied through `__c68k_dispose`, exposed through helpers such as
`M68kRuntime.DisposeObject(ref value)` and
`M68kRuntime.DisposeInt32Array(ref value)`; the compiler never calls it
automatically. `M68kMemoryManagement.ManagedPoolMarkSweepGc` emits a built-in
non-compacting pool allocator with startup/shutdown, block splitting, and
explicit dispose reuse over the range carried by `M68kHeapOptions`. Its
explicit collection path marks compiler-known static and current-frame roots,
iteratively traces object reference fields and reference-array elements from
descriptor metadata, sweeps unmarked allocated blocks, and coalesces adjacent
free blocks.
`ExecPoolMarkSweepGc` remains a possible Amiga-backed alternative.
`M68kGcSweepStrategy` selects whether collection is explicit only, retried on
allocation failure, run before every allocation, or triggered from approximate
stale-pressure telemetry. Compiler-emitted allocation-site collection marks the
current frame and typed managed-reference evaluation-stack slots before
sweeping.
`M68kGcTelemetryOptions` carries the
thresholds for that telemetry mode. Once-only type initialization includes
failure caching. Interface dispatch and constructed generic layouts must match
the closed-world contracts in the [managed runtime](Framework/ManagedRuntime.md);
arbitrary runtime generic construction and full runtime generic dictionaries
are not implied. Unsupported interface forms are reported with `C68K0011`.
