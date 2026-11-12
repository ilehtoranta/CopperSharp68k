# 32-bit division pairing

Source verification: 12 November 2026. This describes development source, not a
newly published package.

The backend combines matching CIL `div`/`rem` or `div.un`/`rem.un` operations
inside one basic block. Both operands and signedness must match. The second
operation becomes a copy of the saved result of the first division. Operand
evaluation remains in place, and the pass does not move division onto another
control-flow path. Mutable frame fields and potentially aliased outputs retain
the existing conservative value-tracking rules.

## Native results

The [Motorola programmer's reference manual](https://www.nxp.com/docs/en/reference-manual/M68000PRM.pdf),
DIVS/DIVSL and DIVU/DIVUL sections, documents long division with separate
32-bit quotient and remainder registers. The compiler uses this existing
32/32-to-32r:32q encoding for MC68020 and later output.

| First operation | MC68000 allocated outputs | MC68020/040/060 allocated outputs |
| --- | --- | --- |
| Quotient | quotient D0, remainder D3 | quotient D0, remainder D2 |
| Remainder | remainder D0, quotient D2 | remainder D0, quotient D2 |

`M68kMachineArithmeticOptimizer` now runs division pairing before its MC68000-only
strength-reduction guard and chooses the secondary register for the target.
The native hardware instruction initially produces quotient D0 and remainder
D2. A remainder-first pair uses `EXG D0,D2` to satisfy the allocated primary and
secondary result contract. MC68000 emission is unchanged.

This fixes the former MC68020 behavior where a paired source helper still
emitted two `DIVU.L` instructions. It does not add 64-bit division fusion or
change signed-overflow policy, package identity or public APIs.

## Regression checks

`LongDivisionPairTests` checks:

- Exactly one native division for matching inputs, in either operation order.
- Separate divisions for mutated/aliased inputs, different signedness and
  operations separated by control flow.
- Independent full-width quotient and remainder values for unsigned operands
  and representable signed quotients, including high-bit boundaries and seeded
  random inputs. Minimum signed value divided by -1 is outside this corpus.
- Branches on the remainder's zero/sign conditions after the native exchange,
  with and without the peephole optimizer.
- MC68020 and MC68040 execution. As elsewhere in the suite, compatible integer
  MC68060 output executes on the MC68040 model; this is not MC68060 timing coverage.

The 16 initial code-generation regression cases fail against the frozen old
compiler because they find two divisions instead of one. The final focused
arithmetic selection/pairing run passes all 318 cases, including the existing
divide-by-zero exception/sign tests and MC68000 arithmetic checks.

## MC68020 measurements

Before and after compiler production sources differ only in the two backend
files. Native handlers compile from identical managed input files
and compiler options. The CILica/SDK and existing local modifications are held
constant. Negative percentages below mean fewer emulated cycles.

| Probe | Before | After | Change |
| --- | ---: | ---: | ---: |
| Paired arithmetic wrapper, 290 vectors | 58,996 | 47,470 | -19.54% |
| Actual HUNK `AmigaGeometryMath.DivRem`, 418 vectors | 45,790 | 30,476 | -33.44% |

The separate `Divide`/`Remainder` wrapper control has identical per-vector cycle
counts before and after. Disassembly and execution of the actual CopperFFS
MC68020 HUNK confirm two long division instructions before and one after, with
correct quotient/remainder values, zero-divisor outputs and output-buffer guards.
The MC68000 CopperFFS control is byte-identical between compilers.

The full-ROM paired comparison uses a licensed Kickstart 3.1, an emulated
MC68020, MC68020 handlers, 512-byte blocks and synchronous CopperHDF storage.
Three trials alternate arm order. All data checks, independent image walks and
paired read/write/flush counts pass for the four OFS/FFS comparisons.

| Profile | Mutations | Read/enumeration verification | One-byte reads | Zero-length reads | Seeks |
| --- | ---: | ---: | ---: | ---: | ---: |
| OFS | -0.156% | -0.019% | -0.396% | -0.166% | -0.004% |
| FFS | -0.209% | -0.080% | -0.436% | -0.221% | +0.006% |
| SFS2 CSUN | -0.252% | -0.013% | -0.008% | -0.030% | +0.155% |
| SFS2 TRST | -3.255% | -0.001% | -0.006% | +0.003% | -0.066% |

These are synthetic instruction-cycle and emulated bus-cycle measurements,
respectively, not host timings or physical-device throughput. The FFS seek
difference is within the existing 0.1% workload allowance.
The additional four SFS2 comparisons also pass their data, persisted-image and
paired I/O checks. Three pass the timing allowance; the CSUN small-packet test
fails because seek cycles increase by 0.155%, above the 0.1% allowance. This
regression is retained in the report rather than classifying every workload as
a speedup. Overall, seven of eight full-ROM timing comparisons pass.

## Host and native coverage

- Both compiler solution builds succeed. The current CopperFS Release solution
  build and complete test run pass: 1,439 passed and three external-media skips.
  CopperFS merge work was left untouched; native comparisons use the frozen
  sources and identical input assemblies rather than mixing concurrent edits.
- The focused arithmetic run passes 318 cases. The existing geometry arithmetic
  consumer suite passes all seven cases with each compiler. MC68000 arithmetic
  samples and the separate-call MC68020 control are unchanged.
- The MC68020-only adaptations of CopperStart's copy/cache replays and the
  actual HUNK helper test pass 81 cases, with two optional SFS2 cache-summary
  skips. All three successfully built target handlers report zero managed
  allocation sites. The final executable sizes change from 60,544 to 60,540
  bytes for FFS, 191,428 to 191,268 for SFS2 CSUN, and 182,716 to 182,588 for
  SFS2 TRST.
- The optional broad compiler run was stopped after 32 minutes while running
  MC68000 nullable-float/StringBuilder tests. It reports 7,712 passes, three
  failures and one skip before cancellation; it is **not a complete suite pass**.
  Both `LoopPhiPolicyIsOptInAndRestrictedToItsQualifiedProfile` cases and
  `Net10ProfileBaselineTests.CurrentBaselineCanBeReproduced` also fail with the
  frozen old compiler. Their pinned expectations were not changed. A separate
  old-compiler run of the observed MC68000
  `CoreLibGenericSingleNullableJoinsEntry` case passes in 70 seconds; the broad
  run is not classified as a reproduced deadlock.

## Reproduction and scope

```powershell
dotnet build CopperSharp68k.slnx -c Release
dotnet test Compiler.Tests/CopperSharp.Compiler.Tests.csproj -c Release --filter "FullyQualifiedName~LongDivisionPairTests|FullyQualifiedName~IntegerArithmeticSelectionTests"
```

The retained integration evidence is under the sibling CopperFS checkout's
ignored `artifacts/divrem-68020-20261112/`: frozen compiler/consumer sources,
CLI inputs and HUNKs, arithmetic samples, native replay source adaptations and
TRX receipts, plus `rom/manifest.json`, `rom-sfs2/manifest.json`,
`actual-hunk-divrem-disassembly.txt`, `results-summary.json` and cycle samples. The copied
CopperStart replay fixtures explicitly select MC68020, since MC68000 cannot
execute these output instructions. No production replay source was changed.

The classic buffered SFS MC68020 profile does not build with either compiler:
both fail in `BuildTrstProof` with the same pre-existing unsupported allocated
constant-call error at IL_023B. This profile is not counted as a pass. Physical
hardware and other ROM versions were not qualified. No packages or ROMs were
published.
