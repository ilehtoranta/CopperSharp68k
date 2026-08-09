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

Ordinary `dotnet build` intentionally emits only managed IL. Set
`CopperSharpCompileOnBuild=true` to opt into a separate target artifact under
the build output directory. The former `Copper68k*` properties route through
this packaged compiler for one compatibility release and emit warning
`C68KSDK001`; raw additional arguments are rejected in favor of typed
properties.

```xml
<Project Sdk="Microsoft.NET.Sdk;CopperSharp.Sdk/0.1.0-preview.1">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifier>amiga-m68k</RuntimeIdentifier>
  </PropertyGroup>
</Project>
```
