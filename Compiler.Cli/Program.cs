/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CopperSharp.Compiler;
using CopperSharp.Targets.Amiga;

return Run(args);

static int Run(string[] args)
{
	try
	{
		args = ExpandResponseManifest(args);
		if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal))
		{
			PrintUsage();
			return args.Length == 0 ? 2 : 0;
		}

		var input = args[0];
		var frameworkReport = GetOptional(args, "--framework-report");
		var compatibilityReport = GetOptional(args, "--compatibility-report");
		if (frameworkReport is not null && compatibilityReport is not null)
		{
			throw new ArgumentException(
				"--framework-report and --compatibility-report cannot be used together.");
		}
		var output = frameworkReport is null ? GetRequired(args, "--output") : null;
		var entry = GetRequired(args, "--entry");
		var platform = ParsePlatform(GetOptional(args, "--platform") ?? "generic");
		var cpu = ParseCpu(GetOptional(args, "--cpu") ?? "68000");
		var floatingPoint = ParseFloatingPoint(GetOptional(args, "--fpu") ?? "disabled");
		var clrPolicy = ParseClrPolicy(GetOptional(args, "--clr") ?? "auto");
		var exceptionMode = ParseExceptionMode(GetOptional(args, "--exceptions") ?? "full");
		var format = ParseFormat(GetOptional(args, "--format") ?? "hunk");
		var runtimeProfile = ParseRuntimeProfile(
			GetOptional(args, "--runtime") ??
			format switch
			{
				M68kOutputFormat.Hunk => "application",
				M68kOutputFormat.KickstartRom => "rom",
				_ => "freestanding"
			});
		var romSize = ParseInt(GetOptional(args, "--rom-size") ?? "524288");
		var romBase = ParseUInt(GetOptional(args, "--rom-base") ?? "0");
		var stack = ParseUInt(GetOptional(args, "--stack") ?? "0x80000");
		var imports = ParseImports(args);
		var managedAssemblyPaths = ParseManagedAssemblyPaths(
			GetOptional(args, "--managed-assemblies"),
			GetAll(args, "--managed-assembly"));

		var request = new M68kCompilationRequest
		{
			AssemblyPath = input,
			EntryPoint = entry,
			Cpu = cpu,
			FloatingPoint = floatingPoint,
			ClrPolicy = clrPolicy,
			ExceptionMode = exceptionMode,
			OutputFormat = format,
			RuntimeProfile = runtimeProfile,
			Imports = imports,
			ManagedAssemblyPaths = managedAssemblyPaths,
			Rom = new KickstartRomOutputOptions
			{
				Size = romSize,
				BaseAddress = romBase,
				InitialStackPointer = stack
			}
		};

		if (frameworkReport is not null)
		{
			var analysis = platform == CompilerPlatform.Amiga
				? AmigaM68kCompiler.AnalyzeFramework(request)
				: M68kCompiler.AnalyzeFramework(request);
			WriteFrameworkReport(frameworkReport, analysis, request, platform);

			return analysis.IsCompatible ? 0 : 1;
		}

		var result = platform == CompilerPlatform.Amiga
			? AmigaM68kCompiler.Compile(request)
			: M68kCompiler.Compile(request);
		if (compatibilityReport is not null)
		{
			WriteFrameworkReport(
				compatibilityReport,
				result.FrameworkAnalysis,
				request,
				platform);
		}

		var directory = Path.GetDirectoryName(Path.GetFullPath(output!));
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		WriteOutputAtomically(output!, result.Image, result.Map);
		Console.WriteLine(
			$"Wrote {result.Image.Length} bytes for {cpu} to '{output}' (entry ${result.EntryPoint:X8}).");
		return 0;
	}
	catch (M68kCompilationException exception)
	{
		Console.Error.WriteLine(exception.Message);
		return 1;
	}
	catch (Exception exception) when (
		exception is ArgumentException or
		IOException or
		UnauthorizedAccessException or
		FormatException or
		OverflowException)
	{
		Console.Error.WriteLine($"copper68kc: {exception.Message}");
		return 2;
	}
}

static void WriteOutputAtomically(string outputPath, byte[] image, string map)
{
	var fullOutputPath = Path.GetFullPath(outputPath);
	var directory = Path.GetDirectoryName(fullOutputPath)!;
	var temporarySuffix = $".{Guid.NewGuid():N}.tmp";
	var temporaryOutput = fullOutputPath + temporarySuffix;
	var mapPath = fullOutputPath + ".map";
	var temporaryMap = mapPath + temporarySuffix;
	try
	{
		File.WriteAllBytes(temporaryOutput, image);
		File.WriteAllText(temporaryMap, map);
		File.Move(temporaryOutput, fullOutputPath, overwrite: true);
		File.Move(temporaryMap, mapPath, overwrite: true);
	}
	finally
	{
		File.Delete(temporaryOutput);
		File.Delete(temporaryMap);
	}
}

static string[] ExpandResponseManifest(string[] args)
{
	if (args.Length == 0 || !args[0].StartsWith('@'))
	{
		return args;
	}
	if (args.Length != 1 || args[0].Length == 1)
	{
		throw new ArgumentException(
			"A CopperSharp response manifest must be the only command-line argument.");
	}

	var manifestPath = Path.GetFullPath(args[0][1..]);
	var lines = File.ReadAllLines(manifestPath);
	if (lines.Length == 0 || lines[0] != "coppersharp-response-v1")
	{
		throw new ArgumentException(
			$"Response manifest '{manifestPath}' does not start with supported version 'coppersharp-response-v1'.");
	}

	string? input = null;
	var result = new List<string>();
	var singleKeys = new HashSet<string>(StringComparer.Ordinal);
	for (var index = 1; index < lines.Length; index++)
	{
		var line = lines[index];
		if (line.Length == 0)
		{
			continue;
		}
		var separator = line.IndexOf('=');
		if (separator <= 0)
		{
			throw new ArgumentException(
				$"Response manifest '{manifestPath}' has invalid line {index + 1}; expected key=value.");
		}

		var key = line[..separator];
		var value = line[(separator + 1)..];
		if (key == "input")
		{
			if (input is not null)
			{
				throw new ArgumentException(
					$"Response manifest '{manifestPath}' contains duplicate key 'input'.");
			}
			input = value;
			continue;
		}

		var option = key switch
		{
			"entry" => "--entry",
			"output" => "--output",
			"platform" => "--platform",
			"cpu" => "--cpu",
			"fpu" => "--fpu",
			"clr" => "--clr",
			"exceptions" => "--exceptions",
			"format" => "--format",
			"runtime" => "--runtime",
			"rom-size" => "--rom-size",
			"rom-base" => "--rom-base",
			"stack" => "--stack",
			"framework-report" => "--framework-report",
			"compatibility-report" => "--compatibility-report",
			"managed-assembly" => "--managed-assembly",
			"import" => "--import",
			_ => throw new ArgumentException(
				$"Response manifest '{manifestPath}' contains unknown key '{key}'.")
		};
		if (key is not ("managed-assembly" or "import") && !singleKeys.Add(key))
		{
			throw new ArgumentException(
				$"Response manifest '{manifestPath}' contains duplicate key '{key}'.");
		}
		result.Add(option);
		result.Add(value);
	}

	if (string.IsNullOrWhiteSpace(input))
	{
		throw new ArgumentException(
			$"Response manifest '{manifestPath}' does not define 'input'.");
	}
	result.Insert(0, input);
	return [.. result];
}

static void WriteFrameworkReport(
	string reportPath,
	M68kFrameworkAnalysisResult analysis,
	M68kCompilationRequest request,
	CompilerPlatform platform)
{
	var options = new JsonSerializerOptions
	{
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
	};
	var compilerVersion = GetPackageVersion(typeof(M68kCompiler).Assembly);
	var target = platform == CompilerPlatform.Amiga
		? new CompatibilityTargetIdentity(
			"amiga-m68k",
			"CopperSharp.Targets.Amiga",
			GetPackageVersion(typeof(AmigaM68kCompiler).Assembly))
		: new CompatibilityTargetIdentity(
			"m68k",
			"CopperSharp.Compiler",
			compilerVersion);
	var report = new CompatibilityReport(
		1,
		new CompatibilityPackageIdentity("CopperSharp.Compiler", compilerVersion),
		$"{analysis.Contract.TargetFramework}-{analysis.Contract.ReferencePackVersion}",
		analysis.Contract,
		target,
		request.RuntimeProfile,
		request.Cpu,
		request.OutputFormat,
		analysis.IsCompatible,
		analysis.Members,
		analysis.ManagedAllocationSites);
	var json = JsonSerializer.Serialize(report, options);
	if (reportPath == "-")
	{
		Console.WriteLine(json);
		return;
	}

	var fullPath = Path.GetFullPath(reportPath);
	var directory = Path.GetDirectoryName(fullPath)!;
	Directory.CreateDirectory(directory);
	var temporaryPath = fullPath + $".{Guid.NewGuid():N}.tmp";
	try
	{
		File.WriteAllText(temporaryPath, json);
		File.Move(temporaryPath, fullPath, overwrite: true);
	}
	finally
	{
		File.Delete(temporaryPath);
	}
	Console.WriteLine(
		$"Wrote framework compatibility report for {analysis.Members.Count} reachable members to '{reportPath}'.");
}

static string GetPackageVersion(Assembly assembly)
{
	var informationalVersion = assembly
		.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
		.InformationalVersion;
	if (!string.IsNullOrWhiteSpace(informationalVersion))
	{
		var metadataSeparator = informationalVersion.IndexOf('+');
		return metadataSeparator < 0
			? informationalVersion
			: informationalVersion[..metadataSeparator];
	}

	return assembly.GetName().Version?.ToString() ?? "unknown";
}

static string GetRequired(string[] args, string name) =>
	GetOptional(args, name) ??
	throw new ArgumentException($"Required option {name} was not supplied.");

static string? GetOptional(string[] args, string name)
{
	for (var index = 1; index < args.Length; index++)
	{
		if (args[index] == name)
		{
			if (index + 1 >= args.Length)
			{
				throw new ArgumentException($"Option {name} requires a value.");
			}

			return args[index + 1];
		}
	}

	return null;
}

static IReadOnlyList<string> GetAll(string[] args, string name)
{
	var result = new List<string>();
	for (var index = 1; index < args.Length; index++)
	{
		if (args[index] != name)
		{
			continue;
		}
		if (++index >= args.Length)
		{
			throw new ArgumentException($"Option {name} requires a value.");
		}
		result.Add(args[index]);
	}
	return result;
}

static CompilerPlatform ParsePlatform(string value) =>
	value.ToLowerInvariant() switch
	{
		"generic" or "none" => CompilerPlatform.Generic,
		"amiga" => CompilerPlatform.Amiga,
		_ => throw new ArgumentException($"Unknown platform '{value}'.")
	};

static M68kCpuTarget ParseCpu(string value) =>
	value.ToLowerInvariant() switch
	{
		"68000" or "m68000" => M68kCpuTarget.M68000,
		"68020" or "m68020" => M68kCpuTarget.M68020,
		"68040" or "m68040" => M68kCpuTarget.M68040,
		"68060" or "m68060" => M68kCpuTarget.M68060,
		_ => throw new ArgumentException($"Unknown CPU '{value}'.")
	};

static M68kFloatingPointMode ParseFloatingPoint(string value) =>
	value.ToLowerInvariant() switch
	{
		"disabled" or "none" => M68kFloatingPointMode.Disabled,
		"040" or "68040" or "m68040" or "native" => M68kFloatingPointMode.M68040,
		"68882" or "m68882" => M68kFloatingPointMode.M68882,
		"soft" or "softfloat" or "copperfloat" => M68kFloatingPointMode.SoftFloat,
		_ => throw new ArgumentException($"Unknown floating-point mode '{value}'.")
	};

static M68kClrPolicy ParseClrPolicy(string value) =>
	value.ToLowerInvariant() switch
	{
		"auto" => M68kClrPolicy.Auto,
		"always" or "on" => M68kClrPolicy.Always,
		_ => throw new ArgumentException($"Unknown CLR policy '{value}'.")
	};

static M68kExceptionMode ParseExceptionMode(string value) =>
	value.ToLowerInvariant() switch
	{
		"full" or "safe" => M68kExceptionMode.Full,
		"yolo" or "fatal" => M68kExceptionMode.Yolo,
		_ => throw new ArgumentException($"Unknown exception mode '{value}'.")
	};

static M68kOutputFormat ParseFormat(string value) =>
	value.ToLowerInvariant() switch
	{
		"hunk" => M68kOutputFormat.Hunk,
		"rom" or "kickstart" => M68kOutputFormat.KickstartRom,
		"asm" or "assembly" or "text" => M68kOutputFormat.Assembly,
		_ => throw new ArgumentException($"Unknown output format '{value}'.")
	};

static M68kRuntimeProfile ParseRuntimeProfile(string value) =>
	value.ToLowerInvariant() switch
	{
		"freestanding" or "unknown" => M68kRuntimeProfile.Freestanding,
		"application" or "app" => M68kRuntimeProfile.Application,
		"rom" or "persistent" => M68kRuntimeProfile.Rom,
		_ => throw new ArgumentException($"Unknown runtime profile '{value}'.")
	};

static int ParseInt(string value) =>
	checked((int)ParseUInt(value));

static uint ParseUInt(string value) =>
	value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
		? uint.Parse(value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)
		: uint.Parse(value, CultureInfo.InvariantCulture);

static IReadOnlyDictionary<string, uint> ParseImports(string[] args)
	=> ParseNamedAddresses(args, "--import");

static IReadOnlyList<string> ParseManagedAssemblyPaths(
	string? listPath,
	IReadOnlyList<string> directPaths)
{
	if (listPath is null && directPaths.Count == 0)
	{
		return [];
	}

	var fullListPath = listPath is null ? null : Path.GetFullPath(listPath);
	var result = new List<string>();
	var pathsByFileName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	var candidates = directPaths.Concat(
		fullListPath is null ? [] : File.ReadLines(fullListPath));
	foreach (var line in candidates)
	{
		if (string.IsNullOrWhiteSpace(line))
		{
			continue;
		}

		var path = Path.GetFullPath(line);
		if (!File.Exists(path))
		{
			throw new ArgumentException(
				$"Managed assembly input names missing file '{path}'.");
		}
		var fileName = Path.GetFileName(path);
		if (pathsByFileName.TryGetValue(fileName, out var previousPath))
		{
			if (!FilesHaveEqualContent(previousPath, path))
			{
				throw new ArgumentException(
					$"Managed assembly inputs contain different files with the same assembly filename '{fileName}': '{previousPath}' and '{path}'.");
			}
			continue;
		}

		pathsByFileName.Add(fileName, path);
		result.Add(path);
	}
	return result;
}

static bool FilesHaveEqualContent(string leftPath, string rightPath)
{
	if (string.Equals(leftPath, rightPath, StringComparison.OrdinalIgnoreCase))
	{
		return true;
	}
	var leftInfo = new FileInfo(leftPath);
	var rightInfo = new FileInfo(rightPath);
	if (leftInfo.Length != rightInfo.Length)
	{
		return false;
	}
	using var left = File.OpenRead(leftPath);
	using var right = File.OpenRead(rightPath);
	return SHA256.HashData(left).AsSpan().SequenceEqual(SHA256.HashData(right));
}

static IReadOnlyDictionary<string, uint> ParseNamedAddresses(string[] args, string option)
{
	var result = new Dictionary<string, uint>(StringComparer.Ordinal);
	for (var index = 1; index < args.Length; index++)
	{
		if (args[index] != option)
		{
			continue;
		}

		if (++index >= args.Length)
		{
			throw new ArgumentException($"Option {option} requires name=address.");
		}

		var pair = args[index].Split('=', 2);
		if (pair.Length != 2 || string.IsNullOrWhiteSpace(pair[0]))
		{
			throw new ArgumentException($"Invalid import '{args[index]}'; expected name=address.");
		}

		result.Add(pair[0], ParseUInt(pair[1]));
	}

	return result;
}

static void PrintUsage()
{
	Console.WriteLine(
		"""
		copper68kc <assembly.dll> --entry Namespace.Type::Method --output <file>
		copper68kc <assembly.dll> --entry Namespace.Type::Method --framework-report <file|->
		copper68kc @response-manifest
		  [--platform generic|amiga]
		  [--cpu 68000|68020|68040|68060] [--fpu disabled|040|68882|soft]
		  [--clr auto|always]
		  [--exceptions full|yolo] [--format hunk|rom|asm]
		  [--runtime freestanding|application|rom]
		  [--rom-size 262144|524288] [--rom-base <address>] [--stack <address>]
		  [--import name=address ...]
		  [--managed-assemblies <UTF-8 path-list>]
		  [--compatibility-report <file|->]
		""");
}

enum CompilerPlatform
{
	Generic,
	Amiga
}

sealed record CompatibilityPackageIdentity(string PackageId, string PackageVersion);

sealed record CompatibilityTargetIdentity(
	string RuntimeIdentifier,
	string PackageId,
	string PackageVersion);

sealed record CompatibilityReport(
	int SchemaVersion,
	CompatibilityPackageIdentity Compiler,
	string ContractId,
	M68kFrameworkContract Contract,
	CompatibilityTargetIdentity Target,
	M68kRuntimeProfile RuntimeProfile,
	M68kCpuTarget Cpu,
	M68kOutputFormat OutputFormat,
	bool IsCompatible,
	IReadOnlyList<M68kFrameworkMemberAnalysis> Members,
	IReadOnlyList<M68kManagedAllocationSite> ManagedAllocationSites);
