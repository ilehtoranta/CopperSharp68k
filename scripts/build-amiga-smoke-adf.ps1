[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $OutputPath,
    [string] $EntryPoint = 'CopperSharp.Compiler.Tests.WinUaeFixture.WinUaeSmokeFixture::ManagedGcMain',
    [ValidateSet('M68000', 'M68040')]
    [string] $Cpu = 'M68000',
    [ValidateSet('Disabled', 'M68040')]
    [string] $FloatingPoint = 'Disabled',
    [switch] $ManagedAmiga = $true,
    [string] $ExpectedValue = '0x43534850',
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$fixtureProject = Join-Path $repositoryRoot 'Compiler.Tests.WinUaeFixture\CopperSharp.Compiler.Tests.WinUaeFixture.csproj'
$fixtureAssembly = Join-Path $repositoryRoot 'Compiler.Tests.WinUaeFixture\bin\Debug\net10.0\CopperSharp.Compiler.Tests.WinUaeFixture.dll'
$builderProject = Join-Path $repositoryRoot 'Compiler.BootAdf\CopperSharp.Compiler.BootAdf.csproj'
$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)

if (-not $NoBuild) {
    Write-Host 'Building Workbench-free Amiga smoke fixture and ADF builder...'
    & dotnet build $fixtureProject --nologo --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "Fixture build failed with exit code $LASTEXITCODE." }
    & dotnet build $builderProject --nologo --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "ADF builder build failed with exit code $LASTEXITCODE." }
}

if (-not (Test-Path -LiteralPath $fixtureAssembly -PathType Leaf)) {
    throw "Amiga smoke fixture assembly was not found at '$fixtureAssembly'."
}

$builderArguments = @(
    $fixtureAssembly, $EntryPoint, $resolvedOutputPath,
    '--cpu', $Cpu.ToLowerInvariant(),
    '--fpu', $FloatingPoint.ToLowerInvariant(),
    '--success-value', $ExpectedValue
)
if ($ManagedAmiga) { $builderArguments += '--managed-amiga' }
& dotnet run --project $builderProject --no-build -- @builderArguments
if ($LASTEXITCODE -ne 0) { throw "ADF builder failed with exit code $LASTEXITCODE." }

$image = Get-Item -LiteralPath $resolvedOutputPath
if ($image.Length -ne 901120) {
    throw "Expected a standard 880 KiB ADF (901120 bytes), but '$resolvedOutputPath' is $($image.Length) bytes."
}
Write-Host "Created Workbench-free boot image '$resolvedOutputPath'."
