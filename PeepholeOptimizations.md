# M68k Machine Optimizer

This document describes the current machine-level optimizer. It is not a
catalogue of possible 68000 peepholes. New rules should be driven by generated
code, apply generally, and preserve the compiler's calling convention and
managed-language semantics.

## Pipeline

Machine optimization runs after CIL lowering and before linking and assembly
rendering:

- `M68kCodeGenerator` performs transformations that require CIL types, control
  flow, method metadata, or ABI knowledge.
- `M68kAssembler` owns encoded instructions and exposes the optimization entry
  point.
- `M68kAssemblyBuffer` owns bytes, labels, and fixups. Byte removal updates all
  affected offsets and removes fixups contained in deleted instructions.
- `M68kOptimizerPipeline` orders machine-level passes.
- `M68kPeepholeOptimizer` applies local rewrites repeatedly to a fixed point.

Keeping CIL-aware and machine-level optimization separate is intentional.
Machine rules must be provable from emitted instructions and their associated
dataflow facts.

## Analysis

`M68kInstructionDataflow` builds a control-flow graph from the emitted
instruction stream and computes conservative facts to a fixed point:

- data-register and address-register uses, definitions, and liveness;
- individual X, N, Z, V, and C condition-code uses, definitions, and liveness;
- exact stack-pointer deltas for understood instructions;
- coarse stack, known-address, indirect, and unknown memory effects;
- data-register value ranges;
- known stack-relative address aliases and stack-slot values.

Calls, undecoded instructions, and uncertain memory operations are barriers.
Uncertainty prevents optimization rather than permitting a speculative rewrite.

## Implemented Rules

The optimizer currently covers these rule families:

- dead register arithmetic, moves, tests, and discarded call results when all
  outputs, including condition codes, are dead;
- redundant stack spills, reloads, duplicates, argument shuffles, and temporary
  return-value round trips;
- direct source-to-destination fusion where the 68000 supports a memory or
  immediate operand without an intermediate register;
- zero materialization using `clr.l` where the destination is safe;
- zero comparisons using `tst`;
- removal of redundant tests after instructions that already establish the
  required condition codes;
- long-to-word arithmetic and logical-immediate narrowing when value-range and
  condition-code analysis prove equivalence;
- same-register address-adjustment canonicalization:
  - `addq.l` or `subq.l` for displacements from 1 through 8;
  - `lea d16(An),An` for larger signed 16-bit displacements;
  - merging or cancelling adjacent adjustments without crossing labels;
- removal of branches to the next instruction;
- `bsr` or internal `jsr` followed by `rts` converted to a tail branch or jump;
- compact register-set transfers using `movem` where the size policy selects it;
- canonical assembly notation such as `(An)` instead of `0(An)`.

The code generator additionally performs frame-layout optimization, deferred
local initialization, register allocation for suitable locals, reusable vararg
scratch-frame allocation, ABI-aware argument placement, small-method inlining,
and removal of methods that are always inlined.

## Safety Requirements

Every new machine rewrite must account for:

- condition-code differences, including partial CCR preservation;
- full 32-bit value equivalence when changing operand width;
- address-register instructions, which have different CCR behavior from
  equivalent-looking data-register instructions;
- A7 movement and the resulting change to stack-relative operands;
- labels or fixups inside any replaced byte range;
- volatile memory and unknown aliases;
- 68000 memory behavior, notably the read-before-write behavior of `clr`;
- the selected CPU target when introducing post-68000 instructions.

Rules must use decoded instruction effects and dataflow where correctness
depends on liveness, aliases, ranges, or flags. Opcode adjacency alone is not
sufficient proof.

## Future Work

The remaining high-value opportunities are no longer primarily peepholes:

- branch threading and jump-chain simplification;
- unreachable basic-block elimination;
- broader address and memory alias analysis;
- global register allocation across basic blocks;
- instruction scheduling and addressing-mode selection informed by a 68000
  byte-and-cycle cost model;
- broader interprocedural inlining and tail-call analysis;
- broader table-driven CPU cost policies beyond the existing MC68020,
  MC68040, and MC68060 instruction-selection rules.

These should be implemented as explicit optimizer passes with control-flow and
analysis support, not accumulated as example-specific patterns in
`M68kPeepholeOptimizer`.
