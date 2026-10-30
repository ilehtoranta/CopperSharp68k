param(
	[Parameter(Mandatory = $true)]
	[string] $CliAssembly,

	[Parameter(Mandatory = $true)]
	[string] $RootManifest,

	[string] $FrameworkImplementationManifest,

	[Parameter(Mandatory = $true)]
	[string] $OutputPath
)

$ErrorActionPreference = 'Stop'

function Resolve-ManifestPath([string] $Path, [string] $ManifestDirectory) {
	if ([System.IO.Path]::IsPathRooted($Path)) {
		return [System.IO.Path]::GetFullPath($Path)
	}
	return [System.IO.Path]::GetFullPath((Join-Path $ManifestDirectory $Path))
}

function Invoke-FrameworkAnalysis($Root, [string] $ManifestDirectory, [string] $CliPath, [string] $PackManifest) {
	$assembly = Resolve-ManifestPath $Root.assembly $ManifestDirectory
	if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
		throw "Root '$($Root.id)' assembly does not exist: $assembly"
	}

	$arguments = [System.Collections.Generic.List[string]]::new()
	$arguments.Add($CliPath)
	$arguments.Add($assembly)
	$arguments.Add('--entry')
	$arguments.Add([string] $Root.entry)
	$arguments.Add('--framework-report')
	$arguments.Add('-')
	$arguments.Add('--platform')
	$arguments.Add([string] $(if ($Root.platform) { $Root.platform } else { 'amiga' }))
	$arguments.Add('--cpu')
	$arguments.Add([string] $(if ($Root.cpu) { $Root.cpu } else { '68000' }))
	$arguments.Add('--fpu')
	$arguments.Add([string] $(if ($Root.fpu) { $Root.fpu } else { 'disabled' }))
	$arguments.Add('--runtime')
	$arguments.Add([string] $(if ($Root.runtime) { $Root.runtime } else { 'application' }))
	$arguments.Add('--format')
	$arguments.Add([string] $(if ($Root.format) { $Root.format } else { 'hunk' }))
	$arguments.Add('--memory')
	$arguments.Add([string] $(if ($Root.memory) { $Root.memory } else { 'default' }))
	if ($Root.heapSize) {
		$arguments.Add('--heap-size')
		$arguments.Add([string] $Root.heapSize)
	}
	$arguments.Add('--exports')
	$arguments.Add([string] $(if ($Root.exports) { $Root.exports } else { 'none' }))
	foreach ($exportName in @($Root.includedExports)) {
		if ($exportName) {
			$arguments.Add('--include-export')
			$arguments.Add([string] $exportName)
		}
	}
	$rootPackManifest = if ($Root.frameworkImplementationManifest) {
		$Root.frameworkImplementationManifest
	} else {
		$PackManifest
	}
	if ($rootPackManifest) {
		$implementationManifest = Resolve-ManifestPath $rootPackManifest $ManifestDirectory
		$arguments.Add('--framework-implementation-manifest')
		$arguments.Add($implementationManifest)
	}
	foreach ($managedAssembly in @($Root.managedAssemblies)) {
		if ($managedAssembly) {
			$arguments.Add('--managed-assembly')
			$arguments.Add((Resolve-ManifestPath $managedAssembly $ManifestDirectory))
		}
	}

	$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
	$startInfo.FileName = 'dotnet'
	$startInfo.UseShellExecute = $false
	$startInfo.RedirectStandardOutput = $true
	$startInfo.RedirectStandardError = $true
	foreach ($argument in $arguments) {
		$startInfo.ArgumentList.Add($argument)
	}

	$process = [System.Diagnostics.Process]::new()
	$process.StartInfo = $startInfo
	if (-not $process.Start()) {
		throw "Could not start framework analysis for root '$($Root.id)'."
	}
	$stdoutTask = $process.StandardOutput.ReadToEndAsync()
	$stderrTask = $process.StandardError.ReadToEndAsync()
	$process.WaitForExit()
	$stdout = $stdoutTask.GetAwaiter().GetResult()
	$stderr = $stderrTask.GetAwaiter().GetResult()
	if ($process.ExitCode -notin @(0, 1)) {
		throw "Framework analysis failed for root '$($Root.id)' (exit $($process.ExitCode)): $stderr"
	}
	try {
		$report = $stdout | ConvertFrom-Json
	} catch {
		throw "Framework analysis for root '$($Root.id)' did not return a JSON report. Exit $($process.ExitCode). Error: $stderr"
	}
	if ($null -eq $report.members) {
		throw "Framework analysis for root '$($Root.id)' returned no members field."
	}
	return $report
}

$manifestPath = [System.IO.Path]::GetFullPath($RootManifest)
$manifestDirectory = [System.IO.Path]::GetDirectoryName($manifestPath)
$cliPath = [System.IO.Path]::GetFullPath($CliAssembly)
$packManifest = if ([string]::IsNullOrWhiteSpace($FrameworkImplementationManifest)) {
	$null
} else {
	Resolve-ManifestPath $FrameworkImplementationManifest $manifestDirectory
}
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
	throw "Root manifest does not exist: $manifestPath"
}
if (-not (Test-Path -LiteralPath $cliPath -PathType Leaf)) {
	throw "Compiler CLI assembly does not exist: $cliPath"
}

$rootManifestData = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
if ($rootManifestData.schemaVersion -ne 1 -or @($rootManifestData.roots).Count -eq 0) {
	throw 'The root manifest must have schemaVersion 1 and at least one root.'
}
$rootIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($root in $rootManifestData.roots) {
	if ([string]::IsNullOrWhiteSpace($root.id) -or
		[string]::IsNullOrWhiteSpace($root.assembly) -or
		[string]::IsNullOrWhiteSpace($root.entry)) {
		throw 'Every root must define non-empty id, assembly, and entry fields.'
	}
	if (-not $rootIds.Add([string] $root.id)) {
		throw "Duplicate root id '$($root.id)'."
	}
}

$rootResults = [System.Collections.Generic.List[object]]::new()
$gapGroups = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
$contractId = $null
$implementationPack = $null
$compiler = $null
$target = $null
foreach ($root in $rootManifestData.roots) {
	Write-Host "Analyzing $($root.id): $($root.entry)"
	$report = Invoke-FrameworkAnalysis $root $manifestDirectory $cliPath $packManifest
	if ($null -eq $contractId) {
		$contractId = $report.contractId
		$implementationPack = $report.implementationPack
		$compiler = $report.compiler
		$target = $report.target
	} elseif ($report.contractId -ne $contractId) {
		throw "Root '$($root.id)' used contract '$($report.contractId)', expected '$contractId'."
	} elseif ((ConvertTo-Json -InputObject $report.compiler -Depth 20 -Compress) -ne
		(ConvertTo-Json -InputObject $compiler -Depth 20 -Compress) -or
		(ConvertTo-Json -InputObject $report.target -Depth 20 -Compress) -ne
		(ConvertTo-Json -InputObject $target -Depth 20 -Compress)) {
		throw "Root '$($root.id)' used a different compiler or target package; closure reports require one toolchain."
	} elseif ((ConvertTo-Json -InputObject $report.implementationPack -Depth 20 -Compress) -ne
		(ConvertTo-Json -InputObject $implementationPack -Depth 20 -Compress)) {
		throw "Root '$($root.id)' used a different implementation pack; closure reports require one pinned input."
	}

	$incompatibleCount = 0
	foreach ($member in $report.members) {
		if ($member.status -notin @('deferred', 'unsupported')) {
			continue
		}
		$incompatibleCount++
		$key = ConvertTo-Json -InputObject @($member.member.displayName, $member.status, $member.reason) -Compress
		if (-not $gapGroups.ContainsKey($key)) {
			$gapGroups.Add($key, [ordered] @{
				member = $member.member.displayName
				status = $member.status
				reason = $member.reason
				rootIds = [System.Collections.Generic.List[string]]::new()
				callSiteCount = 0
				examplePaths = [System.Collections.Generic.List[object]]::new()
			})
		}
		$gap = $gapGroups[$key]
		$gap.rootIds.Add([string] $root.id)
		foreach ($site in $member.callSites) {
			$gap.callSiteCount++
			if ($gap.examplePaths.Count -lt 3) {
				$gap.examplePaths.Add([ordered] @{
					rootId = [string] $root.id
					caller = $site.caller
					ilOffset = $site.ilOffset
					path = @($site.rootPath) + @($member.member.displayName)
				})
			}
		}
	}
	$rootResults.Add([ordered] @{
		id = [string] $root.id
		entry = [string] $root.entry
		isCompatible = [bool] $report.isCompatible
		cpu = $report.cpu
		runtimeProfile = $report.runtimeProfile
		outputFormat = $report.outputFormat
		reachableMethodCount = [int] $report.reachableMethodCount
		frameworkMemberCount = @($report.members).Count
		gapCount = $incompatibleCount
	})
}

$rankedGaps = @($gapGroups.Values | ForEach-Object {
	[ordered] @{
		member = $_.member
		status = $_.status
		reason = $_.reason
		rootCount = $_.rootIds.Count
		callSiteCount = $_.callSiteCount
		rootIds = @($_.rootIds | Sort-Object -Unique)
		examplePaths = @($_.examplePaths)
	}
} | Sort-Object -Property @{ Expression = 'rootCount'; Descending = $true }, @{ Expression = 'callSiteCount'; Descending = $true }, @{ Expression = 'member'; Descending = $false })

$result = [ordered] @{
	schemaVersion = 1
	generatedAtUtc = [DateTime]::UtcNow.ToString('O')
	compiler = $compiler
	target = $target
	contractId = $contractId
	implementationPack = $implementationPack
	rootManifest = [System.IO.Path]::GetFileName($manifestPath)
	rootManifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
	cliAssemblySha256 = (Get-FileHash -LiteralPath $cliPath -Algorithm SHA256).Hash.ToLowerInvariant()
	frameworkImplementationManifestSha256 = if ($packManifest) { (Get-FileHash -LiteralPath $packManifest -Algorithm SHA256).Hash.ToLowerInvariant() } else { $null }
	summary = [ordered] @{
		rootCount = $rootResults.Count
		compatibleRootCount = @($rootResults | Where-Object isCompatible).Count
		incompatibleRootCount = @($rootResults | Where-Object { -not $_.isCompatible }).Count
		distinctGapCount = $rankedGaps.Count
	}
	roots = @($rootResults)
	prioritizedGaps = $rankedGaps
}

$json = ConvertTo-Json -InputObject $result -Depth 40
$fullOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($fullOutputPath)
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$temporaryPath = $fullOutputPath + ".$( [Guid]::NewGuid().ToString('N') ).tmp"
try {
	[System.IO.File]::WriteAllText($temporaryPath, $json, [System.Text.UTF8Encoding]::new($false))
	[System.IO.File]::Move($temporaryPath, $fullOutputPath, $true)
} finally {
	if (Test-Path -LiteralPath $temporaryPath) {
		Remove-Item -LiteralPath $temporaryPath -Force
	}
}

Write-Host "Wrote $($rankedGaps.Count) prioritized framework gaps from $($rootResults.Count) roots to $fullOutputPath"
