/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
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
		var samples = new List<BaselineSample>
		{
			CaptureFixture("ArithmeticEntry", measureCycles: true),
			CaptureFixture("QuickArithmeticEntry", measureCycles: true),
			CaptureFixture("BranchAssignedLocalsEntry", measureCycles: true),
			CaptureFixture("NarrowByteArithmeticEntry", measureCycles: true),
			CaptureFixture("NarrowShortArithmeticEntry", measureCycles: true),
			CaptureFixture("StringLiteralEntry", measureCycles: false),
			CaptureFixture("NullableUIntDefaultEntry", measureCycles: false)
		};
		samples.AddRange(ExampleWorkloads.Select(CaptureExample));

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
		return CreateSample(
			$"fixture:{entry}",
			assembly,
			hunk.Image.Length,
			assembly.FrameworkAnalysis.ManagedAllocationSites.Count,
			measureCycles ? MeasureCycles(entry) : null,
			includeQualityMetrics: false);
	}

	private static BaselineSample CaptureExample(ExampleWorkload workload)
	{
		var assembly = AmigaM68kCompiler.Compile(
			workload.CreateRequest(M68kOutputFormat.Assembly),
			AmigaOptions);
		var hunk = AmigaM68kCompiler.Compile(
			workload.CreateRequest(M68kOutputFormat.Hunk),
			AmigaOptions);
		return CreateSample(
			$"example:{workload.Name}",
			assembly,
			hunk.Image.Length,
			assembly.FrameworkAnalysis.ManagedAllocationSites.Count,
			null);
	}

	private static BaselineSample CreateSample(
		string name,
		M68kCompilationResult result,
		int hunkBytes,
		int managedAllocationSites,
		long? cycles,
		bool includeQualityMetrics = true)
	{
		var nativeCompatibility = result.NativeCompatibility;
		var shape = MeasureAssembly(result.Text ?? string.Empty);
		return new BaselineSample(
			name,
			hunkBytes,
			result.Code.Length,
			includeQualityMetrics ? Encoding.UTF8.GetByteCount(result.Text ?? string.Empty) : null,
			result.AllocationStatistics.Count,
			result.AllocationStatistics.Sum(static item => item.SpilledValues),
			result.AllocationStatistics.Max(static item => item.SpillFrameBytes),
			result.AllocationStatistics.Sum(static item => item.StackMemoryInstructions),
			managedAllocationSites,
			cycles,
			includeQualityMetrics ? result.Symbols.Count : null,
			includeQualityMetrics ? result.Relocations.Count : null,
			includeQualityMetrics ? shape.Instructions : null,
			includeQualityMetrics ? shape.Calls : null,
			includeQualityMetrics ? shape.Branches : null,
			includeQualityMetrics ? shape.ExplicitStackOperations : null,
			includeQualityMetrics ? nativeCompatibility.ExceptionRegionCount : null,
			includeQualityMetrics ? nativeCompatibility.FatalMachineFaultSiteCount : null,
			includeQualityMetrics ? nativeCompatibility.ExceptionMode.ToString() : null,
			includeQualityMetrics ? nativeCompatibility.MemoryManagement.ToString() : null,
			includeQualityMetrics ? M68kCpuTarget.M68000.ToString() : null,
			includeQualityMetrics ? M68kRuntimeProfile.Application.ToString() : null,
			includeQualityMetrics ? M68kPeepholeOptimizationMode.FixedPoint.ToString() : null,
			includeQualityMetrics ? false : null,
			includeQualityMetrics
				? nativeCompatibility.RuntimeFeatures.Order(StringComparer.Ordinal).ToArray()
				: DetectAssemblyFeatures(result.Text ?? string.Empty),
			includeQualityMetrics
				? nativeCompatibility.RuntimeHelpers.Order(StringComparer.Ordinal).ToArray()
				: null,
			includeQualityMetrics
				? nativeCompatibility.ExternalNativeTargets.Order(StringComparer.Ordinal).ToArray()
				: null,
			includeQualityMetrics
				? nativeCompatibility.ReachableAssemblies
					.OrderBy(static assembly => assembly.Name, StringComparer.Ordinal)
					.ThenBy(static assembly => assembly.Version, StringComparer.Ordinal)
				.Select(static assembly => new BaselineAssemblyIdentity(
					assembly.Name,
					assembly.Version))
					.ToArray()
				: null);
	}

	private static string[] DetectAssemblyFeatures(string text)
	{
		var features = new List<string>();
		AddFeature("managed-strings", "C68K_string_", features, text);
		AddFeature("native-cstrings", "C68K_cstring_", features, text);
		AddFeature("exceptions", "__c68k_exception_raise:", features, text);
		AddFeature("managed-gc", "__c68k_gc_mark_roots:", features, text);
		AddFeature("managed-arrays", "C68K_array_type_", features, text);
		AddFeature("runtime-type-metadata", "C68K_type_", features, text);
		return features.ToArray();
	}

	private static void AddFeature(
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

	private static AssemblyShape MeasureAssembly(string text)
	{
		var instructions = 0;
		var calls = 0;
		var branches = 0;
		var explicitStackOperations = 0;
		foreach (var line in text.Split('\n'))
		{
			if (!TryGetOpcode(line, out var opcode))
			{
				continue;
			}

			instructions++;
			if (opcode is "bsr.s" or "bsr.w" or "jsr")
			{
				calls++;
			}
			if (IsBranch(opcode))
			{
				branches++;
			}
			if (line.Contains("-(a7)", StringComparison.Ordinal) ||
				line.Contains("(a7)+", StringComparison.Ordinal))
			{
				explicitStackOperations++;
			}
		}
		return new AssemblyShape(instructions, calls, branches, explicitStackOperations);
	}

	private static bool TryGetOpcode(string line, out string opcode)
	{
		opcode = string.Empty;
		if (!line.StartsWith('\t'))
		{
			return false;
		}

		var end = line.IndexOfAny(['\t', ' ', '\r'], 1);
		opcode = (end < 0 ? line[1..] : line[1..end]).ToLowerInvariant();
		return opcode.Length != 0 && opcode is not
			"mc68000" and not "section" and not "even" and not "cnop" and not
			"xdef" and not "xref" and not "end" && !opcode.StartsWith("dc.", StringComparison.Ordinal);
	}

	private static bool IsBranch(string opcode) => opcode is "jmp" or "dbra" or
		"bra.s" or "bra.w" or "bcc.s" or "bcc.w" or "bcs.s" or "bcs.w" or
		"beq.s" or "beq.w" or "bge.s" or "bge.w" or "bgt.s" or "bgt.w" or
		"bhi.s" or "bhi.w" or "ble.s" or "ble.w" or "bls.s" or "bls.w" or
		"blt.s" or "blt.w" or "bmi.s" or "bmi.w" or "bne.s" or "bne.w" or
		"bpl.s" or "bpl.w" or "bvc.s" or "bvc.w" or "bvs.s" or "bvs.w";

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
		[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		int? AssemblyTextBytes,
		int CompiledMethods,
		int SpilledValues,
		int MaximumSpillFrameBytes,
		int StackMemoryInstructions,
		int ManagedAllocationSites,
		long? Mc68000Cycles,
		[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SymbolCount,
		[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? RelocationCount,
		[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? InstructionCount,
		[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? CallInstructionCount,
		[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? BranchInstructionCount,
		[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ExplicitStackOperationCount,
		[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ExceptionRegionCount,
		[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? FatalMachineFaultSiteCount,
		[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExceptionMode,
		[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MemoryManagement,
		[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CpuTarget,
		[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RuntimeProfile,
		[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PeepholeOptimization,
		[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? HunkSymbolsIncluded,
		IReadOnlyList<string> RuntimeFeatures,
		[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? RuntimeHelpers,
		[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? ExternalNativeTargets,
		[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<BaselineAssemblyIdentity>? ReachableAssemblies);

	private sealed record BaselineAssemblyIdentity(
		string Name,
		string Version);

	private sealed record AssemblyShape(
		int Instructions,
		int Calls,
		int Branches,
		int ExplicitStackOperations);

	private sealed record ExampleWorkload(
		string Name,
		string AssemblyPath,
		string EntryPoint,
		M68kExceptionMode ExceptionMode,
		M68kMemoryManagement MemoryManagement,
		M68kHeapOptions? Heap = null,
		IReadOnlyDictionary<string, uint>? Imports = null)
	{
		public M68kCompilationRequest CreateRequest(M68kOutputFormat format) => new()
		{
			AssemblyPath = AssemblyPath,
			EntryPoint = EntryPoint,
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = format,
			RuntimeProfile = M68kRuntimeProfile.Application,
			ExceptionMode = ExceptionMode,
			MemoryManagement = MemoryManagement,
			PeepholeOptimization = M68kPeepholeOptimizationMode.FixedPoint,
			Heap = Heap ?? new M68kHeapOptions(),
			Hunk = new HunkOutputOptions { IncludeSymbols = false },
			Imports = CreateImports()
		};

		private IReadOnlyDictionary<string, uint> CreateImports()
		{
			var imports = Imports is null
				? new Dictionary<string, uint>()
				: new Dictionary<string, uint>(Imports, StringComparer.Ordinal);
			if (MemoryManagement == M68kMemoryManagement.ExternalAllocator)
			{
				imports.TryAdd(M68kRuntimeImports.Allocate, 0x0000_2800);
			}
			return imports;
		}
	}

	private static readonly AmigaCompilationOptions AmigaOptions = new()
	{
		LibraryBases = new Dictionary<string, uint>
		{
			["exec.library"] = 0x0000_0400
		}
	};

	private static readonly IReadOnlyDictionary<string, uint> MuiImports =
		new Dictionary<string, uint>
		{
			["amiga.boopsi.DoMethodA"] = 0x0000_2600,
			["amiga.boopsi.DoSuperMethodA"] = 0x0000_2700
		};

	private static readonly IReadOnlyList<ExampleWorkload> ExampleWorkloads =
	[
		new("ConsoleIO", ExampleAssemblyPath("ConsoleIO"),
			"ConsoleIOExample.Program::Main", M68kExceptionMode.Full,
			M68kMemoryManagement.ExternalAllocator),
		new("CopperBars", ExampleAssemblyPath("CopperBars"),
			"CopperBarsExample.Program::Main", M68kExceptionMode.Yolo,
			M68kMemoryManagement.ExternalAllocator),
		new("DOS", ExampleAssemblyPath("DOS"),
			"DOSExample.Program::Main", M68kExceptionMode.Yolo,
			M68kMemoryManagement.ExternalAllocator),
		new("FileStats", ExampleAssemblyPath("FileStats"),
			"FileStatsExample.Program::Main", M68kExceptionMode.Yolo,
			M68kMemoryManagement.ExternalAllocator),
		new("IFFInspect", ExampleAssemblyPath("IFFInspect"),
			"IFFInspect.Program::Main", M68kExceptionMode.Full,
			M68kMemoryManagement.ManagedPoolMarkSweepGc,
			new M68kHeapOptions { StartAddress = 0x0001_0000, Size = 0x0000_8000 }),
		new("MUISunflower", ExampleAssemblyPath("MUISunflower"),
			"MUISunflower.Program::Main", M68kExceptionMode.Yolo,
			M68kMemoryManagement.ExternalAllocator, Imports: MuiImports),
		new("MUITaskList", ExampleAssemblyPath("MUITaskList"),
			"MUITaskList.Program::Main", M68kExceptionMode.Yolo,
			M68kMemoryManagement.ExternalAllocator,
			Imports: MuiImports),
		new("Polymorphism", ExampleAssemblyPath("Polymorphism"),
			"PolymorphismExample.Program::Main", M68kExceptionMode.Yolo,
			M68kMemoryManagement.ExternalAllocator),
		new("StopwatchBenchmark", ExampleAssemblyPath("StopwatchBenchmark"),
			"StopwatchBenchmarkExample.Program::Main", M68kExceptionMode.Full,
			M68kMemoryManagement.ExternalAllocator)
	];

	private static string ExampleAssemblyPath(string assemblyName) => Path.Combine(
		AppContext.BaseDirectory,
		$"{assemblyName}.dll");
}
