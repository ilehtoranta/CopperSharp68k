/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using Copper68k;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableConcatenationRetainsOwnersUnderCollection(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, [nameof(CompilerFixtures.StringBuilderConcatenationOwnershipEntry)]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableConcatenationPreservesSingleAllocation(M68kCpuTarget target, M68kCpuModel model)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.FixedPoint, M68kPeepholeOptimizationMode.Disabled })
		{
			const string entry = nameof(CompilerFixtures.StringBuilderConcatenationAllocationEntry);
			var result = M68kCompiler.Compile(new M68kCompilationRequest {
				AssemblyPath = FixtureAssembly, EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::" + entry, IncludedExportNames = [],
				Cpu = target, OutputFormat = M68kOutputFormat.Hunk, ExceptionMode = M68kExceptionMode.Full,
				PeepholeOptimization = mode, MemoryManagement = M68kMemoryManagement.ExternalAllocator,
				Imports = new Dictionary<string, uint> { [M68kRuntimeImports.Allocate] = 0x2800, ["fixture.string-builder-allocation-failure"] = 0x2A00 },
				FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
			});
			Assert.True(result.FrameworkAnalysis.IsCompatible);
			Assert.Contains("sha256=" + pack.Sha256, result.Map, StringComparison.Ordinal);
			// Reuse the independent allocator instrumentation: its short-leaf
			// contract requires regions [0,1,1,0,1,1] and exactly two failures.
			var failures = ExecuteFloatingAllocationImage(result, model, entry, mode, true, 12, 0, out var instructions);
			var artifact = Path.Combine(AppContext.BaseDirectory, $"stringbuilder-stable-concat-allocation-{target}-{mode}");
			File.WriteAllText(artifact + ".map", result.Map);
			File.WriteAllText(artifact + ".json", System.Text.Json.JsonSerializer.Serialize(new {
				CoreLibSha256 = pack.Sha256, EnableUnlistedManagedBodies = false, Cpu = target.ToString(), Optimization = mode.ToString(),
				Result = 42, Failures = failures, Instructions = instructions, InstructionLimit = 100_000_000,
				ExpectedRegions = new[] { 0, 1, 1, 0, 1, 1 }, MemoryManagement = "ExternalAllocator"
			}));
		}
	}
}
