# Copper68k.Compiler.Amiga

Optional Amiga target integration for `Copper68k.Compiler`. It resolves
`[AmigaLibrary]` and `[AmigaLvo]` metadata into generic compiler platform calls.

```csharp
var result = AmigaM68kCompiler.Compile(request, new AmigaCompilationOptions
{
    LibraryBases = new Dictionary<string, uint>
    {
        ["dos.library"] = dosBase
    }
});
```

ExecBase is read from address 4 once at each native entry boundary and cached
in A5. Library vectors use A6 and `jsr d16(a6)`. Cached library bases receive a
writable published symbol from `AmigaLibraryBaseSymbols.For(name)`.
