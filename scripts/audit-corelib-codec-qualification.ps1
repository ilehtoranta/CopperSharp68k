param(
	[string] $RepositoryRoot,
	[string] $QualificationDirectory,
	[string] $RegressionTrx,
	[string] $GateDirectory
)

$ErrorActionPreference = 'Stop'

function Resolve-FromRoot([string] $Value, [string] $Default) {
	if ([string]::IsNullOrWhiteSpace($Value)) { $Value = $Default }
	if ([System.IO.Path]::IsPathRooted($Value)) { return [System.IO.Path]::GetFullPath($Value) }
	return [System.IO.Path]::GetFullPath((Join-Path $RepositoryRoot $Value))
}

function Require-File([string] $Path) {
	if (-not [System.IO.File]::Exists($Path)) { throw "Required qualification artifact is missing: $Path" }
	return (Get-Item -LiteralPath $Path)
}

function Assert-True([bool] $Condition, [string] $Message) {
	if (-not $Condition) { throw $Message }
}

function Get-Hash([string] $Path) {
	[void] (Require-File $Path)
	return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Read-Trx([string] $Path, [int] $ExactCount) {
	[void] (Require-File $Path)
	[xml] $document = Get-Content -LiteralPath $Path -Raw
	Assert-True ($document.TestRun -and $document.TestRun.ResultSummary -and $document.TestRun.ResultSummary.Counters) "Invalid TRX: $Path"
	$counter = $document.TestRun.ResultSummary.Counters
	$results = @($document.TestRun.Results.UnitTestResult)
	$total = [int] $counter.total
	Assert-True ($total -gt 0) "TRX has no tests: $Path"
	Assert-True ([int] $counter.executed -eq $total -and [int] $counter.passed -eq $total -and
		[int] $counter.failed -eq 0 -and [int] $counter.notExecuted -eq 0 -and
		[int] $counter.error -eq 0 -and [int] $counter.timeout -eq 0 -and
		[int] $counter.aborted -eq 0 -and [int] $counter.inconclusive -eq 0 -and
		[int] $counter.notRunnable -eq 0 -and [int] $counter.pending -eq 0 -and
		[int] $counter.inProgress -eq 0 -and [int] $counter.warning -eq 0) "TRX contains failed, skipped, or unfinished tests: $Path ($($counter.OuterXml))"
	Assert-True ($results.Count -eq $total) "TRX counter/result mismatch: $Path ($total versus $($results.Count))"
	Assert-True (@($results | Where-Object { $_.outcome -ne 'Passed' }).Count -eq 0) "TRX contains a non-passing test result: $Path"
	if ($ExactCount -gt 0) { Assert-True ($total -eq $ExactCount) "TRX expected $ExactCount cases, found $total`: $Path" }
	return [pscustomobject]@{
		Path = $Path
		Sha256 = Get-Hash $Path
		Total = $total
		Passed = [int] $counter.passed
		Counters = [ordered]@{
			total = $total; executed = [int] $counter.executed; passed = [int] $counter.passed
			failed = [int] $counter.failed; notExecuted = [int] $counter.notExecuted
			error = [int] $counter.error; timeout = [int] $counter.timeout
			aborted = [int] $counter.aborted; inconclusive = [int] $counter.inconclusive
			notRunnable = [int] $counter.notRunnable; warning = [int] $counter.warning
		}
		StartedUtc = ([datetimeoffset]::Parse([string] $document.TestRun.Times.start)).UtcDateTime.ToString('o')
		FinishedUtc = ([datetimeoffset]::Parse([string] $document.TestRun.Times.finish)).UtcDateTime.ToString('o')
		Results = $results
	}
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
	$RepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
} else {
	$RepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
}
$qualificationDirectory = Resolve-FromRoot $QualificationDirectory 'TestResults/CoreLibCodecQualification'
$RegressionTrx = Resolve-FromRoot $RegressionTrx (Join-Path $qualificationDirectory 'codec-final-regressions.trx')
$GateDirectory = Resolve-FromRoot $GateDirectory (Join-Path $qualificationDirectory 'full-gate')
$manifestPath = Join-Path $RepositoryRoot 'Compiler/Framework/corelib-closure-roots.json'
$closurePath = Join-Path $GateDirectory 'closure.json'
$gateTrxPath = Join-Path $GateDirectory 'execution/corelib-execution.trx'
$outputPath = Join-Path $qualificationDirectory 'qualification-audit.json'
if ([System.IO.File]::Exists($outputPath)) { Remove-Item -LiteralPath $outputPath -Force }

$builtFiles = [ordered]@{
	compiler = 'Compiler/bin/Release/net10.0/CopperSharp.Compiler.dll'
	runtime = 'Runtime.Managed/bin/Release/net10.0/CopperSharp.Runtime.Managed.dll'
	tests = 'Compiler.Tests/bin/Release/net10.0/CopperSharp.Compiler.Tests.dll'
	cli = 'Compiler.Cli/bin/Release/net10.0/CopperSharp.Compiler.Cli.dll'
}
$buildHashes = [ordered]@{}
$newestBuild = [datetime]::MinValue
foreach ($key in $builtFiles.Keys) {
	$path = Join-Path $RepositoryRoot $builtFiles[$key]
	$file = Require-File $path
	$buildHashes[$key] = [ordered]@{ path = $path; sha256 = Get-Hash $path; lastWriteUtc = $file.LastWriteTimeUtc.ToString('o') }
	if ($file.LastWriteTimeUtc -gt $newestBuild) { $newestBuild = $file.LastWriteTimeUtc }
}
foreach ($copy in @(
	@('Compiler.Cli/bin/Release/net10.0/CopperSharp.Compiler.dll', 'compiler'),
	@('Compiler.Tests/bin/Release/net10.0/CopperSharp.Compiler.dll', 'compiler'),
	@('Compiler.Cli/bin/Release/net10.0/CopperSharp.Runtime.Managed.dll', 'runtime'),
	@('Compiler.Tests/bin/Release/net10.0/CopperSharp.Runtime.Managed.dll', 'runtime')
)) {
	$copyPath = Join-Path $RepositoryRoot $copy[0]
	Assert-True ((Get-Hash $copyPath) -eq $buildHashes[$copy[1]].sha256) "Release output contains a stale dependency copy: $copyPath"
}

$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
$coreLibPath = Join-Path $nugetRoot 'microsoft.netcore.app.runtime.win-x64/10.0.9/runtimes/win-x64/lib/net10.0/System.Private.CoreLib.dll'
$coreLibHash = Get-Hash $coreLibPath
Assert-True ($coreLibHash -eq 'dc1945de746f94987ec705a1f27d512abf72a41a1da37e414d1951e4e823037a') 'Installed CoreLib does not match the pinned 10.0.9 SHA-256.'
$manifestHash = Get-Hash $manifestPath
$regression = Read-Trx $RegressionTrx 0
$newestTestBuild = @('compiler', 'runtime', 'tests') |
	ForEach-Object { [datetime]::Parse([string] $buildHashes[$_].lastWriteUtc).ToUniversalTime() } |
	Sort-Object -Descending | Select-Object -First 1
Assert-True ((Require-File $RegressionTrx).LastWriteTimeUtc -ge $newestTestBuild) 'Final regression TRX predates a compiler, runtime, or test Release assembly; rerun the regression suite.'

$testClass = 'CopperSharp.Compiler.Tests.CompilerExecutionTests.'
$entryMethods = @(
	'CoreLibCodecPipelineExecutesOnTarget', 'Utf8CodecSurfaceExecutesOnTarget',
	'Base64CodecSurfaceExecutesOnTarget', 'HexCodecSurfaceExecutesOnTarget'
)
$matrixMethods = @(
	'Utf8CodecGeneratedCorpusMatchesHost', 'Base64CodecGeneratedCorpusMatchesHost',
	'HexCodecGeneratedCorpusMatchesHost', 'Base64CodecAdversarialCorpusMatchesHost',
	'NarrowPromotedComparisonsMatchHost', 'CoreLibCodecStreamingRetriesMatchHost'
)
$cpus = [ordered]@{ M68000 = 'M68000'; M68020 = 'M68020'; M68040 = 'M68040'; M68060 = 'M68040' }
$modes = @('Disabled', 'FixedPoint')
$nativeEvidence = [ordered]@{}
$nativeNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($method in $entryMethods + $matrixMethods) {
	$names = [System.Collections.Generic.List[string]]::new()
	foreach ($cpu in $cpus.Keys) {
		$baseName = "$testClass$method(target: $cpu, model: $($cpus[$cpu])"
		if ($method -in $entryMethods) {
			$names.Add("$baseName)")
		} else {
			foreach ($mode in $modes) { $names.Add("$baseName, mode: $mode)") }
		}
	}
	foreach ($name in $names) {
		$matches = @($regression.Results | Where-Object { [string] $_.testName -ceq $name })
		Assert-True ($matches.Count -eq 1) "Expected exactly one passing native case '$name' in final regression TRX; found $($matches.Count)."
		[void] $nativeNames.Add($name)
	}
	$nativeEvidence[$method] = [ordered]@{
		count = $names.Count
		cpuTargets = @($cpus.Keys)
		optimizationModes = if ($method -in $entryMethods) { @('FixedPoint (fixture default)') } else { $modes }
		cases = @($names)
	}
}
Assert-True ($nativeNames.Count -eq 64) "Expected 64 unique native cases; found $($nativeNames.Count)."
$observedNative = @($regression.Results | Where-Object {
	$testName = [string] $_.testName
	foreach ($method in $entryMethods + $matrixMethods) {
		if ($testName.StartsWith("$testClass$method(", [System.StringComparison]::Ordinal)) { return $true }
	}
	return $false
})
Assert-True ($observedNative.Count -eq 64) "Final regression TRX contains $($observedNative.Count) codec/narrow native cases; expected exactly 64."

$hostOraclePath = Join-Path $qualificationDirectory 'host-oracle/host-oracle.json'
$hostOracleFile = Require-File $hostOraclePath
$hostOracle = Get-Content -LiteralPath $hostOraclePath -Raw | ConvertFrom-Json
Assert-True ($hostOracle.schemaVersion -eq 1 -and $hostOracle.frameworkDescription -ceq '.NET 10.0.12' -and
	$hostOracle.verdict -ceq 'passed' -and $hostOracle.runtimeVersion -ceq '10.0.12') 'Independent host oracle did not pass on the recorded .NET 10.0.12 runtime.'
foreach ($name in @('utf8', 'base64', 'hex', 'pipeline', 'narrowComparisons')) {
	Assert-True ($hostOracle.entries.$name -eq 42) "Host fixture entry failed: $name"
}
$expectedChecksums = [ordered]@{
	utf8Generated = 'A63B47C4'; base64Generated = 'AB1E1E34'; hexGenerated = '0C733167'
	base64Adversarial = '045E1F44'; streamingGenerated = '19F5074C'; narrowComparisonsGenerated = 'F1F8A995'
}
foreach ($name in $expectedChecksums.Keys) {
	Assert-True ($hostOracle.checksums.$name -ceq $expectedChecksums[$name]) "Unexpected host fixture checksum: $name"
}
foreach ($name in @('Utf8CodecFixtures', 'Base64CodecFixtures', 'HexCodecFixtures', 'CoreLibCodecPipelineFixtures', 'CoreLibCodecStreamingFixtures', 'NarrowCodecComparisonFixtures')) {
	Assert-True ($hostOracleFile.LastWriteTimeUtc -ge (Require-File (Join-Path $RepositoryRoot "Compiler.Tests/$name.cs")).LastWriteTimeUtc) "Host oracle predates fixture '$name'."
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$roots = @($manifest.roots)
Assert-True ($roots.Count -eq 19 -and @($roots | Select-Object -ExpandProperty id -Unique).Count -eq 19) 'Root manifest must contain 19 distinct roots.'
$closure = Get-Content -LiteralPath (Require-File $closurePath).FullName -Raw | ConvertFrom-Json
Assert-True ($closure.schemaVersion -eq 1 -and $closure.contractId -eq 'net10.0-10.0.9') 'Closure report has the wrong schema or pinned contract.'
Assert-True ($closure.implementationPack.PackVersion -eq '10.0.9' -and @($closure.implementationPack.Assemblies).Count -eq 1) 'Closure report is not pinned to the single 10.0.9 CoreLib assembly.'
Assert-True (([string] $closure.implementationPack.Assemblies[0].Sha256).ToLowerInvariant() -eq $coreLibHash) 'Closure report CoreLib SHA-256 differs from installed pinned 10.0.9.'
Assert-True (([string] $closure.rootManifestSha256).ToLowerInvariant() -eq $manifestHash) 'Closure report was generated from another root manifest.'
Assert-True (([string] $closure.cliAssemblySha256).ToLowerInvariant() -eq $buildHashes.cli.sha256) 'Closure report was generated by another compiler CLI build.'
Assert-True ($closure.summary.rootCount -eq 19 -and $closure.summary.compatibleRootCount -eq 19 -and
	$closure.summary.incompatibleRootCount -eq 0 -and $closure.summary.distinctGapCount -eq 0 -and
	@($closure.roots).Count -eq 19 -and @($closure.prioritizedGaps).Count -eq 0) 'Closure report does not establish 19 compatible roots with zero gaps.'
Assert-True ((Require-File $closurePath).LastWriteTimeUtc -ge $newestBuild) 'Closure report predates a final Release assembly; rerun the AOT gate.'

$gate = Read-Trx $gateTrxPath 19
$hunkEvidence = [System.Collections.Generic.List[object]]::new()
$newestHunk = [datetime]::MinValue
foreach ($root in $roots) {
	$rootId = [string] $root.id
	$rootClosure = @($closure.roots | Where-Object { $_.id -ceq $rootId })
	Assert-True ($rootClosure.Count -eq 1 -and $rootClosure[0].entry -ceq $root.entry -and
		$rootClosure[0].isCompatible -eq $true -and $rootClosure[0].gapCount -eq 0 -and
		$rootClosure[0].outputFormat -eq 'hunk') "Closure mismatch or gap for root '$rootId'."
	$hunkPath = Join-Path $GateDirectory "hunks/$rootId.hunk"
	$compatibilityPath = Join-Path $GateDirectory "hunks/$rootId.compatibility.json"
	$hunkFile = Require-File $hunkPath
	[void] (Require-File $compatibilityPath)
	Assert-True ($hunkFile.Length -ge 4) "Empty Hunk image for '$rootId'."
	$stream = [System.IO.File]::OpenRead($hunkPath)
	try {
		$header = [byte[]]::new(4)
		[void] $stream.Read($header, 0, 4)
	} finally { $stream.Dispose() }
	Assert-True ($header[0] -eq 0 -and $header[1] -eq 0 -and $header[2] -eq 3 -and $header[3] -eq 243) "Invalid HUNK_HEADER for '$rootId'."
	$compatible = Get-Content -LiteralPath $compatibilityPath -Raw | ConvertFrom-Json
	Assert-True ($compatible.IsCompatible -eq $true -and $compatible.ContractId -eq 'net10.0-10.0.9' -and
		$compatible.ImplementationPack.PackVersion -eq '10.0.9' -and
		$compatible.ImplementationPack.Assemblies[0].Sha256 -eq $coreLibHash) "Compiled compatibility report mismatch for '$rootId'."
	Assert-True ($hunkFile.LastWriteTimeUtc -ge (Require-File $closurePath).LastWriteTimeUtc -and
		(Require-File $compatibilityPath).LastWriteTimeUtc -ge (Require-File $closurePath).LastWriteTimeUtc) "Compiled artifacts for '$rootId' predate the final closure report."
	if ($hunkFile.LastWriteTimeUtc -gt $newestHunk) { $newestHunk = $hunkFile.LastWriteTimeUtc }
	$gateMatches = @($gate.Results | Where-Object {
		$name = [string] $_.testName
		$name -ceq $root.qualificationTest -or $name.StartsWith("$($root.qualificationTest)(", [System.StringComparison]::Ordinal)
	})
	if ($root.qualificationDisplayName) {
		$gateMatches = @($gateMatches | Where-Object { ([string] $_.testName).Contains([string] $root.qualificationDisplayName) })
	}
	Assert-True ($gateMatches.Count -eq 1) "Expected exactly one mapped AOT execution case for '$rootId'; found $($gateMatches.Count)."
	$mapPath = "$hunkPath.map"
	$mapEvidence = [ordered]@{ status = 'not-generated' }
	if ([System.IO.File]::Exists($mapPath)) {
		$mapLines = Get-Content -LiteralPath $mapPath
		$descriptorCount = 0
		foreach ($line in $mapLines) {
			if ($line -match '^([0-9A-Fa-f]+)\s+(runtime:(?:type-descriptor:|string-descriptor\b)|type:)') {
				$offset = [Convert]::ToInt64($Matches[1], 16)
				Assert-True (($offset % 2) -eq 0) "Unaligned descriptor relocation in '$mapPath': $line"
				$descriptorCount++
			}
		}
		Assert-True (@($mapLines | Where-Object { $_ -ceq "IMPLEMENTATION ASSEMBLY System.Private.CoreLib 10.0.0.0 pkt=7cec85d7bea7798e mvid=$($compatible.ImplementationPack.Assemblies[0].Mvid) sha256=$coreLibHash" }).Count -eq 1) "Map CoreLib identity/hash mismatch for '$rootId'."
		Assert-True (@($mapLines | Where-Object { $_ -match "^CopperSharp\.Compiler\.Tests .* sha256=$($buildHashes.tests.sha256)$" }).Count -eq 1) "Map test assembly hash mismatch for '$rootId'."
		$runtimeAssemblyLines = @($mapLines | Where-Object { $_ -match '^CopperSharp\.Runtime\.Managed .* sha256=' })
		if ($runtimeAssemblyLines.Count -gt 0) {
			Assert-True ($runtimeAssemblyLines.Count -eq 1 -and $runtimeAssemblyLines[0] -match " sha256=$($buildHashes.runtime.sha256)$") "Map runtime assembly hash mismatch for '$rootId'."
		}
		$mapEvidence = [ordered]@{ status = 'checked'; path = $mapPath; sha256 = Get-Hash $mapPath; alignedDescriptorRelocations = $descriptorCount }
	}
	$hunkEvidence.Add([ordered]@{
		id = $rootId; entry = $root.entry; expectedReturnValue = $root.expectedReturnValue
		hunk = [ordered]@{ path = $hunkPath; sha256 = Get-Hash $hunkPath; bytes = $hunkFile.Length; hunkHeader = '000003f3' }
		compatibility = [ordered]@{ path = $compatibilityPath; sha256 = Get-Hash $compatibilityPath; isCompatible = $true }
		map = $mapEvidence; executionCase = [string] $gateMatches[0].testName
	})
}
Assert-True ((Require-File $gateTrxPath).LastWriteTimeUtc -ge $newestHunk) 'AOT execution TRX predates a compiled Hunk image.'

$sourceFiles = @(
	'Runtime.Managed/ShadowUtf8.cs', 'Runtime.Managed/ShadowBase64.cs',
	'Runtime.Managed/ShadowConvertBase64.cs', 'Runtime.Managed/ShadowConvertHex.cs',
	'Compiler/Framework/Utf8CodecSurface.cs', 'Compiler/Framework/Base64CodecSurface.cs',
	'Compiler/Framework/HexCodecSurface.cs', 'Compiler/Framework/CoreLibCodecSurface.cs',
	'Compiler/Framework/FrameworkImplementationProfile.cs',
	'Compiler/Framework/FrameworkReachabilityAnalyzer.cs',
	'Compiler/Metadata/CompilationModule.CoreLibCodecs.cs',
	'Compiler/Backend/M68kComparisonPromotion.cs', 'Compiler/Backend/CilMachineIrBuilder.cs',
	'Compiler/Backend/M68kCodeGenerator.cs', 'Compiler/Backend/M68kSpillAndCopy.cs',
	'Compiler.Tests/Utf8CodecFixtures.cs', 'Compiler.Tests/Base64CodecFixtures.cs',
	'Compiler.Tests/HexCodecFixtures.cs', 'Compiler.Tests/CoreLibCodecPipelineFixtures.cs',
	'Compiler.Tests/CoreLibCodecStreamingFixtures.cs', 'Compiler.Tests/NarrowCodecComparisonFixtures.cs',
	'Compiler.Tests/Utf8CodecContractTests.cs', 'Compiler.Tests/Base64CodecContractTests.cs',
	'Compiler.Tests/HexCodecContractTests.cs', 'Compiler.Tests/CoreLibCodecAdmissionTests.cs',
	'Compiler.Tests/CoreLibCodecExecutionTests.cs',
	'Compiler.Tests/M68kComparisonPromotionTests.cs', 'Compiler.Tests/M68kPhiSpillWidthTests.cs',
	'Compiler/Framework/corelib-closure-roots.json', 'scripts/qualify-corelib-aot.ps1'
)
$sourceHashes = [ordered]@{}
foreach ($relativePath in $sourceFiles) {
	$path = Join-Path $RepositoryRoot $relativePath
	$file = Require-File $path
	$assemblyKey = if ($relativePath.StartsWith('Runtime.Managed/')) { 'runtime' }
		elseif ($relativePath.StartsWith('Compiler.Tests/')) { 'tests' }
		elseif ($relativePath.StartsWith('Compiler/') -and $relativePath -ne 'Compiler/Framework/corelib-closure-roots.json') { 'compiler' }
		else { $null }
	if ($assemblyKey) {
		$assemblyTime = [datetime]::Parse([string] $buildHashes[$assemblyKey].lastWriteUtc).ToUniversalTime()
		Assert-True ($file.LastWriteTimeUtc -le $assemblyTime) "Final Release $assemblyKey DLL predates source '$relativePath'; rebuild and rerun qualification."
	}
	$sourceHashes[$relativePath] = [ordered]@{ sha256 = Get-Hash $path; lastWriteUtc = $file.LastWriteTimeUtc.ToString('o') }
}

$audit = [ordered]@{
	schemaVersion = 1
	generatedAtUtc = [datetime]::UtcNow.ToString('o')
	auditor = [ordered]@{ path = $PSCommandPath; sha256 = Get-Hash $PSCommandPath }
	repositoryRoot = $RepositoryRoot
	contractId = 'net10.0-10.0.9'
	pinnedCoreLib = [ordered]@{ path = $coreLibPath; sha256 = $coreLibHash }
	builds = $buildHashes
	sources = $sourceHashes
	regressions = [ordered]@{ path = $regression.Path; sha256 = $regression.Sha256; counters = $regression.Counters; finishedUtc = $regression.FinishedUtc }
	native = [ordered]@{ total = 64; entryCases = 16; optimizedMatrixCases = 48; methods = $nativeEvidence }
	hostOracle = [ordered]@{ path = $hostOraclePath; sha256 = Get-Hash $hostOraclePath; runtime = $hostOracle.runtimeVersion; entries = $hostOracle.entries; checksums = $hostOracle.checksums }
	closure = [ordered]@{ path = $closurePath; sha256 = Get-Hash $closurePath; manifestSha256 = $manifestHash; roots = 19; compatible = 19; gaps = 0 }
	aotExecution = [ordered]@{ path = $gate.Path; sha256 = $gate.Sha256; counters = $gate.Counters; finishedUtc = $gate.FinishedUtc }
	hunks = @($hunkEvidence)
}
$audit | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $outputPath -Encoding utf8
Write-Host "CoreLib codec qualification audit passed: $($regression.Total) regressions; 64 native codec/narrow cases across 4 CPUs and both optimizer modes; 19 compatible roots, 19 AOT cases, 19 Hunk images, 0 gaps."
Write-Host "SHA-256 inventory: $outputPath"
