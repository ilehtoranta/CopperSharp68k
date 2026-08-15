# Post-emission M68k Peephole Optimizer

This document describes optimization of encoded 68k instructions after register
allocation and emission. The SSA machine-IR optimizer is documented separately
in [MachineIrOptimizations.md](MachineIrOptimizations.md). This is not a
catalogue of possible 68000 peepholes. New rules should be driven by generated
code, apply generally, and preserve the compiler's calling convention and
managed-language semantics.

## Pipeline

Peephole optimization runs after machine-IR optimization, ABI lowering, register
allocation, and instruction emission, but before linking and assembly rendering:

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
- retargeting zero-extended absolute word loads from dead temporary registers,
  and removal of upper-word clears when every subsequent operation and store is
  word-sized;
- replacing a long register mask with `andi.w` when the destination upper word
  is known zero and the mask sign bit is clear, preserving both value and CCR;
- same-register address-adjustment canonicalization:
  - `addq.l` or `subq.l` for displacements from 1 through 8;
  - `lea d16(An),An` for larger signed 16-bit displacements;
  - merging or cancelling adjacent adjustments without crossing labels;
- removal of branches to the next instruction;
- `bsr` or internal `jsr` followed by `rts` converted to a tail branch or jump;
- compact register-set transfers using `movem` where the size policy selects it;
- canonical assembly notation such as `(An)` instead of `0(An)`.

The earlier machine-IR pipeline owns typed control flow, scalar and memory
optimization, closed-world call analysis, inlining, and reachability. The code
generator retains frame-layout optimization, reusable vararg scratch-frame
allocation, ABI-aware argument placement, and physical register allocation.

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

## Optimization boundaries

Global machine-IR register allocation is now the default backend path; its
architecture and acceptance measurements are documented in
[RegisterAllocation.md](RegisterAllocation.md). Generated real-program findings
and the generalized fixes derived from them are recorded in
[GeneratedAssemblyAudit.md](GeneratedAssemblyAudit.md).

Further high-value work such as branch threading, unreachable-block removal,
broader alias analysis, instruction scheduling, addressing-mode selection,
interprocedural inlining, and CPU cost modeling belongs in explicit optimizer
passes with control-flow and analysis support. It should not accumulate as
example-specific opcode patterns in `M68kPeepholeOptimizer`.
