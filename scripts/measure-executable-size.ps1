[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [string] $OutputDirectory,
    [string] $CompilerPath,
    [string] $CompareCompilerPath,
    [string] $RuntimeDirectory,
    [string] $CompareRuntimeDirectory,
    [switch] $NoBuild,
    [switch] $CompareApplicationMethods,
    [switch] $CompareApplicationMetadata,
    [switch] $CompareApplicationGcSites,
    [switch] $CompareApplicationRuntime,
    [switch] $CompareApplicationArithmetic,
    [switch] $CompareApplicationRegisterCopies,
    [switch] $CompareApplicationCallArguments,
    [switch] $CompareApplicationPlatformBases,
    [switch] $CompareApplicationStatics
)

$ErrorActionPreference = 'Stop'
$comparePolicy = $CompareApplicationMethods -or $CompareApplicationMetadata -or $CompareApplicationGcSites -or $CompareApplicationRuntime -or $CompareApplicationArithmetic -or $CompareApplicationRegisterCopies -or $CompareApplicationCallArguments -or $CompareApplicationPlatformBases -or $CompareApplicationStatics
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts/executable-size/$Configuration"
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
if ($CompilerPath -and -not $NoBuild) { throw '-CompilerPath requires -NoBuild and an already built compiler.' }
if ($CompareCompilerPath -and (-not $NoBuild -or -not $comparePolicy)) {
    throw '-CompareCompilerPath requires -NoBuild and an application policy comparison.'
}
$previousCompiler = if ($CompareCompilerPath) { [IO.Path]::GetFullPath($CompareCompilerPath) } else { $null }
$compiler = if ($CompilerPath) { [IO.Path]::GetFullPath($CompilerPath) }
    else { Join-Path $repositoryRoot "Compiler.Cli/bin/$Configuration/net10.0/CopperSharp.Compiler.Cli.dll" }
$samples = @(
    @{ Name = 'DOS'; Entry = 'DOSExample.Program::Main'; Exceptions = 'yolo'; Memory = 'external' },
    @{ Name = 'FileStats'; Entry = 'FileStatsExample.Program::Main'; Exceptions = 'yolo'; Memory = 'external' },
    @{ Name = 'CopperBars'; Entry = 'CopperBarsExample.Program::Main'; Exceptions = 'yolo'; Memory = 'external' },
    @{ Name = 'ConsoleIO'; Entry = 'ConsoleIOExample.Program::Main'; Exceptions = 'full'; Memory = 'external' },
    @{ Name = 'StopwatchBenchmark'; Entry = 'StopwatchBenchmarkExample.Program::Main'; Exceptions = 'full'; Memory = 'external' },
    @{ Name = 'Polymorphism'; Entry = 'PolymorphismExample.Program::Main'; Exceptions = 'full'; Memory = 'managed-pool' },
    @{ Name = 'IFFInspect'; Entry = 'IFFInspect.Program::Main'; Exceptions = 'full'; Memory = 'managed-pool' },
    @{ Name = 'MUISunflower'; Entry = 'MUISunflower.Program::Main'; Exceptions = 'yolo'; Memory = 'external'; Mui = $true },
    @{ Name = 'MUITaskList'; Entry = 'MUITaskList.Program::Main'; Exceptions = 'yolo'; Memory = 'external'; Mui = $true }
)

function Build-Checked([string] $Project) {
    & dotnet build $Project -c $Configuration --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $Project" }
}

# Normalize the actual serialized executable by removing only HUNK_SYMBOL.
# Comparing the remaining bytes also checks code/data, relocations, allocation
# sizes and hunk layout, rather than trusting sizes or the textual map alone.
function Get-ExecutableWithoutSymbols([string] $Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    $state = @{ Position = 0 }
    $kept = [Collections.Generic.List[byte]]::new()
    function Read-Long {
        if ($state.Position + 4 -gt $bytes.Length) { throw "Truncated HUNK: $Path" }
        $value = [uint32]$bytes[$state.Position] * 16777216L +
            [uint32]$bytes[$state.Position + 1] * 65536L +
            [uint32]$bytes[$state.Position + 2] * 256L + $bytes[$state.Position + 3]
        $state.Position += 4
        return [uint32]$value
    }
    if ((Read-Long) -ne 0x3f3 -or (Read-Long) -ne 0) { throw "Unsupported HUNK header: $Path" }
    $count = Read-Long
    $first = Read-Long
    $last = Read-Long
    if ($first -ne 0 -or $last + 1 -ne $count) { throw "Unsupported HUNK table: $Path" }
    $state.Position += 4 * $count
    if ($state.Position -gt $bytes.Length) { throw "Truncated HUNK table: $Path" }
    $kept.AddRange([byte[]]$bytes[0..($state.Position - 1)])
    $ends = 0
    while ($state.Position -lt $bytes.Length) {
        $start = $state.Position
        $kind = Read-Long
        switch ($kind) {
            0x3e9 { $words = Read-Long; $state.Position += 4 * $words } # CODE
            0x3ea { $words = Read-Long; $state.Position += 4 * $words } # DATA
            0x3eb { $null = Read-Long }                 # BSS
            0x3ec {                                   # RELOC32
                while (($relocations = Read-Long) -ne 0) {
                    $null = Read-Long
                    $state.Position += 4 * $relocations
                }
            }
            0x3f0 {                                   # SYMBOL
                while (($words = Read-Long) -ne 0) { $state.Position += 4 * ($words + 1) }
            }
            0x3f2 { $ends++ }
            default { throw ('Unsupported HUNK record 0x{0:X}: {1}' -f $kind, $Path) }
        }
        if ($state.Position -gt $bytes.Length) { throw "Truncated HUNK record: $Path" }
        if ($kind -ne 0x3f0) { $kept.AddRange([byte[]]$bytes[$start..($state.Position - 1)]) }
    }
    if ($ends -ne $count) { throw "Incomplete HUNK: $Path" }
    return [Convert]::ToBase64String($kept.ToArray())
}

if (-not $NoBuild) {
    Build-Checked (Join-Path $repositoryRoot 'Compiler.Cli/CopperSharp.Compiler.Cli.csproj')
    foreach ($sample in $samples) {
        Build-Checked (Join-Path $repositoryRoot "Sdk.Amiga/Examples/$($sample.Name)/$($sample.Name).csproj")
    }
}
if (-not (Test-Path -LiteralPath $compiler)) { throw "Missing compiler: $compiler" }

$rows = foreach ($sample in $samples) {
    $assembly = Join-Path $repositoryRoot "Sdk.Amiga/Examples/$($sample.Name)/bin/$Configuration/net10.0/$($sample.Name).dll"
    $inputHash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
    $common = @($assembly, '--entry', $sample.Entry, '--platform', 'amiga', '--cpu', '68000',
        '--format', 'hunk', '--runtime', 'application', '--exceptions', $sample.Exceptions, '--memory', $sample.Memory)
    if ($sample.Memory -eq 'managed-pool') { $common += @('--heap-size', '32768') }
    # Match the existing baseline's fixed native MUI test imports. These two
    # images measure code size; these addresses are not deployable MUI stubs.
    if ($sample.Mui) {
        $common += @('--import', 'amiga.boopsi.DoMethodA=0x2600', '--import', 'amiga.boopsi.DoSuperMethodA=0x2700')
    }
    $paths = @{}
    $variants = @('symbols', 'stripped', 'policy')
    if ($CompareRuntimeDirectory) { $variants += 'previous-runtime' }
    if ($comparePolicy) { $variants += 'previous-policy' }
    foreach ($variant in $variants) {
        $output = Join-Path $OutputDirectory "$($sample.Name).$variant"
        $paths[$variant] = $output
        $arguments = $common + @('--output', $output, '--symbols', $(if ($variant -eq 'symbols') { 'on' } else { 'off' }))
        if ($variant -in @('policy', 'previous-runtime')) { $arguments += @('--code-size-optimizations', 'on') }
        $runtime = if ($variant -eq 'previous-runtime') { $CompareRuntimeDirectory } else { $RuntimeDirectory }
        if ($runtime) {
            foreach ($name in @('CopperSharp.Runtime.Managed.dll', 'CopperSharp.Runtime.AmigaPal.dll')) {
                $runtimeAssembly = [IO.Path]::GetFullPath((Join-Path $runtime $name))
                if (-not (Test-Path -LiteralPath $runtimeAssembly)) { throw "Missing runtime: $runtimeAssembly" }
                $arguments += @('--managed-assembly', $runtimeAssembly)
            }
        }
        if ($variant -eq 'previous-policy' -or ($variant -eq 'previous-runtime' -and $CompareApplicationRuntime)) {
            $previousPasses = 'CompactApplicationAddresses,ShareApplicationRuntimeMetadata,CompactApplicationExceptionSites'
            if ($CompareApplicationMetadata) { $previousPasses += ',RemoveApplicationRegisterMoves,ShareReturnSequences' }
            if ($CompareApplicationGcSites -or $CompareApplicationRuntime -or $CompareApplicationArithmetic -or $CompareApplicationRegisterCopies -or $CompareApplicationCallArguments -or $CompareApplicationPlatformBases -or $CompareApplicationStatics) {
                $previousPasses += ',RemoveApplicationRegisterMoves,ShareReturnSequences,PruneApplicationUnwindMetadata,NarrowApplicationExceptionSites,ShareApplicationUnwindRestores'
            }
            if ($CompareApplicationRuntime -or $CompareApplicationArithmetic -or $CompareApplicationRegisterCopies -or $CompareApplicationCallArguments -or $CompareApplicationPlatformBases -or $CompareApplicationStatics) { $previousPasses += ',CompactApplicationGcSites,PruneApplicationLeafSites' }
            if ($CompareApplicationArithmetic -or $CompareApplicationRegisterCopies -or $CompareApplicationCallArguments -or $CompareApplicationPlatformBases -or $CompareApplicationStatics) { $previousPasses += ',CompactApplicationExceptionRuntime' }
            if ($CompareApplicationRegisterCopies -or $CompareApplicationCallArguments -or $CompareApplicationPlatformBases -or $CompareApplicationStatics) { $previousPasses += ',RemoveApplicationDeadArithmetic,TrackApplicationNarrowValues,FoldApplicationMemoryOperands' }
            if ($CompareApplicationCallArguments -or $CompareApplicationPlatformBases -or $CompareApplicationStatics) { $previousPasses += ',OptimizeApplicationRegisterCopies' }
            if ($CompareApplicationPlatformBases -or $CompareApplicationStatics) { $previousPasses += ',OptimizeApplicationCallArguments' }
            if ($CompareApplicationStatics) { $previousPasses += ',CompactApplicationPlatformBases' }
            $arguments += @('--code-size-optimizations', 'on', '--code-size-passes',
                $previousPasses)
        }
        $variantCompiler = if ($variant -eq 'previous-policy' -and $previousCompiler) { $previousCompiler } else { $compiler }
        & dotnet $variantCompiler @arguments | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $($sample.Name) $variant" }
        if ((Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -ne $inputHash) {
            throw "Input was rebuilt during measurement: $($sample.Name). Rerun after builds finish."
        }
    }
    if ((Get-ExecutableWithoutSymbols $paths.symbols) -cne (Get-ExecutableWithoutSymbols $paths.stripped)) {
        throw "Removing symbols changed executable contents: $($sample.Name)"
    }
    $before = (Get-Item -LiteralPath $paths.symbols).Length
    $after = (Get-Item -LiteralPath $paths.stripped).Length
    $policy = (Get-Item -LiteralPath $paths.policy).Length
    $map = Get-Content -LiteralPath "$($paths.stripped).map" -Raw
    $metrics = @{}
    $metricLine = [regex]::Match($map, '(?m)^METRICS .+$').Value
    foreach ($match in [regex]::Matches($metricLine, '([\w-]+)=(\d+)')) { $metrics[$match.Groups[1].Value] = [long]$match.Groups[2].Value }
    $policyMetrics = @{}
    $policyMap = Get-Content -LiteralPath "$($paths.policy).map" -Raw
    foreach ($match in [regex]::Matches([regex]::Match($policyMap, '(?m)^METRICS .+$').Value, '([\w-]+)=(\d+)')) {
        $policyMetrics[$match.Groups[1].Value] = [long]$match.Groups[2].Value
    }
    [pscustomobject]@{
        Example = $sample.Name; Configuration = $Configuration
        Exceptions = $sample.Exceptions; Memory = $sample.Memory
        SymbolsBytes = $before; StrippedBytes = $after
        SavedPercent = [math]::Round(100.0 * ($before - $after) / $before, 2)
        PolicyBytes = $policy; PolicySavedPercent = [math]::Round(100.0 * ($after - $policy) / $after, 2)
        PolicyImageIdentical = [Convert]::ToBase64String([IO.File]::ReadAllBytes($paths.stripped)) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($paths.policy))
        InstructionBytes = $metrics['rom-code-bytes']; ReadOnlyDataBytes = $metrics['rom-rodata-bytes']
        InitializedRamBytes = $metrics['initialized-ram-bytes']; BssBytes = $metrics['bss-bytes']
        Relocations = $metrics['relocations']; Symbols = $metrics['symbols']
        PolicyInstructionBytes = $policyMetrics['rom-code-bytes']; PolicyReadOnlyDataBytes = $policyMetrics['rom-rodata-bytes']
        PolicyRelocations = $policyMetrics['relocations']
        InputSha256 = $inputHash
        ExecutableSha256 = (Get-FileHash -LiteralPath $paths.stripped -Algorithm SHA256).Hash
        PolicySha256 = (Get-FileHash -LiteralPath $paths.policy -Algorithm SHA256).Hash
        PreviousPolicyBytes = $(if ($comparePolicy) { (Get-Item -LiteralPath $paths['previous-policy']).Length } else { $null })
        PreviousPolicySha256 = $(if ($comparePolicy) { (Get-FileHash -LiteralPath $paths['previous-policy'] -Algorithm SHA256).Hash } else { $null })
        PreviousRuntimeBytes = $(if ($CompareRuntimeDirectory) { (Get-Item -LiteralPath $paths['previous-runtime']).Length } else { $null })
        PreviousRuntimeSha256 = $(if ($CompareRuntimeDirectory) { (Get-FileHash -LiteralPath $paths['previous-runtime'] -Algorithm SHA256).Hash } else { $null })
    }
}
$rows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'measurements.csv') -NoTypeInformation
[ordered]@{
    CompilerSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $compiler) 'CopperSharp.Compiler.dll') -Algorithm SHA256).Hash
    PreviousCompilerSha256 = $(if ($previousCompiler) { (Get-FileHash -LiteralPath (Join-Path (Split-Path $previousCompiler) 'CopperSharp.Compiler.dll') -Algorithm SHA256).Hash } else { $null })
    RuntimeHashes = @($(foreach ($directory in @($RuntimeDirectory, $CompareRuntimeDirectory) | Where-Object { $_ }) {
        foreach ($name in @('CopperSharp.Runtime.Managed.dll', 'CopperSharp.Runtime.AmigaPal.dll')) {
            $path = [IO.Path]::GetFullPath((Join-Path $directory $name))
            [pscustomobject]@{ Path = $path; Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
        }
    }))
    Samples = @($rows)
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'measurements.json')
$rows | Format-Table Example, SymbolsBytes, StrippedBytes, SavedPercent, PolicyBytes, PolicySavedPercent
$totalBefore = ($rows | Measure-Object SymbolsBytes -Sum).Sum
$totalAfter = ($rows | Measure-Object StrippedBytes -Sum).Sum
if ($CompareRuntimeDirectory) {
    $runtimeBefore = ($rows | Measure-Object PreviousRuntimeBytes -Sum).Sum
    $runtimeAfter = ($rows | Measure-Object PolicyBytes -Sum).Sum
    Write-Host ('Runtime changes: {0} -> {1} bytes ({2:N2}% smaller)' -f $runtimeBefore, $runtimeAfter,
        (100 * ($runtimeBefore - $runtimeAfter) / $runtimeBefore))
}
Write-Host ('Total: {0} -> {1} bytes ({2:N2}% smaller)' -f $totalBefore, $totalAfter, (100 * ($totalBefore - $totalAfter) / $totalBefore))
$totalPolicy = ($rows | Measure-Object PolicyBytes -Sum).Sum
Write-Host ('Code generation against stripped baseline: {0} -> {1} bytes ({2:N2}% smaller)' -f $totalAfter, $totalPolicy, (100 * ($totalAfter - $totalPolicy) / $totalAfter))
if ($comparePolicy) {
    $previousPolicy = ($rows | Measure-Object PreviousPolicyBytes -Sum).Sum
    Write-Host ('Against previous application policy: {0} -> {1} bytes ({2:N2}% smaller)' -f $previousPolicy, $totalPolicy, (100 * ($previousPolicy - $totalPolicy) / $previousPolicy))
}
