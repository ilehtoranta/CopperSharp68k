# Goal: Excellent MC68000 Register Allocation

## Goal-mode execution contract

Implement a production-quality, global register allocator for CopperSharp68k,
optimized first for the Motorola 68000 and compatible with the existing 68020
and 68040 targets.

Work autonomously through every phase below. Do not mark the goal complete
until every item in the definition of done is satisfied. Preserve unrelated
working-tree changes, keep original IL offsets usable for diagnostics and
exception metadata, and prefer internal compiler changes over new public API.

When implementation discoveries invalidate a detail of this document, choose
the smallest architecture-compatible adjustment, document it in code or tests,
and continue. Stop for user input only if correctness requires changing a
public ABI, serialized output contract, or managed runtime compatibility.

## Required outcome

Replace evaluation-stack-oriented lowering and whole-local promotion with an
SSA-based machine IR and a bank-aware global allocator. Generated code should
keep values in registers across ordinary operations, use precise call
constraints, spill only under genuine pressure, expose live references to the
GC at safepoints, and preserve exception semantics.

The intended pipeline is:

```text
Decoded CIL
    -> IL optimizer
    -> control-flow graph
    -> SSA machine IR
    -> 68000 constraint lowering
    -> liveness and interference
    -> register allocation
    -> spill and parallel-copy insertion
    -> frame layout
    -> 68k emission
    -> peephole optimization
```

The existing `CilOptimizer` remains the normalization stage. The existing
peephole optimizer remains the final encoding cleanup stage, but it must not be
relied upon to repair systematic stack traffic produced by lowering.

## Architectural invariants

### Register banks

| Class | Registers | Policy |
|---|---|---|
| Data | D0-D7 | Integers, Booleans, byte/word operations, arithmetic |
| Address | A0-A6 | References, pointers, effective-address bases, suitable 32-bit scalars |
| Fixed | A7 | Stack pointer; never allocate |
| Conditional | A5 | Unavailable while it is the managed runtime-frame pointer |
| Conditional | A6 | Fixed only where an Amiga library base or cached base requires it |

- Require data registers for byte operations, `MOVEQ`, shifts,
  multiply/divide, and ordinary condition-code-producing arithmetic.
- Prefer address registers for values used as memory bases.
- Allow references and pointers in data registers when profitable, but insert
  D-to-A transfers only where addressing requires them.
- Allow transparent 32-bit scalar wrappers in either compatible bank.
- Model 64-bit values as indivisible consecutive data-register pairs. Spill the
  complete eight-byte value when no legal pair exists.
- Treat condition codes as a non-spillable virtual resource. Keep a compare and
  its consuming branch adjacent instead of materializing a Boolean.

### Internal ABI

- Preserve the current internal argument ABI: D0/D1 for scalar arguments and
  A0/A1 for address arguments, with the existing special handling for 64-bit
  pairs and Amiga startup arguments.
- Treat D0, D1, A0, and A1 as caller-saved.
- Treat D2-D7 and A2-A6 as callee-saved for ordinary internal calls.
- Derive external-call fixed inputs, outputs, base registers, cache registers,
  status registers, and clobbers from `CilExternalCall`.
- Give compiler intrinsics, virtual/interface dispatch, varargs lowering,
  multiply/divide helpers, exception helpers, and GC helpers explicit clobber
  descriptions.
- A method proven to be reachable only as the root entry may use non-reserved
  registers without preserving their incoming values. Exports and methods also
  reachable through ordinary calls must obey the normal ABI.

### GC and exceptions

- Every managed reference live at a safepoint must be present in a GC-visible
  frame slot, even when its working copy is allocated to a register.
- Synchronize dirty register roots immediately before a safepoint.
- Clear a root slot when its value dies so stale roots do not retain objects.
- Never make an address or data register the only GC-invisible copy of a live
  reference at a safepoint.
- Give handler-visible values canonical frame homes before instructions that
  can transfer control to a handler.
- Do not carry unresolved phi copies across try, filter, handler, or finally
  boundaries.
- Preserve A5 whenever a managed runtime or exception frame uses it.
- Preserve original IL offsets on machine operations and generated labels.

## Phase 1: Baseline and machine IR

1. Capture a pre-change baseline for the full test suite, IFFInspect, and
   MUITaskList:
   - code bytes;
   - estimated MC68000 cycles for existing execution fixtures;
   - stack-memory instruction count;
   - push/pop pair count;
   - frame size;
   - callee-save count.
2. Add internal compiler statistics for virtual values, spills, reloads,
   rematerializations, coalesced copies, D/A bank transfers, saved registers,
   allocation iterations, code bytes, and stack-memory instructions.
3. Introduce a machine IR with:
   - explicit basic blocks and successors;
   - typed virtual registers;
   - explicit uses and definitions;
   - phi nodes or block parameters;
   - fixed-register and register-class constraints;
   - clobber masks;
   - safepoint and exception-boundary markers;
   - source method and original IL offset;
   - explicit memory effects.
4. Lower normalized CIL stack states into virtual values. At block merges,
   create phi values rather than stack slots.
5. Add a machine-IR verifier. It must reject undefined uses, invalid phi
   inputs, inconsistent CFG edges, illegal type widths, and missing source
   offsets.

Phase gate:

- Existing methods can be lowered to verified machine IR without changing
  emitted code.
- The complete compiler test suite still passes.

## Phase 2: 68000 instruction constraints

1. Describe every emitted operation using legal register sets rather than
   implicit D0/D1/A0 scratch assumptions.
2. Precolor ABI inputs, return values, library bases, runtime-frame uses, and
   helper-specific operands.
3. Model partial-width operations correctly:
   - byte operations cannot use address registers;
   - word/byte results must retain their signedness and extension semantics;
   - `MOVEA` and `ADDA`/`SUBA` do not provide interchangeable condition-code
     behavior;
   - address-register writes have long-address semantics.
4. Represent comparisons and branches with an explicit condition-code
   dependency.
5. Model 64-bit register pairs and prohibit overlapping pair allocations.
6. Reserve scratch registers only at the constrained instruction, allowing
   live-range splitting around that point.
7. Encode precise call clobbers from the actual internal or external call
   descriptor. Do not conservatively invalidate the entire register file.

Phase gate:

- Constraint-verifier tests cover every supported CIL opcode and compiler
  intrinsic.
- Fixed-register and clobber behavior agrees with current ABI execution tests.

## Phase 3: Liveness and interference

1. Compute block `use`, `def`, `liveIn`, and `liveOut` sets to a fixed point.
2. Build split live ranges from SSA values, preserving holes where a value is
   not live.
3. Build an interference graph including:
   - ordinary simultaneous liveness;
   - precolored physical registers;
   - fixed operands and clobbers;
   - register-pair overlap;
   - phi-copy semantics;
   - condition-code dependencies.
4. Calculate loop nesting and use weights. Use approximately `10^loopDepth`
   for dynamic importance, saturating safely for deeply nested control flow.
5. Add verifier tests that compare computed liveness against a slow reference
   implementation on generated small CFGs.

Phase gate:

- No two interfering values can be assigned the same candidate register in
  allocator property tests.
- Unreachable blocks and exception edges are handled explicitly.

## Phase 4: Global allocator

Implement bank-aware optimistic graph coloring with iterated coalescing:

1. Add precolored physical-register nodes.
2. Partition values by legal register set while retaining interference between
   overlapping classes.
3. Coalesce copies and phi moves only when their legal sets intersect and
   George/Briggs safety permits it.
4. Simplify nodes whose degree is below their effective register count.
5. Freeze unprofitable coalescing candidates.
6. Choose spill candidates using the MC68000 cost model.
7. Assign colors in reverse simplification order.
8. Rewrite spills and split ranges, then rerun liveness/allocation until the
   graph is colorable.
9. Detect non-progress and fail with an internal compiler diagnostic rather
   than looping indefinitely.

Spill priority must include:

```text
weighted uses * reload cost
    + weighted definitions * store cost
    + bank-transfer cost
    + loop weight
    - rematerialization benefit
```

Also account for:

- the one-time save/restore and frame cost of opening a callee-saved register;
- shared `MOVEM` savings when multiple preserved registers are used;
- call-crossing ranges, which should prefer preserved registers;
- short ranges, which should prefer caller-saved registers;
- cold exception paths, which are preferred spill locations;
- D/A transfers and lost effective-address opportunities.

Phase gate:

- Allocator invariant and randomized-pressure tests pass.
- Spill rewriting converges for methods requiring more registers than exist.
- The allocator can use all legal registers in a root-only entry method.

## Phase 5: Splitting, spilling, and rematerialization

1. Split live ranges around:
   - calls;
   - fixed-register operations;
   - loop boundaries;
   - cold exception edges;
   - transitions between address-oriented and data-oriented use.
2. Allocate fixed frame spill slots after register allocation. Reuse a spill
   slot only when size, alignment, GC classification, and lifetimes permit it.
3. Reserve outgoing-call storage separately so spill displacements stay fixed.
4. Rematerialize rather than spill:
   - zero and small `MOVEQ` constants;
   - static, string, and type-descriptor addresses;
   - frame-relative addresses;
   - cheap sign or zero extensions when their recomputation is cheaper.
5. Resolve remaining parallel copies, including cycles, using a free register
   when possible and one dedicated non-GC temporary slot otherwise.
6. Ensure spill loads/stores use legal MC68000 addressing modes and widths.

Phase gate:

- No spill slot aliases a simultaneously live value.
- GC reference slots never alias scalar spill slots.
- Parallel-copy tests cover swaps, longer cycles, fixed destinations, and
  mixed D/A copies.

## Phase 6: GC, EH, and frame integration

1. Derive the frame layout from final allocation:
   - runtime header;
   - GC root homes;
   - scalar spills;
   - handler-visible homes;
   - outgoing arguments;
   - varargs and direct-call scratch;
   - actual callee-saved registers.
2. Replace evaluation-stack root synchronization with virtual-value liveness
   at safepoints.
3. Emit root stores only when a register copy is dirty and clear killed root
   homes before the next possible collection.
4. Spill handler-live values before potentially throwing instructions.
5. Verify exception-state stores remain ordered before the operation that can
   fault.
6. Generate prologue/epilogue saves from the registers actually allocated.
   Select individual moves or `MOVEM` using MC68000 byte/cycle cost.
7. Remove the old whole-local promotion decisions once the new allocator
   supplies local, argument, and temporary locations.

Phase gate:

- Forced-GC tests pass with references held in every allocatable D and A
  register.
- Try/catch/finally tests pass with live register values and forced spills.
- Frame metadata contains every required root and no scalar-only slot.

## Phase 7: Emission and migration

1. Teach 68k emission to consume allocated machine IR.
2. Keep a temporary internal old/new lowering switch for differential testing;
   do not add a public compiler option.
3. Compare old and new execution results automatically across all existing
   fixtures and CPU targets.
4. Remove backend pattern matchers made obsolete by virtual-register lowering,
   but retain profitable target-specific instruction selection.
5. Keep final peephole passes for encoding-level cleanup such as redundant
   moves, branch shortening, safe `CLR`, and post-allocation flag reuse.
6. Remove the old lowering path and internal switch only after all acceptance
   criteria pass.

## Required tests

Add focused tests for:

- straight-line arithmetic and comparisons;
- Boolean branches without materialization;
- loops with counters, accumulators, and pointer walks;
- nested calls, recursion, tail calls, and leaf methods;
- internal calls using D0/D1/A0/A1;
- Amiga library calls with A6 and cached bases;
- imports, exports, virtual/interface dispatch, and varargs;
- byte, word, long, transparent scalar, nullable, reference, and 64-bit values;
- maximum D-register, A-register, and pair pressure;
- GC at every safepoint with register-held references;
- try/catch/finally and exception filters supported by the compiler;
- root-only entries versus exported or ordinarily called entries;
- spill-slot reuse and rematerialization;
- M68000 execution plus M68020/M68040 compatibility.

After each phase, run the narrow relevant tests. Before completion, run:

```powershell
dotnet test .\Compiler.Tests\CopperSharp.Compiler.Tests.csproj --no-restore
dotnet build .\CopperSharp68k.slnx --no-restore
```

Regenerate the checked-in IFFInspect assembly and map through
`AmigaM68kCompiler`, using the same options as its compiler regression test.

## Definition of done

The goal is complete only when all of the following are true:

- [ ] The machine IR, verifier, liveness analysis, and allocator are active by
      default.
- [ ] The old whole-local promotion and evaluation-stack lowering path has been
      removed.
- [ ] All register constraints, interference, fixed operands, call clobbers,
      register pairs, and GC rules are mechanically verified.
- [ ] The complete compiler test suite passes.
- [ ] IFFInspect and MUITaskList compile and execute correctly where execution
      fixtures exist.
- [ ] Checked-in IFFInspect assembly and map are regenerated.
- [ ] No avoidable evaluation-stack traffic remains inside straight-line basic
      blocks.
- [ ] No self-moves or immediate push/pop pairs remain before peephole
      optimization.
- [ ] Representative workloads reduce stack-memory instructions by at least
      25% from the captured baseline.
- [ ] Aggregate estimated MC68000 cycles improve by at least 10%.
- [ ] No representative method regresses by more than 2% without a documented
      correctness or code-size reason.
- [ ] Original IL offsets still appear in diagnostics and exception metadata.
- [ ] `git diff --check` reports no whitespace errors.
- [ ] Final compiler statistics and before/after measurements are reported to
      the user.
