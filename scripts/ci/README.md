# CI execution-test scheduling

The Build workflow runs the existing fast-test filter and all discovered
`CompilerExecutionTests` cases. The 16 execution shards may run concurrently;
each runner starts two independent PowerShell/.NET test processes. Production
compiler code, assertions, CPU targets, and theory data are unchanged.

`TestSharding.psm1` keeps the existing method/CPU selection groups intact and
assigns the longest estimated groups first to the least-loaded worker. The
versioned `execution-test-durations.json` comes from passing TRX reports. Each
entry is `[baseline case count, elapsed seconds]`, keyed by method and optional
CPU display-name parameter. Changed theory sizes scale the recorded estimate;
new groups use the baseline mean per-case duration and remain fully included.
The profile affects scheduling only; discovery determines what runs.

`run-test-shard.ps1` starts workers together, streams their console output, and
retains separate logs and TRX reports. Every invocation gets a fresh result
directory. Workers have separate temporary and generated-probe directories, so
test fixtures remain isolated and previous results are preserved. Native
filters stay below the existing 20,000-character limit.

Each worker compares its complete discovery list with its TRX results, including
the multiplicity of abbreviated theory display names. Missing, duplicate,
unexpected, or unexecuted cases fail the shard. Failed test batches do not stop
later batches or other workers from producing their results. CI retains the
plans, worker summaries, console logs, and all TRX reports for 14 days. The
fast job also runs `test-test-sharding.ps1`, which checks scheduling, filter
limits, coverage errors, process isolation, console output, and failure propagation.

Build the solution with the pinned SDK before running a shard locally:

```powershell
./scripts/run-test-shard.ps1 -ShardNumber 1 -ShardCount 16 -Workers 2 -Configuration Release
./scripts/run-test-shard.ps1 -ShardNumber 1 -ShardCount 16 -Workers 2 -Configuration Release -ListOnly
```

`Workers` defaults to 1 for local use. Set `DOTNET` to the pinned SDK executable
when it is installed outside `PATH`. `-DiscoveryFile` permits an offline planning
review with `-ListOnly`; actual execution always performs fresh discovery.

To refresh the timing profile, download **all 16** execution-result artifacts
from a successful Build run into an empty directory and run:

```powershell
./scripts/update-test-timings.ps1 -ResultsDirectory artifacts/passing-execution-results -BaselineRun https://github.com/ilehtoranta/CopperSharp68k/actions/runs/RUN_ID -SourceRevision SOURCE_SHA
```

Review and commit the profile with its source/run provenance. Use one run's
reports, not combined reruns or duplicate downloads. Benchmark changes with the
whole Build workflow and compare the exact test-name multiplicities and outcomes
against the baseline, in addition to wall-clock and runner time. The initial
profile records all 4,373 passing execution cases from main `fa32cd8`.

The website publication still waits for the complete Build workflow to pass.
These scripts do not change publication history, recovery refs, or backups.
