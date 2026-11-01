# CI execution-test scheduling

The Build workflow runs the existing fast-test filter and all discovered
`CompilerExecutionTests` cases. The 19 execution shards may run concurrently;
each runner starts four independent PowerShell/.NET test processes. Production
compiler code, assertions, CPU targets, and theory data are unchanged.
Together with the fast-test job, the matrix uses at most 20 concurrent jobs.
An independent `execution-build` job builds the compiler execution-test project
and its dependencies once with the pinned SDK. All execution shards download
the same complete output, including runtime dependencies and pinned CoreLib,
instead of restoring and rebuilding it on every runner. The fast-test job
continues to build and test the complete solution independently. The shared
build artifact is retained for 14 days; each worker still receives its own
private copy before executing tests.

`TestSharding.psm1` schedules independently named theory rows separately and
assigns the longest estimated groups first to the least-loaded worker. Exact
display-name filters escape VSTest operators. Names that VSTest cannot distinguish
(case-insensitive matches or abbreviated duplicate names) remain together, with
their complete multiplicities. The
versioned `execution-test-durations.json` comes from passing TRX reports. Each
entry is `[baseline case count, elapsed seconds]`, keyed by method and optional
CPU display-name parameter and, for split theories, a SHA-256 display-name identity.
Schema 2 records per-selection timings; schema 1 profiles remain supported by
scaling the old method/CPU estimate across its rows. Changed theory sizes scale the recorded estimate;
new groups use the baseline mean per-case duration and remain fully included.
The profile affects scheduling only; discovery determines what runs.

`run-test-shard.ps1` resolves the built test assembly once and runs it directly
through `dotnet test`, avoiding repeated project evaluation and preserving literal
quotes in display-name filters. It starts workers together, streams their console output, and
retains separate logs and TRX reports. Every invocation gets a fresh result
directory. Workers receive private copies of the complete built test output, as
well as separate temporary and generated-probe directories. Diagnostic writes
beside an assembly cannot race, and the original build output and previous
results are preserved. Native
filters stay below the existing 20,000-character limit.

Each worker compares its complete discovery list with its TRX results, including
the multiplicity of abbreviated theory display names. Missing, duplicate,
unexpected, or unexecuted cases fail the shard. Failed test batches do not stop
later batches or other workers from producing their results. CI retains the
plans, worker summaries, console logs, and all TRX reports for 14 days. Worker
summaries include UTC start/end times, monotonic elapsed seconds, and
each native batch's elapsed time and exit code. Use these measurements for
worker wall time: the xUnit adapter's per-case TRX timestamps can describe
result reporting rather than the actual execution interval. TRX case durations
remain useful for scheduling weights.
The fast job also runs `test-test-sharding.ps1`, which checks scheduling, filter
limits, coverage errors, process isolation, console output, and failure propagation.

Build the solution with the pinned SDK before running a shard locally:

```powershell
./scripts/run-test-shard.ps1 -ShardNumber 1 -ShardCount 19 -Workers 4 -Configuration Release
./scripts/run-test-shard.ps1 -ShardNumber 1 -ShardCount 19 -Workers 4 -Configuration Release -ListOnly
```

`Workers` defaults to 1 for local use. Set `DOTNET` to the pinned SDK executable
when it is installed outside `PATH`. `-DiscoveryFile` permits an offline planning
review with `-ListOnly`; actual execution always performs fresh discovery.

To refresh the timing profile, download **all 19** execution-result artifacts
from a successful Build run into an empty directory and run:

```powershell
./scripts/update-test-timings.ps1 -ResultsDirectory artifacts/passing-execution-results -BaselineRun https://github.com/ilehtoranta/CopperSharp68k/actions/runs/RUN_ID -SourceRevision SOURCE_SHA
```

Review and commit the profile with its source/run provenance. Use one run's
reports, not combined reruns or duplicate downloads. Benchmark changes with the
whole Build workflow and compare the exact test-name multiplicities and outcomes
against the baseline, in addition to wall-clock and runner time. The current
profile records all 4,376 passing execution cases from main `72aa1354`, including
the three execution-harness regressions. It uses timings collected with the
counted execution loop and default host tiered compilation.

The website publication still waits for the complete Build workflow to pass.
These scripts do not change publication history, recovery refs, or backups.

## Compilation versus emulation

Workers retain `phase-timings/phases-PID.jsonl` for the floating allocation,
StringBuilder value-formatting, and stable-text execution families. Each record
identifies the fixture entry, CPU, guest optimization mode, phase, and elapsed
milliseconds. Compilation wraps the actual compiler call; emulation includes
core creation, initialization, instruction stepping, and return checks. Pack
construction and assertions outside those scopes are excluded. Failed calls
also produce records; use the TRX reports to establish success before comparing.
Measurements never cache a compilation or omit a guest execution.

Summarize one run's downloaded execution artifacts with:

```powershell
./scripts/summarize-execution-phases.ps1 -ResultsDirectory artifacts/passing-execution-results
```

For a selected local test, set `COPPERSHARP_EXECUTION_PHASE_PROFILE` to a fresh
directory before starting `dotnet test`. An optional
`COPPERSHARP_EXECUTION_OPTIMIZER_PROFILE=1` additionally records internal compiler
stage snapshots there. Leave that second option off for timing comparisons.
Direct test runs without a phase directory do not collect timings. CI records
one measurement per compilation/execution, never per instruction. Sum of phase
times across workers is accumulated elapsed time, not workflow wall time.

The counted execution path removes callbacks used only to increment a counter
and retains the core's mutable state reference when no observation callback is
needed. Observation callbacks still run before every instruction that needs
them. Both paths execute one instruction per iteration, check halt and return,
and enforce the original instruction budget. Gateway lookups reject addresses
outside the registered range before consulting the dictionary; endpoints and
gaps retain their existing behavior.
