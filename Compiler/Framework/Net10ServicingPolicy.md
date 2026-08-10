# .NET 10 profile servicing policy

## Purpose

The profile is versioned across several independent compatibility axes. This policy defines which changes are compatible, which require a new profile coordinate, and how reference-pack and implementation-pack updates are validated.

## Version axes

| Axis | Meaning | Current value |
|---|---|---|
| Contract schema | JSON shape and interpretation of framework bindings | 1 |
| Target framework | public compile-time framework family | `net10.0` |
| Reference pack | exact official contract inputs | 10.0.9 |
| Implementation-manifest schema | JSON shape for verified managed bodies | 1 |
| Implementation profile | compatible body-selection and mapping rules | `corelib-common-il-v1` |
| SDK/package line | distributed toolchain behavior | `0.1.0-preview.1` |
| Response-manifest schema | SDK-to-compiler invocation contract | 1 |
| Compatibility-report schema | diagnostic and ledger interchange | 1 |
| Private runtime ABI | object, call, GC, descriptor, and helper conventions | 1 |

All required axes are validated as an exact tuple. Matching `net10.0` alone is insufficient.

## Schema versions and profile versions

A schema version changes when a machine-readable document cannot be interpreted safely by an older reader. Adding an optional field with a defined default may be schema-compatible; changing identity rules, required fields, or field meaning requires a new schema.

A profile version changes when the supported surface, binding selection, implementation semantics, or target behavior changes even if the JSON schema is unchanged.

The private runtime ABI is separately versioned because managed implementation bodies, compiler lowering, runtime objects, stack maps, and PAL objects must agree on it.

## Change classification

| Change | Required action |
|---|---|
| Add an exact supported member without changing existing behavior | compatible package update; update manifest, tests, inventory, and provenance |
| Improve code generation without observable semantic or ABI change | compatible patch/prerelease update with performance evidence |
| Correct an implementation to match the documented .NET contract | compatible servicing fix; add regression evidence and call out behavior change |
| Remove a supported member or narrow an admitted signature/layout | new incompatible profile/package line |
| Change an existing member's documented semantics | new incompatible profile/package line unless it is clearly a contract correction |
| Change binding kind with identical public semantics and private ABI | compatible update, with semantic and performance comparison |
| Change object layout, calling convention, GC map rules, descriptor layout, or runtime helper ABI | new private runtime ABI generation; all implementation and runtime inputs must be rebuilt |
| Change manifest identity or matching rules | new contract schema |
| Change response/provenance document meaning incompatibly | corresponding schema increment |
| Update the .NET reference pack | follow the full reference-pack update procedure below |

During the `0.x` preview line, compatible additions and fixes use patch or prerelease increments. Intentional breaking profile changes use at least a minor version increment and must remain diagnosable from provenance.

## Reference-pack update procedure

An update from one .NET 10 servicing pack to another is not automatic. It requires:

1. Pin and hash the candidate official reference pack.
2. Diff every admitted type and member identity against the current contract.
3. Regenerate the contract manifest and review additions, removals, signature changes, and forwarded identities.
4. Pin candidate implementation assemblies and regenerate their verified manifest.
5. Revalidate every managed implementation binding against its official contract identity and private runtime ABI.
6. Run semantic, ABI, corpus, output-profile, and performance suites; update baselines only after review.
7. Publish a new package coordinate and record both old and new coordinates in release notes.

A servicing update must never select installed host assemblies implicitly or preserve an old package version while changing hashed inputs.

## Adding profile surface

Each newly admitted member needs:

- an exact contract-manifest entry;
- one executable binding with declared effects and feature group;
- an implementation body, intrinsic, shadow, or PAL operation;
- positive semantic tests and adjacent unsupported-member tests;
- ABI/GC/ownership coverage where relevant;
- map and pay-for-play assertions;
- corpus-ledger review; and
- performance budgets for hot or allocation-sensitive paths.

Documentation should describe a feature family and important limitations. The machine-readable manifest remains the exhaustive member inventory.

## Removing or deprecating surface

Supported members are not silently removed. A removal requires an incompatible profile line unless the binding was unusable because of a confirmed implementation defect. Deprecation is expressed in diagnostics and release notes before removal where practical.

Unsupported members may be added later without breaking existing artifacts, but newly introduced implementation dependencies must still respect output-profile and pay-for-play rules.

## Private runtime ABI policy

Until the toolchain can select and link multiple private ABI generations explicitly, incompatible runtime ABI changes are prohibited within a package line. Compiler, implementation pack, runtime, PAL, and link objects are shipped and validated as a coherent set.

When multiple generations are introduced, the generation must appear in response manifests, implementation manifests, compatibility reports, maps, provenance, and cache keys.

## Release evidence

A releasable profile coordinate requires:

- manifest/registry agreement;
- verified reference and implementation hashes;
- successful compiler and SDK builds;
- semantic, runtime ABI, GC, PAL, corpus, and package integration tests;
- stable unsupported-member diagnostics;
- pay-for-play and performance checks;
- reproducible provenance and artifact hashing; and
- an updated concise profile inventory.

Transient work logs, local timing notes, and checkpoint instructions are not release documentation. Detailed implementation chronology belongs in Git history; durable decisions belong in these specifications.
