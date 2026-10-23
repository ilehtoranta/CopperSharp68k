param(
	[string] $OutputDirectory = 'TestResults/CoreLibCodecQualification/host-oracle',
	[string] $DotNetWorkingDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
	[System.IO.Path]::GetFullPath($OutputDirectory)
} else {
	[System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
}
$dotnetWorkingDirectory = if ([string]::IsNullOrWhiteSpace($DotNetWorkingDirectory)) {
	$repositoryRoot
} elseif ([System.IO.Path]::IsPathRooted($DotNetWorkingDirectory)) {
	[System.IO.Path]::GetFullPath($DotNetWorkingDirectory)
} else {
	[System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $DotNetWorkingDirectory))
}
if (-not [System.IO.Directory]::Exists($dotnetWorkingDirectory)) {
	throw "The selected .NET working directory does not exist: $dotnetWorkingDirectory"
}
[System.IO.Directory]::CreateDirectory($outputRoot) | Out-Null

$fixtures = @(
	'Utf8CodecFixtures.cs', 'Base64CodecFixtures.cs', 'HexCodecFixtures.cs',
	'CoreLibCodecStreamingFixtures.cs', 'NarrowCodecComparisonFixtures.cs',
	'CoreLibCodecPipelineFixtures.cs'
)
$compileItems = [System.Collections.Generic.List[string]]::new()
foreach ($fixture in $fixtures) {
	$path = Join-Path $repositoryRoot "Compiler.Tests/$fixture"
	if (-not [System.IO.File]::Exists($path)) { throw "Host-oracle fixture is missing: $path" }
	$escaped = [System.Security.SecurityElement]::Escape($path)
	$compileItems.Add("    <Compile Include=`"$escaped`" Link=`"$fixture`" />")
}
$projectPath = Join-Path $outputRoot 'host-oracle.csproj'
$programPath = Join-Path $outputRoot 'Program.cs'
$reportPath = Join-Path $outputRoot 'host-oracle.json'
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>host_oracle</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
$($compileItems -join "`n")
  </ItemGroup>
</Project>
"@
$program = @'
using System.Runtime.InteropServices;
using System.Text.Json;
using CopperSharp.Compiler.Tests;

var entries = new Dictionary<string, int>
{
    ["utf8"] = Utf8CodecFixtures.Entry(),
    ["base64"] = Base64CodecFixtures.Entry(),
    ["hex"] = HexCodecFixtures.Entry(),
    ["pipeline"] = CoreLibCodecPipelineFixtures.Entry(),
    ["narrowComparisons"] = NarrowCodecComparisonFixtures.Entry()
};
foreach (var (name, value) in entries)
    if (value != 42) throw new InvalidOperationException($"{name} Entry returned {value}, expected 42");

var checksums = new Dictionary<string, string>
{
    ["utf8Generated"] = $"{Utf8CodecFixtures.GeneratedEntry():X8}",
    ["base64Generated"] = $"{Base64CodecFixtures.GeneratedEntry():X8}",
    ["hexGenerated"] = $"{HexCodecFixtures.GeneratedEntry():X8}",
    ["base64Adversarial"] = $"{Base64CodecFixtures.AdversarialEntry():X8}",
    ["streamingGenerated"] = $"{CoreLibCodecStreamingFixtures.GeneratedEntry():X8}",
    ["narrowComparisonsGenerated"] = $"{NarrowCodecComparisonFixtures.GeneratedEntry():X8}"
};
var expected = new Dictionary<string, string>
{
    ["utf8Generated"] = "A63B47C4",
    ["base64Generated"] = "AB1E1E34",
    ["hexGenerated"] = "0C733167",
    ["base64Adversarial"] = "045E1F44",
    ["streamingGenerated"] = "19F5074C",
    ["narrowComparisonsGenerated"] = "F1F8A995"
};
foreach (var (name, value) in expected)
    if (checksums[name] != value) throw new InvalidOperationException($"{name} returned {checksums[name]}, expected {value}");

var runtimeVersion = Environment.Version.ToString();
var frameworkDescription = RuntimeInformation.FrameworkDescription;
if (runtimeVersion != "10.0.12" || frameworkDescription != ".NET 10.0.12")
    throw new InvalidOperationException($"Host oracle requires .NET 10.0.12, found {frameworkDescription} ({runtimeVersion})");

var report = new
{
    schemaVersion = 1,
    frameworkDescription,
    runtimeVersion,
    entries,
    checksums,
    verdict = "passed"
};
var output = args.Length == 0 ? "host-oracle.json" : args[0];
var fullPath = Path.GetFullPath(output);
Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(fullPath, json + Environment.NewLine);
Console.WriteLine(json);
'@
$utf8 = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($projectPath, $project, $utf8)
[System.IO.File]::WriteAllText($programPath, $program, $utf8)
if ([System.IO.File]::Exists($reportPath)) { Remove-Item -LiteralPath $reportPath -Force }

Push-Location $dotnetWorkingDirectory
try {
	& dotnet run --project $projectPath --configuration Release -- $reportPath
	if ($LASTEXITCODE -ne 0) { throw "CoreLib codec host oracle failed with exit code $LASTEXITCODE." }
} finally {
	Pop-Location
}
if (-not [System.IO.File]::Exists($reportPath)) { throw "CoreLib codec host oracle produced no report: $reportPath" }
Write-Host "CoreLib codec host oracle passed on .NET 10.0.12: $reportPath"
