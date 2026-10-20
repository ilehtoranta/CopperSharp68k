/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Runtime.CompilerServices;
using Copper68k;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	[Theory]
	[InlineData("PureEntry", true)]
	[InlineData("ThrowingEntry", false)]
	[InlineData("OpaqueEntry", false)]
	public void ApplicationLeafSitesRequireCompleteSafeCallGraph(string entry, bool prunes)
	{
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.FixedPoint, M68kPeepholeOptimizationMode.Disabled })
		{
			var request = new M68kCompilationRequest {
				AssemblyPath = FixtureAssembly, EntryPoint = typeof(ApplicationLeafFixtures).FullName + "::" + entry,
				Cpu = M68kCpuTarget.M68000, OutputFormat = M68kOutputFormat.Hunk,
				RuntimeProfile = M68kRuntimeProfile.Application, ExceptionMode = M68kExceptionMode.Full,
				PeepholeOptimization = mode, IncludedExportNames = [], Hunk = new() { IncludeSymbols = false },
				Imports = new Dictionary<string, uint> { ["size.opaque"] = 0x2800 },
				CodeSizeOptimizations = new() { PruneApplicationLeafSites = false }
			};
			var baseline = M68kCompiler.Compile(request);
			var result = M68kCompiler.Compile(request with { CodeSizeOptimizations = new() });
			Assert.Equal(prunes, result.Map.Contains("pass=PruneApplicationLeafSites", StringComparison.Ordinal));
			if (prunes) Assert.True(result.Image.Length < baseline.Image.Length);
			else Assert.Equal(baseline.Image, result.Image);
			foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
			foreach (uint load in new uint[] { 0x10000, 0x20000 })
			{
				var bus = LoadApplicationSizeHunk(result.Image, load, 0x40000);
				var calls = 0;
				bus.RegisterGateway(0x2800, state => { calls++; state.D[0] = 42; });
				Assert.Equal(42u, Execute(bus, model, load + result.EntryPoint, maxInstructions: 100_000));
				Assert.Equal(entry == "OpaqueEntry" ? 1 : 0, calls);
			}
		}
	}
}

public static class ApplicationLeafFixtures
{
	public static int PureEntry()
	{
		var value = PureRecursive(40, 2);
		try { return ThrowingRecursive(0, 2); }
		catch (DivideByZeroException) { return value; }
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int PureRecursive(int value, int depth) => depth == 0 ? value : PureRecursive(value + 1, depth - 1);

	public static int ThrowingEntry()
	{
		try { return ThrowingRecursive(0, 2); }
		catch (DivideByZeroException) { return 42; }
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ThrowingRecursive(int divisor, int depth) =>
		depth == 0 ? 42 / divisor : ThrowingRecursive(divisor, depth - 1);

	public static int OpaqueEntry() => OpaqueWrapper();
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int OpaqueWrapper() => Opaque();
	[M68kImport("size.opaque")]
	private static int Opaque() => throw new PlatformNotSupportedException();
}
