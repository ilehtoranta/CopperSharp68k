param([Parameter(Mandatory)][string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
$records = @(foreach ($file in (Get-ChildItem -LiteralPath $ResultsDirectory -Recurse -Filter 'phases-*.jsonl')) {
    foreach ($line in [IO.File]::ReadLines($file.FullName)) {
        if (-not [string]::IsNullOrWhiteSpace($line)) { $line | ConvertFrom-Json }
    }
})
if (-not $records.Count) { throw 'No execution phase measurements found.' }
$summary = @(foreach ($group in ($records | Group-Object Entry, Cpu, Optimization)) {
    $compilation = @($group.Group | Where-Object Phase -eq 'compilation')
    $emulation = @($group.Group | Where-Object Phase -eq 'emulation')
    $compileMs = ($compilation | Measure-Object Milliseconds -Sum).Sum
    $emulateMs = ($emulation | Measure-Object Milliseconds -Sum).Sum
    [pscustomobject]@{
        Entry = $group.Group[0].Entry; Cpu = $group.Group[0].Cpu
        Optimization = $group.Group[0].Optimization
        Compilations = $compilation.Count; Executions = $emulation.Count
        CompilationSeconds = [math]::Round($compileMs / 1000, 3)
        EmulationSeconds = [math]::Round($emulateMs / 1000, 3)
        TotalSeconds = [math]::Round(($compileMs + $emulateMs) / 1000, 3)
    }
})
$summary | Sort-Object TotalSeconds -Descending | ConvertTo-Json -Depth 4
