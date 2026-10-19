param(
    [Parameter(Mandatory)][string]$BinPath,
    [Parameter(Mandatory)][string]$ResultsDirectory,
    [string[]]$Profiles = @('inherited-list', 'floating-left-allocation', 'join-callbacks'),
    [ValidateRange(1, 20)][int]$Repetitions = 1
)

$ErrorActionPreference = 'Stop'
$bin = (Resolve-Path -LiteralPath $BinPath).Path
$results = [IO.Path]::GetFullPath($ResultsDirectory)
if (Test-Path -LiteralPath $results) { throw 'Use a fresh results directory; existing measurements are never overwritten.' }
if ((& dotnet --version).Trim() -ne '10.0.301') { throw 'This fixture corpus requires SDK 10.0.301.' }
New-Item -ItemType Directory -Path $results | Out-Null
$project = Join-Path $PSScriptRoot 'OptimizerProfile/OptimizerProfile.csproj'
$artifacts = Join-Path $results 'probe-artifacts'
& dotnet build $project -c Release --artifacts-path $artifacts -m:1 /nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw 'Could not build the profiling driver.' }
$probe = Join-Path $artifacts 'bin/OptimizerProfile/release/OptimizerProfile.dll'
& dotnet $probe --validate-inputs $bin
if ($LASTEXITCODE -ne 0) { throw 'Profiling inputs did not validate.' }
$previousTiering = $env:DOTNET_TieredCompilation
$previousProfiling = $env:COPPERSHARP_PROFILE_OPTIMIZER
try {
    $env:DOTNET_TieredCompilation = '0'
    $env:COPPERSHARP_PROFILE_OPTIMIZER = '1'
    foreach ($repeat in 1..$Repetitions) {
        foreach ($profile in $Profiles) {
            & dotnet $probe $profile $bin (Join-Path $results "$repeat/$profile") FixedPoint
            if ($LASTEXITCODE -ne 0) { throw "Profiling failed: $repeat/$profile" }
        }
    }
} finally {
    $env:DOTNET_TieredCompilation = $previousTiering
    $env:COPPERSHARP_PROFILE_OPTIMIZER = $previousProfiling
}
