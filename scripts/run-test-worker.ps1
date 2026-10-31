param([Parameter(Mandatory = $true)][string]$Plan)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Import-Module (Join-Path $PSScriptRoot 'TestSharding.psm1') -Force -DisableNameChecking
$worker = Get-Content -LiteralPath $Plan -Raw | ConvertFrom-Json
foreach ($directory in @($worker.ResultsDirectory, $worker.TempDirectory, $worker.ProbeDirectory)) {
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
}
# These changes are local to this worker process, avoiding environment races.
$env:TEMP = $env:TMP = $env:TMPDIR = $worker.TempDirectory
$env:COPPERSHARP_TEST_PROBE_ROOT = $worker.ProbeDirectory
if ([string]::IsNullOrEmpty($env:COPPERSHARP_EXECUTION_PHASE_PROFILE)) {
    $env:COPPERSHARP_EXECUTION_PHASE_PROFILE = Join-Path $worker.ResultsDirectory 'phase-timings'
}
$exitCode = 0
for ($batch = 0; $batch -lt $worker.Filters.Count; $batch++) {
    Write-Host "Worker $($worker.Worker): batch $($batch + 1)/$($worker.Filters.Count)."
    & $worker.DotNet test $worker.Assembly `
        --filter $worker.Filters[$batch] --logger "trx;LogFileName=tests-$($batch + 1).trx" `
        --logger 'console;verbosity=normal' --results-directory $worker.ResultsDirectory
    if ($LASTEXITCODE -ne 0) { $exitCode = $LASTEXITCODE }
}
$results = @()
try {
    $results = @(foreach ($report in (Get-ChildItem -LiteralPath $worker.ResultsDirectory -Filter 'tests-*.trx')) {
        [xml]$document = [System.IO.File]::ReadAllText($report.FullName)
        foreach ($result in $document.TestRun.Results.UnitTestResult) {
            [pscustomobject]@{ Name = $result.testName; Outcome = $result.outcome }
        }
    })
    Assert-ExecutionTestCoverage -Expected $worker.ExpectedCases -Results $results
}
catch {
    Write-Error $_ -ErrorAction Continue
    $exitCode = 1
}
$failed = @($results | Where-Object Outcome -eq 'Failed').Count
if ($failed -gt 0 -and $exitCode -eq 0) { $exitCode = 1 }
[pscustomobject]@{
    Worker = $worker.Worker; Discovered = $worker.ExpectedCases.Count; Reported = $results.Count
    Passed = @($results | Where-Object Outcome -eq 'Passed').Count
    Failed = $failed
    ExitCode = $exitCode
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $worker.ResultsDirectory 'worker-summary.json')
exit $exitCode
