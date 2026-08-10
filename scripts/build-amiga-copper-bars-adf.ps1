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
$demoProject = Join-Path $repositoryRoot 'Sdk.Amiga\Examples\CopperBars\CopperBars.csproj'
$demoAssembly = Join-Path (Split-Path -Parent $demoProject) "bin\$configuration\$framework\CopperBars.dll"
$stageRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('coppersharp-copper-bars-adf-' + [Guid]::NewGuid().ToString('N'))

function Invoke-Checked([string] $Description, [scriptblock] $Action) {
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "$Description failed with exit code $LASTEXITCODE." }
}

try {
    if (-not $NoBuild) {
        Invoke-Checked 'Compiler CLI build' { dotnet build $compilerProject --nologo --verbosity minimal }
        Invoke-Checked 'ADF builder build' { dotnet build $builderProject --nologo --verbosity minimal }
        Invoke-Checked 'CopperBars example build' { dotnet build $demoProject --nologo --verbosity minimal }
    }

    if (-not (Test-Path -LiteralPath $demoAssembly -PathType Leaf)) {
        throw "CopperBars assembly was not found at '$demoAssembly'."
    }

    $sDirectory = New-Item -ItemType Directory -Path (Join-Path $stageRoot 'S') -Force
    $demoPath = Join-Path $stageRoot 'CopperBars'
    & dotnet run --project $compilerProject --no-build -- `
        $demoAssembly `
        --entry 'CopperBarsExample.Program::Main' `
		--output $demoPath `
		--platform amiga --cpu 68000 --format hunk `
		--runtime application --exceptions yolo --symbols off
    if ($LASTEXITCODE -ne 0) { throw "CopperBars compilation failed with exit code $LASTEXITCODE." }

    $startupSequence = @(
        'CopperBars'
        'Echo "COPPERSHARP68K_COPPER_" NOLINE >RESULT.OK'
        'Echo "BARS_OK" >>RESULT.OK'
        ''
    ) -join "`n"
    [System.IO.File]::WriteAllText(
        (Join-Path $sDirectory.FullName 'startup-sequence'),
        $startupSequence,
        [System.Text.Encoding]::ASCII)

    $resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
    & dotnet run --project $builderProject --no-build -- `
        --filesystem $stageRoot $resolvedOutput --volume CopperBars
    if ($LASTEXITCODE -ne 0) { throw "Filesystem ADF builder failed with exit code $LASTEXITCODE." }

    $image = Get-Item -LiteralPath $resolvedOutput
    if ($image.Length -ne 901120) {
        throw "Expected a standard 880 KiB ADF, got $($image.Length) bytes."
    }
    Write-Host "Created CopperBars test image '$resolvedOutput'."
} finally {
    if (Test-Path -LiteralPath $stageRoot) {
        Remove-Item -LiteralPath $stageRoot -Recurse -Force
    }
}
