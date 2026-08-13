# Framework binding and implementation packs

## Overview

CopperSharp68k compiles against official .NET reference identities but supplies target implementations through an explicit binding registry. This keeps source and package compatibility separate from the implementation strategy used on the Amiga.

The binding registry is the sole authority for framework-member resolution. Backends must not route calls by display name, namespace prefix, token coincidence, or heuristic signature matching.

## Exact identity

A framework member is identified structurally by its declaring assembly identity, declaring type, member name, generic arity, calling convention, return type, parameter types, and relevant modifiers. Type forwarders and implementation-pack mappings are resolved before the binding is selected.

Each binding declares:

- its exact public identity;
- its implementation kind and implementation target;
- feature group;
- allocation, throwing, safepoint, and platform effects;
- output-profile restrictions; and
- any required runtime helpers or metadata.

The compiler validates agreement between the contract manifest and executable binding registry. Missing, duplicate, or conflicting entries are build errors.

## Binding kinds

### Managed body

The selected implementation body comes from a verified implementation assembly. The body is analyzed and compiled like application IL, subject to the same CIL and runtime restrictions.

### Private shadow

The public framework identity is redirected to a private implementation method. A shadow binding names its implementation assembly, type, and method explicitly. Shadow methods are not roots by themselves; they become reachable only through a reachable public binding.

Shadows are useful when the official implementation depends on unavailable runtime machinery, when a smaller target-specific algorithm is materially better, or as a verified fallback. They must be covered by semantic tests against the public contract.

### Intrinsic

The compiler lowers the operation directly when its semantics naturally belong in the compiler or runtime ABI. Examples include selected object, array, numeric, memory, and type operations. Intrinsics still carry explicit effects and participate in reachability and compatibility reporting.

### Platform operation

The public member binds to a PAL operation with an explicit output-profile implementation. Platform bindings are limited to services such as console I/O, filesystem access, environment values, and clocks; the framework layer does not call Amiga SDK declarations directly.

### Unsupported

Unsupported entries provide a stable diagnostic reason. Any unlisted reachable identity is also unsupported. Rejection occurs before backend lowering.

## Official implementation packs

Where suitable, CopperSharp68k can compile IL from pinned Microsoft implementation assemblies. It never implicitly reads the developer machine's installed runtime. An implementation pack is accepted only when its manifest matches the requested profile and every input assembly has the expected identity and content hash.

The implementation-pack manifest records:

- schema and profile identifiers;
- reference-pack coordinate;
- implementation assembly identities and hashes;
- permitted public-to-implementation mappings;
- package version and provenance.

This makes managed implementation reuse reproducible and auditable. A servicing update cannot silently replace method bodies merely because a newer runtime is installed on the build host.

## Bounded unlisted-body ingestion spike

An August 2026 architectural spike tested whether a verified CoreLib pack could replace the per-member managed-body allowlist. The spike resolves an arbitrary official framework identity to the matching `System.Private.CoreLib` definition, constructs closed generic declaring types and generic methods from the application arguments, and recursively admits only reachable IL. Compiler intrinsics and required PAL bindings retain precedence. The experiment is isolated behind an internal test switch; normal implementation-pack behavior is unchanged.

Two probes were used without adding `StringBuilder` or `List<T>` binding rules:

- `StringBuilder.Append(int)`, followed by length and character checks;
- construction and property access on `List<int>`.

Both probes crossed the facade-to-CoreLib boundary and entered closed generic CoreLib bodies. A follow-up dispatch gate added direct resolution for primitive constrained receivers, default interface bodies, constructed generic interface methods, and the `constrained.` plus `call` form used by static interface members. Static concrete interface bodies are treated as direct calls rather than runtime interface-table slots. These changes let both probes pass the original numeric-formatting and generic-comparison boundary, including CoreLib's static generic conversion methods and constrained `List<T>.Enumerator.Dispose` calls.

Before introducing a target-runtime cut point, both probes stopped at the same substantially later boundary: `System.Exception.ToString()` reached stack-trace and runtime-type helpers, expanding into the reflection cache graph where `RuntimeTypeCache.MemberInfoCache<RuntimeConstructorInfo>` contains the unsupported `CerHashtable<string, RuntimeConstructorInfo[]>` layout. This is useful negative evidence for efficiency: unrestricted official-body ingestion remains closed-world, but ordinary validation and formatting paths can import a large runtime subsystem unless target-specific cut points replace those dependencies.

The next bounded gate introduced exactly one experimental cut point. While unlisted-body ingestion is enabled, the exact `System.Exception.ToString(): string` identity resolves to `CopperSharp.Runtime.ShadowException.ToString()`, which returns the compact target-owned text `System.Exception`. The override is applied consistently during framework reachability, static analysis, and final code discovery, and it remains virtual-dispatch capable. A dedicated M68000 execution probe forces the exact base call through the complete backend and verifies both the compact result and the absence of stack-trace/reflection symbols. The override is not registered in the normal public compatibility profile, so ordinary implementation-pack behavior and the advertised .NET surface remain unchanged.

That cut point clears the reflection-cache boundary for both probes. Their next common stop is now in globalization/platform reachability: numeric or resource formatting requests `CultureInfo.DateTimeFormat`, then Japanese-era initialization reaches the Windows registry and environment expansion path; `Win32Marshal.GetExceptionForWin32Error` finally requires the unsupported `System.OperationCanceledException::_cancellationToken` field of type `System.Threading.CancellationToken`. This is a separate boundary and is intentionally left unresolved by this one-cut-point spike.

The spike therefore validates on-demand identity, generic-body ingestion, and the constrained-dispatch shapes encountered by the probes, but not an end-to-end replacement for shadows. Before this path can become a supported profile feature, the compiler needs:

- a small target-runtime override table for CoreLib methods that have no IL body, including bulk memory movement;
- controlled formatting, globalization, resource, and target-OS cut points so ordinary validation branches do not import host-specific subsystems;
- completion tests for the remaining static-abstract interface encoding shapes before advertising general static-interface support; and
- corpus and size/cycle gates proving that closed-world CoreLib ingestion remains pay-for-play.

This is a compiler-time reachability problem, not an offline analyzer or monthly manual CoreLib member inventory. The implementation pack remains hash-pinned; servicing changes are evaluated by recompiling the compatibility corpus and reviewing newly reached runtime boundaries.

## Selection rules

Binding selection follows these rules:

1. Resolve the reachable member to an exact official contract identity.
2. Find exactly one binding for the active profile coordinate.
3. Validate output-profile and CPU restrictions.
4. Resolve and verify the selected implementation target.
5. Add its declared helpers, metadata, and platform requirements to reachability.
6. Compile or lower it according to its binding kind.

The choice may differ between framework members, but it is deterministic for a given profile coordinate. There is no general preference for shadows or for official bodies: use ordinary verified IL when it is compatible and efficient, an intrinsic when compiler knowledge is necessary, and a shadow or PAL binding when the target requires a controlled substitute.

## Public dispatch and type behavior

A binding does not create a second public type system. Virtual slots, interface implementations, exception catches, boxing descriptors, generic identity, and reflection-free type tests continue to use official framework identities. Shadow implementation types cannot leak through public signatures or object descriptors.

## Reachability and pay-for-play

Implementation bodies and their dependencies join the same closed-world graph as application methods. Unused shadows, PAL groups, descriptors, literal data, and helpers must not be emitted. Feature-map output records which binding groups contributed code, data, allocations, and estimated cycles.

## Failure modes

Compilation fails when:

- the reference-pack identity differs from the contract coordinate;
- an implementation-pack hash or assembly identity differs;
- a public identity has no exact binding or has multiple bindings;
- the selected body contains unsupported reachable CIL or framework calls;
- an implementation leaks a private type across the public boundary;
- an output profile lacks a required PAL implementation; or
- the contract, implementation manifest, and executable registry disagree.

These failures are compatibility diagnostics, not linker surprises.
