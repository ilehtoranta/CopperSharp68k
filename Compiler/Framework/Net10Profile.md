# CopperSharp68k .NET 10 compatibility profile

## Purpose

The profile lets developers compile a useful, deliberately bounded subset of ordinary .NET 10 code for classic Amiga systems. Application assemblies target `net10.0` and compile against Microsoft's reference assemblies. CopperSharp68k then resolves the reachable program, verifies every reachable framework dependency against an exact contract, and emits 68k-native output.

Compatibility means semantic compatibility for the admitted surface, not binary compatibility with CoreCLR and not availability of the entire base class library.

## Current profile coordinate

| Axis | Current value |
|---|---|
| Target framework | `net10.0` |
| Reference pack | `Microsoft.NETCore.App.Ref` 10.0.9 |
| Contract schema | 1 |
| Implementation-manifest schema | 1 |
| Implementation profile | `corelib-common-il-v1` |
| SDK package line | `0.1.0-preview.1` |
| Runtime identifier | `amiga-m68k` |
| CPU targets | MC68000, MC68020, MC68040, MC68060 |
| Primary outputs | Amiga HUNK application, freestanding assembler, 512 KiB ROM image |

These axes are versioned independently. A successful compile records the exact coordinate and input hashes in its provenance output.

## Compatibility boundary

The compiler uses closed-world reachability. Only members reachable from the selected roots are required, and only reachable runtime helpers, shadows, metadata, strings, platform services, and static data are emitted. An unused supported feature has no code or data cost.

Every reachable framework member is classified by exact structural identity as one of:

- managed implementation body;
- private managed shadow;
- compiler intrinsic;
- platform abstraction layer operation; or
- unsupported.

The current contract manifest contains 283 exact bindings: 154 managed or shadow bindings, 84 intrinsics, and 45 platform bindings, organized into 22 feature groups. These counts describe the current package coordinate; the JSON manifest remains authoritative if documentation and implementation ever diverge.

## Public type identity

Programs continue to reference official `System.*` identities from the .NET 10 reference pack. CopperSharp68k does not ship a public replacement `System.Private.CoreLib` or ask applications to compile against look-alike framework types.

Private shadow types are implementation details. The compiler may redirect an admitted official member to a shadow body, but public metadata identity, virtual dispatch identity, exception identity, and compatibility diagnostics remain expressed in terms of the official framework contract.

## Semantic promise

For an admitted member, the implementation aims to preserve observable .NET behavior within the documented platform limits:

- evaluation order, exceptions, bounds checks, integer overflow rules, and null behavior;
- UTF-16 managed strings and ordinal string operations;
- stable object and type identity;
- delegate invocation order and collection enumeration rules;
- once-only static initialization, including failure caching;
- exact cleanup and ownership behavior at managed/native boundaries.

Native representations and specialized lowering are allowed when they are not observable. Hot paths such as integer formatting, span traversal, collection loops, console writes, and clock reads should remain allocation-free where the managed contract permits it.

## Supported areas

The exact list is in the contract manifest. At a design level, the profile includes:

- core object, value-type, enum, exception, array, and primitive operations;
- managed strings, ordinal search/comparison, concatenation, slicing, character copying, and selected invariant formatting;
- boxing, unboxing, delegates, selected generic specialization, managed byrefs, arrays, spans, and memory views;
- selected `List<T>`, `Dictionary<TKey,TValue>`, and provenance-directed LINQ operations;
- selected math, bit conversion, runtime helpers, and low-level compiler intrinsics;
- Amiga console, filesystem, environment, and monotonic clock services;
- application, freestanding assembler, and ROM-oriented output profiles.

Some nearby members intentionally remain absent even when their declaring type is supported. Notable current gaps include console stream redirection and encoding surfaces, directory creation and recursive deletion, cross-volume file moves, and `Stopwatch` members that require a complete `TimeSpan` scaling surface.

## Explicit exclusions

The profile does not currently admit:

- `System.Reflection.Emit.*`;
- dynamic assembly loading and `AssemblyLoadContext`;
- COM and `System.Runtime.InteropServices.ComTypes.*`;
- desktop stacks such as GDI+, Windows Forms, and WPF;
- arbitrary platform invocation or host-native library discovery;
- runtime code generation, CoreCLR hosting, or JIT compilation;
- culture-sensitive behavior for which no compatible globalization service is provided;
- framework members whose required operating-system primitive has no Amiga PAL implementation.

Unlisted identities are unsupported. The compiler must reject them during compatibility analysis rather than silently substituting a similar member or failing later in code generation.

## Diagnostics

An unsupported reachable member produces a deterministic compatibility diagnostic containing:

- the exact structural member identity;
- the calling method and IL offset;
- the rejection reason;
- a shortest reachable path from an application root; and
- the active profile coordinate.

Analysis also rejects ambiguous framework provenance, mismatched reference/implementation packs, unsupported managed-byref escape, incompatible generic layouts, and unsupported output-profile dependencies.

## Performance contract

The design is intended to stay efficient on 68k hardware:

- framework analysis happens at build time, not on the target;
- emitted support code is reachability-trimmed;
- exact bindings avoid reflection and runtime lookup tables;
- intrinsics remain available for semantics that map directly to machine operations;
- private managed implementations are used where ordinary IL optimizes well;
- platform operations cross a narrow PAL boundary;
- managed/native string conversion is explicit and scoped;
- allocation, code size, stack use, spills, and cycle estimates are regression-tested.

This is a hybrid implementation, not a wholesale shadow runtime. A complete shadow .NET library would increase semantic drift, maintenance cost, and target footprint without improving ordinary project compatibility.

## Related specifications

- [Framework binding](FrameworkBinding.md)
- [Managed runtime](ManagedRuntime.md)
- [Amiga PAL](AmigaPal.md)
- [SDK and publishing](SdkPublishing.md)
- [Compatibility testing](CompatibilityTesting.md)
- [Servicing policy](Net10ServicingPolicy.md)
