[CmdletBinding()]
param(
    [ValidateSet('1.3', '3.1', 'Both')]
    [string] $Kickstart = '3.1',
    [string] $WinUaePath = 'C:\Program Files\WinUAE\winuae64.exe',
    [string] $Kickstart13Path,
    [string] $Kickstart31Path,
    [ValidateRange(5, 300)]
    [int] $TimeoutSeconds = 60,
    [switch] $NoBuild,
    [switch] $KeepArtifacts
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class CopperSharpWinUaeWindow {
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);
}
'@
$successMarker = 'COPPERSHARP68K_DOS_OK'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$builder = Join-Path $PSScriptRoot 'build-amiga-dos-examples-adf.ps1'
$defaultRomDirectory = Join-Path (Split-Path -Parent $repositoryRoot) 'MedPlayer\CopperScreen\ROM'
if (-not $Kickstart13Path) { $Kickstart13Path = Join-Path $defaultRomDirectory 'Kickstart_13.rom' }
if (-not $Kickstart31Path) { $Kickstart31Path = Join-Path $defaultRomDirectory 'kickstart-3.1-a500.rom' }

function Test-AdfMarker([string] $AdfPath, [string] $Marker) {
    try {
        $stream = [System.IO.File]::Open($AdfPath, 'Open', 'Read', 'ReadWrite')
        try {
            $buffer = [byte[]]::new($stream.Length)
            [void]$stream.Read($buffer, 0, $buffer.Length)
            return [System.Text.Encoding]::ASCII.GetString($buffer).Contains($Marker, [StringComparison]::Ordinal)
        } finally { $stream.Dispose() }
    } catch { }
    return $false
}

function Send-CliCommand([System.Diagnostics.Process] $Process, [string] $Command) {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while ($Process.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 100
        $Process.Refresh()
    }
    if ($Process.MainWindowHandle -eq [IntPtr]::Zero) { throw 'WinUAE did not create an emulator window for CLI input.' }
    [void][CopperSharpWinUaeWindow]::SetForegroundWindow($Process.MainWindowHandle)
    Start-Sleep -Milliseconds 200
    [System.Windows.Forms.SendKeys]::SendWait($Command + '{ENTER}')
}

if (-not (Test-Path -LiteralPath $WinUaePath -PathType Leaf)) { throw "WinUAE was not found at '$WinUaePath'." }
$profiles = switch ($Kickstart) {
    '1.3' { @(@{ Name = 'Kickstart13'; Rom = $Kickstart13Path }) }
    '3.1' { @(@{ Name = 'Kickstart31'; Rom = $Kickstart31Path }) }
    default { @(@{ Name = 'Kickstart13'; Rom = $Kickstart13Path }, @{ Name = 'Kickstart31'; Rom = $Kickstart31Path }) }
}
foreach ($profile in $profiles) {
    if (-not (Test-Path -LiteralPath $profile.Rom -PathType Leaf)) { throw "$($profile.Name) ROM was not found at '$($profile.Rom)'." }
}

$artifactRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('coppersharp-winuae-dos-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $artifactRoot | Out-Null
try {
    $adfPath = Join-Path $artifactRoot 'CopperSharpDOS.adf'
    & $builder -OutputPath $adfPath -NoBuild:$NoBuild
    if ($LASTEXITCODE -ne 0) { throw "DOS example ADF build failed with exit code $LASTEXITCODE." }
    foreach ($profile in $profiles) {
        $profileRoot = Join-Path $artifactRoot $profile.Name
        New-Item -ItemType Directory -Path $profileRoot | Out-Null
        $profileAdf = Join-Path $profileRoot 'CopperSharpDOS.adf'
        Copy-Item -LiteralPath $adfPath -Destination $profileAdf
        $configPath = Join-Path $profileRoot 'coppersharp-dos.uae'
        $config = @(
            'config_description=CopperSharp68k AmigaDOS example test'
            'config_hardware=true'
            'config_host=true'
            'use_gui=no'
            'sound_output=none'
            'quickstart=a500,0'
            "kickstart_rom_file=$($profile.Rom.Replace('\', '/'))"
            'cpu_model=68000'
            'cpu_compatible=true'
            'cpu_cycle_exact=true'
            'blitter_cycle_exact=true'
            'cpu_speed=real'
            'chipset=ocs'
            'chipmem_size=1'
            'bogomem_size=0'
            'fastmem_size=0'
            'nr_floppies=1'
            "floppy0=$($profileAdf.Replace('\', '/'))"
            'floppy0type=0'
            'floppy0wp=false'
            'floppy0sound=0'
        )
        [System.IO.File]::WriteAllLines($configPath, $config, [System.Text.Encoding]::ASCII)

        $startInfo = [System.Diagnostics.ProcessStartInfo]::new($WinUaePath)
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.WorkingDirectory = $profileRoot
        foreach ($argument in @('-f', $configPath, '-s', 'use_gui=no')) { [void]$startInfo.ArgumentList.Add($argument) }
        $process = [System.Diagnostics.Process]::Start($startInfo)
        try {
            $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
            while ([DateTime]::UtcNow -lt $deadline) {
                if (Test-AdfMarker $profileAdf $successMarker) { Write-Host "$($profile.Name) DOS examples passed."; break }
                if ($process.HasExited) { throw "WinUAE exited with code $($process.ExitCode) before $($profile.Name) completed." }
                Start-Sleep -Milliseconds 250
            }
            if (-not (Test-AdfMarker $profileAdf $successMarker)) {
                throw "$($profile.Name) timed out before writing the result marker. Artifacts: '$profileRoot'."
            }
        } finally {
            if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
            $process.Dispose()
        }
    }
} finally {
    if ($KeepArtifacts) { Write-Host "Artifacts retained at '$artifactRoot'." }
    elseif (Test-Path -LiteralPath $artifactRoot) { Remove-Item -LiteralPath $artifactRoot -Recurse -Force }
}

Write-Host 'WinUAE AmigaDOS example tests passed.'
