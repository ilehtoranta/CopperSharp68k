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

## Sweep Strategies

`M68kCompilationRequest.GcSweepStrategy` controls when the linked GC runtime
runs collection:

- `OnDemand` runs only when user code calls `M68kRuntime.Collect()`.
- `OnAllocationFailure` tries allocation first, collects once on failure, then
  retries allocation. This is the default for GC backends.
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

The current built-in `ManagedPoolMarkSweepGc` runtime uses exact static and
current-frame roots for explicit `M68kRuntime.Collect()` calls and compiler-
emitted allocation-site collection. It also uses typed evaluation-stack maps so
only stack slots known to hold managed references are marked. Object reference
fields and reference-array elements are traced from descriptor metadata without
recursive native calls.

## Runtime Hooks

The compiler/runtime boundary uses these explicit hook conventions (separate
from the managed [internal ABI](InternalAbi.md)):

- `__c68k_alloc`: size in D0; returns a zero-filled managed payload address or
  zero in D0.
- `__c68k_dispose`: reference-slot address in A0; may free the payload and
  should clear the slot.
- `__c68k_gc_init`: config address in D0; returns nonzero in D0 on success.
- `__c68k_gc_collect()` runs an explicit collection cycle.
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
  flags  uint   ; mark bit and backend-private flags

payload + 0  descriptor pointer
payload + 4  object size in bytes
payload + 8  array length, for arrays and strings
payload + 12 first array/string element
```

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
```

Each vtable entry is a 32-bit code pointer. Derived class tables retain base
slot numbering and replace entries for overrides in place.

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
