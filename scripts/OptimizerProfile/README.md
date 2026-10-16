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

Compile metrics exclude pack construction, inventory hashing, artifact writing
and target execution. Instrumented profiles locate costs; qualify performance
with profiling disabled and matched inputs that differ only in the compiler.
Use reversed variant order for repeated comparisons, retain all samples, and
compare generated image, map and framework-analysis hashes. A small number of
local samples does not establish statistical significance or a universal speedup.
