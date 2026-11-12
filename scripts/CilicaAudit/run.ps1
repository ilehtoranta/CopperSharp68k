param(
    [string]$ResultsDirectory = 'TestResults/CilicaAuditRun',
    [string]$SdkVersion = '10.0.301',
    [ValidateRange(1, 20)][int]$Repetitions = 3,
    [switch]$Restricted
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$results = if ([IO.Path]::IsPathRooted($ResultsDirectory)) { [IO.Path]::GetFullPath($ResultsDirectory) } else { [IO.Path]::GetFullPath((Join-Path $repo $ResultsDirectory)) }
if (Test-Path -LiteralPath $results) { throw "Choose a fresh results directory: $results" }
New-Item -ItemType Directory -Path $results | Out-Null
$revision = 'ca199fd583f492e156f2617dff43b5f28ff8684d'
$source = Join-Path $results 'upstream'
function Invoke-Dotnet {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $Arguments" }
}
& git clone https://github.com/hez2010/Cilica.git $source
if ($LASTEXITCODE -ne 0) { throw 'Clone failed' }
& git -C $source checkout --detach $revision
if ($LASTEXITCODE -ne 0) { throw 'Checkout failed' }
& git -C $repo rev-parse HEAD | Set-Content (Join-Path $results 'compiler-head.txt')
& git -C $repo diff --binary | Set-Content (Join-Path $results 'compiler-worktree.patch')
& git -C $repo ls-files --others --exclude-standard | Set-Content (Join-Path $results 'untracked-inputs.txt')
@{ sdk = @{ version = $SdkVersion; rollForward = 'disable' } } | ConvertTo-Json | Set-Content (Join-Path $results 'global.json')
$build = Join-Path $results 'build'
$driverProject = Join-Path $PSScriptRoot 'CilicaAudit.csproj'
$fixtureProject = Join-Path $PSScriptRoot 'Fixtures/Fixtures.csproj'
$pipelines = if ($Restricted) { @('portable', 'restricted') } else { @('default', 'scalar', 'portable', 'restricted') }
$previousTiering = $env:DOTNET_TieredCompilation
Push-Location $results
try {
    $env:DOTNET_TieredCompilation = '0'
    Invoke-Dotnet @('build', $driverProject, '-c', 'Release', '--artifacts-path', $build, '-m:1', '/nodeReuse:false')
    Invoke-Dotnet @('build', $fixtureProject, '-c', 'Release', '--artifacts-path', $build, '-m:1', '/nodeReuse:false')
    $driver = Join-Path $build 'bin/CilicaAudit/release/CilicaAudit.dll'
    $fixture = Join-Path $build 'bin/Fixtures/release/CilicaAudit.Fixtures.dll'
    foreach ($pipeline in $pipelines) {
        $rewritten = Join-Path (Split-Path $fixture) "$pipeline/CilicaAudit.Fixtures.dll"
        Invoke-Dotnet @($driver, 'rewrite', $fixture, $rewritten, $pipeline)
        Invoke-Dotnet @($driver, 'inspect', $rewritten, (Join-Path $results "$pipeline.il.txt"))
    }
    Invoke-Dotnet @($driver, 'inspect', $fixture, (Join-Path $results 'baseline.il.txt'))
    if (!$Restricted) {
        Invoke-Dotnet @('build', (Join-Path $source 'src/Cilica.Cli/Cilica.Cli.csproj'), '-c', 'Release', '--artifacts-path', (Join-Path $results 'upstream-build'), '-m:1', '/nodeReuse:false')
        $cliOutput = Join-Path $results 'cli-default.dll'
        Invoke-Dotnet @((Join-Path $results 'upstream-build/bin/Cilica.Cli/release/Cilica.Cli.dll'), '-i', $fixture, '-o', $cliOutput, '--strict', '--verify-each', '--no-pdb-gen', '--verbosity', 'Warn')
        if ((Get-FileHash $cliOutput).Hash -ne (Get-FileHash (Join-Path (Split-Path $fixture) 'default/CilicaAudit.Fixtures.dll')).Hash) { throw 'Core API output differs from stock CLI' }
    }
    if ($Restricted) {
        Invoke-Dotnet @($driver, 'bench', $fixture, (Join-Path $results 'kernels'), "$Repetitions", 'baseline,portable,restricted')
        & (Join-Path $PSScriptRoot 'summarize-restricted.ps1') -ResultsDirectory (Join-Path $results 'kernels') -Repetitions $Repetitions
    } else {
        Invoke-Dotnet @($driver, 'bench', $fixture, (Join-Path $results 'kernels'), "$Repetitions")
    }
    # Build examples against the same already-built compiler inputs. These are
    # compilation/footprint probes; native OS calls are not executed by this bus.
    foreach ($name in @('Polymorphism', 'StopwatchBenchmark')) {
        Invoke-Dotnet @('build', (Join-Path $repo "Sdk.Amiga/Examples/$name/$name.csproj"), '-c', 'Release', '--artifacts-path', $build, '-p:BuildProjectReferences=false', '-m:1', '/nodeReuse:false')
        $example = Join-Path $build "bin/$name/release/$name.dll"
        foreach ($pipeline in $pipelines) {
            Invoke-Dotnet @($driver, 'rewrite', $example, (Join-Path (Split-Path $example) "$pipeline/$name.dll"), $pipeline)
        }
        $entry = if ($name -eq 'Polymorphism') { 'PolymorphismExample.Program::Main' } else { 'StopwatchBenchmarkExample.Program::Main' }
        Invoke-Dotnet @($driver, 'example', $example, (Join-Path $results "example-$name"), $entry)
    }
}
finally {
    $env:DOTNET_TieredCompilation = $previousTiering
    Pop-Location
}
