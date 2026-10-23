using System.Runtime.CompilerServices;
using Copper68k;
using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	[Fact]
	public void ApplicationRuntimeHelperPruningRemovesUnusedPoolEntrypoints()
	{
		var request = CompactGcRequest(typeof(CompilerFixtures).FullName +
			"::PoolCollectTracesCallerFrameEntry");
		var baseline = M68kCompiler.Compile(request with {
			CodeSizeOptimizations = new() { PruneApplicationRuntimeHelpers = false }
		});
		var compact = M68kCompiler.Compile(request);
		Assert.True(compact.Image.Length < baseline.Image.Length);
		foreach (var name in new[] { "Dispose", "GetStaleBytes", "GetStaleBlocks" })
		{
			var symbol = "CopperSharp.Runtime.ManagedPool::" + name;
			Assert.Contains(baseline.Symbols, item => item.Name == symbol);
			Assert.DoesNotContain(compact.Symbols, item => item.Name == symbol);
		}
		foreach (var result in new[] { baseline, compact })
		foreach (var model in new[]
			{ M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
		foreach (uint load in new uint[] { 0x10000, 0x20000 })
			Assert.Equal(42u, Execute(LoadApplicationSizeHunk(result.Image, load, 0x40000),
				model, load + result.EntryPoint, maxInstructions: 5_000_000));
	}

	[Theory]
	[InlineData("PoolDisposeReuseEntry", 42u, "Dispose")]
	[InlineData("PoolTelemetryCountersEntry", 1u, "GetStaleBlocks")]
	public void ApplicationRuntimeHelperPruningRetainsExplicitIntrinsics(
		string entry, uint expected, string retainedHelper)
	{
		var request = CompactGcRequest(typeof(CompilerFixtures).FullName + "::" + entry) with {
			GcSweepStrategy = M68kGcSweepStrategy.OnDemand
		};
		foreach (var enabled in new[] { false, true })
		{
			var result = M68kCompiler.Compile(request with {
				CodeSizeOptimizations = new() { PruneApplicationRuntimeHelpers = enabled }
			});
			Assert.Contains(result.Symbols, item =>
				item.Name == "CopperSharp.Runtime.ManagedPool::" + retainedHelper);
			foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020,
				M68kCpuModel.M68040 })
				Assert.Equal(expected, Execute(LoadApplicationSizeHunk(result.Image, 0x10000, 0x40000),
					model, 0x10000 + result.EntryPoint, maxInstructions: 5_000_000));
		}
	}

	[Fact]
	public void ApplicationRuntimeHelperPruningRetainsStaleByteQuery()
	{
		var request = CompactGcRequest(typeof(ApplicationRuntimeHelperFixtures).FullName +
			"::StaleBytesEntry") with { GcSweepStrategy = M68kGcSweepStrategy.OnDemand };
		var result = M68kCompiler.Compile(request);
		Assert.Contains(result.Symbols, item =>
			item.Name == "CopperSharp.Runtime.ManagedPool::GetStaleBytes");
		Assert.Equal(42u, Execute(LoadApplicationSizeHunk(result.Image, 0x10000, 0x40000),
			M68kCpuModel.M68000, 0x10000 + result.EntryPoint, maxInstructions: 5_000_000));
	}

	[Fact]
	public void ApplicationRuntimeHelperPruningRetainsDirectManagedPoolCall()
	{
		var request = CompactGcRequest(typeof(ApplicationRuntimeHelperFixtures).FullName +
			"::DirectStaleBlocksEntry") with { GcSweepStrategy = M68kGcSweepStrategy.OnDemand };
		var result = M68kCompiler.Compile(request);
		Assert.Contains(result.Symbols, item =>
			item.Name == "CopperSharp.Runtime.ManagedPool::GetStaleBlocks");
		Assert.Equal(1u, Execute(LoadApplicationSizeHunk(result.Image, 0x10000, 0x40000),
			M68kCpuModel.M68000, 0x10000 + result.EntryPoint, maxInstructions: 5_000_000));
	}

	[Fact]
	public void ApplicationRuntimeHelperPruningIsM68000ApplicationHunkOnly()
	{
		var request = CompactGcRequest(typeof(CompilerFixtures).FullName +
			"::PoolCollectTracesCallerFrameEntry") with { Cpu = M68kCpuTarget.M68020 };
		var baseline = M68kCompiler.Compile(request with {
			CodeSizeOptimizations = new() { PruneApplicationRuntimeHelpers = false }
		});
		var enabled = M68kCompiler.Compile(request);
		Assert.Equal(baseline.Image, enabled.Image);
	}

	[Fact]
	public void ApplicationRuntimeHelperPruningDoesNotChangeExternalCollectorImages()
	{
		var request = new M68kCompilationRequest {
			AssemblyPath = FixtureAssembly,
			EntryPoint = typeof(CompilerFixtures).FullName + "::ExplicitCollectWithDynamicFrameEntry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = M68kRuntimeProfile.Application,
			ExceptionMode = M68kExceptionMode.Full,
			Imports = new Dictionary<string, uint> { [M68kRuntimeImports.GcCollect] = 0x2C00 },
			IncludedExportNames = [],
			Hunk = new() { IncludeSymbols = false },
			CodeSizeOptimizations = new() { PruneApplicationRuntimeHelpers = false }
		};
		var baseline = M68kCompiler.Compile(request);
		var enabled = M68kCompiler.Compile(request with { CodeSizeOptimizations = new() });
		Assert.Equal(baseline.Image, enabled.Image);
	}

	[Theory]
	[InlineData("CopperSharp.Compiler.Tests.CompilerFixtures::DynamicStackallocGcEntry", 42u)]
	[InlineData("CopperSharp.Compiler.Tests.FinalizerExecutionFixtures::FinalizesUnreachableEntry", 1u)]
	[InlineData("CopperSharp.Compiler.Tests.TypeInitializationRuntimeFixtures::FailureEntry", 42u)]
	public void ApplicationRuntimeHelperPruningPreservesExtendedAndFinalizerPaths(
		string entry, uint expected)
	{
		var result = M68kCompiler.Compile(CompactGcRequest(entry));
		foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020,
			M68kCpuModel.M68040 })
			Assert.Equal(expected, Execute(LoadApplicationSizeHunk(result.Image, 0x10000, 0x40000),
				model, 0x10000 + result.EntryPoint, maxInstructions: 5_000_000));
	}
}

public static class ApplicationRuntimeHelperFixtures
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint DirectStaleBlocksEntry()
	{
		var values = new int[4];
		values[0] = 1;
		return ManagedPool.GetStaleBlocks();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint StaleBytesEntry()
	{
		var values = new int[4];
		values[0] = 1;
		return M68kRuntime.GetGcStaleBytes() > 0 ? 42u : 0u;
	}
}
