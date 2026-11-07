[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InputAssembly,
    [Parameter(Mandatory)][string]$OutputAssembly,
    [string[]]$ResolverPaths = @(),
    [string]$CilicaSource = '',
    [string]$SdkVersion = ''
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$inputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($InputAssembly)
$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputAssembly)
if (!(Test-Path -LiteralPath $inputPath -PathType Leaf)) { throw "Input assembly not found: $inputPath" }
$resolvedSearchPaths = @($ResolverPaths | ForEach-Object { $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($_) })
$cache = Join-Path $repo '.artifacts/CilicaOptimizer'
$revision = 'a879c85f1608c764c7ab4f52b66b2b5db1ee60b6'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
if (!$CilicaSource) {
    $CilicaSource = Join-Path $repo 'ThirdParty/Cilica'
} else {
    $CilicaSource = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($CilicaSource)
}
if (!(Test-Path -LiteralPath $CilicaSource -PathType Container)) { throw "CILica source not found: $CilicaSource" }
if (Test-Path -LiteralPath (Join-Path $CilicaSource '.git')) {
    $actualRevision = & git -C $CilicaSource rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $actualRevision -ne $revision) { throw "CILica source must be pinned to $revision" }
    $sourceChanges = & git -C $CilicaSource status --porcelain
    if ($LASTEXITCODE -ne 0 -or $sourceChanges) { throw 'Use an unmodified CILica checkout' }
} else {
    $upstream = Get-Content -LiteralPath (Join-Path $CilicaSource 'UPSTREAM.json') -Raw | ConvertFrom-Json
    if ($upstream.revision -ne $revision) { throw "CILica source must be pinned to $revision" }
}
if (!$SdkVersion) { $SdkVersion = (Get-Content -LiteralPath (Join-Path $repo 'global.json') -Raw | ConvertFrom-Json).sdk.version }
@{ sdk = @{ version = $SdkVersion; rollForward = 'disable' } } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $cache 'global.json')
$build = Join-Path $cache 'build'
Push-Location $cache
try {
    & dotnet build (Join-Path $PSScriptRoot 'CilicaOptimizer/CilicaOptimizer.csproj') -c Release --artifacts-path $build "-p:CilicaSource=$CilicaSource" -m:1 /nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw 'Restricted optimizer build failed' }
    $optimizerArgs = @((Join-Path $build 'bin/CilicaOptimizer/release/CilicaOptimizer.dll'), $inputPath, $outputPath)
    foreach ($path in $resolvedSearchPaths) { $optimizerArgs += @('--resolver', $path) }
    & dotnet @optimizerArgs
    if ($LASTEXITCODE -ne 0) { throw 'Restricted IL optimization failed' }
}
finally { Pop-Location }
