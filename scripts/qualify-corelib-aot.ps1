param(
	[string] $CoreLibAssembly,
	[string] $OutputDirectory = './TestResults/CoreLibAotQualification',
	[string] $DotNetWorkingDirectory,
	[switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dotnetWorkingDirectory = if ([string]::IsNullOrWhiteSpace($DotNetWorkingDirectory)) {
	$repositoryRoot
} elseif ([System.IO.Path]::IsPathRooted($DotNetWorkingDirectory)) {
	[System.IO.Path]::GetFullPath($DotNetWorkingDirectory)
} else {
	[System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $DotNetWorkingDirectory))
}
$rootManifest = Join-Path $repositoryRoot 'Compiler/Framework/corelib-closure-roots.json'
$cliProject = Join-Path $repositoryRoot 'Compiler.Cli/CopperSharp.Compiler.Cli.csproj'
$testsProject = Join-Path $repositoryRoot 'Compiler.Tests/CopperSharp.Compiler.Tests.csproj'
$cliAssembly = Join-Path $repositoryRoot 'Compiler.Cli/bin/Release/net10.0/CopperSharp.Compiler.Cli.dll'
$testAssembly = Join-Path $repositoryRoot 'Compiler.Tests/bin/Release/net10.0/CopperSharp.Compiler.Tests.dll'
$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
	[System.IO.Path]::GetFullPath($OutputDirectory)
} else {
	[System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
}

if ([string]::IsNullOrWhiteSpace($CoreLibAssembly)) {
	$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
	$CoreLibAssembly = Join-Path $nugetRoot 'microsoft.netcore.app.runtime.win-x64/10.0.9/runtimes/win-x64/lib/net10.0/System.Private.CoreLib.dll'
}
$CoreLibAssembly = [System.IO.Path]::GetFullPath($CoreLibAssembly)
if (-not (Test-Path -LiteralPath $CoreLibAssembly -PathType Leaf)) {
	throw "Pinned .NET 10.0.9 CoreLib assembly is missing: $CoreLibAssembly"
}

if (-not $NoBuild) {
	Push-Location $dotnetWorkingDirectory
	try {
		& dotnet build $cliProject --configuration Release
		if ($LASTEXITCODE -ne 0) { throw "Compiler CLI build failed with exit code $LASTEXITCODE." }
		& dotnet build $testsProject --configuration Release
		if ($LASTEXITCODE -ne 0) { throw "Compiler test build failed with exit code $LASTEXITCODE." }
	} finally {
		Pop-Location
	}
}

foreach ($requiredPath in @($cliAssembly, $testAssembly)) {
	if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
		throw "Required Release build output is missing: $requiredPath"
	}
}

$packDirectory = Join-Path $outputRoot 'pack'
& (Join-Path $PSScriptRoot 'create-pinned-corelib-pack.ps1') `
	-CoreLibAssembly $CoreLibAssembly `
	-OutputDirectory $packDirectory
$packManifest = Join-Path $packDirectory 'corelib-pack.json'
$reportPath = Join-Path $outputRoot 'closure.json'
& (Join-Path $PSScriptRoot 'analyze-framework-closure.ps1') `
	-CliAssembly $cliAssembly `
	-RootManifest $rootManifest `
	-FrameworkImplementationManifest $packManifest `
	-OutputPath $reportPath

$closure = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
if ($closure.summary.rootCount -ne 19 -or
	$closure.summary.compatibleRootCount -ne 19 -or
	$closure.summary.distinctGapCount -ne 0) {
	throw "CoreLib closure gate failed: $($closure.summary.compatibleRootCount)/$($closure.summary.rootCount) roots compatible, $($closure.summary.distinctGapCount) distinct gaps. See $reportPath"
}

$rootManifestData = Get-Content -LiteralPath $rootManifest -Raw | ConvertFrom-Json
if (@($rootManifestData.roots).Count -ne 19) {
	throw "The pinned CoreLib qualification gate expects exactly 19 roots; found $(@($rootManifestData.roots).Count)."
}
foreach ($root in $rootManifestData.roots) {
	if ([string]::IsNullOrWhiteSpace($root.id) -or
		[string]::IsNullOrWhiteSpace($root.assembly) -or
		[string]::IsNullOrWhiteSpace($root.entry) -or
		[string]::IsNullOrWhiteSpace($root.qualificationTest) -or
		$null -eq $root.expectedReturnValue) {
		throw 'Every root must define id, assembly, entry, expectedReturnValue, and qualificationTest fields.'
	}
}
$hunkDirectory = Join-Path $outputRoot 'hunks'
[System.IO.Directory]::CreateDirectory($hunkDirectory) | Out-Null
foreach ($root in $rootManifestData.roots) {
	$assembly = if ([System.IO.Path]::IsPathRooted($root.assembly)) {
		[System.IO.Path]::GetFullPath($root.assembly)
	} else {
		[System.IO.Path]::GetFullPath((Join-Path (Split-Path $rootManifest -Parent) $root.assembly))
	}
	$hunkPath = Join-Path $hunkDirectory ($root.id + '.hunk')
	$compatibilityPath = Join-Path $hunkDirectory ($root.id + '.compatibility.json')
	$arguments = [System.Collections.Generic.List[string]]::new()
	$arguments.Add($cliAssembly)
	$arguments.Add($assembly)
	$arguments.Add('--entry')
	$arguments.Add([string] $root.entry)
	$arguments.Add('--platform')
	$arguments.Add([string] $(if ($root.platform) { $root.platform } else { 'amiga' }))
	$arguments.Add('--cpu')
	$arguments.Add([string] $(if ($root.cpu) { $root.cpu } else { '68000' }))
	$arguments.Add('--fpu')
	$arguments.Add([string] $(if ($root.fpu) { $root.fpu } else { 'disabled' }))
	$arguments.Add('--runtime')
	$arguments.Add([string] $(if ($root.runtime) { $root.runtime } else { 'application' }))
	$arguments.Add('--format')
	$arguments.Add([string] $(if ($root.format) { $root.format } else { 'hunk' }))
	$arguments.Add('--memory')
	$arguments.Add([string] $(if ($root.memory) { $root.memory } else { 'default' }))
	if ($root.heapSize) {
		$arguments.Add('--heap-size')
		$arguments.Add([string] $root.heapSize)
	}
	$arguments.Add('--exports')
	$arguments.Add([string] $(if ($root.exports) { $root.exports } else { 'none' }))
	foreach ($managedAssembly in @($root.managedAssemblies)) {
		if ($managedAssembly) {
			$managedPath = if ([System.IO.Path]::IsPathRooted($managedAssembly)) {
				[System.IO.Path]::GetFullPath($managedAssembly)
			} else {
				[System.IO.Path]::GetFullPath((Join-Path (Split-Path $rootManifest -Parent) $managedAssembly))
			}
			$arguments.Add('--managed-assembly')
			$arguments.Add($managedPath)
		}
	}
	$arguments.Add('--framework-implementation-manifest')
	$arguments.Add($packManifest)
	$arguments.Add('--compatibility-report')
	$arguments.Add($compatibilityPath)
	$arguments.Add('--output')
	$arguments.Add($hunkPath)

	Write-Host "Compiling $($root.id) to Hunk"
	& dotnet @arguments
	if ($LASTEXITCODE -ne 0) {
		throw "AOT compilation failed for root '$($root.id)' with exit code $LASTEXITCODE."
	}
	$compiledCompatibility = Get-Content -LiteralPath $compatibilityPath -Raw | ConvertFrom-Json
	if (-not $compiledCompatibility.isCompatible) {
		throw "Compiled root '$($root.id)' reported framework compatibility gaps."
	}
	$hunkBytes = [System.IO.File]::ReadAllBytes($hunkPath)
	if ($hunkBytes.Length -lt 4 -or
		$hunkBytes[0] -ne 0 -or $hunkBytes[1] -ne 0 -or
		$hunkBytes[2] -ne 3 -or $hunkBytes[3] -ne 243) {
		throw "Root '$($root.id)' did not produce a valid HUNK_HEADER image."
	}
}

Push-Location $dotnetWorkingDirectory
try {
	$testFilters = @($rootManifestData.roots | ForEach-Object {
		$filter = "FullyQualifiedName~$($_.qualificationTest)"
		if ($_.qualificationDisplayName) {
			$filter += "&DisplayName~$($_.qualificationDisplayName)"
		}
		$filter
	})
	$testFilter = $testFilters -join '|'
	Write-Host "Running the mapped M68000 emulator qualification cases"
	$executionDirectory = Join-Path $outputRoot 'execution'
	& dotnet test $testsProject --configuration Release --no-build --filter $testFilter `
		--logger 'trx;LogFileName=corelib-execution.trx' --results-directory $executionDirectory
	if ($LASTEXITCODE -ne 0) { throw "CoreLib AOT execution qualification failed with exit code $LASTEXITCODE." }
	[xml] $executionReport = Get-Content -LiteralPath (Join-Path $executionDirectory 'corelib-execution.trx') -Raw
	$counters = $executionReport.TestRun.ResultSummary.Counters
	$expectedCases = @($rootManifestData.roots).Count
	if ([int] $counters.total -ne $expectedCases -or
		[int] $counters.executed -ne $expectedCases -or
		[int] $counters.passed -ne $expectedCases -or [int] $counters.failed -ne 0) {
		throw "CoreLib execution selection must pass all $expectedCases mapped cases without skips: $($counters.OuterXml)"
	}
} finally {
	Pop-Location
}

Write-Host "CoreLib AOT qualification passed for all 19 roots. Reports and Hunk images: $outputRoot"
