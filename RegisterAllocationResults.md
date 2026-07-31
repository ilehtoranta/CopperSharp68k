# MC68000 Register Allocation Results

This report records the acceptance measurements for
`RegisterAllocationGoal.md`. Measurements use the baseline definitions in
`RegisterAllocationBaseline.md`.

## Verification

- Compiler tests: 402 passed, 0 failed, 0 skipped.
- Solution build: succeeded with 0 warnings and 0 errors.
- IFFInspect: compiled through `AmigaM68kCompiler` with the regression-test
  options; its checked-in assembly and map were regenerated.
- MUITaskList: compiled through its regression-test fixture.
- The allocated emitter verifies its instruction stream before the final
  peephole optimizer and rejects self-moves and immediate long push/pop or
  push/discard pairs.

## Generated sample code

| Sample | Baseline code bytes | Final code bytes | Baseline assembly lines | Final assembly lines | Baseline stack-memory instructions | Final stack-memory instructions | Change |
|---|---:|---:|---:|---:|---:|---:|---:|
| IFFInspect | 7,682 | 8,466 | 2,637 | 3,210 | 789 | 449 | -43.09% |
| MUITaskList | 3,667 | 6,352 | 1,340 | 2,495 | 262 | 331 | +26.34% |
| Aggregate | 11,349 | 14,818 | 3,977 | 5,705 | 1,051 | 780 | **-25.79%** |

The aggregate stack-memory result satisfies the required 25% reduction. The
larger generated images are primarily the cost of explicit per-method
allocated control flow, precise EH/GC state handling, and broader reachable
sample coverage. Code size was not traded for stack traffic blindly: the
cycle-measured execution corpus below improves in every method.

Final post-peephole sample scans found:

| Sample | Pushes | Pops | Immediate push/pop pairs | Code self-moves |
|---|---:|---:|---:|---:|
| IFFInspect | 8 | 9 | 0 | 0 |
| MUITaskList | 81 | 7 | 0 | 0 |

The textual MUITaskList data section contains the word `$3000`, which the
assembly renderer displays as `move.w d0,d0`; it is data, not an executed
self-move.

## Deterministic MC68000 cycle corpus

| Fixture entry | Baseline cycles | Final cycles | Change |
|---|---:|---:|---:|
| ArithmeticEntry | 6,012 | 5,770 | -4.03% |
| QuickArithmeticEntry | 278 | 106 | -61.87% |
| BranchAssignedLocalsEntry | 296 | 72 | -75.68% |
| NarrowByteArithmeticEntry | 196 | 136 | -30.61% |
| NarrowShortArithmeticEntry | 168 | 72 | -57.14% |
| Aggregate | 6,950 | 6,156 | **-11.42%** |

The aggregate satisfies the required 10% improvement. No representative
method regressed, so the 2% per-method regression allowance was not used.
These measurements are enforced by
`RegisterAllocationCycleCorpusMeetsMc68000Target`.

## Final allocator statistics

Statistics below are sums of per-method data captured before final peephole
optimization.

| Statistic | IFFInspect | MUITaskList |
|---|---:|---:|
| Methods | 24 | 15 |
| Original virtual values | 1,314 | 703 |
| Allocated/rewrite virtual values | 1,314 | 722 |
| Spilled values | 0 | 22 |
| Reloads | 0 | 20 |
| Rematerialized values | 0 | 8 |
| Coalesced copies | 682 | 268 |
| D/A bank transfers | 52 | 33 |
| Callee-saved register uses | 67 | 28 |
| Spill-frame bytes | 0 | 48 |
| GC root slots | 8 | 2 |
| Allocation iterations | 24 | 16 |
| Pre-peephole code bytes | 5,776 | 3,890 |
| Pre-peephole stack-memory instructions | 521 | 461 |

Allocated/rewrite virtual values can exceed original virtual values after
spill rewriting creates reload and rematerialization values.

## Definition of done

- [x] Machine IR, verification, liveness, and allocation are active by default.
- [x] The former whole-local/evaluation-stack method lowering path and its
      internal switch are removed.
- [x] Constraints, interference, fixed operands, clobbers, register pairs,
      roots, and exception boundaries are verified mechanically and by focused
      tests.
- [x] The complete compiler test suite passes.
- [x] IFFInspect and MUITaskList compile and their available execution fixtures
      pass.
- [x] IFFInspect assembly and map are regenerated.
- [x] Straight-line allocated emission is register based; its pre-peephole
      verifier rejects immediate stack round trips.
- [x] No pre-peephole self-moves or immediate push/pop pairs remain.
- [x] Representative stack-memory traffic is reduced by at least 25%.
- [x] Aggregate estimated MC68000 cycles improve by at least 10%.
- [x] No representative method regresses.
- [x] Original IL offsets remain attached to MIR operations, diagnostics, and
      exception metadata.
- [x] `git diff --check` reports no whitespace errors.

