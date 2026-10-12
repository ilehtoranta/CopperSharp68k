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
	public void ColdDefaultCultureAllocationFailuresPermitRetry(M68kCpuTarget target, M68kCpuModel model)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.FixedPoint, M68kPeepholeOptimizationMode.Disabled })
		{
			var result = Compile(target, M68kOutputFormat.Hunk,
				"CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibColdDefaultCultureAllocationFailureEntry",
				imports: new Dictionary<string, uint> { [M68kRuntimeImports.Allocate] = 0x2800, ["fixture.string-builder-allocation-failure"] = 0x2A00, [M68kRuntimeImports.UnhandledException] = 0x2D00 },
				peepholeOptimization: mode,
				frameworkImplementationPack: new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
			var bus = CreateHunkBus(result);
			var heap = 0x0010_0000u;
			var countdown = 0u;
			var regions = 0;
			var attempts = 0;
			var failures = 0;
			bus.RegisterGateway(0x2D00, state => throw new Xunit.Sdk.XunitException($"Unhandled cold-culture exception: {target}/{mode}, countdown={countdown}, attempts={attempts}, failures={failures}, regions={regions}, reason={state.D[0]}, exception=0x{state.A[0]:X8}"));
			bus.RegisterGateway(0x2A00, state => { countdown = state.D[0]; if (countdown != 0) regions++; });
			bus.RegisterGateway(0x2800, state =>
			{
				if (countdown != 0) attempts++;
				if (countdown == 1) { failures++; state.D[0] = 0; return; }
				if (countdown > 1) countdown--;
				var size = state.D[0];
				var address = heap;
				heap += (size + 3u) & ~3u;
				Array.Clear(bus.Memory, checked((int)address), checked((int)size));
				state.D[0] = address;
			});
			Assert.Equal(42u, Execute(bus, model, HunkLoadAddress + result.EntryPoint, maxInstructions: 2_000_000));
			Assert.Equal(2, regions);
			Assert.Equal(2, attempts);
			Assert.Equal(2, failures);
			Assert.Equal(0u, countdown);
		}
	}
}
