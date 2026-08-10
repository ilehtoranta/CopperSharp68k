# Managed runtime model

## Scope

This document defines the runtime conventions required by the admitted .NET 10 profile. It complements the [internal compiler ABI](../InternalAbi.md), [runtime memory model](../RuntimeMemory.md), and [exception runtime](../ExceptionRuntime.md).

The runtime is closed-world and non-reflective. The compiler knows every reachable managed type, method, static field, generic instantiation, delegate shape, and platform operation before code generation.

## Object and descriptor layout

All managed references are 32-bit target addresses. The common object prefix is:

| Offset | Field |
|---:|---|
| 0 | type-descriptor address |
| 4 | allocation size in bytes |

Arrays and strings add a 32-bit logical length at offset 8 and begin their payload at offset 12. String payload is UTF-16 target-endian data; array payload uses the element layout and alignment chosen by the compiler.

A descriptor is the canonical runtime identity for a constructed managed type. It carries the information needed by the admitted runtime operations, including base type, interface membership, instance size, GC/reference layout, element information for arrays, and dispatch data. Descriptor addresses are stable within the linked artifact.

The layout is versioned as part of the private runtime ABI. Public package compatibility does not permit incompatible layout changes without a new runtime ABI generation.

## Static initialization

Each type with runtime initialization has a compact state cell representing uninitialized, initializing, initialized, or failed. Initialization is once-only and supports recursive entry according to .NET type-initialization rules. A failed initializer caches the failure and subsequent access throws `TypeInitializationException` rather than rerunning the initializer.

Checks are inserted only at reachable operations that require the type to be initialized. Types and state cells that are never reached are omitted.

## Type tests, casts, and array stores

`isinst`, `castclass`, interface tests, and reference-array covariance use descriptors rather than public reflection metadata. A cast follows base/interface relationships encoded in the closed-world descriptor graph. Reference-array stores validate assignability before modifying the array.

Null succeeds for reference casts and fails only where the corresponding CIL operation requires a non-null value. Failed casts throw the official framework exception identity.

## Boxing and unboxing

A boxed value is an ordinary managed object:

| Offset | Field |
|---:|---|
| 0 | exact boxed type descriptor |
| 4 | allocation size |
| 8 | aligned value payload |

Boxing copies the value and preserves its exact constructed type identity. `unbox` returns a managed byref into the payload; `unbox.any` performs the required type test and copies the value. Interface dispatch on a box uses descriptor thunks that adapt the boxed payload to the value-type instance method ABI.

Only value layouts supported by the compiler may be boxed. The GC map for a box includes payload references when the admitted value layout contains managed references.

## Delegates

Delegates are immutable managed objects with a uniform invocation ABI. Their runtime data identifies the delegate type, optional closed target, invocation entry, and ordered invocation list. Single-cast delegates use an inline representation; multicast forms use compiler-known descriptors and immutable reference payloads.

The current implementation supports multicast lists up to 28 entries. `Combine` appends invocation lists, `Remove` removes the last matching subsequence, equality compares delegate type plus ordered targets and methods, and invocation returns the last result after calling each entry in order. Exceptions stop the sequence and propagate normally.

Delegate construction and calls are resolved structurally. `DynamicInvoke`, reflection-created delegates, unmanaged function-pointer conversion, and unbounded runtime delegate shapes are outside the profile.

## Aggregates and managed byrefs

Non-scalar value types are represented by a stable address to compiler-owned storage when they cannot remain entirely in registers. Whole-value copies use the exact layout and GC map. Aggregate return values use a hidden caller-provided return buffer so ownership and lifetime are explicit.

A managed byref carries compiler dataflow provenance even though its machine representation is an address. Provenance records the referent, owner needed to keep the storage alive, read-only state, and lifetime class. Array, string, box, span, and interior-field byrefs keep their managed owner live across safepoints.

The compiler rejects byrefs that escape their valid lifetime, enter unmanaged storage, merge with incompatible provenance, or are stored where the GC cannot describe them. A callee may borrow caller storage only within the verified call lifetime.

## Generics

Generic code is specialized by value layout, calling convention, GC shape, and observable type identity. Implementations may be shared only when these properties are representation-equivalent. Constructed types retain distinct descriptors and static storage even when some machine code is shared.

The closed-world model avoids runtime generic dictionaries for admitted cases. Unsupported layouts or operations are rejected during analysis.

## Strings

Managed strings are immutable UTF-16 objects. Their logical length counts UTF-16 code units. A trailing zero may be present as an implementation convenience but is never part of the string length and does not turn a managed string into a native C string.

The profile provides selected ordinal operations including indexing, equality, concatenation, substring, copying, `ToCharArray`, search, invariant integer formatting, and interpolation. Culture-sensitive operations are admitted only when the manifest names a compatible implementation; an ordinal operation must not silently become culture-sensitive or vice versa.

Important implementation properties include:

- concatenation computes a checked final length and performs at most one result allocation;
- a full-range substring may return the original immutable string;
- empty results use the canonical empty string;
- copying and search traverse UTF-16 directly without native conversion;
- integer formatting and interpolation use bounded target-side buffers and allocate only the required managed result.

### Native C-string boundary

Amiga APIs use byte-oriented, zero-terminated strings. Conversion is explicit and currently uses a validated Latin-1 contract:

- embedded `U+0000` and characters above `U+00FF` are rejected when a C string is required;
- compile-time literals may use static native data;
- short-lived calls use a scoped `CStringBuffer` with deterministic cleanup;
- longer-lived native ownership uses a disposable `CStringStorage` managed owner;
- conversion failure or native allocation failure occurs before the platform call.

Native C strings never replace managed strings inside application code. Console output that accepts a managed string uses an explicit length and therefore preserves embedded zero characters.

## Spans and memory views

The private span representation is a data address, element count, and optional managed owner. Bounds checks use the logical count, and the owner is kept live across safepoints. `Span<T>` and `ReadOnlySpan<T>` operations are admitted only for element layouts the compiler and GC can describe.

The current surface includes selected array and reference constructors, length, indexers, slicing, emptiness checks, conversions, string `AsSpan`, selected equality, and copying. Span operations do not allocate. Returning or storing a span/byref beyond its verified lifetime is rejected.

`Memory<T>` and related views retain an owning managed object plus range. Only manifest-listed members and element layouts are supported; they do not imply general support for every `System.Memory` API.

## Collections

### `List<T>`

The list implementation uses a managed array, count, and mutation version. Capacity grows geometrically, starting at four and doubling subject to checked limits. Mutation updates the version so enumeration detects modification. The admitted member set includes selected construction, capacity, indexing, mutation, searching, copying, and enumeration operations for supported scalar, reference, and selected value layouts.

### `Dictionary<TKey,TValue>`

The dictionary uses target-owned parallel storage and open addressing with linear probing and a bounded load factor. Admitted key shapes include selected integral and enum types plus ordinal strings. Value shapes include selected scalars, strings, managed references, and reference-free value layouts. Enumeration and `Values` behavior are deterministic for the implementation but callers must not treat order as a public sorting guarantee.

### LINQ

LINQ support is provenance-directed, not a general `IEnumerable<T>` runtime. The analyzer admits exact source and operator chains whose layout and callback behavior it understands, including selected `Range`, `Repeat`, `Select`, `Where`, `Any`, `Take`, `Sum`, `ToArray`, `OrderBy`, and `ThenBy` cases.

Deferred operators remain deferred and invoke callbacks in .NET order. A supported terminal operation does not make an unknown producer compatible; mixed or lost provenance is rejected rather than routed through a general iterator fallback.

## Garbage collection and ownership

Descriptors, stack maps, byref provenance, delegate payloads, generic layouts, and collection storage all feed the same precise managed-reference model. Native PAL resources are not managed references and use deterministic ownership with cleanup on normal and exceptional paths.

No runtime service may infer ownership from an untyped 32-bit address. The compiler must be able to identify every managed root and interior owner at each safepoint.
