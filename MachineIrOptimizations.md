# SSA Machine-IR Optimization

The always-on machine-IR optimizer runs on typed SSA values before physical
register allocation. It is separate from the encoded-instruction peephole
optimizer described in [PeepholeOptimizations.md](PeepholeOptimizations.md).

## Pipeline

The implemented pipeline is:

```text
CIL lowering and local CIL canonicalization
→ raw machine-IR verification
→ condition-flow canonicalization
→ scalar, memory, CFG, and conservative loop optimization
→ build every reachable raw machine function
→ closed-world target discovery, SCC analysis, and transactional scalar inlining
→ whole-program reachability pruning
→ logical-call finalization / ABI boundary
→ byref, GC-root, register-allocation, and spill verification
→ instruction emission and post-emission peepholes
```

`M68kCompilationRequest` and the public optimization surface are unchanged.
Machine optimization is always enabled.

## IR contracts

Every instruction lowered from a managed method carries an origin containing
the source `CilMethod`, source instruction, and an inline-site chain. Metadata
tokens are therefore resolved in their defining module after cross-method
cloning. The verifier rejects missing or malformed origins for lowered
functions.

Constants use a typed, bit-preserving representation for `int32`, `int64`,
Boolean, null, and 32/64-bit floating bit patterns. Folding is currently limited
to integral, Boolean, and null facts.

CFG edges are typed as normal, exception dispatch, leave-to-finally, or finally
continuation. Catch/finally regions own their try and handler blocks, active
scopes are attached to blocks, and catch values are explicit definitions.
Dominance, liveness, root planning, byref-owner dominance, reachability, and
terminal memory liveness include exceptional edges. Phi inputs remain attached
only to normal incoming edges.

Logical calls record dispatch kind, closed-world target identities, logical
arguments and results, null-check requirements, and source origin. Descriptors
exist only through whole-program optimization and are removed at the ABI
boundary before spill rewriting changes SSA identities.

## Local fixed point

The deterministic scalar fixed point is capped at 16 rounds. Reaching the cap
is recorded in internal statistics and valid IR continues through the backend.
Implemented transformations include:

- explicit constant discovery and integral folding with divide-by-zero and
  signed-overflow preservation;
- copy-chain and trivial-phi elimination with register/GC/byref compatibility;
- commutative canonicalization and algebraic identities;
- constant conditional-branch and switch folding, empty-block threading,
  single-predecessor merging, redundant-branch removal, phi repair, and
  unreachable normal-block cleanup;
- dominator-based GVN for pure, non-throwing expressions;
- generalized dead instruction elimination with memory, volatile, safepoint,
  exception, condition-code, caller-frame, and managed-owner barriers;
- exact local/argument-home load forwarding and overwritten-store removal in
  loop-free functions, avoiding live-range extension across backedges;
- conservative LICM for pure, non-throwing 32-bit data-register arithmetic in
  call-free loops without fixed-register values. Copies, conversions, address
  values, narrow values, and condition-dependent operations remain in place.

Exact frame homes, argument homes, static fields, library bases, runtime slots,
managed roots, and unknown heap memory have distinct memory-object identities.
Unknown calls, safepoints, volatile operations, throwing operations, and escaped
frame addresses invalidate applicable facts.

## Whole-program calls

Raw functions are collected before any method is allocated or emitted. The
module optimizer constructs a deterministic call graph and processes strongly
connected components bottom-up. Virtual and interface calls are assigned the
closed-world target set when the metadata model can enumerate one; monomorphic
sites become direct logical calls. Framework-private virtual-shaped methods
without runtime slots remain conservative.

The current transactional inliner handles direct, single-block scalar callees.
It absolutely honors `NoInlining`, rejects recursive SCC edges, EH, `localloc`,
safepoints, memory operations, and throwing operations, and commits only when
the machine-instruction count does not grow. Cloned instructions retain their
original source origin with the caller site appended.

General multi-block, aggregate/byref, callvirt-null-check, and EH-aware cloning
remain guarded off until their transactional clone and unwind-state paths are
implemented. Existing calls continue through the established 68k ABI staging.

Reachability starts from the entry point, exports, managed runtime and lifecycle
roots, dispatch-table targets, folded aliases, address-taken methods, and type
initializer edges. Incomplete dynamic target sets retain the compiler's full
pre-discovered dispatch closure.

## Statistics and safety

Internal per-method statistics record rounds, rewrites, removed instructions and
blocks, forwarded loads, removed stores, LICM moves, and the fixed-point cap.
Module statistics record SCCs, devirtualized and inlined calls, retained methods,
and estimated pre/post IR cost.

Every phase finishes with machine-IR verification. The pre-peephole emitter still
rejects self-moves, invalid stack round trips, missing roots, conflicting fixed
registers, and spill-allocation failures.

## 2026-08-14 corpus

The five tracked MC68000 register-allocation fixtures improved from 6,950 to
5,208 aggregate cycles (25.1%), exceeding the 10% acceptance threshold. All
seven exception-cycle fixtures and the tracked Range/Select/Where/Take/Sum LINQ
paths pass their individual budgets.

The representative code-size samples measured 5,558 bytes for IFFInspect and
4,242 bytes for MUITaskList. CopperBars emits 1,284 code bytes in a 1,348-byte
stripped HUNK, with 435 assembly instructions, 43 calls, no prohibited long
mask constants, and one retained word zero-extension mask. Across the recorded
profile samples, aggregate code changed from 11,622 to 11,534 bytes (-0.8%).

The full test assembly has seven unrelated dirty-baseline failures: two Exec ABI
count expectations, one public ABI structure cast, shadow rounding, the checked-
in compatibility ledger and profile baseline, and the optional CopperScreen DOS
diagnostic bridge. Focused optimizer, allocator, framework, exception, DOS,
aggregate, string, ABI, and CopperBars validation passes 321 tests.
