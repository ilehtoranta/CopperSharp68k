# CopperSharp.Compiler

`CopperSharp.Compiler` is an optional closed-world CIL-to-68k ahead-of-time
compiler. It emits Amiga loadable HUNK executables and 256/512 KiB Kickstart ROM
images, plus portable assembler text, for MC68000, MC68020, MC68040, and MC68060 targets.

The compiler package is deliberately separate from the `Copper68k` emulator.
Installing or using the emulator does not pull in the compiler.

## Floating point

Floating point is opt-in through `M68kCompilationRequest.FloatingPoint`; the
default `Disabled` mode preserves the integer-only behavior. `M68040` targets
the integrated FPU on 68040/68060, while `M68882` targets an external
coprocessor with a 68020 host. The native backend supports exact IEEE
single/double constants plus add, subtract, multiply, divide, and negation.
For MC68040, double-precision operations reuse one 8-byte `(a7)` scratch slot
for both operands and the result, avoiding repeated stack writeback chains.
For MC68060, double-precision operations use one shared stack scratch window so
operand loads and result stores do not repeatedly adjust A7; single-precision
operations remain direct D-register-to-FPU transfers.

`SoftFloat` reserves the raw-bit software ABI. Operations requiring the bundled
CopperFloat target runtime currently produce an explicit unsupported-operation
diagnostic rather than emitting native FPU instructions.

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

```text
--import platform.service=0x00F81234 --import __c68k_alloc=0x00002800
```

The package also contains an opt-in MSBuild target. Set
`Copper68kCompile=true`, `Copper68kEntry`, and ensure `copper68kc` is available
on `PATH`.

## ABI

Compiled methods use a hybrid private ABI. 32-bit scalar arguments use D0/D1,
address-class arguments (including `this`) use A0/A1, and only excess
arguments use a compact caller-owned stack block. Scalar results return in D0,
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

- Signed and unsigned 32-bit integer arithmetic, comparisons, conversions,
  branches, calls, locals, and arguments.
- 64-bit integer constants, locals, returns, and register-pair import/platform
  call parameters/results. 64-bit arithmetic and comparisons are still rejected.
- `Nullable<T>` locals for 32-bit scalar values and transparent 32-bit structs,
  including null initialization, construction, `HasValue`, `Value`, and
  `GetValueOrDefault`.
- Static and instance 32-bit fields, object construction with hybrid
  register/stack arguments, UTF-16 string literals, and
  one-, two-, and four-byte scalar arrays plus reference arrays.
- Classes with single inheritance, statically direct instance calls, and
  vtable-based class virtual dispatch. Base-class instance fields retain their
  inherited offsets, inherited reference fields remain in GC descriptor
  bitmaps, and overrides replace the inherited slot without renumbering later
  slots. Virtual calls use the same hybrid ABI as direct calls.
- Closed-world interface dispatch through per-type interface maps, including
  interface inheritance, implementations inherited from base classes, multiple
  interfaces, and explicit interface implementations. Interface calls use the
  same hybrid ABI as direct calls.
- Shared generic method bodies when every generic value uses the same
  four-byte scalar/reference representation.
- MC68000-compatible software long multiply/divide, with MC68020/MC68040/MC68060 long
  arithmetic instructions selected for those targets.
- Table-driven managed exceptions with catch, finally, rethrow, leave, and
  callee-saved restoration during unwind; see
  [ExceptionRuntime.md](ExceptionRuntime.md).
- HUNK executable output with relocations and symbols, and 256/512 KiB
  Kickstart ROM output with reset vectors and checksum.

This preview intentionally rejects floating point, general 64-bit arithmetic,
boxing, delegates, reflection, P/Invoke, and unsupported CIL opcodes. Allocation
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
thresholds for that telemetry mode. Default interface methods, generic
interfaces, cross-module interface maps, type initializers, and full generic
dictionaries are not part of this first package version. Unsupported interface
forms are reported with `C68K0011`.
