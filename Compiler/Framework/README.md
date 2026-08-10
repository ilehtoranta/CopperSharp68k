# .NET 10 profile documentation

CopperSharp68k accepts ordinary `net10.0` assemblies, analyzes their reachable code as a closed world, and ahead-of-time compiles the supported subset for Motorola 68k targets. It does not embed CoreCLR, load framework assemblies on the Amiga, or attempt to reproduce every .NET runtime service.

The documentation is organized by stable responsibility rather than implementation phase:

- [Profile and compatibility contract](Net10Profile.md) — target identity, supported surface, exclusions, and compatibility rules.
- [Framework binding](FrameworkBinding.md) — how official .NET identities bind to managed IL, compiler intrinsics, private shadows, or platform services.
- [Managed runtime](ManagedRuntime.md) — object layout, type identity, boxing, delegates, generics, byrefs, strings, and collections.
- [Amiga platform abstraction layer](AmigaPal.md) — console, filesystem, environment, and clock services.
- [SDK and publishing](SdkPublishing.md) — project setup, restore/build/publish behavior, packages, RID policy, and output provenance.
- [Compatibility testing](CompatibilityTesting.md) — executable contracts, baselines, corpus checks, and performance policy.
- [Servicing policy](Net10ServicingPolicy.md) — version axes and rules for evolving the profile.
- [Implementation history](ImplementationHistory.md) — a concise record of the completed implementation stages and major corrections.

Related runtime specifications remain separate because they are useful beyond the .NET profile:

- [Internal compiler ABI](../InternalAbi.md)
- [Runtime memory model](../RuntimeMemory.md)
- [Exception runtime](../ExceptionRuntime.md)

## Sources of truth

The exact supported member set is machine-readable in [`net10.0-10.0.9.json`](net10.0-10.0.9.json). Documentation describes the policy and architecture; the manifest, implementation-pack manifest, compiler validation, and tests decide whether a particular member is accepted.

Compatibility corpus expectations live in [`net10.0-phase11-ledger.json`](../../Compiler.Tests/Baselines/net10.0-phase11-ledger.json). The historical filename is retained because tests and release evidence refer to it; it does not mean that the profile is still a work in progress.
