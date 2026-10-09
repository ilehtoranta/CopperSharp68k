param(
    [string]$Rom = $env:COPPERSHARP_KICKSTART31_ROM,
    [string]$CopperScreenRoot = (Join-Path $PSScriptRoot '../../CopperScreen'),
    [string]$ResultsDirectory = (Join-Path $PSScriptRoot '../TestResults/NativeOs')
)

$ErrorActionPreference = 'Stop'
if (-not $Rom -or -not (Test-Path -LiteralPath $Rom -PathType Leaf)) {
    throw 'Supply -Rom or COPPERSHARP_KICKSTART31_ROM. Native execution requires your own Kickstart 3.1 ROM.'
}
$project = Join-Path $PSScriptRoot '../Compiler.NativeOsRunner.Tests/CopperSharp.Compiler.NativeOsRunner.Tests.csproj'
$engineRoot = (Resolve-Path -LiteralPath $CopperScreenRoot).Path
$restoreArguments = @('restore', $project, "-p:CopperScreenRoot=$engineRoot")
$engineNuGetConfig = Join-Path $engineRoot 'NuGet.Config'
if (Test-Path -LiteralPath $engineNuGetConfig) {
    $restoreArguments += @('--configfile', $engineNuGetConfig)
}
& dotnet @restoreArguments
if ($LASTEXITCODE -ne 0) { throw 'Native OS runner dependency restore failed.' }

$previousRom = $env:COPPERSHARP_KICKSTART31_ROM
$previousFileStatsRecords = $env:COPPERSHARP_NATIVE_FILESTATS_RESULTS
try {
    $env:COPPERSHARP_KICKSTART31_ROM = (Resolve-Path -LiteralPath $Rom).Path
    $env:COPPERSHARP_NATIVE_FILESTATS_RESULTS = Join-Path ([System.IO.Path]::GetFullPath($ResultsDirectory)) 'FileStats'
    & dotnet test $project -c Release --no-restore "-p:CopperScreenRoot=$engineRoot" `
        --logger 'trx;LogFileName=native-os.trx' --results-directory $ResultsDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Native OS verification failed; inspect the TRX report.' }
}
finally {
    $env:COPPERSHARP_KICKSTART31_ROM = $previousRom
    $env:COPPERSHARP_NATIVE_FILESTATS_RESULTS = $previousFileStatsRecords
}
