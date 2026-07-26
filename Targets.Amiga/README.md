# CopperSharp.Targets.Amiga

Optional Amiga target integration for `CopperSharp.Compiler`. It resolves
`[AmigaLibrary]` and `[AmigaLvo]` metadata into generic compiler platform calls.

```csharp
var result = AmigaM68kCompiler.Compile(request, new AmigaCompilationOptions
{
    LibraryBasePolicies = new Dictionary<string, AmigaLibraryBasePolicy>
    {
        ["dos.library"] = AmigaLibraryBasePolicy.Provided
    },
    LibraryBases = new Dictionary<string, uint>
    {
        ["dos.library"] = dosBase
    }
});
```

ExecBase is read from address 4 once at each native entry boundary and cached
in A5. Library vectors use A6 and `jsr d16(a6)`. SDK library declarations do
not choose manual or automatic opening by themselves. Manual library bases
receive a writable C-style published symbol such as `_DOSLibraryBase`, returned
by `AmigaLibraryBaseSymbols.For(name)`. Provided bases are linked as immediate
base addresses. `AutoOpen` is reserved for generated startup code.

When `AutoOpen` is selected, the Amiga target runs a metadata analyzer before
code generation. It rejects calls to auto-open libraries from static
initialization paths, because generated startup has not opened those bases yet.
