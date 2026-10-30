/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;
using CopperSharp.Compiler.Framework;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class CoreLibClosureAdmissionTests
{
	[Fact]
	public void PinnedLinqOrderingGraphAdmitsItsOwnedNotSupportedGuard()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var analysis = AmigaM68kCompiler.AnalyzeFramework(
			new M68kCompilationRequest
			{
				AssemblyPath = typeof(CompilerFixtures).Assembly.Location,
				EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::LinqDictionaryValuesOrderByThenByEntry",
				IncludedExportNames = [],
				Cpu = M68kCpuTarget.M68000,
				FloatingPoint = M68kFloatingPointMode.Disabled,
				OutputFormat = M68kOutputFormat.Hunk,
				RuntimeProfile = M68kRuntimeProfile.Application,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0010_0000,
					Size = 32 * 1024
				},
				ManagedAssemblyPaths =
				[
					typeof(CopperSharp.Runtime.ManagedPool).Assembly.Location,
					typeof(CopperSharp.Runtime.AmigaPal.EnvironmentPal).Assembly.Location
				],
				FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
			});

		Assert.True(analysis.IsCompatible, string.Join("\n", analysis.Members
			.Where(static member => member.Status is M68kFrameworkCompatibilityStatus.Unsupported or M68kFrameworkCompatibilityStatus.Deferred)
			.Select(static member => $"{member.Member.DisplayName}: {member.Reason}")));
		Assert.Contains(analysis.Members, static member =>
			member.Member.TypeName == "System.NotSupportedException" &&
			member.Member.Name == ".ctor" &&
			member.Status == M68kFrameworkCompatibilityStatus.Implemented);
	}
}
