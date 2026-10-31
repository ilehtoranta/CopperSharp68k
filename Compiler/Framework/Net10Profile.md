# CopperSharp68k .NET 10 compatibility profile

## Purpose

The profile lets developers compile a useful, deliberately bounded subset of ordinary .NET 10 code for classic Amiga systems. Application assemblies target `net10.0` and compile against Microsoft's reference assemblies. CopperSharp68k then resolves the reachable program, verifies every reachable framework dependency against an exact contract, and emits 68k-native output.

Compatibility means semantic compatibility for the admitted surface, not binary compatibility with CoreCLR and not availability of the entire base class library.

This document describes the current `main` source build, reviewed on 31 October
2026. The package coordinate below is not a claim that every current source
capability is present in an older published preview package. Inspect the exact
restored inputs and the compatibility/provenance report from your own build.

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
| Primary outputs | Amiga HUNK application, assembler text, 256/512 KiB ROM image |

These axes are versioned independently. A successful compile records the exact coordinate and input hashes in its provenance output.

## Compatibility boundary

The compiler uses closed-world reachability. Only members reachable from the selected roots are required, and only reachable runtime helpers, shadows, metadata, strings, platform services, and static data are emitted. An unused supported feature has no code or data cost.

Every reachable framework member is classified by exact structural identity as one of:

- managed implementation body;
- private managed shadow;
- compiler intrinsic;
- platform abstraction layer operation; or
- unsupported.

### Manifest inventory

The current [`net10.0-10.0.9.json`](net10.0-10.0.9.json) inventory is:

| Manifest measure | Count |
|---|---:|
| Exact binding entries | 305 |
| `Implemented` (managed/shadow) | 158 |
| `Intrinsic` | 90 |
| `Platform` | 57 |
| Distinct declaring-type patterns | 47 |
| Feature groups | 23 |

The status counts sum to the binding-entry count. Type patterns can contain
generic wildcards; they are not a count of all supported framework types.
Feature groups are the distinct feature names used by these entries. The JSON
manifest is authoritative if these documentation counts diverge.

This inventory is not the total number of callable CoreLib APIs. The executable
registry also has audited, input-specific admission rules, including the
[103-signature StringBuilder surface](FrameworkBinding.md#stringbuilder-implementation-and-admission).
Those rules verify the released input identity/hash, exact member and caller,
closed payload/layout, and declared effects as applicable. Their public and
private dependencies must not be added to the JSON counts as an independent,
disjoint API total. Stable StringBuilder admission does not require
`EnableUnlistedManagedBodies`; the unlisted-body experiment does not authorize
general CoreLib reuse.

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

The exact manifest bindings and audited registry rules define admission. At a design level, the profile includes:

- core object, value-type, enum, exception, array, and primitive operations;
- managed strings, ordinal search/comparison, concatenation, slicing, character copying, and selected invariant formatting;
- boxing, unboxing, delegates, selected generic specialization, managed byrefs, arrays, spans, and memory views;
- selected `List<T>`, `Dictionary<TKey,TValue>`, and provenance-directed LINQ operations;
- selected math, bit conversion, runtime helpers, and low-level compiler intrinsics;
- admitted 64-bit arithmetic/comparisons and opt-in software/native floating point;
- the verified-input StringBuilder surface and its audited formatting/join dependencies;
- Amiga console, filesystem, environment, and monotonic clock services;
- listed Stopwatch elapsed operations and TimeSpan construction, properties and comparisons;
- application, freestanding assembler, and ROM-oriented output profiles.

Some nearby members intentionally remain absent even when their declaring type
is supported. Notable gaps include console stream redirection and encoding
surfaces, directory creation and recursive deletion, cross-volume file moves,
and date/time members outside the exact admitted forms. Listed Stopwatch
elapsed conversions and TimeSpan members are supported; they do not imply the
entire date/time framework.

### Numeric support

`Int64`/`UInt64` values use high:low longword pairs for admitted locals, calls
and results. The backend supports unchecked addition, subtraction and
multiplication, signed/unsigned comparisons and branches, bitwise operations,
and shifts whose counts are masked to six bits. Full-width signed/unsigned
division and remainder lower to the bundled integer runtime. With managed
exception handling enabled, division by zero raises `DivideByZeroException`;
signed `long.MinValue / -1` raises
`OverflowException`, while `long.MinValue % -1` returns zero. Checked arithmetic
and conversion shapes require their own opcode support; unchecked operations
do not imply all checked forms.

Floating point is opt-in. `Disabled` rejects floating values. `SoftFloat`
supplies integer-only single/double add, subtract, multiply, divide, remainder,
negation, IEEE comparisons and admitted numeric conversions, without an FPU.
The `M68882` mode requires a 68020 host; `M68040` uses the integrated FPU on
68040/68060 targets for admitted native arithmetic. Comparisons, negation and
numeric conversions use shared runtime lowering in these modes. Native
floating-point remainder remains unsupported. Values retain canonical IEEE
bits in the private register/stack ABI in either software or native mode.

Framework math remains member-specific. The manifest includes selected
integral and Single/Double `Abs`, `Min`, `Max`, `Clamp`, `Sign`, `BigMul`,
`CopySign`, rounding and `Sqrt` forms, plus listed IEEE classifiers and bit
operations. This does not admit general `MathF`, transcendental functions or
Decimal arithmetic. Audited Decimal/floating formatting has its own
verified-input and layout rules.

The lowering boundary is in
[`CompilationModule.Numeric.cs`](../Metadata/CompilationModule.Numeric.cs).
Independent contracts include
[`Integer64ArithmeticTests`](../../Compiler.Tests/Integer64ArithmeticTests.cs),
[`LongPairComparisonExecutionTests`](../../Compiler.Tests/LongPairComparisonExecutionTests.cs),
[`FloatingPointArithmeticTests`](../../Compiler.Tests/FloatingPointArithmeticTests.cs),
[`FloatingPointConversionTests`](../../Compiler.Tests/FloatingPointConversionTests.cs),
and [`NumericLoweringTests`](../../Compiler.Tests/NumericLoweringTests.cs).
See the [internal ABI](../InternalAbi.md) for register pairs and aggregate transport.

### Clock and TimeSpan boundary

Stopwatch uses the target's `timer.device` E-clock and device-reported frequency.
The admitted surface includes the instance state machine, raw `ElapsedTicks`,
scaled `ElapsedMilliseconds` and `Elapsed`, and both `GetElapsedTime` overloads.
Listed TimeSpan members include tick construction/`FromTicks`, `Ticks`, day
through millisecond components/totals, and comparison operators. Floating totals
require an admitted floating-point mode. Pinned CoreLib Stopwatch bodies are
preferred when a verified implementation pack is supplied; `ShadowStopwatch`
remains an explicit fallback. See the [Amiga PAL](AmigaPal.md#clock-and-stopwatch)
and the exact manifest for signatures and dependencies.

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

A reachable identity without an exact binding or applicable audited admission
rule is unsupported. The compiler rejects it during compatibility analysis
rather than silently substituting a similar member or falling back to arbitrary
host CoreLib bodies. Compiler-known type identity/name rendering does not
provide general reflection.

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
