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

## StringBuilder implementation and admission

StringBuilder uses released Microsoft CoreLib CIL together with explicitly declared target intrinsics, runtime shadows and PAL leaves. Stable admission requires the verified `Microsoft.NETCore.App.Runtime.win-x64` 10.0.9 implementation input, including its exact CoreLib identity and SHA-256. Synthetic packs and adjacent servicing versions do not satisfy this gate. Stable tests leave `EnableUnlistedManagedBodies` disabled. The internal unlisted-body experiment remains available for architectural probes; it is not needed by the audited StringBuilder acceptance graph and does not authorize general CoreLib ingestion.

The public inventory contains 103 instance constructor/method/accessor signatures. Exact effect declarations additionally cover the reached private StringBuilder helpers, ChunkEnumerator, its ManyChunkInfo, and AppendInterpolatedStringHandler. Structural matching includes assembly/type identity, calling convention, generic arity, parameter and return shapes, modifiers and substituted generic arguments. Private serialization members, adjacent signatures and application lookalikes receive no automatic declaration. Callback allocation, collection and exception effects remain separate from StringBuilder's own storage effects.

Bindings for implementation dependencies validate the actual input, member, caller, closed payload and target layout as applicable. A matching display name, fabricated method record or token coincidence is insufficient. Owned call-site restrictions keep private CoreLib resource, numeric and memory helpers from becoming unrestricted application APIs. Compatibility analysis records the exact unsupported identity and root path before backend rejection; a deferred numeric-lowering error must not hide that diagnostic.

## Storage, borrowing and collection

Strings contain immutable UTF-16 target data. Character copies handle overlapping ranges, self-copy and zero lengths and preserve NUL, surrogate and non-ASCII code units. Allocation byte counts are checked before overflow. Target spans retain a twelve-byte data/length/owner representation. Projected references and snapshots retain owners through collection after the original source variable dies. Borrowed character references are admitted only for the audited synchronous nonescaping StringBuilder consumers.

Chunk enumeration and readonly-memory views retain their builder and backing arrays. GetChunks construction above eight chunks can allocate an index object and reference array, so its declarations include allocation and collection; enumeration advance and Current do not allocate. Fixed two-/three-object formatting storage occupies eight/twelve bytes and marks every reference slot. The exact character NumberBuffer and ValueListBuilder views preserve their digit/span/array owners; their private ABI checks do not enable arbitrary ref-like fields.

Primitive initialized arrays validate constant length, immediate RVA metadata, primitive width and blob size, reject interior control-flow entry, and convert PE little-endian elements to target byte order. Nested managed aggregate reference bitmaps, including admitted nullable application payloads, propagate into ordinary/constructed containers, iterator state and boxed payloads, retaining overflow checks. A nullable payload's CoreLib identity does not remove its managed references from a containing runtime shadow. Allocation and implicit boxing participate in normal safepoints and cleanup; allocation-failure handling must preserve committed output and dispose acquired iterators according to the public contract.

Unwind entries are sorted by final native return address after optimization. Managed root walkers use lower-bound search, preserving the first registered duplicate and missing-address termination. Root maps retain their original registration labels. Boundary and physical retention tests verify both table widths, unsigned addresses, duplicates, missing keys, caller roots and finalizers. The faster lookup preserves the original native instruction ceilings.

## Formatting and public behavior

The audited bodies cover construction, capacity/length/indexing, append and insert, replacement/removal, snapshots, array/span/memory/pointer copying, chunks, joins, composite formatting and interpolated handlers. Tests compare content, ranges, validation order, partial prefixes, snapshots, reuse, disposal and callback counts with CoreLib. Managed-pool execution collects on every allocation and within callbacks. Success-only examples do not establish exception or allocation-failure semantics.

Integer, Decimal and floating formatting share the verified numeric settings and managed formatting paths. Closed generic and nested payload identities remain significant; a method specialization must match its actual caller and complete element shape. Floating acceptance uses SoftFloat. Object, enum, application-value/reference and nullable formatting retain their separate exact dispatch and boxing/layout gates. A resolved constrained value implementation executes directly rather than redispatching through an empty boxed virtual table. Default ValueType names come from resolved metadata; this does not supply general reflection.

Target-owned CurrentCulture and DefaultThreadCurrentCulture references participate in static GC rooting. An explicit current culture wins over the default; an unset pair selects invariant culture. Numeric settings can come from NumberFormatInfo or an application IFormatProvider. Application culture overrides preserve their virtual/interface slots. Culture storage currently represents one execution context; thread/async culture isolation and named-culture locale data remain outside this runtime implementation.

Typed validation exceptions retain parameter and actual-value payloads. Resource adapters use deterministic target resource text and exact released resource identities; full localized message rendering and reflection-based exception formatting are separate runtime facilities. Exception constructors, aliases and cleanup leaves require their audited callers and effects rather than namespace-wide admission.

## List enumeration and runtime identity

Join array fast paths and List field views are separate from public custom enumeration. The List field view requires an exact List descriptor; subclasses retain public IEnumerable dispatch, including explicit reimplementations. Acquired custom iterators are disposed if adapter allocation fails. Enumeration callbacks can allocate, collect or throw, and their ownership and disposal-failure precedence must remain intact.

Admitted application List subclasses inherit the verified twenty-byte base storage and reference bitmap before their own fields. Both released and shadow List storage require _items, _size and _version at offsets eight, twelve and sixteen. Runtime base descriptors retain the actual closed List construction. Open Decimal List constructor/Add and enumeration references require an actual matching closed application caller. This scoped support does not imply unrestricted framework subclassing.

Ordinary List subclasses inherit the base enumeration interface slots. Local explicit and implicit reimplementations retain precedence. Nonempty shadow enumeration boxes the value enumerator and preserves generic/nongeneric Current, completion, Reset and mutation detection. Empty interface enumeration uses CoreLib's cached empty-enumerator behavior, including retention through collection. The direct value-returning List.GetEnumerator path keeps its separate empty/mutation contract.

Boxed shadow enumerators and the exact released Decimal List enumerator register actual Runtime.Managed/CoreLib metadata layouts only for the pinned StringBuilder input, after checking size and reference bitmap against admitted value storage. A synthetic value layout with no metadata handle cannot supply an interface table. Constructed GetType maps preserve the emitted layout's actual module and handle; re-resolving a public alias by name can select a different physical identity than its constructor and type token. Public names and target descriptor identities must stay consistent.

## Verification and costs

The public signature inventory and explicit-use tests audit all 103 released public signatures. Unified native acceptance runs every CPU in both optimizer modes with full exceptions, the exact input hash, disabled unlisted bodies and physical every-allocation collection. Dedicated deeper matrices exercise nullable/application behavior and all 33 original allocation-failure families without reducing the inventory, guarded failure regions, result oracles or instruction limits.

Representative cost tests retain exact allocation counts and independent image, loaded-image and per-CPU cycle ceilings for baseline, presized/growing text, integer handlers and integer composite formatting. Trimming checks reject unused Decimal/floating and general integer formatter dependencies; the baseline contains no StringBuilder implementation. These gates are budgets for the named programs, not universal workload performance claims. General example-program baselines and external integration availability remain separate checks.

StringBuilder acceptance is complete for the pinned 10.0.9 input on all four CPUs in both optimizer modes. See [CompatibilityTesting.md](CompatibilityTesting.md) for retained evidence covering released-input provenance, dependency compatibility, native behavior, allocation failures, GC ownership and costs, together with the separate repository-wide failures. Historical exploratory measurements and superseded checkpoint claims are not proof of the current source.
