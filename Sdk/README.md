# CopperSharp.Sdk

MSBuild project SDK for publishing ordinary .NET 10 C# applications through
CopperSharp to Motorola 68k Amiga output.

The SDK supplies the `amiga-m68k` runtime graph, rejects host-runtime publish
modes that cannot describe a CopperSharp image, and carries the compiler host
used by `dotnet publish`. No global compiler tool is required.

Publish writes the target artifact, a `.map` file with exact compiler,
framework-contract, target-package, profile, CPU, and format provenance, and a
`.framework.json` compatibility report from the same closed-world analysis.
An unchanged second publish skips target compilation.

Set `CopperSharpSymbols=off` to omit embedded `HUNK_SYMBOL` records from HUNK
executables. Both publish and opt-in target builds support this property; the
default remains `on`. The separate `.map` file retains method names and
addresses. This reduces the executable's file size without changing generated
instructions, data, exception handling, or memory management.

```powershell
dotnet publish -c Release -p:CopperSharpSymbols=off
```

Set `CopperSharpCodeSizeOptimizations=on` to enable the opt-in code-size policy
for build and publish (default: `off`). MC68000 application HUNKs use shorter
local address loads, shared immutable unwind/GC records and smaller unwind
routines. Applications without a collector also use compact relative exception
tables, with 16-bit fields when every final offset fits and automatic 32-bit
fallback otherwise. Unreferenced unwind records are omitted; restore routines
can share code across frame sizes without changing the descriptor or GC ABI.
Eligible methods also remove redundant register moves and share identical
return sequences within the method. Methods with exception handlers or dynamic
stack allocation are excluded from these two passes; when a collector can run,
methods with GC references or managed byrefs are excluded too. Exception handling
and memory-management settings remain unchanged.

```powershell
dotnet publish -c Release -p:CopperSharpSymbols=off -p:CopperSharpCodeSizeOptimizations=on
```

To apply it only to Release configurations in a project:

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <CopperSharpSymbols>off</CopperSharpSymbols>
</PropertyGroup>
```

Ordinary `dotnet build` intentionally emits only managed IL. Set
`CopperSharpCompileOnBuild=true` to opt into a separate target artifact under
the build output directory. The former `Copper68k*` properties route through
this packaged compiler for one compatibility release and emit warning
`C68KSDK001`; raw additional arguments are rejected in favor of typed
properties.

To consume compiler-approved bodies from a pinned CoreLib, set
`CopperSharpFrameworkImplementationManifest` to an explicit JSON manifest.
The SDK passes that same file to build and publish and tracks it as an
incremental input. CopperSharp never discovers or bundles an installed host
runtime; the manifest's assembly SHA-256, MVID, and metadata identity define the
input. Version 1 requires profile `corelib-common-il-v1`, `net10.0`, reference
pack `Microsoft.NETCore.App.Ref` version `10.0.9`, and exactly one relative
`System.Private.CoreLib.dll` artifact.

For the supported Stopwatch instance slice, a verified pinned pack is the
recommended configuration: it uses the official CoreLib bodies and currently
produces smaller, faster output than the target shadow in the compiler's
cross-CPU benchmark. When this property is omitted, CopperSharp deliberately
uses `ShadowStopwatch` as its deterministic fallback; it never discovers an
installed host runtime implicitly.

```json
{
  "schemaVersion": 1,
  "packId": "my.corelib.pack",
  "packVersion": "10.0.9",
  "runtimeIdentifier": "source-runtime-rid",
  "targetFramework": "net10.0",
  "referencePack": "Microsoft.NETCore.App.Ref",
  "referencePackVersion": "10.0.9",
  "implementationProfile": "corelib-common-il-v1",
  "assemblies": [{
    "name": "System.Private.CoreLib",
    "file": "System.Private.CoreLib.dll",
    "version": "10.0.0.0",
    "publicKeyToken": "7cec85d7bea7798e",
    "mvid": "00000000-0000-0000-0000-000000000000",
    "sha256": "64-lowercase-hex-characters"
  }]
}
```

The identity and fingerprint values in this illustrative manifest must be
replaced with values read from the exact assembly being supplied. The manifest
describes artifacts only; it cannot authorize additional framework members or
target substitutions.

```xml
<Project Sdk="Microsoft.NET.Sdk;CopperSharp.Sdk/0.1.0-preview.1">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifier>amiga-m68k</RuntimeIdentifier>
  </PropertyGroup>
</Project>
```
