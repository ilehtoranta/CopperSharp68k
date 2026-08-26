<!--
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 -->

# Runtime Memory

Managed allocation is controlled by `M68kCompilationRequest.MemoryManagement`.
When it is omitted, ROM profile uses `None` and other profiles use
`ExternalAllocator`.

## Policies

- `None` rejects managed `new` and array allocation at compile time.
- `ExternalAllocator` emits calls to user-provided `__c68k_alloc` and optional
  `__c68k_dispose`.
- `BumpAllocator` is reserved for generated startup/runtime code.
- `ManagedPoolMarkSweepGc` emits a built-in non-compacting pool runtime over
  a memory range described by `M68kHeapOptions`.
- `ExecPoolMarkSweepGc` emits the same compiler/runtime contract, but is
  intended for an Amiga-backed alternative using
  `CreatePool`, `AllocPooled`, and `FreePooled`.

The GC backends are intentionally non-compacting. Managed object addresses must
remain stable because Amiga APIs commonly work with raw pointers and handles.

The managed pool backend owns its block metadata, free list, allocation list,
and block splitting. Explicit dispose returns blocks to the free list. Explicit
collection marks compiler-known roots, iteratively scans marked blocks for
object reference fields and reference-array elements until the graph is closed,
sweeps unmarked allocated blocks back to the free list, and coalesces adjacent
free physical blocks. It does not depend on Exec pool internals.

`ManagedPoolMarkSweepGc` also supports C# finalizers when `ExceptionMode` is
`Full`. Finalization is synchronous: a collection promotes unreachable
registered objects, traces their object graphs, sweeps everything else, and
then drains their finalizers on the collecting thread. A finalizer exception is
caught and discarded so later finalizers still run. Nested collection and
allocation are supported; recursive queue draining is deferred until the outer
drain resumes. `ExecPoolMarkSweepGc`, external allocation, and unmanaged
profiles continue to diagnose reachable finalizable allocations.

Finalizer support is linked only when a reachable `newobj` creates a type with
an effective finalizer, including an inherited one. Programs without such an
allocation retain the original collector, 20-byte class descriptors, and
single mark/trace/sweep pass.

## Sweep Strategies

`M68kCompilationRequest.GcSweepStrategy` controls when the linked GC runtime
runs collection:

- `OnDemand` runs only when user code calls `M68kRuntime.Collect()`.
- `OnAllocationFailure` tries allocation first, collects once on failure, then
  retries allocation. This is the default for GC backends. When finalizers ran
  but their retained blocks still prevent that retry, the generated code runs
  one additional reclaim cycle and makes one final retry.
- `EveryAllocation` runs root-aware collection before each allocation. It is
  useful for tests but is intentionally expensive.
- `TelemetryTriggered` lets the runtime publish approximate stale-pressure
  block and byte counts. Allocation triggers a normal stop-the-world collection
  when those counters exceed `M68kGcTelemetryOptions` thresholds.

The option is valid only with `ManagedPoolMarkSweepGc` or `ExecPoolMarkSweepGc`.
Telemetry does not free memory in the background; it only estimates whether a
foreground collection is worth running. The built-in managed pool currently
uses allocation pressure since the previous collection as this approximate
signal and resets the counters after collection.

The current built-in `ManagedPoolMarkSweepGc` runtime uses exact static roots
and return-PC-selected stack maps for explicit `M68kRuntime.Collect()` calls and
compiler-emitted allocation-site collection. It walks callers from the
suspended A7 slot using each method descriptor's frame and callee-save sizes.
Typed maps ensure only slots known to hold managed references are marked.
Object reference fields and reference-array elements are traced from descriptor
metadata without recursive native calls.

## Runtime Hooks

The compiler/runtime boundary uses these explicit hook conventions (separate
from the managed [internal ABI](InternalAbi.md)):

- `__c68k_alloc`: size in D0; returns a zero-filled managed payload address or
  zero in D0.
- `__c68k_dispose`: reference-slot address in A0; may free the payload and
  should clear the slot.
- `__c68k_gc_init`: config address in D0; returns nonzero in D0 on success.
- `__c68k_gc_collect` runs an explicit collection cycle. D0 is the address of
  the suspended return-PC slot, D1 is that resume PC, A0 points to the unified
  method table, and A1 points to the static-root table.
- `__c68k_gc_get_stale_bytes()` returns approximate stale-pressure bytes.
- `__c68k_gc_get_stale_blocks()` returns approximate stale-pressure blocks.
- `__c68k_gc_shutdown()` shuts down a linked managed runtime after `Main`.

Hooks may clobber D0, D1, A0, and A1 and preserve D2-D7 and A2-A6 unless their
specific contract states otherwise.

`ManagedPoolMarkSweepGc` provides these symbols internally. `ExternalAllocator`
and `ExecPoolMarkSweepGc` resolve them as imports.

The generated config block contains these 32-bit fields:

```text
memoryManagement
gcSweepStrategy
heapStartAddress
heapSize
staleBytesThreshold
staleBlocksThreshold
telemetryIntervalTicks
```

## GC Block Layout

GC-managed heaps should store a header immediately before the managed payload:

```text
GcHeader:
  next   uint
  prev   uint
  size   uint   ; total block size, including header
  flags  uint   ; mark/scan bits and backend-private finalizer state/count

payload + 0  descriptor pointer
payload + 4  object size in bytes
payload + 8  array length, for arrays and strings
payload + 12 first array/string element
```

Reference-free value-type arrays store exact-size elements inline from
`payload + 12`; no element is boxed and no per-element descriptor is emitted.
The array descriptor has a zero reference bitmap, so the collector skips the
payload. Reference arrays retain four-byte elements and the array-special
descriptor bitmap that traces each element. The compiler validates
`12 + length * elementSize` before allocation and records the resulting total
object size at `payload + 4`.

Reference-free indirect value-type operations use exact compile-time layouts.
`ldobj` snapshots the complete payload into a compiler-owned frame home;
`stobj` copies every word from a stable aggregate value; `initobj` clears every
word; and `cpobj` stages through a private frame home before writing the
destination, preserving value semantics even when source and destination
overlap. These transfers are inline, allocate no managed memory, and are not
safepoints.

Reference-bearing value types are initially admitted only in exact local and
argument frame homes. Their layout bitmap is payload-relative, and the frame
planner emits one ordinary root-map offset for each reference word. Non-reference
words remain unreported. Exact `initobj` and field access use the home address
directly; whole-value copies, calls, returns, arrays, and boxing remain gated.
This adds no object wrapper, runtime helper, or conservative stack scan.

Managed byrefs remain one 32-bit native address, with compiler-only provenance.
Frame and static byrefs need no heap owner. A directly produced object-field,
array-element, or boxed-payload byref records its owning managed reference; when
the byref is live across a safepoint, a zero-code-size post-safepoint keepalive
extends that owner's ordinary SSA lifetime through collection. The existing root planner
therefore stores the exact owner payload address in a precise root slot. It
never passes an interior address to `ManagedPool.Mark`, which requires the exact
payload base immediately following its 16-byte block header.

Internal managed byref parameters borrow the dynamic caller chain. Since every
ancestor frame remains available to the root-map walker, frame referents remain
allocated and direct interior owners remain rooted in the originating caller.
This composes through forwarding calls without widening the native ABI. Image
entries and exports do not receive borrowed provenance; byref returns and heap
escapes normally remain gated because they can outlive the caller chain. The
exact static `return ref parameter` shape is summarized as an identity and the
caller reuses its original byref provenance after the call, adding no ABI word.
Other return origins and incompatible owner merges are rejected until an
explicit owner can be transported. Same-owner phis canonicalize equivalent GC
reference copies but choose an actually dominating value for the root
	keepalive. Exceptional call edges retain the caller root when normal-path
	cleanup is skipped. A pre-allocation escape validator rejects a managed byref
	when it is the value stored into an object field, static, array element, or
	indirect location. It does not reject storing an ordinary value through a
	managed byref, nor ref-local SSA rebinding. The
	compiler separately tracks exact referent identity and readonly state for
	managed-pointer SSA values. This metadata has no runtime representation; it is
	used to reject incompatible merges, mismatched tokened indirect access, and
	writes through readonly refs before allocation. The
	compiler's own resolved `ManagedPool` type is a
trusted low-level boundary for raw `M68kAddress` construction, selected by exact
module/type identity rather than names.

The compiler-generated managed pointer is always the payload address, not the
header address.

The descriptor pointer at payload offset zero addresses:

```text
TypeDescriptor:
  +0  object size in bytes, or zero for variable-size objects
  +4  object reference-field bitmap
  +8  base type descriptor pointer
  +12 class vtable pointer, or zero when the type has no virtual slots
  +16 interface map pointer, or zero when no reachable interface call applies
  +20 finalizer code pointer, only when finalizer support is linked
```

The first 20 bytes remain the common class-descriptor prefix. The `+20`
extension is emitted for every fixed-size class descriptor only in a closed
world that contains a reachable finalizable allocation. Variable-size array
and string descriptors keep their existing layout.

Finalizable objects are registered after their descriptor and size fields are
initialized and before their constructor runs. The allocation header stores
pending/running/suppressed state and a bounded outstanding-registration count;
the header remains 16 bytes. `GC.SuppressFinalize(object)` is idempotent and
suppresses one outstanding registration, while every
`GC.ReRegisterForFinalize(object)` adds one. Counter overflow raises
`InvalidOperationException`. Both calls raise `ArgumentNullException` for a
null argument and otherwise do nothing for objects whose type has no finalizer.

`GC.Collect()` aliases the normal root-aware collection intrinsic.
`GC.WaitForPendingFinalizers()` returns immediately because draining is
synchronous, and `GC.KeepAlive(object)` is a code-free intrinsic whose argument
use extends managed liveness through the call site. Pending and currently
running finalizers remain GC roots. Re-registration during a running finalizer
is reserved for a later collection, which permits resurrection without
recursively invoking the same finalizer.

After a normal return from `Main`, the entry adapter snapshots all outstanding
registrations and drains that snapshot before managed lifecycle and platform
shutdown. Allocations and re-registrations performed by shutdown finalizers are
not added to the snapshot. Abnormal/unhandled termination does not run this
exit drain. The low-level `M68kRuntime.Dispose*` hooks remain immediate explicit
free operations: a released object is removed from the pool and is not later
finalized.

Each vtable entry is a 32-bit code pointer. Derived class tables retain base
slot numbering and replace entries for overrides in place.

Framework-declared virtual slots are registered lazily from an exact public
binding. A private shadow implementation supplies the canonical fallback, and
each exact reachable allocated layout replaces the entry when it has a user
override. Registration invalidates cached tables before code generation, so
call-site slot selection and emitted tables always agree. Unused framework
virtual contracts add no descriptor or vtable data.

The interface map uses a compact linear representation:

```text
InterfaceMap:
  +0  entry count
  repeated entries:
      interface identity pointer
      interface method-table pointer
```

Interface method tables contain one 32-bit code pointer per inherited or
declared interface slot. A class receives a separate method table for each
reachable interface it implements, allowing two explicit implementations with
the same method signature to select different code. Linear lookup keeps the
runtime and metadata deterministic and is appropriate for the small interface
counts expected on 68k targets.
