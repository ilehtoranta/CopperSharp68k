# Register Allocation Baseline

Captured before machine-IR register allocation was enabled.

## Environment

- CPU target: MC68000
- Compiler tests: 380 passed
- IFFInspect and MUITaskList: checked-in generated assembly
- Code bytes: highest mapped symbol end
- Stack-memory instructions: assembly instructions containing `(a7)`,
  `(a7)+`, or `-(a7)`
- Immediate push/pop pairs: adjacent long push followed by pop or four-byte
  stack discard

## Samples

| Sample | Code bytes | Assembly lines | Stack-memory instructions | Pushes | Pops | Immediate push/pop pairs | Self-moves |
|---|---:|---:|---:|---:|---:|---:|---:|
| IFFInspect | 7,682 | 2,637 | 789 | 128 | 114 | 3 | 12 |
| MUITaskList | 3,667 | 1,340 | 262 | 62 | 40 | 23 | 12 |
| Aggregate | 11,349 | 3,977 | 1,051 | 190 | 154 | 26 | 24 |

## Deterministic MC68000 cycle corpus

The pre-change compiler was reconstructed from `HEAD` in an isolated archive
and the same fixture methods were compiled as HUNK images and executed to the
return sentinel on Copper68k's MC68000 model.

| Fixture entry | Baseline cycles |
|---|---:|
| ArithmeticEntry | 6,012 |
| QuickArithmeticEntry | 278 |
| BranchAssignedLocalsEntry | 296 |
| NarrowByteArithmeticEntry | 196 |
| NarrowShortArithmeticEntry | 168 |
| Aggregate | 6,950 |

The initial aggregate acceptance thresholds are therefore:

- Stack-memory instructions: at most 788, a reduction of at least 25%.
- Estimated MC68000 cycles: at most 6,255 across the deterministic corpus, a
  reduction of at least 10%.
- Per-method regression: no more than 2% without a documented correctness or
  code-size tradeoff.
