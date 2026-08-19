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
$methods = $listedTests |
    ForEach-Object {
        $line = $_.ToString().Trim()
        if ($line -match $methodPattern) {
            $Matches[1]
        }
    } |
    Group-Object |
    Sort-Object @{ Expression = "Count"; Descending = $true }, Name

if ($methods.Count -eq 0) {
    throw "No $className tests were discovered."
}

$assignments = @()
$loads = [long[]]::new($ShardCount)
for ($index = 0; $index -lt $ShardCount; $index++) {
    $assignments += ,([System.Collections.Generic.List[string]]::new())
}

foreach ($method in $methods) {
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

$filter = ($selectedMethods |
    Sort-Object |
    ForEach-Object { "FullyQualifiedName=$_" }) -join "|"
$caseCount = $loads[$shardIndex]
Write-Host "Running execution-test shard ${ShardNumber}/${ShardCount}: $($selectedMethods.Count) methods, $caseCount discovered cases."
if ($ListOnly) {
    exit 0
}

$testArguments = @(
    "test", $project,
    "--configuration", $Configuration,
    "--no-build",
    "--no-restore",
    "--filter", $filter,
    "--logger", "trx;LogFileName=tests.trx",
    "--results-directory", $ResultsDirectory
)
& dotnet @testArguments
exit $LASTEXITCODE
