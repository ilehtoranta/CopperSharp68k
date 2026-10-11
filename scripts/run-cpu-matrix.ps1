param(
    [string]$Configuration = 'Release',
    [string]$ResultsDirectory = 'TestResults/CpuMatrix',
    [ValidateRange(1, 64)][int]$ShardCount = 32,
    [int[]]$ShardNumbers = @(),
    [switch]$InitializeOnly,
    [switch]$AuditOnly,
    [switch]$Resume
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repoRoot 'Compiler.Tests/CopperSharp.Compiler.Tests.csproj'
$binRoot = Join-Path $repoRoot "Compiler.Tests/bin/$Configuration/net10.0"
$resultsRoot = [IO.Path]::GetFullPath($ResultsDirectory)
$manifestPath = Join-Path $resultsRoot 'manifest.json'
$className = 'CopperSharp.Compiler.Tests.CompilerExecutionTests'
New-Item -ItemType Directory -Force -Path $resultsRoot | Out-Null

function Get-BinaryInputs {
    @(Get-ChildItem -LiteralPath $binRoot -Filter '*.dll' -Recurse |
        Sort-Object FullName | ForEach-Object {
            [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($binRoot, $_.FullName); Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
        })
}

function Assert-BinaryInputs {
    $current = Get-BinaryInputs | ConvertTo-Json -Depth 4 -Compress
    $expected = $manifest.BinaryInputs | ConvertTo-Json -Depth 4 -Compress
    if ($current -ne $expected) { throw 'Matrix binaries changed. Use a new results directory for the new build.' }
    if ((& dotnet --version).Trim() -ne $manifest.Sdk) { throw 'The SDK differs from the matrix manifest.' }
}

function Get-ExpectedCases($shard) {
    @($shard.Methods | ForEach-Object { $discoveredCases[$_] })
}

function Read-ShardResult($path, $methods, $minimumCases = 0, $expectedCases = @()) {
    if (!(Test-Path -LiteralPath $path)) { return $null }
    [xml]$trx = Get-Content -Raw -LiteralPath $path
    $counts = $trx.TestRun.ResultSummary.Counters
    $definitions = @{}
    foreach ($definition in $trx.TestRun.TestDefinitions.UnitTest) {
        $definitions[$definition.id] = $definition.TestMethod.className + '.' + $definition.TestMethod.name
    }
    $results = @($trx.TestRun.Results.UnitTestResult | Where-Object { $_ })
    $observed = @($results | ForEach-Object { $definitions[$_.testId] } | Sort-Object -Unique)
    $missing = @($methods | Where-Object { $_ -notin $observed })
    $unexpected = @($observed | Where-Object { $_ -notin $methods })
    $caseNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($result in $results) { [void]$caseNames.Add($result.testName) }
    # A method-only discovery entry is a deferred theory: execution can
    # expand it. Explicitly discovered rows must each appear in the TRX.
    $missingCases = @($expectedCases | Where-Object { $_.Contains('(') -and !$caseNames.Contains($_) })
    $nonPassingResults = @($results | Where-Object outcome -ne 'Passed')
    $errors = @($trx.TestRun.ResultSummary.RunInfos.RunInfo | Where-Object { $_ -and $_.outcome -eq 'Error' })
    $passed = $counts -and [int]$counts.total -gt 0 -and [int]$counts.total -ge $minimumCases -and [int]$counts.failed -eq 0 -and
        [int]$counts.notExecuted -eq 0 -and [int]$counts.aborted -eq 0 -and
        [int]$counts.passed -eq [int]$counts.total -and $missing.Count -eq 0 -and
        $unexpected.Count -eq 0 -and $errors.Count -eq 0 -and
        $results.Count -eq [int]$counts.total -and $nonPassingResults.Count -eq 0 -and $missingCases.Count -eq 0
    [pscustomobject]@{ Passed = [bool]$passed; Total = [int]$counts.total; Failed = [int]$counts.failed;
        MissingMethods = $missing; UnexpectedMethods = $unexpected; MissingCases = $missingCases; Trx = $path }
}

if (Test-Path -LiteralPath $manifestPath) {
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    if ($manifest.ShardCount -ne $ShardCount -or $manifest.Configuration -ne $Configuration -or $manifest.Repository -ne $repoRoot) {
        throw 'Requested matrix settings differ from the existing manifest.'
    }
    Assert-BinaryInputs
} else {
    $listed = & dotnet test $project -c $Configuration --no-build --no-restore --list-tests --filter "FullyQualifiedName~$className" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Test discovery failed: $listed" }
    $listed | Set-Content -LiteralPath (Join-Path $resultsRoot 'discovery.txt')
    $pattern = "^($([regex]::Escape($className))\.[^(]+)"
    $methods = @($listed | ForEach-Object {
        if ($_.ToString().Trim() -match $pattern) { $Matches[1] }
    } | Group-Object | Sort-Object @{ Expression = 'Count'; Descending = $true }, Name)
    if ($methods.Count -lt $ShardCount) { throw 'Too few discovered methods for this shard count.' }
    $shards = @(1..$ShardCount | ForEach-Object {
        [pscustomobject]@{ Number = $_; DiscoveredCases = 0; Methods = [Collections.Generic.List[string]]::new() }
    })
    foreach ($method in $methods) {
        $shard = $shards | Sort-Object DiscoveredCases, Number | Select-Object -First 1
        $shard.Methods.Add($method.Name)
        $shard.DiscoveredCases += $method.Count
    }
    $manifest = [pscustomobject]@{ SchemaVersion = 1; Repository = $repoRoot; Configuration = $Configuration;
        Sdk = (& dotnet --version).Trim(); ShardCount = $ShardCount; MethodCount = $methods.Count;
        BinaryInputs = Get-BinaryInputs; Shards = $shards;
        DiscoverySha256 = (Get-FileHash -LiteralPath (Join-Path $resultsRoot 'discovery.txt')).Hash }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath
    Assert-BinaryInputs
}

$discoveryPath = Join-Path $resultsRoot 'discovery.txt'
if (!(Test-Path -LiteralPath $discoveryPath)) { throw 'The retained test discovery is missing.' }
if ($manifest.DiscoverySha256 -and (Get-FileHash -LiteralPath $discoveryPath).Hash -ne $manifest.DiscoverySha256) {
    throw 'The retained test discovery differs from the manifest.'
}
$discoveredCases = @{}
foreach ($shard in $manifest.Shards) {
    foreach ($method in $shard.Methods) {
        if ($discoveredCases.ContainsKey($method)) { throw "Duplicate method assignment: $method" }
        $discoveredCases[$method] = [Collections.Generic.List[string]]::new()
    }
}
foreach ($line in Get-Content -LiteralPath $discoveryPath) {
    $name = $line.Trim()
    if ($name -match '^([^(]+)' -and $discoveredCases.ContainsKey($Matches[1])) {
        $discoveredCases[$Matches[1]].Add($name)
    } elseif ($name.StartsWith($className + '.', [StringComparison]::Ordinal)) {
        throw "Discovered method is absent from the manifest: $name"
    }
}
if ($discoveredCases.Count -ne $manifest.MethodCount) { throw 'Manifest method count differs from its assignments.' }
foreach ($shard in $manifest.Shards) {
    if (@($shard.Methods | Where-Object { $discoveredCases[$_].Count -eq 0 }).Count -or
        @(Get-ExpectedCases $shard).Count -ne $shard.DiscoveredCases) {
        throw "Retained discovery differs from shard $($shard.Number)."
    }
}

if ($InitializeOnly) {
    Write-Host "Initialized $($manifest.MethodCount) methods in $ShardCount shards. Discovery counts are advisory; theories may expand during execution."
    exit 0
}
if ($AuditOnly) {
    $completed = @()
    $incomplete = @()
    $total = 0
    foreach ($shard in $manifest.Shards) {
        $summaryPath = Join-Path $resultsRoot ('shard-{0:D2}/summary.json' -f $shard.Number)
        $result = $null
        if (Test-Path -LiteralPath $summaryPath) {
            $saved = Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json
            $result = Read-ShardResult $saved.Trx $shard.Methods $shard.DiscoveredCases (Get-ExpectedCases $shard)
        }
        if ($result -and $result.Passed) { $completed += $shard.Number; $total += $result.Total }
        else { $incomplete += $shard.Number }
    }
    $audit = [pscustomobject]@{ Complete = $incomplete.Count -eq 0; VerifiedShards = $completed;
        IncompleteShards = $incomplete; PassedCases = $total; MethodCount = $manifest.MethodCount;
        Manifest = $manifestPath }
    $audit | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $resultsRoot 'audit.json')
    $audit | ConvertTo-Json -Depth 5
    if (!$audit.Complete) { exit 2 }
    exit 0
}
if ($ShardNumbers.Count -eq 0) { $ShardNumbers = @(1..$ShardCount) }
if (@($ShardNumbers | Sort-Object -Unique).Count -ne $ShardNumbers.Count -or
    @($ShardNumbers | Where-Object { $_ -lt 1 -or $_ -gt $ShardCount }).Count -ne 0) {
    throw 'ShardNumbers must be unique and within the manifest range.'
}
$failedShards = 0
foreach ($number in $ShardNumbers) {
    Assert-BinaryInputs
    $shard = $manifest.Shards | Where-Object Number -eq $number
    $shardRoot = Join-Path $resultsRoot ('shard-{0:D2}' -f $number)
    $summaryPath = Join-Path $shardRoot 'summary.json'
    if ($Resume -and (Test-Path -LiteralPath $summaryPath)) {
        $previous = Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json
        $verified = Read-ShardResult $previous.Trx $shard.Methods $shard.DiscoveredCases (Get-ExpectedCases $shard)
        if ($verified -and $verified.Passed) { Write-Host "Verified completed shard $number; resuming after it."; continue }
    }
    New-Item -ItemType Directory -Force -Path $shardRoot | Out-Null
    $attempt = 1
    while (Test-Path -LiteralPath (Join-Path $shardRoot "attempt-$attempt")) { $attempt++ }
    $attemptRoot = Join-Path $shardRoot "attempt-$attempt"
    New-Item -ItemType Directory -Path $attemptRoot | Out-Null
    $filter = ($shard.Methods | Sort-Object | ForEach-Object { "FullyQualifiedName=$_" }) -join '|'
    Write-Host "Running shard $number/$ShardCount, attempt $attempt, $($shard.Methods.Count) methods."
    & dotnet test $project -c $Configuration --no-build --no-restore --filter $filter --logger 'trx;LogFileName=tests.trx' --results-directory $attemptRoot 2>&1 |
        Tee-Object -FilePath (Join-Path $attemptRoot 'test.log')
    $testExitCode = $LASTEXITCODE
    Assert-BinaryInputs
    $result = Read-ShardResult (Join-Path $attemptRoot 'tests.trx') $shard.Methods $shard.DiscoveredCases (Get-ExpectedCases $shard)
    if (!$result) { $result = [pscustomobject]@{ Passed = $false; Total = 0; Failed = 0; Trx = (Join-Path $attemptRoot 'tests.trx') } }
    if ($testExitCode -ne 0) { $result.Passed = $false }
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $summaryPath
    if (!$result.Passed) { $failedShards++ }
}
if ($failedShards -gt 0) { exit 1 }
