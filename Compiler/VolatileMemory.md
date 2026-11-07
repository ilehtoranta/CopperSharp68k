# Volatile memory accesses

CopperSharp supports the CIL `volatile.` prefix on scalar and reference memory
accesses for MC68000, MC68020, MC68040 and MC68060 output. This includes C#
`volatile` fields and prefixed `ldfld`, `stfld`, `ldsfld`, `stsfld`, `ldind.*`,
`stind.*`, and scalar `ldobj`/`stobj`. Floating-point access requires an admitted
floating-point mode, as elsewhere in the compiler.

Each prefixed access remains observable, including discarded reads and repeated
stores of the same value. The compiler preserves its operand width and does not
reuse a previous read, promote the location into a register, merge stores, narrow
the access, or move ordinary memory observations across its ordering boundary.
Method memory summaries carry this boundary through managed calls.

Volatile reads provide acquire ordering and volatile writes provide release
ordering. The initial implementation deliberately uses stronger synchronization:
each emitted volatile memory operation has `NOP` before and after it. Native FPU
modes additionally emit `FNOP` before each synchronization `NOP`. These barriers
are recorded as non-removable memory effects. Volatile zero stores use `MOVE`,
even with `ClrPolicy.Always`, avoiding the MC68000 `CLR` read-before-write cycle.

The hardware basis is the Motorola
[M68000 Family Programmer's Reference Manual](https://www.nxp.com/docs/en/reference-manual/M68000PRM.pdf),
section 3.3.5 and the `FNOP` instruction description;
[M68040 User's Manual](https://www.nxp.com/docs/en/reference-manual/MC68040UM.pdf),
section 7.7; and
[M68060 User's Manual](https://www.nxp.com/docs/en/data-sheet/MC68060UM.pdf),
section 10.2, timing note 6. Integer pipeline/bus synchronization and FPU
synchronization are distinct, so native FPU configurations retain both.

The decoder folds the prefix into its following instruction, retaining the
prefix's IL offset. Branches cannot enter between the prefix and memory access.
Trailing, repeated or misplaced prefixes report `C68K0009`. Aggregate, bulk
(`cpblk`/`initblk`) and `unaligned.` volatile accesses report `C68K0004`; these
forms have no implementation in this scope. Volatility qualifies the individual
access, rather than making all accesses to the same address volatile, following
the [CIL prefix semantics](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes.volatile?view=net-10.0).

Machine IR optimization remains enabled with volatile memory barriers. Late
access-combining preparations and raw-byte peepholes are conservatively disabled
inside methods containing volatile accesses. In `FixedPoint` mode, other methods
retain method-local peepholes; cross-method byte rewrites are disabled for that
image. `Bounded` mode skips raw-byte peepholes for the image to preserve its work
limit. Final branch and address relaxation still runs. Images without volatile
accesses keep their existing optimization policy.

Volatile does not add atomic read-modify-write operations, locks, or atomic
64-bit access. Wide scalars retain the backend's split longword accesses; even
a 32-bit operand can require multiple physical bus cycles on MC68000. Device
memory must have appropriate platform cache/MMU attributes. The compiler does
not flush caches or establish DMA or multicore cache coherence. Framework
`System.Threading.Volatile`, `Interlocked` and threading APIs are not newly
admitted by this prefix support.

## Execution coverage

[`VolatileExecutionTests`](../Compiler.Tests/VolatileExecutionTests.cs) runs
changing static and instance reads, reference reads, discarded reads, repeated
writes, polling, byte/word/long widths, split 64-bit transport, typed indirect
accesses, acquire/release publication and calls. Integer cases run across all four
output targets with disabled, fixed-point and bounded peepholes. Floating-point
cases cover software float and both native FPU configurations. Negative cases
cover malformed prefixes, prefix-bypassing branches and unsupported aggregate,
bulk and unaligned forms.

Execution uses Copper68k's bus interface to script changing values and assert
access count, width and order. MC68060 output runs on its MC68040 core; the
MC68020/68882 case also uses that core for the common FPU instruction subset.
These tests verify emitted accesses and program behavior, not physical cache,
write-buffer or external 68882 timing.

```powershell
dotnet test Compiler.Tests/CopperSharp.Compiler.Tests.csproj -c Release `
  --filter FullyQualifiedName~VolatileExecutionTests
```
