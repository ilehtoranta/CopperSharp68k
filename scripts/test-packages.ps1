param(
    [string]$Configuration = "Release",
    [switch]$KeepArtifacts
)

$ErrorActionPreference = "Stop"

function Invoke-DotNet {
    $dotNetArguments = $args
    & dotnet @dotNetArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($DotNetArguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Assert-DotNetFailure {
    param(
        [string]$ExpectedText,
        [string[]]$Arguments
    )

    $output = & dotnet @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0) {
        throw "dotnet $($Arguments -join ' ') unexpectedly succeeded."
    }
    if ($output.IndexOf($ExpectedText, [StringComparison]::Ordinal) -lt 0) {
        throw "dotnet $($Arguments -join ' ') did not report '$ExpectedText'. Output: $output"
    }
    $script:LastDotNetFailureOutput = $output
    $global:LASTEXITCODE = 0
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$tempRoot = [System.IO.Path]::GetTempPath().TrimEnd([System.IO.Path]::DirectorySeparatorChar)
$auditRoot = Join-Path $tempRoot ("CopperSharp package ää-" + [Guid]::NewGuid().ToString("N"))
$feed = Join-Path $auditRoot "feed"
$cliHome = Join-Path $auditRoot "cli-home"
$projectPath = Join-Path $auditRoot "HelloAmiga"
$nugetConfig = Join-Path $auditRoot "nuget.config"
[xml]$buildProperties = Get-Content -Raw (Join-Path $repoRoot "Directory.Build.props")
$packageVersion = [string]$buildProperties.Project.PropertyGroup.CopperSharpPackageVersion
if ([string]::IsNullOrWhiteSpace($packageVersion)) {
    throw "Directory.Build.props does not define CopperSharpPackageVersion."
}
$explicitProjectVersions = [System.IO.Directory]::EnumerateFiles(
    $repoRoot,
    "*.csproj",
    [System.IO.SearchOption]::AllDirectories) |
    Where-Object {
        $_ -notmatch '[\\/](bin|obj)[\\/]' -and
        (Get-Content -Raw -LiteralPath $_).IndexOf(
            '<PackageVersion>',
            [StringComparison]::Ordinal) -ge 0
    }
if ($explicitProjectVersions.Count -ne 0) {
    throw "PackageVersion must be owned by Directory.Build.props, not project files: $($explicitProjectVersions -join ', ')"
}

$templateProject = Get-Content -Raw (Join-Path $repoRoot "Templates\AmigaApp\CopperSharpAmiga.csproj")
$templateVersions = [regex]::Matches($templateProject, 'PackageReference[^>]+Version="([^"]+)"') |
    ForEach-Object { $_.Groups[1].Value }
if ($templateVersions.Count -eq 0 -or $templateVersions.Where({ $_ -ne $packageVersion }).Count -ne 0) {
    throw "Template PackageReference versions must match $packageVersion."
}
$templateSdkVersion = [regex]::Match($templateProject, 'CopperSharp\.Sdk/([^";]+)').Groups[1].Value
if ($templateSdkVersion -ne $packageVersion) {
    throw "Template CopperSharp.Sdk version must match $packageVersion."
}

$oldCliHome = $env:DOTNET_CLI_HOME
$oldNuGetPackages = $env:NUGET_PACKAGES

try {
    New-Item -ItemType Directory -Path $feed, $cliHome | Out-Null
    $env:DOTNET_CLI_HOME = $cliHome
    $env:NUGET_PACKAGES = Join-Path $auditRoot "packages"

    foreach ($project in @(
        "Compiler\CopperSharp.Compiler.csproj",
        "Compiler.Tests.PackageDependency\CopperSharp.Compiler.Tests.PackageDependency.csproj",
        "Sdk.Amiga\CopperSharp.Sdk.Amiga.csproj",
        "Sdk.Amiga.Support\CopperSharp.Sdk.Amiga.Support.csproj",
        "Sdk\CopperSharp.Sdk.csproj",
        "Compiler.Cli\CopperSharp.Compiler.Cli.csproj",
        "Templates\CopperSharp.Templates.csproj"
    )) {
        $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project.Replace('\', '/'))
        $packBuildRoot = Join-Path $auditRoot "pack-bin\$projectName"
        $packArguments = @(
            "pack", (Join-Path $repoRoot $project), "-c", $Configuration,
            "-o", $feed, "--nologo", "-p:BaseOutputPath=$packBuildRoot\"
        )
        if ($project -eq "Sdk\CopperSharp.Sdk.csproj") {
            $toolOutput = Join-Path $packBuildRoot "$Configuration\net10.0"
            $packArguments += "-p:CopperSharpCompilerToolOutputPath=$toolOutput"
        }
        Invoke-DotNet @packArguments
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($package in [System.IO.Directory]::EnumerateFiles($feed, "*.nupkg").Where({
        -not $_.EndsWith('.snupkg', [StringComparison]::OrdinalIgnoreCase)
    })) {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($package)
        try {
            $nuspecEntries = @($archive.Entries.Where({
                $_.FullName.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase)
            }))
            if ($nuspecEntries.Count -ne 1) {
                throw "$package does not contain exactly one nuspec."
            }
            $nuspecEntry = $nuspecEntries[0]
            $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
            try {
                [xml]$nuspec = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }
            $metadata = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
            if ($metadata.version -ne $packageVersion) {
                throw "$package has package version '$($metadata.version)' instead of '$packageVersion'."
            }
            foreach ($dependency in $nuspec.SelectNodes("//*[local-name()='dependency']")) {
                if ($dependency.id.StartsWith('CopperSharp.', [StringComparison]::Ordinal) -and
                    $dependency.version.IndexOf($packageVersion, [StringComparison]::Ordinal) -lt 0) {
                    throw "$package dependency $($dependency.id) has version '$($dependency.version)' instead of '$packageVersion'."
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }

    $amigaSdkPackage = Join-Path $feed "CopperSharp.Sdk.Amiga.$packageVersion.nupkg"
    $amigaSupportPackage = Join-Path $feed "CopperSharp.Sdk.Amiga.Support.$packageVersion.nupkg"
    foreach ($requiredPackage in @($amigaSdkPackage, $amigaSupportPackage)) {
        if (-not (Test-Path -LiteralPath $requiredPackage -PathType Leaf)) {
            throw "The package audit did not produce $requiredPackage."
        }
    }

    $amigaSdkArchive = [System.IO.Compression.ZipFile]::OpenRead($amigaSdkPackage)
    try {
        $amigaSdkEntries = $amigaSdkArchive.Entries.FullName
        if ("lib/net10.0/CopperSharp.Sdk.Amiga.dll" -notin $amigaSdkEntries) {
            throw "$amigaSdkPackage does not contain the application SDK assembly."
        }
        if ($amigaSdkEntries.Where({
            $_.IndexOf("CopperSharp.Sdk.Amiga.Support", [StringComparison]::OrdinalIgnoreCase) -ge 0
        }).Count -ne 0) {
            throw "$amigaSdkPackage unexpectedly contains host-support artifacts."
        }
    }
    finally {
        $amigaSdkArchive.Dispose()
    }

    $amigaSupportArchive = [System.IO.Compression.ZipFile]::OpenRead($amigaSupportPackage)
    try {
        $amigaSupportEntries = $amigaSupportArchive.Entries.FullName
        if ("lib/net10.0/CopperSharp.Sdk.Amiga.Support.dll" -notin $amigaSupportEntries) {
            throw "$amigaSupportPackage does not contain the host-support assembly."
        }
        $supportNuspecEntries = @($amigaSupportArchive.Entries.Where({
            $_.FullName.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase)
        }))
        if ($supportNuspecEntries.Count -ne 1) {
            throw "$amigaSupportPackage does not contain exactly one nuspec."
        }
        $supportNuspecEntry = $supportNuspecEntries[0]
        $supportReader = [System.IO.StreamReader]::new($supportNuspecEntry.Open())
        try {
            [xml]$supportNuspec = $supportReader.ReadToEnd()
        }
        finally {
            $supportReader.Dispose()
        }
        $sdkDependency = $supportNuspec.SelectSingleNode(
            "//*[local-name()='dependency' and @id='CopperSharp.Sdk.Amiga']")
        if ($null -eq $sdkDependency -or
            $sdkDependency.version.IndexOf($packageVersion, [StringComparison]::Ordinal) -lt 0) {
            throw "$amigaSupportPackage does not depend on CopperSharp.Sdk.Amiga $packageVersion."
        }
    }
    finally {
        $amigaSupportArchive.Dispose()
    }

    $sdkPackage = Join-Path $feed "CopperSharp.Sdk.$packageVersion.nupkg"
    $sdkArchive = [System.IO.Compression.ZipFile]::OpenRead($sdkPackage)
    try {
        $sdkEntries = $sdkArchive.Entries.FullName
        foreach ($requiredEntry in @(
            "Sdk/Sdk.props",
            "Sdk/Sdk.targets",
            "Sdk/amiga-m68k.runtime.json",
            "tools/net10.0/any/CopperSharp.Compiler.Cli.dll",
            "tools/net10.0/any/CopperSharp.Compiler.Cli.runtimeconfig.json",
            "tools/net10.0/any/CopperSharp.Runtime.Managed.dll",
            "tools/net10.0/any/CopperSharp.Runtime.AmigaPal.dll",
            "tools/net10.0/any/CopperSharp.Targets.Amiga.dll"
        )) {
            if ($requiredEntry -notin $sdkEntries) {
                throw "$sdkPackage does not contain $requiredEntry."
            }
        }
        $forbiddenPackagePaths = @(
            $repoRoot,
            $repoRoot.Replace('\', '/'))
        foreach ($entry in $sdkArchive.Entries.Where({
            $_.FullName.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase) -or
            $_.FullName.EndsWith('.props', [StringComparison]::OrdinalIgnoreCase) -or
            $_.FullName.EndsWith('.targets', [StringComparison]::OrdinalIgnoreCase) -or
            $_.FullName.EndsWith('.json', [StringComparison]::OrdinalIgnoreCase) -or
            $_.FullName.EndsWith('.md', [StringComparison]::OrdinalIgnoreCase)
        })) {
            $reader = [System.IO.StreamReader]::new($entry.Open())
            try {
                $packageText = $reader.ReadToEnd()
                foreach ($forbiddenPath in $forbiddenPackagePaths) {
                    if ($packageText.IndexOf($forbiddenPath, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                        throw "$sdkPackage entry $($entry.FullName) contains build-host path '$forbiddenPath'."
                    }
                }
            }
            finally {
                $reader.Dispose()
            }
        }
    }
    finally {
        $sdkArchive.Dispose()
    }

    Invoke-DotNet new nugetconfig -o $auditRoot --force
    Invoke-DotNet nuget add source $feed --name CopperSharpLocal --configfile $nugetConfig

    $templatePackage = Join-Path $feed "CopperSharp.Templates.$packageVersion.nupkg"
    Invoke-DotNet new install $templatePackage
    Invoke-DotNet new amiga -n HelloAmiga -o $projectPath --cpu 68000
    Invoke-DotNet restore (Join-Path $projectPath "HelloAmiga.csproj") --configfile $nugetConfig

    $generatedProject = Join-Path $projectPath "HelloAmiga.csproj"
    Assert-DotNetFailure "received 'linux-x64'" @(
        "msbuild", $generatedProject, "-t:CopperSharpValidateSdk",
        "-p:RuntimeIdentifier=linux-x64", "-p:TargetFramework=net10.0", "--nologo")
    Assert-DotNetFailure "requires TargetFramework 'net10.0'" @(
        "msbuild", $generatedProject, "-t:CopperSharpValidateSdk",
        "-p:RuntimeIdentifier=amiga-m68k", "-p:TargetFramework=net9.0", "--nologo")
    foreach ($mode in @(
        @{ Property = "SelfContained"; Text = "cannot use a Microsoft self-contained runtime pack" },
        @{ Property = "UseAppHost"; Text = "does not use a Microsoft apphost" },
        @{ Property = "PublishAot"; Text = "CopperSharp owns target AOT compilation" },
        @{ Property = "PublishSingleFile"; Text = "is a CoreCLR publish mode" },
        @{ Property = "PublishTrimmed"; Text = "CopperSharp performs closed-world reachability" }
    )) {
        Assert-DotNetFailure $mode.Text @(
            "msbuild", $generatedProject, "-t:CopperSharpValidateSdk",
            "-p:$($mode.Property)=true", "--nologo")
    }

    Invoke-DotNet build (Join-Path $projectPath "HelloAmiga.csproj") `
        -c $Configuration --no-restore --nologo

    $buildHunkPath = Join-Path $projectPath "bin\$Configuration\net10.0\amiga-m68k\HelloAmiga.hunk"
    if (Test-Path -LiteralPath $buildHunkPath) {
        throw "A normal build unexpectedly produced $buildHunkPath."
    }

    Invoke-DotNet publish (Join-Path $projectPath "HelloAmiga.csproj") `
        -c $Configuration --no-restore --nologo

    $hunkPath = Join-Path $projectPath "bin\$Configuration\net10.0\amiga-m68k\publish\HelloAmiga.hunk"
    if (-not (Test-Path -LiteralPath $hunkPath -PathType Leaf)) {
        throw "The generated template did not produce $hunkPath."
    }
    if (-not (Test-Path -LiteralPath ($hunkPath + ".map") -PathType Leaf)) {
        throw "The generated template did not produce $hunkPath.map."
    }
    $mapPath = $hunkPath + ".map"
    $map = Get-Content -Raw -LiteralPath $mapPath
    foreach ($provenanceLine in @(
        "COMPILER CopperSharp.Compiler $packageVersion",
        "CONTRACT net10.0-10.0.9 Microsoft.NETCore.App.Ref 10.0.9",
        "TARGET amiga-m68k CopperSharp.Targets.Amiga $packageVersion",
        "PROFILE Application",
        "CPU M68000",
        "FORMAT Hunk"
    )) {
        if ($map.IndexOf($provenanceLine, [StringComparison]::Ordinal) -lt 0) {
            throw "$mapPath does not contain deterministic provenance '$provenanceLine'."
        }
    }
    if ($map.IndexOf($repoRoot, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $map.IndexOf($auditRoot, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "$mapPath contains a build-host path."
    }
    $frameworkReportPath = $hunkPath + ".framework.json"
    if (-not (Test-Path -LiteralPath $frameworkReportPath -PathType Leaf)) {
        throw "The generated template did not produce $frameworkReportPath."
    }
    $frameworkReport = Get-Content -Raw -LiteralPath $frameworkReportPath | ConvertFrom-Json
    if (-not $frameworkReport.isCompatible) {
        throw "The generated template framework report is not compatible."
    }
    if ($frameworkReport.SchemaVersion -ne 2 -or
        $frameworkReport.Compiler.PackageId -ne "CopperSharp.Compiler" -or
        $frameworkReport.Compiler.PackageVersion -ne $packageVersion -or
        $frameworkReport.ContractId -ne "net10.0-10.0.9" -or
        $frameworkReport.Target.RuntimeIdentifier -ne "amiga-m68k" -or
        $frameworkReport.Target.PackageId -ne "CopperSharp.Targets.Amiga" -or
        $frameworkReport.Target.PackageVersion -ne $packageVersion -or
        $frameworkReport.RuntimeProfile -ne "application" -or
        $frameworkReport.Cpu -ne "m68000" -or
        $frameworkReport.OutputFormat -ne "hunk") {
        throw "The generated template framework report does not contain matching deterministic provenance."
    }

    $responseManifestPath = Join-Path $projectPath "obj\$Configuration\net10.0\amiga-m68k\coppersharp.publish.rsp"
    $incrementalPaths = @($responseManifestPath, $hunkPath, $mapPath, $frameworkReportPath)
    $beforeSecondPublish = @{}
    foreach ($path in $incrementalPaths) {
        $beforeSecondPublish[$path] = @{
            Hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            Timestamp = (Get-Item -LiteralPath $path).LastWriteTimeUtc.Ticks
        }
    }
    Start-Sleep -Milliseconds 1100
    Invoke-DotNet publish (Join-Path $projectPath "HelloAmiga.csproj") `
        -c $Configuration --no-restore --nologo
    foreach ($path in $incrementalPaths) {
        $afterHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        $afterTimestamp = (Get-Item -LiteralPath $path).LastWriteTimeUtc.Ticks
        if ($afterHash -ne $beforeSecondPublish[$path].Hash) {
            throw "The no-op second publish changed bytes in $path."
        }
        if ($afterTimestamp -ne $beforeSecondPublish[$path].Timestamp) {
            throw "The no-op second publish rewrote $path instead of skipping compiler execution."
        }
    }

    $buildHookPath = Join-Path $auditRoot "build-hook\HelloAmiga.hunk"
    Invoke-DotNet build (Join-Path $projectPath "HelloAmiga.csproj") `
        -c $Configuration --no-restore --nologo `
        "-p:CopperSharpCompileOnBuild=true" `
        "-p:CopperSharpBuildOutputPath=$buildHookPath"
    if (-not (Test-Path -LiteralPath $buildHookPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath ($buildHookPath + ".map") -PathType Leaf) -or
        -not (Test-Path -LiteralPath ($buildHookPath + ".framework.json") -PathType Leaf)) {
        throw "CopperSharpCompileOnBuild did not emit its isolated build outputs."
    }

    $legacyBuildPath = Join-Path $auditRoot "legacy-build\HelloAmiga.hunk"
    $legacyBuildOutput = & dotnet build (Join-Path $projectPath "HelloAmiga.csproj") `
        -c $Configuration --no-restore --nologo `
        "-p:Copper68kCompile=true" `
        "-p:Copper68kOutputPath=$legacyBuildPath" 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "The legacy Copper68k* alias build failed: $legacyBuildOutput"
    }
    if ($legacyBuildOutput.IndexOf("C68KSDK001", [StringComparison]::Ordinal) -lt 0 -or
        -not (Test-Path -LiteralPath $legacyBuildPath -PathType Leaf)) {
        throw "The legacy Copper68k* alias did not warn and emit through the packaged compiler."
    }

    $cpuMatrixRoot = Join-Path $auditRoot "cpu-matrix"
    foreach ($cpu in @("68020", "68040", "68060")) {
        $cpuOutput = Join-Path $cpuMatrixRoot "HelloAmiga-$cpu.hunk"
        Invoke-DotNet publish (Join-Path $projectPath "HelloAmiga.csproj") `
            -c $Configuration --no-restore --nologo `
            "-p:CopperSharpCpu=$cpu" `
            "-p:CopperSharpOutputPath=$cpuOutput" `
            "-p:CopperSharpFrameworkReportPath=$cpuOutput.framework.json"
        $expectedCpu = "CPU M$cpu"
        if ((Get-Content -Raw -LiteralPath ($cpuOutput + ".map")).IndexOf(
            $expectedCpu,
            [StringComparison]::Ordinal) -lt 0) {
            throw "The CPU matrix output for $cpu does not contain '$expectedCpu'."
        }
    }

    $toolPath = Join-Path $auditRoot "tools"
    New-Item -ItemType Directory -Path $toolPath | Out-Null
    Invoke-DotNet tool install --tool-path $toolPath --add-source $feed `
        CopperSharp.Compiler.Cli --version $packageVersion --ignore-failed-sources

    $compatibleProject = Join-Path $repoRoot "Compiler.Tests.PackageConsumers\Compatible\Compatible.csproj"
    $compatibleIntermediate = Join-Path $auditRoot "compatible-obj"
    Invoke-DotNet restore $compatibleProject --configfile $nugetConfig `
        "-p:BaseIntermediateOutputPath=$compatibleIntermediate/"
    Invoke-DotNet publish $compatibleProject -c $Configuration --no-restore --nologo `
        "-p:BaseOutputPath=$(Join-Path $auditRoot 'compatible-bin')/" `
        "-p:BaseIntermediateOutputPath=$compatibleIntermediate/"
    $compatibleHunk = Join-Path $auditRoot "compatible-bin\Release\net10.0\amiga-m68k\publish\Compatible.hunk"
    if (-not (Test-Path -LiteralPath $compatibleHunk -PathType Leaf)) {
        throw "The compatible package consumer did not produce $compatibleHunk."
    }
    $compatibleReport = Get-Content -Raw -LiteralPath ($compatibleHunk + ".framework.json") | ConvertFrom-Json
    if (-not $compatibleReport.isCompatible) {
        throw "The compatible package consumer report is not compatible."
    }

    $formatMatrixRoot = Join-Path $auditRoot "format-matrix"
    $assemblyOutput = Join-Path $formatMatrixRoot "Compatible.s"
    Invoke-DotNet publish $compatibleProject -c $Configuration --no-restore --nologo `
        "-p:BaseOutputPath=$(Join-Path $auditRoot 'compatible-bin')/" `
        "-p:BaseIntermediateOutputPath=$compatibleIntermediate/" `
        "-p:CopperSharpOutputFormat=asm" `
        "-p:CopperSharpRuntimeProfile=freestanding" `
        "-p:CopperSharpOutputPath=$assemblyOutput" `
        "-p:CopperSharpFrameworkReportPath=$assemblyOutput.framework.json"
    $assemblyMap = Get-Content -Raw -LiteralPath ($assemblyOutput + ".map")
    if ($assemblyMap.IndexOf("FORMAT Assembly", [StringComparison]::Ordinal) -lt 0 -or
        $assemblyMap.IndexOf("PROFILE Freestanding", [StringComparison]::Ordinal) -lt 0) {
        throw "Assembler publish did not map the requested format and profile."
    }

    $romOutput = Join-Path $formatMatrixRoot "Compatible.rom"
    Invoke-DotNet publish $compatibleProject -c $Configuration --no-restore --nologo `
        "-p:BaseOutputPath=$(Join-Path $auditRoot 'compatible-bin')/" `
        "-p:BaseIntermediateOutputPath=$compatibleIntermediate/" `
        "-p:CopperSharpOutputFormat=rom" `
        "-p:CopperSharpRuntimeProfile=rom" `
        "-p:CopperSharpOutputPath=$romOutput" `
        "-p:CopperSharpFrameworkReportPath=$romOutput.framework.json"
    $romMap = Get-Content -Raw -LiteralPath ($romOutput + ".map")
    if ((Get-Item -LiteralPath $romOutput).Length -ne 524288 -or
        $romMap.IndexOf("FORMAT KickstartRom", [StringComparison]::Ordinal) -lt 0 -or
        $romMap.IndexOf("PROFILE Rom", [StringComparison]::Ordinal) -lt 0) {
        throw "ROM publish did not emit the requested 512 KiB ROM/profile."
    }

    $incompatibleProject = Join-Path $repoRoot "Compiler.Tests.PackageConsumers\Incompatible\Incompatible.csproj"
    $incompatibleIntermediate = Join-Path $auditRoot "incompatible-obj"
    Invoke-DotNet restore $incompatibleProject --configfile $nugetConfig `
        "-p:BaseIntermediateOutputPath=$incompatibleIntermediate/"
    $incompatibleHunk = Join-Path $auditRoot "incompatible-bin\Release\net10.0\amiga-m68k\publish\Incompatible.hunk"
    $incompatibleHunkDirectory = Split-Path -Parent $incompatibleHunk
    New-Item -ItemType Directory -Path $incompatibleHunkDirectory -Force | Out-Null
    $sentinel = [byte[]](0x43, 0x53, 0x36, 0x38, 0x4b)
    [System.IO.File]::WriteAllBytes($incompatibleHunk, $sentinel)
    Assert-DotNetFailure "System.String::Concat" @(
        "publish", $incompatibleProject, "-c", $Configuration, "--no-restore", "--nologo",
        "-p:BaseOutputPath=$(Join-Path $auditRoot 'incompatible-bin')/",
        "-p:BaseIntermediateOutputPath=$incompatibleIntermediate/")
    if ($script:LastDotNetFailureOutput.IndexOf("Root path:", [StringComparison]::Ordinal) -lt 0) {
        throw "The incompatible package diagnostic did not contain a rooting path."
    }
    if (-not [System.Collections.StructuralComparisons]::StructuralEqualityComparer.Equals(
        [System.IO.File]::ReadAllBytes($incompatibleHunk),
        $sentinel)) {
        throw "The failed package publish replaced the previous target artifact."
    }

    Write-Host "Package smoke test passed: $hunkPath"
}
finally {
    $env:DOTNET_CLI_HOME = $oldCliHome
    $env:NUGET_PACKAGES = $oldNuGetPackages

    if (-not $KeepArtifacts -and (Test-Path -LiteralPath $auditRoot)) {
        $resolvedAuditRoot = (Resolve-Path -LiteralPath $auditRoot).Path
        $resolvedTempRoot = (Resolve-Path -LiteralPath $tempRoot).Path
        if ($resolvedAuditRoot.StartsWith($resolvedTempRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedAuditRoot -Recurse -Force
        }
    }
}
