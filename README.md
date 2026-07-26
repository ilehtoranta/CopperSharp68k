# CopperSharp68k

CopperSharp68k is a closed-world CIL-to-Motorola 68000-family ahead-of-time
compiler for C# and other .NET languages.

The current target emits 68k code with Amiga HUNK executable, Kickstart ROM, and
assembler output support. Amiga platform bindings and library-call resolution
live in the SDK and Amiga integration projects; the compiler core is kept
separate so additional targets such as MorphOS/PPC can be added later.

## Projects

- `Copper68k.Compiler` - compiler core, 68k backend, HUNK/ROM/assembly output.
- `Copper68k.Compiler.Tool` - command-line driver.
- `Copper68k.Compiler.Amiga` - Amiga library-call resolver and platform helpers.
- `Copper68k.AmigaSdk` - Amiga ABI declarations for compiled code.
- `Copper68k.Compiler.Tests` - compiler tests. These use a sibling
  `MedPlayer/Copper68k` checkout when available, otherwise the `Copper68k`
  package.

## Build

```powershell
dotnet build CopperSharp68k.slnx
dotnet test CopperSharp68k.slnx
```
