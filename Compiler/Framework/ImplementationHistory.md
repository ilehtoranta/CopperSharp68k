# .NET 10 profile implementation history

This is a concise record of the architecture's completed evolution. It is not an active checklist. Detailed patches, intermediate measurements, and debugging chronology remain in Git history.

## Foundation

The initial baseline established deterministic MC68000/020/040/060 fixtures and recorded image size, code size, allocation sites, spills, frames, stack-memory traffic, feature reachability, and cycle estimates.

Framework resolution was then moved to an exact structural binding registry with explicit managed-body, shadow, intrinsic, PAL, and unsupported classifications. Compatibility diagnostics gained caller IL locations and shortest closed-world root paths.

## Managed implementation reuse

Private shadows were introduced as explicit implementation targets while preserving official public type identity. Verified implementation packs later allowed suitable Microsoft CoreLib IL bodies to be compiled without reading arbitrary host runtime files. Hash and coordinate validation made this reproducible.

## Runtime ABI expansion

The runtime acquired canonical type descriptors, versioned object/array/string layouts, static initialization state, descriptor-based casts and array stores, precise GC metadata, and stable exception identities.

Boxing, unboxing, interface dispatch on boxes, delegates and bounded multicast delegates followed. Aggregate homes, hidden return buffers, managed-byref provenance, generic specialization/sharing rules, spans, and memory views extended the ABI without introducing a general reflective runtime.

## Strings and native boundaries

Managed strings were standardized as immutable UTF-16 objects. Ordinal operations, concatenation, slicing, copying, search, invariant integer formatting, and interpolation were implemented with allocation and cycle budgets.

Native Amiga C strings became an explicit validated Latin-1 boundary with static literal, scoped buffer, and owned storage forms. This removed the temptation to use C strings as the managed representation.

## Collections and LINQ

Selected `List<T>`, `Dictionary<TKey,TValue>`, span/memory, and LINQ families were added as exact profile surface. LINQ admission became provenance-directed so supported operator chains retain deferred behavior without requiring a general-purpose `IEnumerable<T>` runtime.

## Amiga PAL

Console output/input, selected filesystem operations, environment constants, and the monotonic clock were separated into `Runtime.AmigaPal`. Each group gained explicit ownership, cleanup, exception mapping, output-profile behavior, and pay-for-play verification.

## SDK and packaging

The build surface was shaped around ordinary `net10.0` projects and the `amiga-m68k` RID. The SDK gained explicit response manifests, duplicate-identity checks, pinned inputs, one closed-world analysis, atomic publishing, incrementality, and machine-readable provenance.

## Final profile hardening

The compatibility ledger and exact support inventory were completed, followed by semantic and performance hardening across all CPU targets. Important corrections included typed stack joins, boolean condition-merge handling, large-frame clearing, narrow-store widths, byref liveness across tail calls, canonical object identity, and complete map metrics.

The resulting profile is serviced as a versioned compatibility product rather than as a sequence of implementation phases. See [the servicing policy](Net10ServicingPolicy.md) for current change rules.
