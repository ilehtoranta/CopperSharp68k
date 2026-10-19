# Optimizer profiling

Build the compiler test fixtures with SDK 10.0.301, then profile their Release
output without rebuilding it between samples:

```powershell
dotnet build Compiler.Tests/CopperSharp.Compiler.Tests.csproj -c Release --artifacts-path TestResults/ProfileBuild -m:1 /nodeReuse:false
./scripts/profile-optimizer.ps1 -BinPath TestResults/ProfileBuild/bin/CopperSharp.Compiler.Tests/release -ResultsDirectory TestResults/OptimizerProfile -Repetitions 2
```

The driver verifies the pinned runtime 10.0.9 CoreLib hash and constructs the
same fingerprinted implementation pack used by the fixture tests. It supports
ten StringBuilder and generic-join workloads; run the driver with `--list` to
inspect them. Each sample is a fresh process with tiered compilation disabled.
Keep other test and profiling workloads stopped when interpreting timings.
The script requires a fresh results directory and restores its environment
variables when finished.

Each sample retains the request, recursive DLL hashes, Hunk image, map,
framework analysis, compile metrics and `optimizer-profile.json`. The profiler
is internal and opt-in on the calling thread. Normal compilation does not
collect measurements or allocate profiler sessions.

Measurements distinguish inclusive from exclusive wall time: nested measured
stages are deducted from the enclosing stage's exclusive time. Allocated bytes
are inclusive calling-thread allocation traffic, so nested values must not be
summed. Counters record analysis rounds, rewrites, instructions, work-list visits,
stream-cache hits, decoded streams and indexed fixups. These diagnostics do not
change optimization order, convergence limits or generated output.

Fixup-index counters distinguish storage builds from storage reuses. Reuse
keeps dictionary capacity; every entry is rebuilt from the current fixups on
each uncached decode. The indexed-fixup count still includes every visit, so
lower allocation must not be interpreted as fewer fixup scans or proven CPU
time savings.

The `analysis.decode.0` through `analysis.decode.F` measurements group the
remaining analysis-dispatch calls by opcode high nibble. Simple one-word
instructions and the existing unrelocated MOVE fast path bypass these scopes.
These are allocation/time measurements of the shared recognition path, not
counts of emitted instruction families. Text rendering requests formatted
operands; instruction analysis uses the same handler order and validation with
text construction disabled.

The `dataflow.caller.*` scopes identify snapshot construction requested by the
peephole and condition-code passes (or direct callers). `dataflow.total` now
covers snapshot construction, liveness and stack depth. Value ranges and
condition provenance run on their first query and retain their own scopes;
they are no longer nested inside `dataflow.total`. When comparing an older
eager profile with a deferred profile, do not compare `dataflow.total` alone
or add the older nested propagation allocations a second time.

The `dataflow.requested-facts`, `dataflow.requested-values` and
`dataflow.requested-conditions` counters count results queried for each component,
once per result rather than once per query. `dataflow.spill-snapshot-reuses`
counts spill checks that consume the current peephole round's result. A successful
earlier rewrite short-circuits that round before the spill check. Deferred
propagation uses captured inputs and does not reuse results across rounds.

Emitted-instruction liveness combines data, address, condition-code and memory
masks in one indexed successor pass. This avoids interface-enumerator boxing
without changing the reverse instruction sweep or fixed-point transfer rules.
The `dataflow.liveness` scope isolates this traversal; warmed regression cases
require zero allocation there, while graph and snapshot construction remain in
`dataflow.total`. Compare `dataflow.liveness-rounds` and exact facts as well as
allocation bytes when validating a traversal change.

The `allocation.*` scopes separate machine block liveness, instruction liveness,
interference construction, coloring, coalescing, individual coalescing checks and
verification. Coalescing checks nest inside coalescing and coloring; inclusive
allocations must not be added together. Liveness helpers can also be called by
other machine passes, so their whole-compilation call counts may exceed allocator
iteration counts. Counters track allocator iterations, liveness rounds/block
visits and interference instructions. Coalescing uses two run-local scratch sets,
cleared and recomputed for each candidate/neighbor; it does not cache group
membership or degrees across merges.

Compile metrics exclude pack construction, inventory hashing, artifact writing
and target execution. Instrumented profiles locate costs; qualify performance
with profiling disabled and matched inputs that differ only in the compiler.
Use reversed variant order for repeated comparisons, retain all samples, and
compare generated image, map and framework-analysis hashes. A small number of
local samples does not establish statistical significance or a universal speedup.
