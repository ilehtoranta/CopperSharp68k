# MC68000 register allocation

## Architecture

The compiler's default backend uses machine IR, verification, liveness analysis, interference construction, global register allocation, spill rewriting, and final emission. The former whole-local/evaluation-stack lowering path has been removed.

Allocation respects the distinct data and address register banks, fixed operands, call clobbers, register pairs, exception boundaries, managed-root requirements, and original IL offsets. Spill and rematerialization rewrites are verified before final peephole optimization. The allocated emitter rejects self-moves and immediate long push/pop or push/discard pairs.

GC-visible references remain either in described registers or typed frame-root slots at safepoints. Exception/unwind state and exported Amiga callback conventions take precedence over local register-pressure improvements.

## Acceptance baseline and result

The following dated measurements record the transition to the current allocator. They are historical evidence, not permanent performance ceilings. The baseline was reconstructed from the pre-allocation compiler and the final measurements used the same MC68000 fixtures.

### Generated samples

| Sample | Baseline code bytes | Final code bytes | Baseline assembly lines | Final assembly lines | Baseline stack-memory instructions | Final stack-memory instructions | Change |
|---|---:|---:|---:|---:|---:|---:|---:|
| IFFInspect | 7,682 | 8,466 | 2,637 | 3,210 | 789 | 449 | -43.09% |
| MUITaskList | 3,667 | 6,352 | 1,340 | 2,495 | 262 | 331 | +26.34% |
| Aggregate | 11,349 | 14,818 | 3,977 | 5,705 | 1,051 | 780 | **-25.79%** |

The aggregate met the required 25% stack-memory reduction. Larger generated samples reflected explicit allocated control flow and precise EH/GC state rather than an unchecked code-size trade.

Final post-peephole scans found no immediate push/pop pairs and no executable self-moves. A textual `$3000` word in MUITaskList may render like `move.w d0,d0`, but it is data rather than an instruction.

### Deterministic MC68000 cycle corpus

| Fixture | Baseline cycles | Final cycles | Change |
|---|---:|---:|---:|
| `ArithmeticEntry` | 6,012 | 5,770 | -4.03% |
| `QuickArithmeticEntry` | 278 | 106 | -61.87% |
| `BranchAssignedLocalsEntry` | 296 | 72 | -75.68% |
| `NarrowByteArithmeticEntry` | 196 | 136 | -30.61% |
| `NarrowShortArithmeticEntry` | 168 | 72 | -57.14% |
| Aggregate | 6,950 | 6,156 | **-11.42%** |

Every representative fixture improved and the aggregate exceeded the required 10% reduction. The cycle corpus remains a regression test.

### Representative allocator statistics

The acceptance run captured these pre-peephole totals:

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

Rewrite virtual values may exceed original values because spill rewriting introduces reload and rematerialization values.

## Verification contract

Future allocator work must preserve:

- mechanical verification of constraints, interference, clobbers, pairs, roots, and exception boundaries;
- original IL offsets in diagnostics and exception metadata;
- end-to-end compilation and execution of representative examples;
- zero pre-peephole self-moves and immediate stack round trips;
- the deterministic cycle and stack-traffic regression suites; and
- explicit documentation for any method-level performance regression accepted for correctness or code-size reasons.

See [the machine optimizer documentation](PeepholeOptimizations.md) for the passes that run after allocated emission.
