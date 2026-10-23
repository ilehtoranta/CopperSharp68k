/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Targets.Amiga;
using System.Reflection.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class CoreLibClosureAdmissionTests
{
	[Fact]
	public void PinnedLinqOrderingGraphUsesOwnedUnsupportedRuntimeGuard()
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
		var voidType = FrameworkTypeId.Primitive("System.Void");
		var guard = new FrameworkMemberId(
			FrameworkTypeId.Named("CopperSharp.Compiler", "CopperSharp.Compiler.M68kRuntime"),
			"ThrowNotSupportedException",
			new FrameworkMethodSignatureId(0, 0, 0, voidType, []));
		var guardContext = new FrameworkBindingContext(
			"CopperSharp.Compiler.M68kRuntime",
			new MethodSignature<CilType>(new SignatureHeader(0),
				new CilType(CilTypeKind.Void, 0, "void"), 0, 0, []),
			null,
			false);
		var binding = Assert.IsType<FrameworkBinding>(
			FrameworkBindingRegistry.TryBind(guard, guardContext));
		Assert.Equal(FrameworkBindingKind.Intrinsic, binding.Kind);
		Assert.Equal("intrinsic:runtime-throw-not-supported", binding.Target);
		Assert.Null(FrameworkBindingRegistry.TryBind(
			guard, guardContext with { TypeName = "Application.Runtime" }));
		Assert.Null(FrameworkBindingRegistry.TryBind(
			new FrameworkMemberId(
				FrameworkTypeId.Named("System.Runtime", "System.NotSupportedException"),
				".ctor",
				new FrameworkMethodSignatureId(0x20, 0, 0, voidType, [])),
			guardContext with { TypeName = "System.NotSupportedException" }));
	}
}
