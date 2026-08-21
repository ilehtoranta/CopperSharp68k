[CmdletBinding()]
param(
    [ValidateSet('1.3', '3.1', 'Both')]
    [string] $Kickstart = 'Both',
    [string] $WinUaePath = 'C:\Program Files\WinUAE\winuae64.exe',
    [string] $Kickstart13Path,
    [string] $Kickstart31Path,
    [ValidateRange(5, 300)]
    [int] $TimeoutSeconds = 45,
    [switch] $DryRun,
    [switch] $NoBuild,
    [switch] $M68040Only,
    [switch] $KeepArtifacts
)

$ErrorActionPreference = 'Stop'
$successMarker = 'COPPERSHARP68K_BOOT_OK'
$enteredValue = [uint32]0x424F4F54
$statusAddress = '6fff0'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$adfBuilder = Join-Path $PSScriptRoot 'build-amiga-smoke-adf.ps1'
$defaultRomDirectory = Join-Path (Split-Path -Parent $repositoryRoot) 'MedPlayer\CopperScreen\ROM'
$resultSectorOffset = 901120 - 512

if (-not $Kickstart13Path) {
    $Kickstart13Path = if ($env:COPPERSHARP_KICKSTART13_ROM) { $env:COPPERSHARP_KICKSTART13_ROM } else { Join-Path $defaultRomDirectory 'Kickstart_13.rom' }
}
if (-not $Kickstart31Path) {
    $Kickstart31Path = if ($env:COPPERSHARP_KICKSTART31_ROM) { $env:COPPERSHARP_KICKSTART31_ROM } else { Join-Path $defaultRomDirectory 'kickstart-3.1-a500.rom' }
}

function Assert-FileExists([string] $Path, [string] $Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Description was not found at '$Path'." }
}

function Read-AdfResult([string] $AdfPath) {
    try {
        $stream = [System.IO.File]::Open($AdfPath, 'Open', 'Read', 'ReadWrite')
        try {
            $stream.Position = $resultSectorOffset
            $buffer = [byte[]]::new(512)
            $read = $stream.Read($buffer, 0, $buffer.Length)
            if ($read -gt 0) { return [System.Text.Encoding]::ASCII.GetString($buffer, 0, $read) }
        } finally {
            $stream.Dispose()
        }
    } catch {
        # Retry while WinUAE is flushing the image.
    }
    return $null
}

function Read-WinUaeStatus {
    try {
        $pipe = [System.IO.Pipes.NamedPipeClientStream]::new(
            '.', 'WinUAE', [System.IO.Pipes.PipeDirection]::InOut)
        try {
            $pipe.Connect(100)
            $command = [System.Text.Encoding]::ASCII.GetBytes("DBG m $statusAddress 1`0")
            $pipe.Write($command, 0, $command.Length)
            $pipe.Flush()
            $buffer = [byte[]]::new(4096)
            $response = [System.IO.MemoryStream]::new()
            do {
                $count = $pipe.Read($buffer, 0, $buffer.Length)
                if ($count -gt 0) { $response.Write($buffer, 0, $count) }
            } while ($count -gt 0 -and $buffer[$count - 1] -ne 0)
            $text = [System.Text.Encoding]::ASCII.GetString($response.ToArray())
            $match = [regex]::Match($text, '(?im)^0006FFF0\s+([0-9A-F]{4})\s+([0-9A-F]{4})')
            if ($match.Success) {
                return [Convert]::ToUInt32($match.Groups[1].Value + $match.Groups[2].Value, 16)
            }
        } finally {
            $pipe.Dispose()
        }
    } catch {
        # WinUAE creates its IPC endpoint after emulator initialization.
    }
    return $null
}

Assert-FileExists $WinUaePath 'WinUAE executable'
Assert-FileExists $adfBuilder 'ADF builder script'

$profiles = switch ($Kickstart) {
    '1.3' { @(@{ Name = 'Kickstart13'; Rom = $Kickstart13Path; Cpu = 'M68000'; Fpu = 'Disabled'; Entry = 'CopperSharp.Compiler.Tests.WinUaeFixture.WinUaeSmokeFixture::ManagedGcMain'; Expected = '0x43534850' }) }
    '3.1' { @(
        @{ Name = 'Kickstart31'; Rom = $Kickstart31Path; Cpu = 'M68000'; Fpu = 'Disabled'; Entry = 'CopperSharp.Compiler.Tests.WinUaeFixture.WinUaeSmokeFixture::ManagedGcMain'; Expected = '0x43534850' }
        @{ Name = 'Kickstart31M68040Integer'; Rom = $Kickstart31Path; Cpu = 'M68040'; Fpu = 'Disabled'; Entry = 'CopperSharp.Compiler.Tests.WinUaeFixture.WinUaeSmokeFixture::M68040IntegerMain'; Expected = '0x43534850'; Managed = $false }
        @{ Name = 'Kickstart31M68040Fpu'; Rom = $Kickstart31Path; Cpu = 'M68040'; Fpu = 'M68040'; Entry = 'CopperSharp.Compiler.Tests.WinUaeFixture.WinUaeSmokeFixture::M68040FpuMain'; Expected = '0x40700000'; Managed = $false }
    ) }
    default { @(
        @{ Name = 'Kickstart13'; Rom = $Kickstart13Path; Cpu = 'M68000'; Fpu = 'Disabled'; Entry = 'CopperSharp.Compiler.Tests.WinUaeFixture.WinUaeSmokeFixture::ManagedGcMain'; Expected = '0x43534850' }
        @{ Name = 'Kickstart31'; Rom = $Kickstart31Path; Cpu = 'M68000'; Fpu = 'Disabled'; Entry = 'CopperSharp.Compiler.Tests.WinUaeFixture.WinUaeSmokeFixture::ManagedGcMain'; Expected = '0x43534850' }
        @{ Name = 'Kickstart31M68040Integer'; Rom = $Kickstart31Path; Cpu = 'M68040'; Fpu = 'Disabled'; Entry = 'CopperSharp.Compiler.Tests.WinUaeFixture.WinUaeSmokeFixture::M68040IntegerMain'; Expected = '0x43534850'; Managed = $false }
        @{ Name = 'Kickstart31M68040Fpu'; Rom = $Kickstart31Path; Cpu = 'M68040'; Fpu = 'M68040'; Entry = 'CopperSharp.Compiler.Tests.WinUaeFixture.WinUaeSmokeFixture::M68040FpuMain'; Expected = '0x40700000'; Managed = $false }
    ) }
}
foreach ($profile in $profiles) { Assert-FileExists $profile.Rom "$($profile.Name) ROM" }
if ($M68040Only) {
    $profiles = @($profiles | Where-Object { $_.Cpu -eq 'M68040' })
}


$artifactRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('coppersharp-winuae-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $artifactRoot | Out-Null

try {
    foreach ($profile in $profiles) {
        $profileRoot = Join-Path $artifactRoot $profile.Name
    $hasBuilt = $NoBuild.IsPresent
        New-Item -ItemType Directory -Path $profileRoot -Force | Out-Null
        $adfPath = Join-Path $profileRoot 'CopperSharp68k-Smoke.adf'
        & $adfBuilder -OutputPath $adfPath -EntryPoint $profile.Entry -Cpu $profile.Cpu -FloatingPoint $profile.Fpu -ExpectedValue $profile.Expected -ManagedAmiga:($profile.Managed -ne $false) -NoBuild:$hasBuilt
        if ($LASTEXITCODE -ne 0) { throw "ADF builder failed with exit code $LASTEXITCODE." }
        $hasBuilt = $true
        $isM68040 = $profile.Cpu -eq 'M68040'

        $escapedAdf = $adfPath.Replace('\', '/')
        $escapedRom = ([System.IO.Path]::GetFullPath($profile.Rom)).Replace('\', '/')
        $configPath = Join-Path $profileRoot 'coppersharp-smoke.uae'
        $config = @(
            'config_description=CopperSharp68k Workbench-free ADF smoke test'
            'config_hardware=true'
            'config_host=true'
            'use_gui=no'
            'show_leds=false'
            'sound_output=none'
            "kickstart_rom_file=$escapedRom"
            'quickstart=a500,0'
            $(if ($isM68040) { 'cpu_model=68040' } else { 'cpu_model=68000' })
            $(if ($isM68040) { 'fpu_model=68040' } else { 'fpu_model=0' })
            $(if ($isM68040) { 'cpu_compatible=false' } else { 'cpu_compatible=true' })
            $(if ($isM68040) { 'cpu_cycle_exact=false' } else { 'cpu_cycle_exact=true' })
            $(if ($isM68040) { 'blitter_cycle_exact=false' } else { 'blitter_cycle_exact=true' })
            $(if ($isM68040) { 'cpu_speed=max' } else { 'cpu_speed=real' })
            'chipset=ocs'
            'chipmem_size=1'
            'bogomem_size=0'
            'fastmem_size=0'
            'nr_floppies=1'
            "floppy0=$escapedAdf"
            'floppy0type=0'
            'floppy0wp=false'
            'floppy0sound=0'
        )
        [System.IO.File]::WriteAllLines($configPath, $config, [System.Text.Encoding]::ASCII)

        if ($DryRun) { Write-Host "Dry run prepared $($profile.Name): $configPath"; continue }

        Write-Host "Running $($profile.Name) from the Workbench-free ADF..."
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new($WinUaePath)
        $startInfo.UseShellExecute = $false
        $startInfo.WorkingDirectory = $profileRoot
        $startInfo.CreateNoWindow = $true
        foreach ($argument in @('-f', $configPath, '-s', 'use_gui=no')) {
            [void]$startInfo.ArgumentList.Add($argument)
        }
        $process = [System.Diagnostics.Process]::Start($startInfo)
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        $result = $null
        $fixtureEntered = $false
        try {
            while ([DateTime]::UtcNow -lt $deadline) {
                $result = Read-AdfResult $adfPath
        $expectedValue = [Convert]::ToUInt32($profile.Expected.Substring(2), 16)
                $status = Read-WinUaeStatus
                if ($status -eq $expectedValue -or ($result -and $result.Contains($successMarker, [StringComparison]::Ordinal))) { Write-Host "$($profile.Name) passed."; break }
                if ($status -eq $enteredValue) { $fixtureEntered = $true }
                elseif ($fixtureEntered -and $null -ne $status) {
                    throw ("{0} executed the fixture but returned 0x{1:X8}." -f $profile.Name, $status)
                }
                if ($result -and $result.Contains('COPPERSHARP68K_BOOT_FAIL', [StringComparison]::Ordinal)) { throw "$($profile.Name) executed the fixture but returned the failure value." }
                if ($process.HasExited) { throw "WinUAE exited before producing a result for $($profile.Name)." }
                Start-Sleep -Milliseconds 250
            }
            if ($status -ne $expectedValue -and (-not $result -or -not $result.Contains($successMarker, [StringComparison]::Ordinal))) {
                throw "Timed out waiting for '$successMarker' in the ADF result sector for $($profile.Name). Artifacts: '$profileRoot'."
            }
        } finally {
            if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
            $process.Dispose()
        }
    }
} finally {
    if ($KeepArtifacts -or $DryRun) { Write-Host "Artifacts retained at '$artifactRoot'." }
    elseif (Test-Path -LiteralPath $artifactRoot) { Remove-Item -LiteralPath $artifactRoot -Recurse -Force }
}

if ($DryRun) { Write-Host 'WinUAE ADF artifacts prepared; emulator execution was skipped.' }
else { Write-Host 'WinUAE Workbench-free ADF smoke tests passed.' }
