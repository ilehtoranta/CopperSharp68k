# CopperSharp68k

CopperSharp68k is a closed-world CIL-to-Motorola 68000-family ahead-of-time
compiler for C# and other .NET languages.

The current target emits 68k code with Amiga HUNK executable, Kickstart ROM, and
assembler output support. Amiga platform bindings and library-call resolution
live in the SDK and Amiga integration projects; the compiler core is kept
separate so additional targets such as MorphOS/PPC can be added later.

## Projects

- `Compiler` - compiler core, 68k backend, HUNK/ROM/assembly output.
- `Compiler.Cli` - command-line driver.
- `Targets.Amiga` - Amiga library-call resolver and platform helpers.
- `Sdk.Amiga` - Amiga ABI declarations for compiled code.
- `Compiler.Tests` - compiler tests. These use a sibling
  `MedPlayer/Copper68k` checkout when available, otherwise the `Copper68k`
  package.

## Build

```powershell
dotnet build CopperSharp68k.slnx
dotnet test CopperSharp68k.slnx
```
