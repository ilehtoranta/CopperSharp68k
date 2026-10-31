param(
    [Parameter(Mandatory = $true)][ValidateRange(1, 64)][int]$ShardNumber,
    [Parameter(Mandatory = $true)][ValidateRange(1, 64)][int]$ShardCount,
    [ValidateRange(1, 8)][int]$Workers = 1,
    [string]$Configuration = 'Debug',
    [string]$ResultsDirectory = 'TestResults',
    [string]$TimingFile = (Join-Path $PSScriptRoot 'ci/execution-test-durations.json'),
    [string]$DiscoveryFile,
    [switch]$ListOnly
)
$ErrorActionPreference = 'Stop'
if ($ShardNumber -gt $ShardCount) { throw 'ShardNumber must not be greater than ShardCount.' }
if ($DiscoveryFile -and -not $ListOnly) { throw 'Offline discovery is supported only with -ListOnly.' }
Import-Module (Join-Path $PSScriptRoot 'TestSharding.psm1') -Force -DisableNameChecking
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repository 'Compiler.Tests/CopperSharp.Compiler.Tests.csproj'
$dotnet = if ($env:DOTNET) { $env:DOTNET } else { 'dotnet' }
$assembly = $null
if ($DiscoveryFile) { $listedTests = Get-Content -LiteralPath $DiscoveryFile }
else {
    $targetPath = & $dotnet msbuild $project -nologo "-property:Configuration=$Configuration" -getProperty:TargetPath 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Could not resolve the built test assembly: $($targetPath -join [Environment]::NewLine)" }
    $assembly = ($targetPath -join [Environment]::NewLine).Trim()
    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw "Build the test project first: $assembly" }
    $arguments = @('test', $assembly,
        '--list-tests', '--filter', 'FullyQualifiedName~CopperSharp.Compiler.Tests.CompilerExecutionTests')
    $listedTests = & $dotnet @arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Test discovery failed: $($listedTests -join [Environment]::NewLine)" }
}
$cases = @(Get-ExecutionTestCases -Lines @($listedTests | ForEach-Object { $_.ToString() }))
$timings = Get-Content -LiteralPath $TimingFile -Raw | ConvertFrom-Json -AsHashtable
$plan = @(New-ExecutionTestPlan -Cases $cases -ShardCount $ShardCount -Workers $Workers -TimingProfile $timings)
$selected = @($plan | Where-Object { $_.Shard -eq $ShardNumber -and $_.CaseCount -gt 0 })
if ($selected.Count -eq 0) { throw "Execution-test shard $ShardNumber of $ShardCount is empty." }
$caseCount = ($selected | Measure-Object CaseCount -Sum).Sum
Write-Host "Execution-test shard $ShardNumber/$ShardCount : $caseCount of $($cases.Count) cases, $($selected.Count) isolated workers."
if ($ListOnly) {
    [pscustomobject]@{
        Shard = $ShardNumber; Cases = $caseCount; TotalCases = $cases.Count
        Workers = @($selected | ForEach-Object {
            [pscustomobject]@{ Worker = $_.Worker; Cases = $_.CaseCount
                EstimatedSeconds = $_.EstimatedSeconds; Filters = @(Get-ExecutionTestFilters -Groups $_.Groups.ToArray()) }
        })
    } | ConvertTo-Json -Depth 5 -Compress
    exit 0
}
# Unique run directories retain old evidence and exclude stale TRX files.
$run = Join-Path ([System.IO.Path]::GetFullPath($ResultsDirectory)) "shard-$ShardNumber-$([guid]::NewGuid().ToString('N'))"
$sourceDirectory = [System.IO.Path]::GetDirectoryName($assembly)
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
if ($run.StartsWith($sourceDirectory.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, $comparison)) {
    throw 'ResultsDirectory must be outside the built test output to avoid recursive copies.'
}
[System.IO.Directory]::CreateDirectory($run) | Out-Null
$records = @()
$processes = @()
$exitCode = 0
function Receive-WorkerOutput($Child, [bool]$IsError) {
    $property = if ($IsError) { 'ErrorTask' } else { 'OutputTask' }
    $reader = if ($IsError) { $Child.Process.StandardError } else { $Child.Process.StandardOutput }
    $log = if ($IsError) { $Child.ErrorLog } else { $Child.OutputLog }
    while ($null -ne $Child.$property -and $Child.$property.IsCompleted) {
        $line = $Child.$property.GetAwaiter().GetResult()
        if ($null -eq $line) { $Child.$property = $null; break }
        Write-Host "[worker $($Child.Worker)] $line"
        $log.WriteLine($line)
        $Child.$property = $reader.ReadLineAsync()
    }
}
try {
    foreach ($worker in $selected) {
        $directory = Join-Path $run "worker-$($worker.Worker)"
        # Some theories write diagnostics beside their assembly. Give each
        # worker its own complete output tree, leaving the built inputs intact.
        $assemblyDirectory = Join-Path $directory 'assembly'
        [System.IO.Directory]::CreateDirectory($assemblyDirectory) | Out-Null
        foreach ($item in (Get-ChildItem -LiteralPath $sourceDirectory -Force)) {
            Copy-Item -LiteralPath $item.FullName -Destination $assemblyDirectory -Recurse
        }
        $workerAssembly = Join-Path $assemblyDirectory ([System.IO.Path]::GetFileName($assembly))
        $record = [pscustomobject]@{
            Shard = $ShardNumber; Worker = $worker.Worker; DotNet = $dotnet
            Project = $project; SourceAssembly = $assembly; Assembly = $workerAssembly; Configuration = $Configuration
            EstimatedSeconds = $worker.EstimatedSeconds
            Filters = @(Get-ExecutionTestFilters -Groups $worker.Groups.ToArray())
            ExpectedCases = @($worker.Groups | ForEach-Object { $_.Cases } | ForEach-Object DisplayName)
            ResultsDirectory = $directory; TempDirectory = (Join-Path $directory 'temporary')
            ProbeDirectory = (Join-Path $directory 'probes')
        }
        $records += $record
        $workerPlan = Join-Path $run "worker-plan-$($worker.Worker).json"
        $record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $workerPlan
        $start = [System.Diagnostics.ProcessStartInfo]::new()
        $start.FileName = (Get-Process -Id $PID).Path
        $start.WorkingDirectory = $repository
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        foreach ($argument in @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'run-test-worker.ps1'), '-Plan', $workerPlan)) {
            $start.ArgumentList.Add($argument)
        }
        $process = [System.Diagnostics.Process]::Start($start)
        $stdout = [System.IO.StreamWriter]::new((Join-Path $run "worker-$($worker.Worker).stdout.log"))
        $stderr = [System.IO.StreamWriter]::new((Join-Path $run "worker-$($worker.Worker).stderr.log"))
        $stdout.AutoFlush = $stderr.AutoFlush = $true
        $processes += [pscustomobject]@{ Process = $process; Worker = $worker.Worker
            OutputTask = $process.StandardOutput.ReadLineAsync(); ErrorTask = $process.StandardError.ReadLineAsync()
            OutputLog = $stdout; ErrorLog = $stderr }
    }
    do {
        $pending = $false
        foreach ($child in $processes) {
            Receive-WorkerOutput $child $false
            Receive-WorkerOutput $child $true
            if (-not $child.Process.HasExited -or $null -ne $child.OutputTask -or $null -ne $child.ErrorTask) { $pending = $true }
        }
        if ($pending) { Start-Sleep -Milliseconds 100 }
    } while ($pending)
    foreach ($child in $processes) {
        $child.Process.WaitForExit()
        if ($child.Process.ExitCode -ne 0) { $exitCode = $child.Process.ExitCode }
    }
}
catch { $exitCode = 1; throw }
finally {
    foreach ($child in $processes) {
        if (-not $child.Process.HasExited) { $child.Process.Kill($true); $child.Process.WaitForExit() }
        $child.OutputLog.Dispose(); $child.ErrorLog.Dispose(); $child.Process.Dispose()
    }
    [pscustomobject]@{
        SchemaVersion = 1; Shard = $ShardNumber; Shards = $ShardCount; TotalCases = $cases.Count
        Cases = $caseCount; Workers = $records; ExitCode = $exitCode
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $run 'shard-plan.json')
}
exit $exitCode
