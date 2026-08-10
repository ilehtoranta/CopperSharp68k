[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $OutputPath,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$configuration = 'Debug'
$framework = 'net10.0'
$compilerProject = Join-Path $repositoryRoot 'Compiler.Cli\CopperSharp.Compiler.Cli.csproj'
$builderProject = Join-Path $repositoryRoot 'Compiler.BootAdf\CopperSharp.Compiler.BootAdf.csproj'
$dosProject = Join-Path $repositoryRoot 'Sdk.Amiga\Examples\DOS\DOS.csproj'
$fileStatsProject = Join-Path $repositoryRoot 'Sdk.Amiga\Examples\FileStats\FileStats.csproj'
$consoleIoProject = Join-Path $repositoryRoot 'Sdk.Amiga\Examples\ConsoleIO\ConsoleIO.csproj'
$polymorphismProject = Join-Path $repositoryRoot 'Sdk.Amiga\Examples\Polymorphism\Polymorphism.csproj'
$stopwatchBenchmarkProject = Join-Path $repositoryRoot 'Sdk.Amiga\Examples\StopwatchBenchmark\StopwatchBenchmark.csproj'
$fixtureProject = Join-Path $repositoryRoot 'Compiler.Tests.WinUaeFixture\CopperSharp.Compiler.Tests.WinUaeFixture.csproj'
$stageRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('coppersharp-dos-adf-' + [Guid]::NewGuid().ToString('N'))

function Invoke-Checked([string] $Description, [scriptblock] $Action) {
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "$Description failed with exit code $LASTEXITCODE." }
}

function Compile-AmigaApplication(
    [string] $Assembly,
    [string] $EntryPoint,
    [string] $Output,
    [string] $ExceptionMode = 'yolo',
    [switch] $ManagedMemory) {
    $compilerArguments = @(
        $Assembly, '--entry', $EntryPoint, '--output', $Output,
        '--platform', 'amiga', '--cpu', '68000', '--format', 'hunk',
        '--runtime', 'application', '--exceptions', $ExceptionMode
    )
    if ($ManagedMemory) {
        $compilerArguments += @('--memory', 'managed-pool', '--heap-size', '8192')
    }
    & dotnet run --project $compilerProject --no-build -- @compilerArguments
    if ($LASTEXITCODE -ne 0) { throw "Compilation of '$EntryPoint' failed with exit code $LASTEXITCODE." }
}

try {
    if (-not $NoBuild) {
        Invoke-Checked 'Compiler CLI build' { dotnet build $compilerProject --nologo --verbosity minimal }
        Invoke-Checked 'ADF builder build' { dotnet build $builderProject --nologo --verbosity minimal }
        Invoke-Checked 'DOS example build' { dotnet build $dosProject --nologo --verbosity minimal }
        Invoke-Checked 'FileStats example build' { dotnet build $fileStatsProject --nologo --verbosity minimal }
        Invoke-Checked 'ConsoleIO example build' { dotnet build $consoleIoProject --nologo --verbosity minimal }
        Invoke-Checked 'Polymorphism example build' { dotnet build $polymorphismProject --nologo --verbosity minimal }
        Invoke-Checked 'StopwatchBenchmark example build' { dotnet build $stopwatchBenchmarkProject --nologo --verbosity minimal }
        Invoke-Checked 'WinUAE fixture build' { dotnet build $fixtureProject --nologo --verbosity minimal }
    }

    $dosAssembly = Join-Path (Split-Path -Parent $dosProject) "bin\$configuration\$framework\DOS.dll"
    $fileStatsAssembly = Join-Path (Split-Path -Parent $fileStatsProject) "bin\$configuration\$framework\FileStats.dll"
    $consoleIoAssembly = Join-Path (Split-Path -Parent $consoleIoProject) "bin\$configuration\$framework\ConsoleIO.dll"
    $polymorphismAssembly = Join-Path (Split-Path -Parent $polymorphismProject) "bin\$configuration\$framework\Polymorphism.dll"
    $stopwatchBenchmarkAssembly = Join-Path (Split-Path -Parent $stopwatchBenchmarkProject) "bin\$configuration\$framework\StopwatchBenchmark.dll"
    $fixtureAssembly = Join-Path (Split-Path -Parent $fixtureProject) "bin\$configuration\$framework\CopperSharp.Compiler.Tests.WinUaeFixture.dll"
    foreach ($assembly in @($dosAssembly, $fileStatsAssembly, $consoleIoAssembly, $polymorphismAssembly, $stopwatchBenchmarkAssembly, $fixtureAssembly)) {
        if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw "Required assembly '$assembly' was not found." }
    }

    $sDirectory = New-Item -ItemType Directory -Path (Join-Path $stageRoot 'S') -Force
    $cDirectory = New-Item -ItemType Directory -Path (Join-Path $stageRoot 'C') -Force
    $testDirectory = New-Item -ItemType Directory -Path (Join-Path $stageRoot 'TestDir') -Force
    [System.IO.File]::WriteAllText(
        (Join-Path $stageRoot 'sample.txt'),
        "CopperSharp68k DOS example test`nSecond line`n",
        [System.Text.Encoding]::ASCII)
    [System.IO.File]::WriteAllText(
        (Join-Path $testDirectory.FullName 'entry.txt'),
        "directory entry`n",
        [System.Text.Encoding]::ASCII)
    [System.IO.File]::WriteAllText(
        (Join-Path $stageRoot 'console-input.txt'),
        # Amiga console.device reports Return as CR; keep the boot test aligned
        # with the interactive path instead of relying on a host-style LF.
        "CopperSharp`r",
        [System.Text.Encoding]::ASCII)

    Compile-AmigaApplication $dosAssembly 'DOSExample.Program::Main' (Join-Path $stageRoot 'DOS')
    Compile-AmigaApplication $fileStatsAssembly 'FileStatsExample.Program::Main' (Join-Path $stageRoot 'FileStats')
    Compile-AmigaApplication $consoleIoAssembly `
        'ConsoleIOExample.Program::Main' `
        (Join-Path $stageRoot 'ConsoleIO') `
        'full'
    Compile-AmigaApplication $polymorphismAssembly `
        'PolymorphismExample.Program::Main' `
        (Join-Path $stageRoot 'Polymorphism') `
        'full' `
        -ManagedMemory
    Compile-AmigaApplication $stopwatchBenchmarkAssembly `
        'StopwatchBenchmarkExample.Program::Main' `
        (Join-Path $stageRoot 'StopwatchBenchmark') `
        'full'
    Compile-AmigaApplication $fixtureAssembly `
        'CopperSharp.Compiler.Tests.WinUaeFixture.WinUaeSmokeFixture::PolymorphismPassedMain' `
        (Join-Path $stageRoot 'PolymorphismPassed')
    Compile-AmigaApplication $fixtureAssembly `
        'CopperSharp.Compiler.Tests.WinUaeFixture.WinUaeSmokeFixture::DosExamplesPassedMain' `
        (Join-Path $stageRoot 'ExamplesPassed')
    Compile-AmigaApplication $fixtureAssembly `
        'CopperSharp.Compiler.Tests.WinUaeFixture.WinUaeSmokeFixture::ExecuteDosExamplesMain' `
        (Join-Path $cDirectory.FullName 'Execute')
    Copy-Item -LiteralPath (Join-Path $cDirectory.FullName 'Execute') -Destination (Join-Path $stageRoot 'Execute')

    $startupSequence = @(
        'StopwatchBenchmark'
        'PolymorphismPassed'
        'ExamplesPassed'
        ''
    ) -join "`n"
    [System.IO.File]::WriteAllText(
        (Join-Path $sDirectory.FullName 'startup-sequence'),
        $startupSequence,
        [System.Text.Encoding]::ASCII)

    $resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
    & dotnet run --project $builderProject --no-build -- `
        --filesystem $stageRoot $resolvedOutput --volume CopperSharpDOS
    if ($LASTEXITCODE -ne 0) { throw "Filesystem ADF builder failed with exit code $LASTEXITCODE." }
    $image = Get-Item -LiteralPath $resolvedOutput
    if ($image.Length -ne 901120) { throw "Expected a standard 880 KiB ADF, got $($image.Length) bytes." }
    Write-Host "Created AmigaDOS example test image '$resolvedOutput'."
} finally {
    if (Test-Path -LiteralPath $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force }
}
