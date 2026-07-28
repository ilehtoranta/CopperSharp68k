/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Globalization;
using CopperSharp.Compiler;

return Run(args);

static int Run(string[] args)
{
	if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal))
	{
		PrintUsage();
		return args.Length == 0 ? 2 : 0;
	}

	try
	{
		var input = args[0];
		var output = GetRequired(args, "--output");
		var entry = GetRequired(args, "--entry");
		var cpu = ParseCpu(GetOptional(args, "--cpu") ?? "68000");
		var clrPolicy = ParseClrPolicy(GetOptional(args, "--clr") ?? "auto");
		var exceptionMode = ParseExceptionMode(GetOptional(args, "--exceptions") ?? "full");
		var format = ParseFormat(GetOptional(args, "--format") ?? "hunk");
		var romSize = ParseInt(GetOptional(args, "--rom-size") ?? "524288");
		var romBase = ParseUInt(GetOptional(args, "--rom-base") ?? "0");
		var stack = ParseUInt(GetOptional(args, "--stack") ?? "0x80000");
		var imports = ParseImports(args);

		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = input,
			EntryPoint = entry,
			Cpu = cpu,
			ClrPolicy = clrPolicy,
			ExceptionMode = exceptionMode,
			OutputFormat = format,
			Imports = imports,
			Rom = new KickstartRomOutputOptions
			{
				Size = romSize,
				BaseAddress = romBase,
				InitialStackPointer = stack
			}
		});

		var directory = Path.GetDirectoryName(Path.GetFullPath(output));
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		File.WriteAllBytes(output, result.Image);
		File.WriteAllText(output + ".map", result.Map);
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

static M68kCpuTarget ParseCpu(string value) =>
	value.ToLowerInvariant() switch
	{
		"68000" or "m68000" => M68kCpuTarget.M68000,
		"68020" or "m68020" => M68kCpuTarget.M68020,
		"68040" or "m68040" => M68kCpuTarget.M68040,
		_ => throw new ArgumentException($"Unknown CPU '{value}'.")
	};

static M68kClrPolicy ParseClrPolicy(string value) =>
	value.ToLowerInvariant() switch
	{
		"auto" => M68kClrPolicy.Auto,
		"never" or "off" => M68kClrPolicy.Never,
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

static int ParseInt(string value) =>
	checked((int)ParseUInt(value));

static uint ParseUInt(string value) =>
	value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
		? uint.Parse(value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)
		: uint.Parse(value, CultureInfo.InvariantCulture);

static IReadOnlyDictionary<string, uint> ParseImports(string[] args)
	=> ParseNamedAddresses(args, "--import");

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
		  [--cpu 68000|68020|68040] [--clr auto|never|always]
		  [--exceptions full|yolo] [--format hunk|rom|asm]
		  [--rom-size 262144|524288] [--rom-base <address>] [--stack <address>]
		  [--import name=address ...]
		""");
}
