[CmdletBinding()]
param(
    [ValidateSet('1.3', '3.1', 'Both')]
    [string] $Kickstart = '3.1',
    [string] $WinUaePath = 'C:\Program Files\WinUAE\winuae64.exe',
    [string] $Kickstart13Path,
    [string] $Kickstart31Path,
    [ValidateRange(10, 300)]
    [int] $TimeoutSeconds = 60,
    [string] $ScreenshotPath,
    [switch] $NoBuild,
    [switch] $KeepArtifacts
)

$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class CopperSharpCopperBarsWindow {
    [StructLayout(LayoutKind.Sequential)]
    public struct Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr window, out Rect rectangle);

    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(IntPtr window, ref Point point);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
'@

$successMarker = 'COPPERSHARP68K_COPPER_BARS_OK'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$builder = Join-Path $PSScriptRoot 'build-amiga-copper-bars-adf.ps1'
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
    } catch {
        # WinUAE can briefly hold the ADF while flushing a guest write.
    }
    return $false
}

function Click-LeftMouse([System.Diagnostics.Process] $Process) {
    $Process.Refresh()
    $window = $Process.MainWindowHandle
    if ($window -eq [IntPtr]::Zero) { return }

    $rectangle = [CopperSharpCopperBarsWindow+Rect]::new()
    if (-not [CopperSharpCopperBarsWindow]::GetClientRect($window, [ref] $rectangle)) { return }
    $point = [CopperSharpCopperBarsWindow+Point]::new()
    $point.X = [Math]::Max(1, [int](($rectangle.Right - $rectangle.Left) / 2))
    $point.Y = [Math]::Max(1, [int](($rectangle.Bottom - $rectangle.Top) / 2))
    if (-not [CopperSharpCopperBarsWindow]::ClientToScreen($window, [ref] $point)) { return }

    [void][CopperSharpCopperBarsWindow]::SetForegroundWindow($window)
    [void][CopperSharpCopperBarsWindow]::SetCursorPos($point.X, $point.Y)
    [CopperSharpCopperBarsWindow]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 75
    [CopperSharpCopperBarsWindow]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
}

function Save-ClientScreenshot(
    [System.Diagnostics.Process] $Process,
    [string] $Path
) {
    $Process.Refresh()
    $window = $Process.MainWindowHandle
    if ($window -eq [IntPtr]::Zero) { return $false }

    $rectangle = [CopperSharpCopperBarsWindow+Rect]::new()
    if (-not [CopperSharpCopperBarsWindow]::GetClientRect($window, [ref] $rectangle)) { return $false }
    $width = $rectangle.Right - $rectangle.Left
    $height = $rectangle.Bottom - $rectangle.Top
    if ($width -le 0 -or $height -le 0) { return $false }
    $origin = [CopperSharpCopperBarsWindow+Point]::new()
    if (-not [CopperSharpCopperBarsWindow]::ClientToScreen($window, [ref] $origin)) { return $false }

    $directory = Split-Path -Parent $Path
    if ($directory -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    $bitmap = [System.Drawing.Bitmap]::new($width, $height)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CopyFromScreen($origin.X, $origin.Y, 0, 0, $bitmap.Size)
        } finally { $graphics.Dispose() }
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
    return $true
}

if (-not (Test-Path -LiteralPath $WinUaePath -PathType Leaf)) {
    throw "WinUAE was not found at '$WinUaePath'."
}
$profiles = @(switch ($Kickstart) {
    '1.3' { @(@{ Name = 'Kickstart13'; Rom = $Kickstart13Path }) }
    '3.1' { @(@{ Name = 'Kickstart31'; Rom = $Kickstart31Path }) }
    default { @(
        @{ Name = 'Kickstart13'; Rom = $Kickstart13Path }
        @{ Name = 'Kickstart31'; Rom = $Kickstart31Path }
    ) }
})
foreach ($profile in $profiles) {
    if (-not (Test-Path -LiteralPath $profile.Rom -PathType Leaf)) {
        throw "$($profile.Name) ROM was not found at '$($profile.Rom)'."
    }
}

$artifactRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('coppersharp-winuae-copper-bars-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $artifactRoot | Out-Null
try {
    $adfPath = Join-Path $artifactRoot 'CopperSharpCopperBars.adf'
    & $builder -OutputPath $adfPath -NoBuild:$NoBuild
    if ($LASTEXITCODE -ne 0) { throw "CopperBars ADF build failed with exit code $LASTEXITCODE." }

    foreach ($profile in $profiles) {
        $profileRoot = Join-Path $artifactRoot $profile.Name
        New-Item -ItemType Directory -Path $profileRoot | Out-Null
        $profileAdf = Join-Path $profileRoot 'CopperSharpCopperBars.adf'
        Copy-Item -LiteralPath $adfPath -Destination $profileAdf
        $configPath = Join-Path $profileRoot 'coppersharp-copper-bars.uae'
        $config = @(
            'config_description=CopperSharp68k CopperBars ADF test'
            'config_hardware=true'
            'config_host=true'
            'use_gui=no'
            'show_leds=false'
            'sound_output=none'
            'quickstart=a500,0'
            "kickstart_rom_file=$($profile.Rom.Replace('\', '/'))"
            'cpu_model=68000'
            'cpu_compatible=true'
            'cpu_cycle_exact=true'
            'blitter_cycle_exact=true'
            'cpu_speed=real'
            'chipset=ocs'
            'ntsc=false'
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
        foreach ($argument in @('-f', $configPath, '-s', 'use_gui=no')) {
            [void]$startInfo.ArgumentList.Add($argument)
        }

        $process = [System.Diagnostics.Process]::Start($startInfo)
        try {
            $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
            # Kickstart may still be showing a black boot viewport at five
            # seconds. Wait until the demo is normally visible so an optional
            # capture records the bars before the same click exits it.
            $nextClick = [DateTime]::UtcNow.AddSeconds(12)
            $screenshotCaptured = -not $ScreenshotPath
            while ([DateTime]::UtcNow -lt $deadline) {
                if (Test-AdfMarker $profileAdf $successMarker) {
                    Write-Host "$($profile.Name) CopperBars demo passed."
                    break
                }
                if ($process.HasExited) {
                    throw "WinUAE exited with code $($process.ExitCode) before $($profile.Name) completed."
                }
                if ([DateTime]::UtcNow -ge $nextClick) {
                    if (-not $screenshotCaptured) {
                        $capturePath = if ($profiles.Count -eq 1) {
                            $ScreenshotPath
                        } else {
                            $extension = [System.IO.Path]::GetExtension($ScreenshotPath)
                            $baseName = $ScreenshotPath.Substring(0, $ScreenshotPath.Length - $extension.Length)
                            "$baseName-$($profile.Name)$extension"
                        }
                        $screenshotCaptured = Save-ClientScreenshot $process $capturePath
                        if ($screenshotCaptured) {
                            Write-Host "Captured $($profile.Name) screenshot '$capturePath'."
                        }
                    }
                    Click-LeftMouse $process
                    $nextClick = [DateTime]::UtcNow.AddSeconds(2)
                }
                Start-Sleep -Milliseconds 250
            }
            if (-not (Test-AdfMarker $profileAdf $successMarker)) {
                throw "$($profile.Name) timed out before the demo exited and wrote its result marker. Artifacts: '$profileRoot'."
            }
        } finally {
            if (-not $process.HasExited) {
                Stop-Process -Id $process.Id -Force
                $process.WaitForExit()
            }
            $process.Dispose()
        }
    }
} finally {
    if ($KeepArtifacts) {
        Write-Host "Artifacts retained at '$artifactRoot'."
    } elseif (Test-Path -LiteralPath $artifactRoot) {
        Remove-Item -LiteralPath $artifactRoot -Recurse -Force
    }
}

Write-Host 'WinUAE CopperBars ADF tests passed.'
