param(
    [Parameter(Mandatory)][string]$TrxPath,
    [string]$ResultsDirectory = 'TestResults/CpuMatrixAuditChecks'
)

$ErrorActionPreference = 'Stop'
$TrxPath = [IO.Path]::GetFullPath($TrxPath)
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
$parseErrors = $null
$tokens = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot 'run-cpu-matrix.ps1'), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
$function = $ast.Find({ param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Read-ShardResult'
}, $true)
Invoke-Expression $function.Extent.Text
$source = Get-Content -Raw -LiteralPath $TrxPath
[xml]$original = $source
$methods = @($original.TestRun.TestDefinitions.UnitTest | ForEach-Object {
    $_.TestMethod.className + '.' + $_.TestMethod.name
} | Sort-Object -Unique)
$cases = @($original.TestRun.Results.UnitTestResult | ForEach-Object testName)
$total = [int]$original.TestRun.ResultSummary.Counters.total
if (!(Read-ShardResult $TrxPath $methods $total $cases).Passed) { throw 'The input must be a complete passing TRX.' }
$checks = [Collections.Generic.List[string]]::new()
$checks.Add('Complete passing results accepted')

function Assert-Rejected($name, $document, $expectedCases = $cases) {
    $path = Join-Path $ResultsDirectory ($name + '.trx')
    $document.Save($path)
    $result = Read-ShardResult $path $methods 0 $expectedCases
    if ($result.Passed) { throw "Invalid result accepted: $name" }
    $checks.Add($name)
}

# Keep the count and method definitions intact while hiding one CPU row
# behind a duplicate display name. Method-only coverage cannot detect this.
[xml]$mutated = $source
$row = $mutated.TestRun.Results.UnitTestResult | Where-Object {
    $_.testName -match 'target: M68060(?:,|\))'
} | Select-Object -First 1
if (!$row) { throw 'The input must include a discovered M68060 theory row.' }
$replacement = $row.testName.Replace('target: M68060', 'target: M68040')
if ($replacement -notin $cases) { throw 'The input must include the matching M68040 row.' }
$row.testName = $replacement
Assert-Rejected 'MissingCpuRowWithUnchangedTotals' $mutated

# A definition without an execution result must not count as coverage.
[xml]$mutated = $source
$fact = $mutated.TestRun.Results.UnitTestResult | Where-Object {
    !$_.testName.Contains('(')
} | Select-Object -First 1
if (!$fact) { throw 'The input must include a fact.' }
[void]$mutated.TestRun.Results.RemoveChild($fact)
foreach ($counter in @('total', 'executed', 'passed')) {
    $mutated.TestRun.ResultSummary.Counters.SetAttribute($counter, [string]($total - 1))
}
Assert-Rejected 'DefinitionWithoutExecution' $mutated @()

[xml]$mutated = $source
[void]$mutated.TestRun.Results.RemoveChild($mutated.TestRun.Results.UnitTestResult[0])
Assert-Rejected 'CounterDisagreesWithResults' $mutated

[xml]$mutated = $source
$mutated.TestRun.Results.UnitTestResult[0].outcome = 'Failed'
Assert-Rejected 'FailureWithPassingSummary' $mutated

if (!(Read-ShardResult $TrxPath $methods $total $methods).Passed) {
    throw 'Method-only discovery must allow deferred theory expansion.'
}
$checks.Add('DeferredTheoryExpansionAccepted')
[pscustomobject]@{ Passed = $checks.Count; Checks = $checks; Source = $TrxPath } |
    ConvertTo-Json -Depth 4 | Tee-Object -FilePath (Join-Path $ResultsDirectory 'checks.json')
