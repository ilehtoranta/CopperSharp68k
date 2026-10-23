/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using Copper68k;
using CopperSharp.Runtime;
using CopperSharp.Runtime.AmigaPal;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	public static TheoryData<M68kCpuTarget, M68kCpuModel, M68kPeepholeOptimizationMode> CodecTargets => new()
	{
		{ M68kCpuTarget.M68000, M68kCpuModel.M68000, M68kPeepholeOptimizationMode.FixedPoint },
		{ M68kCpuTarget.M68020, M68kCpuModel.M68020, M68kPeepholeOptimizationMode.FixedPoint },
		{ M68kCpuTarget.M68040, M68kCpuModel.M68040, M68kPeepholeOptimizationMode.FixedPoint },
		{ M68kCpuTarget.M68060, M68kCpuModel.M68040, M68kPeepholeOptimizationMode.FixedPoint },
		{ M68kCpuTarget.M68000, M68kCpuModel.M68000, M68kPeepholeOptimizationMode.Disabled },
		{ M68kCpuTarget.M68020, M68kCpuModel.M68020, M68kPeepholeOptimizationMode.Disabled },
		{ M68kCpuTarget.M68040, M68kCpuModel.M68040, M68kPeepholeOptimizationMode.Disabled },
		{ M68kCpuTarget.M68060, M68kCpuModel.M68040, M68kPeepholeOptimizationMode.Disabled }
	};

	[Theory, MemberData(nameof(CpuTargets))]
	public void CoreLibCodecPipelineExecutesOnTarget(M68kCpuTarget target, M68kCpuModel model)
		=> RunCodecSurface(target, model, nameof(CoreLibCodecPipelineFixtures), "Entry", 42);

	[Theory, MemberData(nameof(CpuTargets))]
	public void Utf8CodecSurfaceExecutesOnTarget(M68kCpuTarget target, M68kCpuModel model)
		=> RunCodecSurface(target, model, nameof(Utf8CodecFixtures), "Entry", 42);

	[Theory, MemberData(nameof(CpuTargets))]
	public void Base64CodecSurfaceExecutesOnTarget(M68kCpuTarget target, M68kCpuModel model)
		=> RunCodecSurface(target, model, nameof(Base64CodecFixtures), "Entry", 42);

	[Theory, MemberData(nameof(CpuTargets))]
	public void HexCodecSurfaceExecutesOnTarget(M68kCpuTarget target, M68kCpuModel model)
		=> RunCodecSurface(target, model, nameof(HexCodecFixtures), "Entry", 42);

	[Theory, MemberData(nameof(CodecTargets))]
	public void NarrowPromotedComparisonsMatchHost(M68kCpuTarget target, M68kCpuModel model, M68kPeepholeOptimizationMode mode)
	{
		Assert.Equal(42, NarrowCodecComparisonFixtures.Entry());
		RunCodecSurface(target, model, nameof(NarrowCodecComparisonFixtures), "GeneratedEntry", NarrowCodecComparisonFixtures.GeneratedEntry(), mode);
	}

	[Theory, MemberData(nameof(CodecTargets))]
	public void Utf8CodecGeneratedCorpusMatchesHost(M68kCpuTarget target, M68kCpuModel model, M68kPeepholeOptimizationMode mode)
		=> RunCodecSurface(target, model, nameof(Utf8CodecFixtures), "GeneratedEntry", Utf8CodecFixtures.GeneratedEntry(), mode);

	[Theory, MemberData(nameof(CodecTargets))]
	public void Base64CodecAdversarialCorpusMatchesHost(M68kCpuTarget target, M68kCpuModel model, M68kPeepholeOptimizationMode mode)
		=> RunCodecSurface(target, model, nameof(Base64CodecFixtures), "AdversarialEntry", Base64CodecFixtures.AdversarialEntry(), mode);

	[Theory, MemberData(nameof(CodecTargets))]
	public void Base64CodecGeneratedCorpusMatchesHost(M68kCpuTarget target, M68kCpuModel model, M68kPeepholeOptimizationMode mode)
		=> RunCodecSurface(target, model, nameof(Base64CodecFixtures), "GeneratedEntry", Base64CodecFixtures.GeneratedEntry(), mode);

	[Theory, MemberData(nameof(CodecTargets))]
	public void HexCodecGeneratedCorpusMatchesHost(M68kCpuTarget target, M68kCpuModel model, M68kPeepholeOptimizationMode mode)
		=> RunCodecSurface(target, model, nameof(HexCodecFixtures), "GeneratedEntry", HexCodecFixtures.GeneratedEntry(), mode);

	[Theory, MemberData(nameof(CodecTargets))]
	public void CoreLibCodecStreamingRetriesMatchHost(M68kCpuTarget target, M68kCpuModel model, M68kPeepholeOptimizationMode mode)
	{
		var expected = CoreLibCodecStreamingFixtures.GeneratedEntry();
		Assert.Equal(0x19f5074cu, expected);
		RunCodecSurface(target, model, nameof(CoreLibCodecStreamingFixtures), "GeneratedEntry", expected, mode);
	}

	private void RunCodecSurface(M68kCpuTarget target, M68kCpuModel model, string fixture, string entry, uint expected,
		M68kPeepholeOptimizationMode mode = M68kPeepholeOptimizationMode.FixedPoint)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var request = new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = $"CopperSharp.Compiler.Tests.{fixture}::{entry}",
			IncludedExportNames = [],
			ManagedAssemblyPaths = [typeof(ShadowUtf8).Assembly.Location, typeof(EnvironmentPal).Assembly.Location],
			Cpu = target, FloatingPoint = M68kFloatingPointMode.SoftFloat,
			OutputFormat = M68kOutputFormat.Hunk, ExceptionMode = M68kExceptionMode.Full,
			PeepholeOptimization = mode,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions { StartAddress = 0x0010_0000, Size = 32768 },
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		};
		var result = M68kCompiler.Compile(request);
		Assert.True(result.FrameworkAnalysis.IsCompatible);
		Assert.Equal(expected, Execute(CreateHunkBus(result), model, HunkLoadAddress + result.EntryPoint,
			initialStackPointer: 0x0020_0000, maxInstructions: 100_000_000));
	}
}
