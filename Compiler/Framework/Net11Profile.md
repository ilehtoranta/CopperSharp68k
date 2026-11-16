# .NET 11 profile

CopperSharp accepts applications targeting `net11.0` with the .NET 11.0.0
reference pack. The compiler selects this contract through
`M68kCompilationRequest.FrameworkTargetFramework` or the CLI's
`--framework-target-framework net11.0` option. `CopperSharp.Sdk` supplies the
project's `TargetFramework` automatically.

The profile uses the .NET 11 reference-pack assembly inventory and the same
305 exact binding rules as the audited .NET 10 profile. A reachable member is
accepted only when it matches one of those exact rules or is supported by the
compiler's existing managed implementation path. Newly introduced .NET 11
members do not become supported just because they appear in the reference
pack.

The machine-readable contract is
[`net11.0-11.0.0.json`](net11.0-11.0.0.json). It records target framework
`net11.0`, `Microsoft.NETCore.App.Ref` version `11.0.0`, and the .NET 11
reference-pack assembly inventory. .NET 11-specific binding additions and
implementation-pack admission require their own compatibility and execution
qualification before they are added.
