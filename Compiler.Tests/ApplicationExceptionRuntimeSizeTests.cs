/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Runtime.CompilerServices;
using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	[Theory]
	[InlineData(M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kPeepholeOptimizationMode.Disabled)]
	public void ApplicationCompactExceptionRuntimePreservesUnhandledReasonsAndRegisters(M68kPeepholeOptimizationMode mode)
	{
		var request = new M68kCompilationRequest {
			AssemblyPath = FixtureAssembly,
			EntryPoint = typeof(ApplicationExceptionRuntimeSizeFixtures).FullName + "::Entry",
			Cpu = M68kCpuTarget.M68000, OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = M68kRuntimeProfile.Application, ExceptionMode = M68kExceptionMode.Full,
			IncludedExportNames = [], Hunk = new() { IncludeSymbols = false },
			PeepholeOptimization = mode,
			Imports = new Dictionary<string, uint> {
				[M68kRuntimeImports.UnhandledException] = 0x2900
			},
			CodeSizeOptimizations = new() { CompactApplicationExceptionRuntime = false }
		};
		var baseline = AmigaM68kCompiler.Compile(request);
		var compact = AmigaM68kCompiler.Compile(request with { CodeSizeOptimizations = new() });
		Assert.True(compact.Image.Length < baseline.Image.Length);
		Assert.DoesNotContain("__c68k_exception_resume", compact.Map);
		Assert.DoesNotContain("__c68k_exception_jump_state", compact.Map);
		Assert.Contains("__c68k_exception_unwind_frame", compact.Map);
		foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
		foreach (uint load in new uint[] { 0x10000, 0x20000 })
		foreach (var reason in new uint[] { 0, 3, 4, 6, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19 })
		{
			uint[]? expectedRegisters = null;
			foreach (var result in new[] { baseline, compact })
			{
				var bus = LoadApplicationSizeHunk(result.Image, load, 0x40000);
				bus.WriteLong(4, 0x3000);
				var hooks = 0; var requests = 0; var closes = 0;
				bus.RegisterGateway(0x3000 - 216, state => state.D[0] = reason);
				bus.RegisterGateway(0x2900, state => {
					hooks++;
					Assert.Equal(reason == 0 ? 1u : reason, state.D[0]);
					Assert.NotEqual(0u, state.A[0]);
					Assert.Equal(8u, bus.ReadLong(state.A[0] + 4));
					var registers = state.D.Skip(2).Concat(state.A.Skip(2).Take(5)).ToArray();
					if (expectedRegisters is null) expectedRegisters = registers;
					else Assert.Equal(expectedRegisters, registers);
				});
				bus.RegisterGateway(0x3000 - 552, state => {
					Assert.Equal("intuition.library", ReadCString(bus, state.A[1]));
					state.D[0] = 0x3800;
				});
				bus.RegisterGateway(0x3800 - 348, state => { requests++; state.D[0] = 1; });
				bus.RegisterGateway(0x3000 - 414, state => { closes++; Assert.Equal(0x3800u, state.A[1]); });
				Assert.Equal(20u, Execute(bus, model, load + result.EntryPoint,
					initialize: state => {
						for (var i = 2; i <= 7; i++) state.D[i] = 0x12340000u + (uint)i;
						for (var i = 2; i <= 6; i++) state.A[i] = 0x56780000u + (uint)i;
					},
					afterReturn: state => Assert.Equal(StackPointer + 4, state.A[7])));
				Assert.Equal(1, hooks);
				Assert.Equal(1, requests);
				Assert.Equal(1, closes);
			}
		}
	}
}

public static class ApplicationExceptionRuntimeSizeFixtures
{
	// A platform call gives this no-handler application its normal startup
	// adapter, including the original stack used by the requester exit path.
	public static int Entry() { Fault(global::Amiga.Exec.AvailMem(0)); return 0; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Fault(uint reason)
	{
		switch (reason)
		{
			case 0: throw null!;
			case 3: M68kRuntime.ThrowDivideByZeroException(); break;
			case 4: M68kRuntime.ThrowOverflowException(); break;
			case 6: M68kRuntime.ThrowOutOfMemoryException(); break;
			case 9: M68kRuntime.ThrowArgumentException(); break;
			case 10: M68kRuntime.ThrowArgumentOutOfRangeException(); break;
			case 11: M68kRuntime.ThrowArgumentNullException(); break;
			case 12: M68kRuntime.ThrowFormatException(); break;
			case 13: M68kRuntime.ThrowInvalidOperationException(); break;
			case 14: M68kRuntime.ThrowKeyNotFoundException(); break;
			case 15: M68kRuntime.ThrowIOException(); break;
			case 16: M68kRuntime.ThrowDirectoryNotFoundException(); break;
			case 17: M68kRuntime.ThrowUnauthorizedAccessException(); break;
			case 18: M68kRuntime.ThrowFileNotFoundException(); break;
			case 19: M68kRuntime.ThrowArithmeticException(); break;
		}
	}

}
