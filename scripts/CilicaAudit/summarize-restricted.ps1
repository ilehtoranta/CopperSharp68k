param(
    [Parameter(Mandatory)][string]$ResultsDirectory,
    [ValidateRange(1, 20)][int]$Repetitions = 3
)
$ErrorActionPreference = 'Stop'
$samples = @(Get-Content -LiteralPath (Join-Path $ResultsDirectory 'samples.json') -Raw | ConvertFrom-Json)
$environment = Get-Content -LiteralPath (Join-Path $ResultsDirectory 'environment.json') -Raw | ConvertFrom-Json
$successfulGroups = @($samples | Where-Object { !$_.Error } | Group-Object Entry, Cpu, Variant)
foreach ($group in $successfulGroups) {
    if ($group.Count -ne $Repetitions -or @($group.Group.ImageSha256 | Sort-Object -Unique).Count -ne 1 -or
        @($group.Group | ForEach-Object { $_.Executions | ConvertTo-Json -Compress } | Sort-Object -Unique).Count -ne 1) {
        throw "Incomplete or unstable benchmark group: $($group.Name)"
    }
}
$regressions = @()
$comparisons = @(foreach ($baseline in $samples | Where-Object { $_.Variant -eq 'baseline' -and $_.Round -eq 0 -and !$_.Error }) {
    $restricted = $samples | Where-Object { $_.Variant -eq 'restricted' -and $_.Entry -eq $baseline.Entry -and $_.Cpu -eq $baseline.Cpu -and $_.Round -eq 0 -and !$_.Error }
    if (!$restricted) { $regressions += "$($baseline.Entry)/$($baseline.Cpu)"; continue }
    $baselineCycles = ($baseline.Executions | Where-Object Input -eq 31).Cycles
    $restrictedCycles = ($restricted.Executions | Where-Object Input -eq 31).Cycles
    $portable = $samples | Where-Object { $_.Variant -eq 'portable' -and $_.Entry -eq $baseline.Entry -and $_.Cpu -eq $baseline.Cpu -and $_.Round -eq 0 -and !$_.Error }
    $portableCycles = if ($portable) { ($portable.Executions | Where-Object Input -eq 31).Cycles } else { $null }
    [pscustomobject]@{
        Kernel = $baseline.Entry.Split('::')[-1]; Cpu = $baseline.Cpu
        BaselineCycles31 = $baselineCycles; RestrictedCycles31 = $restrictedCycles; PortableCycles31 = $portableCycles
        Speedup = $baselineCycles / $restrictedCycles
        BaselineLoadedBytes = $baseline.LoadedBytes; RestrictedLoadedBytes = $restricted.LoadedBytes
    }
})
$coverage = @(foreach ($group in $samples | Group-Object Variant) {
    [pscustomobject]@{ Variant = $group.Name; SuccessfulSamples = @($group.Group | Where-Object { !$_.Error }).Count
        FailedCombinations = @($group.Group | Where-Object Error).Count
        CheckedExecutions = @($group.Group | Where-Object { !$_.Error } | ForEach-Object { $_.Executions }).Count }
})
$incorrect = @($samples | Where-Object { $_.Error -match 'Incorrect|Unbalanced stack|Execution budget exceeded|Stopped at' })
$gains = @($comparisons | Where-Object Kernel -in @('ScalarReplacement', 'Devirtualization'))
$gainsRetained = $gains.Count -eq 6 -and @($gains | Where-Object { $_.Speedup -le 1 -or !$_.PortableCycles31 -or $_.RestrictedCycles31 -gt $_.PortableCycles31 }).Count -eq 0
$summary = [ordered]@{
    CilicaRevision = 'ca199fd583f492e156f2617dff43b5f28ff8684d'
    CompilerHead = (Get-Content -LiteralPath (Join-Path (Split-Path $ResultsDirectory) 'compiler-head.txt') -Raw).Trim()
    Environment = $environment; Repetitions = $Repetitions; Coverage = $coverage
    StableSuccessfulGroups = $successfulGroups.Count; CompatibilityRegressions = $regressions
    IncorrectResults = $incorrect.Count; GainsRetained = $gainsRetained; Comparisons = $comparisons
    Failures = @($samples | Where-Object Error | Select-Object Entry, Cpu, Variant, @{n='Error'; e={$_.Error.Split([Environment]::NewLine)[0]}})
}
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'restricted-summary.json')
$gains | Format-Table Kernel, Cpu, BaselineCycles31, RestrictedCycles31, PortableCycles31, Speedup
if ($regressions.Count -or $incorrect.Count -or !$gainsRetained) { throw 'Restricted benchmark acceptance checks failed; see restricted-summary.json' }
