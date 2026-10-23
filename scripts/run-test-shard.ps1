param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 64)]
    [int]$ShardNumber,

    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 64)]
    [int]$ShardCount,

    [string]$Configuration = "Debug",
    [string]$ResultsDirectory = "TestResults",
    [switch]$ListOnly
)

$ErrorActionPreference = "Stop"

if ($ShardNumber -gt $ShardCount) {
    throw "ShardNumber $ShardNumber must not be greater than ShardCount $ShardCount."
}

$shardIndex = $ShardNumber - 1

$project = Join-Path $PSScriptRoot "..\Compiler.Tests\CopperSharp.Compiler.Tests.csproj"
$className = "CopperSharp.Compiler.Tests.CompilerExecutionTests"
$listArguments = @(
    "test", $project,
    "--configuration", $Configuration,
    "--no-build",
    "--no-restore",
    "--list-tests",
    "--filter", "FullyQualifiedName~$className"
)
$listedTests = & dotnet @listArguments 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Test discovery failed with exit code $LASTEXITCODE.`n$($listedTests -join [Environment]::NewLine)"
}

$methodPattern = "^($([regex]::Escape($className))\.[^(]+)"
$cases = @($listedTests |
    ForEach-Object {
        $line = $_.ToString().Trim()
        if ($line -match $methodPattern) {
            $method = $Matches[1]
            $cpu = $null
            if ($line -match '(?:\(|, )(?<parameter>target|cpu|cpuTarget): (?<cpu>M68000|M68020|M68040|M68060)(?=,|\))') {
                $cpu = "$($Matches.parameter): $($Matches.cpu)"
            }
            [pscustomobject]@{ Method = $method; Cpu = $cpu }
        }
    })

# Keep a theory's CPU rows together, but distribute its targets independently.
# Large formatting theories otherwise occupy most of one runner's entire budget.
$groups = @(foreach ($method in ($cases | Group-Object Method)) {
    if (@($method.Group | Where-Object { -not $_.Cpu }).Count -eq 0) {
        foreach ($cpu in ($method.Group | Group-Object Cpu)) {
            [pscustomobject]@{
                Name = "(FullyQualifiedName=$($method.Name)&DisplayName~$($cpu.Name))"
                Count = $cpu.Count
            }
        }
    }
    else {
        [pscustomobject]@{ Name = "FullyQualifiedName=$($method.Name)"; Count = $method.Count }
    }
}) | Sort-Object @{ Expression = "Count"; Descending = $true }, Name

if ($groups.Count -eq 0) {
    throw "No $className tests were discovered."
}

$assignments = @()
$loads = [long[]]::new($ShardCount)
for ($index = 0; $index -lt $ShardCount; $index++) {
    $assignments += ,([System.Collections.Generic.List[string]]::new())
}

foreach ($method in $groups) {
    $targetShard = 0
    for ($index = 1; $index -lt $ShardCount; $index++) {
        if ($loads[$index] -lt $loads[$targetShard]) {
            $targetShard = $index
        }
    }

    $assignments[$targetShard].Add($method.Name)
    $loads[$targetShard] += $method.Count
}

$selectedMethods = $assignments[$shardIndex]
if ($selectedMethods.Count -eq 0) {
    throw "Execution-test shard $ShardNumber of $ShardCount is empty."
}

# Bound each native command line on Windows as well as hosted Linux runners.
$filters = [System.Collections.Generic.List[string]]::new()
$batch = ""
foreach ($selection in ($selectedMethods | Sort-Object)) {
    if ($batch.Length -ne 0 -and $batch.Length + 1 + $selection.Length -gt 20000) {
        $filters.Add($batch)
        $batch = ""
    }
    $batch = if ($batch.Length -eq 0) { $selection } else { "$batch|$selection" }
}
if ($batch.Length -ne 0) { $filters.Add($batch) }
$caseCount = $loads[$shardIndex]
Write-Host "Running execution-test shard ${ShardNumber}/${ShardCount}: $($selectedMethods.Count) groups, $caseCount discovered cases, $($filters.Count) batches."
if ($ListOnly) {
    [pscustomobject]@{
        Shard = $ShardNumber
        Cases = $caseCount
        TotalCases = $cases.Count
        Filters = $filters.ToArray()
    } | ConvertTo-Json -Compress
    exit 0
}

$shardExitCode = 0
for ($batchIndex = 0; $batchIndex -lt $filters.Count; $batchIndex++) {
    $logName = if ($filters.Count -eq 1) { "tests.trx" } else { "tests-$($batchIndex + 1).trx" }
    $testArguments = @(
        "test", $project,
        "--configuration", $Configuration,
        "--no-build",
        "--no-restore",
        "--filter", $filters[$batchIndex],
        "--logger", "trx;LogFileName=$logName",
        "--logger", "console;verbosity=normal",
        "--results-directory", $ResultsDirectory
    )
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) { $shardExitCode = $LASTEXITCODE }
}
exit $shardExitCode
