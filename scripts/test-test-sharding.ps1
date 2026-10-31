$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'TestSharding.psm1') -Force -DisableNameChecking
function Assert-True($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Assert-Throws([scriptblock]$Action, [string]$Pattern) {
    $caught = $null
    try { & $Action | Out-Null } catch { $caught = $_ }
    if (-not $caught -or $caught.ToString() -notmatch $Pattern) { throw "Expected failure matching '$Pattern', got '$caught'." }
}
$prefix = 'CopperSharp.Compiler.Tests.CompilerExecutionTests.'
$lines = @('Test discovery heading', "    $($prefix)Heavy(target: M68000)",
    "$($prefix)Heavy(target: M68020)", "$($prefix)Short", "$($prefix)Mixed(cpu: M68000)", "$($prefix)Mixed(value: 1)")
$cases = @(Get-ExecutionTestCases -Lines $lines)
Assert-True ($cases.Count -eq 5) 'Discovery lost a case or included a heading.'
$groups = @(Get-ExecutionTestGroups -Cases $cases)
Assert-True ($groups.Count -eq 5) 'Independently named theory rows were not split.'
Assert-True (@($groups | Where-Object TimingFallbackKey -eq Mixed).Count -eq 2) 'Mixed theory lost rows.'
$profile = @{ schemaVersion = 1; fallbackSecondsPerCase = 2.0
    groups = @{ 'Heavy|target: M68000' = @(1, 100.0); 'Heavy|target: M68020' = @(1, 90.0) } }
$plan = @(New-ExecutionTestPlan -Cases $cases -ShardCount 2 -Workers 2 -TimingProfile $profile)
$planned = @($plan | ForEach-Object { $_.Groups } | ForEach-Object { $_.Cases } | ForEach-Object DisplayName)
$results = @($planned | ForEach-Object { [pscustomobject]@{ Name = $_; Outcome = 'Passed' } })
Assert-ExecutionTestCoverage -Expected @($cases.DisplayName) -Results $results
Assert-True ($plan[0].Groups[0].Key -eq 'Heavy|target: M68000') 'Longest group was not scheduled first.'
Assert-True ($plan[1].Groups[0].Key -eq 'Heavy|target: M68020') 'Heavy groups were not spread across workers.'
$reversed = @($cases); [array]::Reverse($reversed)
$again = @(New-ExecutionTestPlan -Cases $reversed -ShardCount 2 -Workers 2 -TimingProfile $profile)
Assert-True (($plan | ConvertTo-Json -Depth 8 -Compress) -eq ($again | ConvertTo-Json -Depth 8 -Compress)) 'Planning depends on discovery order.'
# Exercise the complete CI matrix, including its last shard and every worker.
$matrixCases = @(Get-ExecutionTestCases -Lines @(1..152 | ForEach-Object { "$($prefix)Matrix(value: $_)" }))
$matrixPlan = @(New-ExecutionTestPlan -Cases $matrixCases -ShardCount 19 -Workers 4)
Assert-True ($matrixPlan.Count -eq 76) 'The CI matrix did not create all 76 workers.'
Assert-True (@($matrixPlan | Where-Object { $_.Groups.Count -eq 0 }).Count -eq 0) 'A CI worker received no cases.'
$matrixResults = @($matrixPlan | ForEach-Object Groups | ForEach-Object Cases | ForEach-Object {
    [pscustomobject]@{ Name = $_.DisplayName; Outcome = 'Passed' }
})
Assert-ExecutionTestCoverage -Expected @($matrixCases.DisplayName) -Results $matrixResults
$batches = @(Get-ExecutionTestFilters -Groups $groups -MaximumLength 300)
Assert-True (@($batches | Where-Object Length -gt 300).Count -eq 0) 'A filter exceeds the native command length limit.'
Assert-True (($batches -join '|') -eq (($groups | Sort-Object Key -Culture en-US | ForEach-Object Filter) -join '|')) 'Filter batching lost a group.'
Assert-Throws { Get-ExecutionTestFilters -Groups $groups -MaximumLength 5 } 'length limit'
Assert-Throws { New-ExecutionTestPlan -Cases @() -ShardCount 1 } 'No compiler execution'
Assert-Throws { New-ExecutionTestPlan -Cases $cases -ShardCount 1 -TimingProfile @{ schemaVersion = 99 } } 'schema'
$special = 'Name(value: "a&b|c=d!~(x)\path")'
Assert-True ((ConvertTo-ExecutionFilterValue $special) -eq 'Name\(value: "a\&b\|c\=d\!\~\(x\)\\path"\)') 'Filter operators were not escaped correctly.'
$collisionCases = @(Get-ExecutionTestCases -Lines @("$($prefix)Repeated(value: ABC)", "$($prefix)Repeated(value: abc)", "$($prefix)Repeated(value: ABC)"))
$collisionGroups = @(Get-ExecutionTestGroups -Cases $collisionCases)
Assert-True ($collisionGroups.Count -eq 1 -and $collisionGroups[0].Cases.Count -eq 3) 'Case-insensitive or abbreviated display names overlap between workers.'
Assert-True ($collisionGroups[0].Filter -match '&DisplayName=') 'A split theory does not use an equality filter.'
$legacyRows = @(Get-ExecutionTestCases -Lines @("$($prefix)Rows(target: M68000, entry: A)", "$($prefix)Rows(target: M68000, entry: B)"))
$legacyProfile = @{ schemaVersion = 1; fallbackSecondsPerCase = 1; groups = @{ 'Rows|target: M68000' = @(2, 100) } }
$legacyPlan = @(New-ExecutionTestPlan -Cases $legacyRows -ShardCount 1 -Workers 2 -TimingProfile $legacyProfile)
Assert-True (@($legacyPlan | Where-Object EstimatedSeconds -ne 50).Count -eq 0) 'Legacy CPU-group timing was not scaled across split rows.'
$expected = @('same', 'same', 'other')
$valid = @([pscustomobject]@{ Name = 'same'; Outcome = 'Passed' },
    [pscustomobject]@{ Name = 'same'; Outcome = 'Failed' }, [pscustomobject]@{ Name = 'other'; Outcome = 'Passed' })
Assert-ExecutionTestCoverage -Expected $expected -Results $valid
Assert-Throws { Assert-ExecutionTestCoverage -Expected $expected -Results @($valid[0]) } 'Missing execution'
Assert-Throws { Assert-ExecutionTestCoverage -Expected $expected -Results @($valid + $valid[0]) } 'duplicate'
Assert-Throws { Assert-ExecutionTestCoverage -Expected @('skip') -Results @([pscustomobject]@{ Name = 'skip'; Outcome = 'NotExecuted' }) } 'did not run'
$timings = Get-Content (Join-Path $PSScriptRoot 'ci/execution-test-durations.json') -Raw | ConvertFrom-Json -AsHashtable
$count = ($timings.groups.Values | ForEach-Object { $_[0] } | Measure-Object -Sum).Sum
Assert-True ($count -eq $timings.cases) 'Timing baseline case counts are inconsistent.'
Write-Host 'Planner, bounded filters, fallback weights, and coverage multiplicity checks passed.'

# Exercise real child processes with a small fake test adapter. This verifies
# isolation and failure propagation without recompiling the full fixture suite.
$root = Join-Path ([System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../artifacts/shard-planner-tests'))) ([guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($root) | Out-Null
$fake = Join-Path $root 'dotnet.ps1'
@'
$arguments = @($args)
$prefix = 'CopperSharp.Compiler.Tests.CompilerExecutionTests.'
if ($arguments[0] -eq 'msbuild') {
    $built = Join-Path $PSScriptRoot 'built'
    [IO.Directory]::CreateDirectory($built) | Out-Null
    $assembly = Join-Path $built 'fixture.dll'
    if (-not (Test-Path $assembly)) { [IO.File]::WriteAllBytes($assembly, [byte[]]@()) }
    $assembly
    exit 0
}
if ($arguments -contains '--list-tests') { foreach ($name in @('Alpha','Beta','Gamma','Delta')) { "$prefix$name" }; exit 0 }
if (-not $arguments[1].EndsWith('.dll')) { throw 'The worker must run the already-built assembly.' }
[IO.File]::WriteAllText((Join-Path ([IO.Path]::GetDirectoryName($arguments[1])) 'diagnostic.txt'), 'worker-owned')
$filter = $arguments[[array]::IndexOf($arguments, '--filter') + 1]
if ($filter -notmatch '\.(Alpha|Beta|Gamma|Delta)') { throw "Unexpected filter: $filter" }
$name = "$prefix$($Matches[1])"
$directory = $arguments[[array]::IndexOf($arguments, '--results-directory') + 1]
$log = @($arguments | Where-Object { $_ -like 'trx;LogFileName=*' })[0].Split('=')[1]
[System.IO.Directory]::CreateDirectory($directory) | Out-Null
$mode = $env:COPPERSHARP_SHARD_TEST_MODE
$outcome = if ($mode -in @('failed', 'failed-report')) { 'Failed' } else { 'Passed' }
$row = "<UnitTestResult testName='$name' outcome='$outcome'/>"
if ($mode -eq 'duplicate') { $row += $row }
if ($mode -eq 'missing') { $row = '' }
@{ Temporary = $env:TEMP; Tmp = $env:TMP; TmpDir = $env:TMPDIR; Probes = $env:COPPERSHARP_TEST_PROBE_ROOT; Assembly = $arguments[1] } |
    ConvertTo-Json | Set-Content (Join-Path $directory 'environment.json')
Start-Sleep -Milliseconds 100
"<TestRun><Results>$row</Results></TestRun>" | Set-Content (Join-Path $directory $log)
if ($mode -eq 'failed') { exit 7 }
exit 0
'@ | Set-Content -LiteralPath $fake
$emptyProfile = Join-Path $root 'timings.json'
'{"schemaVersion":1,"fallbackSecondsPerCase":1,"groups":{}}' | Set-Content $emptyProfile
$originalDotNet = $env:DOTNET
$originalMode = $env:COPPERSHARP_SHARD_TEST_MODE
try {
    $env:DOTNET = $fake
    Assert-Throws { & (Join-Path $PSScriptRoot 'run-test-shard.ps1') -ShardNumber 1 -ShardCount 1 -Workers 4 -ResultsDirectory (Join-Path $root 'built/results') -TimingFile $emptyProfile } 'outside the built test output'
    foreach ($mode in @('passed', 'failed', 'failed-report', 'duplicate', 'missing')) {
        $env:COPPERSHARP_SHARD_TEST_MODE = $mode
        $destination = Join-Path $root $mode
        & (Join-Path $PSScriptRoot 'run-test-shard.ps1') -ShardNumber 1 -ShardCount 1 -Workers 4 -ResultsDirectory $destination -TimingFile $emptyProfile *> (Join-Path $root "$mode.log")
        $code = $LASTEXITCODE
        Assert-True (($mode -eq 'passed' -and $code -eq 0) -or ($mode -ne 'passed' -and $code -ne 0)) "Incorrect exit code for $mode : $code"
        Assert-True ((Get-Content (Join-Path $root "$mode.log") -Raw) -match '\[worker [12]\] Worker') 'Worker console output was lost.'
        $summaries = @(Get-ChildItem $destination -Recurse -Filter worker-summary.json)
        Assert-True ($summaries.Count -eq 4) 'One failed worker prevented another from completing.'
        foreach ($summaryFile in $summaries) {
            $summary = Get-Content -LiteralPath $summaryFile.FullName -Raw | ConvertFrom-Json
            Assert-True ($summary.ElapsedSeconds -gt 0 -and $summary.Batches.Count -eq 1) 'Worker elapsed or batch timing was lost.'
            Assert-True ($summary.Batches[0].ElapsedSeconds -gt 0 -and $summary.Batches[0].ElapsedSeconds -le $summary.ElapsedSeconds) 'Batch timing exceeds the complete worker run.'
            Assert-True ([DateTimeOffset]$summary.FinishedAtUtc -gt [DateTimeOffset]$summary.StartedAtUtc) 'Worker UTC timing is invalid.'
            Assert-True (($mode -eq 'failed' -and $summary.Batches[0].ExitCode -eq 7) -or ($mode -ne 'failed' -and $summary.Batches[0].ExitCode -eq 0)) 'Native batch exit code was lost.'
        }
        $environments = @(Get-ChildItem $destination -Recurse -Filter environment.json | ForEach-Object { Get-Content $_.FullName -Raw | ConvertFrom-Json })
        Assert-True ($environments.Count -eq 4) 'All four workers did not execute.'
        Assert-True (@($environments.Temporary | Sort-Object -Unique).Count -eq 4) 'Workers share temporary files.'
        Assert-True (@($environments.Probes | Sort-Object -Unique).Count -eq 4) 'Workers share generated probes.'
        Assert-True (@($environments.Assembly | Sort-Object -Unique).Count -eq 4) 'Workers share their assembly output directory.'
        Assert-True (-not (Test-Path (Join-Path $root 'built/diagnostic.txt'))) 'A worker changed the original build output.'
        foreach ($environment in $environments) {
            Assert-True ($environment.Temporary -eq $environment.Tmp -and $environment.Tmp -eq $environment.TmpDir) 'Temporary-directory environment is inconsistent.'
        }
    }
}
finally { $env:DOTNET = $originalDotNet; $env:COPPERSHARP_SHARD_TEST_MODE = $originalMode }
Write-Host 'Process isolation, complete results, and failure/duplicate/missing-result guards passed.'
exit 0
