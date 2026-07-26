# Copper68k.Compiler

`Copper68k.Compiler` is an optional closed-world CIL-to-68k ahead-of-time
compiler. It emits Amiga loadable HUNK executables and 256/512 KiB Kickstart ROM
images, plus portable assembler text, for MC68000, MC68020, and MC68040 targets.

The compiler package is deliberately separate from the `Copper68k` emulator.
Installing or using the emulator does not pull in the compiler.

## Library API

```csharp
var result = M68kCompiler.Compile(new M68kCompilationRequest
{
    AssemblyPath = "Firmware.dll",
    EntryPoint = "Firmware.Boot::Main",
    Cpu = M68kCpuTarget.M68000,
    OutputFormat = M68kOutputFormat.KickstartRom,
    Rom = new KickstartRomOutputOptions
    {
        Size = 512 * 1024,
        InitialStackPointer = 0x0008_0000
    }
});

File.WriteAllBytes("kick.rom", result.Image);
```

The compiler accepts a deliberately bounded, freestanding subset of CIL. Any
reachable unsupported instruction or type is reported with a stable `C68Kxxxx`
diagnostic rather than silently changing its semantics.

## Command line

Install `Copper68k.Compiler.Tool`, then compile an assembly:

```text
copper68kc Firmware.dll --entry Firmware.Boot::Main --output Firmware.hunk
copper68kc Firmware.dll --entry Firmware.Boot::Main --format asm --output Firmware.s
copper68kc Firmware.dll --entry Firmware.Boot::Main --cpu 68040 \
  --format rom --rom-size 524288 --output kick.rom
```

External symbols are fixed at link time:

```text
--import platform.service=0x00F81234 --import __c68k_alloc=0x00002800
```

The package also contains an opt-in MSBuild target. Set
`Copper68kCompile=true`, `Copper68kEntry`, and ensure `copper68kc` is available
on `PATH`.

## ABI

Compiled methods use a private fast ABI when the complete argument list fits:
32-bit scalar arguments use D0/D1, references (including `this`) use A0/A1,
scalar results return in D0, and reference results return in A0. D0/D1/A0/A1
are caller-saved. Larger and shared-generic signatures use the stack ABI.
Frames, register-argument homes, locals, and the CIL evaluation stack are
addressed relative to A7.

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
attributes and LVO semantics live in the optional `Copper68k.Compiler.Amiga`
and `Copper68k.AmigaSdk` packages.

## Supported subset

- Signed and unsigned 32-bit integer arithmetic, comparisons, conversions,
  branches, calls, locals, and arguments.
- Static and instance 32-bit fields, parameterless object construction,
  UTF-16 string literals, and four-byte scalar/reference arrays.
- Shared generic method bodies when every generic value uses the same
  four-byte scalar/reference representation.
- MC68000-compatible software long multiply/divide, with MC68020/MC68040 long
  arithmetic instructions selected for those targets.
- HUNK executable output with relocations and symbols, and 256/512 KiB
  Kickstart ROM output with reset vectors and checksum.

This preview intentionally rejects floating point, 64-bit values, exceptions,
boxing, delegates, reflection, P/Invoke, and unsupported CIL opcodes. Allocation
is supplied by the `__c68k_alloc` import; a tracing garbage collector, virtual
and interface dispatch, type initializers, and full generic dictionaries are
not part of this first package version.
