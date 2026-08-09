# CopperSharpAmiga

This project builds a managed .NET 10 assembly and then compiles its declared
entry point into a Motorola 68k Amiga HUNK executable.

Build the managed input:

```text
dotnet build
```

Publish the Amiga HUNK without installing a global compiler tool:

```text
dotnet publish -r amiga-m68k
```

The HUNK, `.map`, and `.framework.json` files are written to the standard
publish directory. The sample opens `dos.library`, writes a line, and closes
the library before returning.
