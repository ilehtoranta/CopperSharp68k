param(
    [Parameter(Mandatory = $true)][string]$ResultsDirectory,
    [Parameter(Mandatory = $true)][string]$BaselineRun,
    [string]$SourceRevision = (& git rev-parse HEAD),
    [string]$Output = (Join-Path $PSScriptRoot 'ci/execution-test-durations.json')
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'TestSharding.psm1') -Force -DisableNameChecking
$cases = @(foreach ($report in (Get-ChildItem -LiteralPath $ResultsDirectory -Recurse -Filter '*.trx')) {
    [xml]$document = [System.IO.File]::ReadAllText($report.FullName)
    foreach ($result in $document.TestRun.Results.UnitTestResult) {
        $case = @(Get-ExecutionTestCases -Lines @($result.testName))
        if ($case.Count -eq 0) { continue }
        if ($result.outcome -ne 'Passed') { throw "Use a fully passing baseline: $($result.testName)" }
        $seconds = [timespan]::Parse($result.duration, [cultureinfo]::InvariantCulture).TotalSeconds
        $case[0] | Add-Member -NotePropertyName Seconds -NotePropertyValue $seconds
        $case[0]
    }
})
if ($cases.Count -eq 0) { throw 'No compiler execution results were found.' }
$groups = @(Get-ExecutionTestGroups -Cases $cases | Sort-Object Key -Culture en-US)
$header = [ordered]@{
    schemaVersion = 1; sourceRevision = $SourceRevision; baselineRun = $BaselineRun
    cases = $cases.Count
    fallbackSecondsPerCase = [math]::Round(($cases | Measure-Object Seconds -Average).Average, 6)
} | ConvertTo-Json
$header = $header.Substring(0, $header.LastIndexOf('}')).TrimEnd()
$rows = @(foreach ($group in $groups) {
    $seconds = [math]::Max(0.001, ($group.Cases | Measure-Object Seconds -Sum).Sum)
    '    {0}: [{1}, {2}]' -f ($group.Key | ConvertTo-Json -Compress), $group.Cases.Count,
        $seconds.ToString('0.000', [cultureinfo]::InvariantCulture)
})
$text = $header + ",`n  `"groups`": {`n" + ($rows -join ",`n") + "`n  }`n}`n"
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($Output))) | Out-Null
[System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($Output), $text)
Write-Host "Recorded $($cases.Count) cases in $($groups.Count) method/CPU groups: $Output"
