/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CopperSharp.Targets.Amiga;
using Xunit.Abstractions;

namespace CopperSharp.Compiler.Tests;

public sealed class Net10ProfileBaselineTests
{
	private readonly ITestOutputHelper _output;

	public Net10ProfileBaselineTests(ITestOutputHelper output)
	{
		_output = output;
	}

	[Fact]
	public void CurrentBaselineCanBeReproduced()
	{
		var samples = new[]
		{
			CaptureFixture("ArithmeticEntry", measureCycles: true),
			CaptureFixture("QuickArithmeticEntry", measureCycles: true),
			CaptureFixture("BranchAssignedLocalsEntry", measureCycles: true),
			CaptureFixture("NarrowByteArithmeticEntry", measureCycles: true),
			CaptureFixture("NarrowShortArithmeticEntry", measureCycles: true),
			CaptureFixture("StringLiteralEntry", measureCycles: false),
			CaptureFixture("NullableUIntDefaultEntry", measureCycles: false),
			CaptureIffInspect(),
			CaptureMuiTaskList()
		};

		var json = JsonSerializer.Serialize(
			samples,
			new JsonSerializerOptions { WriteIndented = true });
		_output.WriteLine(json);
		var expectedPath = Path.Combine(
			AppContext.BaseDirectory,
			"Baselines",
			"net10.0-profile-baseline.json");
		var expected = JsonNode.Parse(File.ReadAllText(expectedPath));
		var actual = JsonNode.Parse(json);
		Assert.True(
			JsonNode.DeepEquals(expected, actual),
			$"The .NET 10 profile baseline changed. Re-run this test with detailed output and " +
			$"review the measurements before updating '{expectedPath}'.");
		Assert.All(samples, static sample => Assert.True(sample.CodeBytes > 0));
	}

	private static BaselineSample CaptureFixture(string entry, bool measureCycles)
	{
		M68kCompilationRequest Request(M68kOutputFormat format) => new()
		{
			AssemblyPath = Assembly.GetExecutingAssembly().Location,
			EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = format,
			RuntimeProfile = M68kRuntimeProfile.Application
		};
		var assembly = AmigaM68kCompiler.Compile(Request(M68kOutputFormat.Assembly));
		var hunk = AmigaM68kCompiler.Compile(Request(M68kOutputFormat.Hunk));
		var analysis = M68kCompiler.AnalyzeFramework(Request(M68kOutputFormat.Hunk));
		return CreateSample(
			$"fixture:{entry}",
			assembly,
			hunk.Image.Length,
			analysis.ManagedAllocationSites.Count,
			measureCycles ? MeasureCycles(entry) : null);
	}

	private static BaselineSample CaptureIffInspect()
	{
		M68kCompilationRequest Request(M68kOutputFormat format) => new()
		{
			AssemblyPath = typeof(IFFInspect.Program).Assembly.Location,
			EntryPoint = "IFFInspect.Program::Main",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = format,
			RuntimeProfile = M68kRuntimeProfile.Application,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0001_0000,
				Size = 0x0000_8000
			}
		};
		var options = new AmigaCompilationOptions
		{
			LibraryBases = new Dictionary<string, uint>
			{
				["exec.library"] = 0x0000_0400
			}
		};
		var assembly = AmigaM68kCompiler.Compile(Request(M68kOutputFormat.Assembly), options);
		var hunk = AmigaM68kCompiler.Compile(Request(M68kOutputFormat.Hunk), options);
		var analysis = AmigaM68kCompiler.AnalyzeFramework(
			Request(M68kOutputFormat.Hunk),
			options);
		return CreateSample(
			"example:IFFInspect",
			assembly,
			hunk.Image.Length,
			analysis.ManagedAllocationSites.Count,
			null);
	}

	private static BaselineSample CaptureMuiTaskList()
	{
		M68kCompilationRequest Request(M68kOutputFormat format) => new()
		{
			AssemblyPath = typeof(MUITaskList.Program).Assembly.Location,
			EntryPoint = "MUITaskList.Program::Main",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = format,
			Imports = new Dictionary<string, uint>
			{
				["amiga.boopsi.DoMethodA"] = 0x0000_2600,
				["amiga.boopsi.DoSuperMethodA"] = 0x0000_2700
			}
		};
		var options = new AmigaCompilationOptions
		{
			LibraryBases = new Dictionary<string, uint>
			{
				["exec.library"] = 0x0000_0400
			}
		};
		var assembly = AmigaM68kCompiler.Compile(Request(M68kOutputFormat.Assembly), options);
		var hunk = AmigaM68kCompiler.Compile(Request(M68kOutputFormat.Hunk), options);
		var analysis = AmigaM68kCompiler.AnalyzeFramework(
			Request(M68kOutputFormat.Hunk),
			options);
		return CreateSample(
			"example:MUITaskList",
			assembly,
			hunk.Image.Length,
			analysis.ManagedAllocationSites.Count,
			null);
	}

	private static BaselineSample CreateSample(
		string name,
		M68kCompilationResult result,
		int hunkBytes,
		int managedAllocationSites,
		long? cycles)
	{
		var text = result.Text ?? string.Empty;
		return new BaselineSample(
			name,
			hunkBytes,
			result.Code.Length,
			result.AllocationStatistics.Count,
			result.AllocationStatistics.Sum(static item => item.SpilledValues),
			result.AllocationStatistics.Max(static item => item.SpillFrameBytes),
			result.AllocationStatistics.Sum(static item => item.StackMemoryInstructions),
			managedAllocationSites,
			cycles,
			DetectFeatures(text));
	}

	private static string[] DetectFeatures(string text)
	{
		var features = new List<string>();
		Add("managed-strings", "C68K_string_", features, text);
		Add("native-cstrings", "C68K_cstring_", features, text);
		Add("exceptions", "__c68k_exception_raise:", features, text);
		Add("managed-gc", "__c68k_gc_mark_roots:", features, text);
		Add("managed-arrays", "C68K_array_type_", features, text);
		Add("runtime-type-metadata", "C68K_type_", features, text);
		return features.ToArray();
	}

	private static void Add(
		string feature,
		string marker,
		ICollection<string> features,
		string text)
	{
		if (text.Contains(marker, StringComparison.Ordinal))
		{
			features.Add(feature);
		}
	}

	private static long MeasureCycles(string entry)
	{
		var method = typeof(CompilerExecutionTests).GetMethod(
			"MeasureCycles",
			BindingFlags.NonPublic | BindingFlags.Static) ??
			throw new InvalidOperationException("The cycle-corpus measurement helper was not found.");
		return (long)method.Invoke(
			null,
			[$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}"])!;
	}

	private sealed record BaselineSample(
		string Name,
		int ImageBytes,
		int CodeBytes,
		int CompiledMethods,
		int SpilledValues,
		int MaximumSpillFrameBytes,
		int StackMemoryInstructions,
		int ManagedAllocationSites,
		long? Mc68000Cycles,
		IReadOnlyList<string> RuntimeFeatures);
}
