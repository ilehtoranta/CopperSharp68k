using Copper68k;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	[Fact]
	public void ApplicationCallArgumentsKeepExceptionsFromIgnoredArgumentEvaluation()
	{
		foreach (var enabled in new[] { false, true })
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest {
				AssemblyPath = typeof(ApplicationCallArgumentFixtures).Assembly.Location,
				EntryPoint = typeof(ApplicationCallArgumentFixtures).FullName + "::Entry",
				Cpu = M68kCpuTarget.M68000, OutputFormat = M68kOutputFormat.Hunk,
				RuntimeProfile = M68kRuntimeProfile.Application, ExceptionMode = M68kExceptionMode.Full,
				IncludedExportNames = [], Hunk = new() { IncludeSymbols = false },
				PeepholeOptimization = mode,
				CodeSizeOptimizations = new() { OptimizeApplicationCallArguments = enabled }
			});
			foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
			foreach (var input in new uint[] { 0, 0x7fffffff })
			{
				var bus = LoadApplicationSizeHunk(result.Image, 0x10000, 0x50000);
				bus.WriteLong(UnusedRegisterArgumentFixtures.Input, input);
				var actual = Execute(bus, model, 0x10000 + result.EntryPoint,
					afterReturn: state => Assert.Equal(StackPointer + 4, state.A[7]));
				var expected = input == 0x7fffffff ? 123u : 42u;
				Assert.True(expected == actual,
					$"arguments={enabled}, peephole={mode}, cpu={model}, input={input:X8}: expected {expected}, actual {actual}");
			}
		}
	}

	[Theory]
	[InlineData("Scalar")]
	[InlineData("Effects")]
	[InlineData("Forward")]
	[InlineData("Pointer")]
	[InlineData("Aggregate")]
	[InlineData("UsedPointer")]
	public void ApplicationCallArgumentsPreserveEvaluationAndStackArguments(string scenario)
	{
		foreach (var target in new[] { M68kCpuTarget.M68000, M68kCpuTarget.M68020 })
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
		{
			var request = new M68kCompilationRequest {
				AssemblyPath = typeof(UnusedRegisterArgumentFixtures).Assembly.Location,
				EntryPoint = typeof(UnusedRegisterArgumentFixtures).FullName + "::Entry" + scenario,
				Cpu = target, OutputFormat = M68kOutputFormat.Hunk, RuntimeProfile = M68kRuntimeProfile.Application,
				ExceptionMode = M68kExceptionMode.Full, IncludedExportNames = [],
				PeepholeOptimization = mode, Hunk = new() { IncludeSymbols = false },
				CodeSizeOptimizations = new() { OptimizeApplicationCallArguments = false }
			};
			var before = M68kCompiler.Compile(request);
			var after = M68kCompiler.Compile(request with { CodeSizeOptimizations = new() });
			if (target != M68kCpuTarget.M68000) Assert.Equal(before.Image, after.Image);
			if (target == M68kCpuTarget.M68000 && scenario is "Scalar" or "Effects" or "Forward")
				Assert.True(after.Code.Length < before.Code.Length, $"{scenario}: {before.Code.Length} -> {after.Code.Length}");
			foreach (var input in new uint[] { 0, 41, 0x7fffffff, 0x80000000, 0xffffffff })
			foreach (var selector in new uint[] { 0, 1 })
			{
				uint? control = null;
				foreach (var result in new[] { before, after })
				{
					var bus = LoadApplicationSizeHunk(result.Image, 0x10000, 0x50000);
					bus.WriteLong(UnusedRegisterArgumentFixtures.Input, input);
					bus.WriteLong(UnusedRegisterArgumentFixtures.Selector, selector);
					bus.WriteLong(UnusedRegisterArgumentFixtures.Trace, 0);
					var actual = Execute(bus, target == M68kCpuTarget.M68000 ? M68kCpuModel.M68000 : M68kCpuModel.M68020,
						0x10000 + result.EntryPoint, afterReturn: state => Assert.Equal(StackPointer + 4, state.A[7]));
					if (control is { } expected) Assert.Equal(expected, actual);
					control = actual;
					Assert.Equal(scenario == "Effects" ? 123456u : 0u, bus.ReadLong(UnusedRegisterArgumentFixtures.Trace));
				}
			}
		}
	}
}

public static unsafe class ApplicationCallArgumentFixtures
{
	public static uint Entry()
	{
		try { return Invoke(); }
		catch (OverflowException) { return 123; }
	}
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	private static uint Invoke() => Ignore(checked(*(int*)UnusedRegisterArgumentFixtures.Input + 1), 41);
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	private static uint Ignore(int ignored, uint value) => value == 41 ? 42u : 43u;
}
