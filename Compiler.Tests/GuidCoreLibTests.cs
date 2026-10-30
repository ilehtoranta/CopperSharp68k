/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using Copper68k;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Runtime;
using CopperSharp.Runtime.AmigaPal;

namespace CopperSharp.Compiler.Tests;

public sealed class GuidCoreLibTests
{
	[Fact]
	public void PinnedGuidHasTheVerifiedReferenceFreeSixteenByteLayout()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(typeof(GuidCoreLibFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(ShadowGuid).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		var type = new CilType(CilTypeKind.ValueType, 0, "System.Guid");
		Assert.True(module.TryGetStructLayout(type, "System.Private.CoreLib", out var raw));
		var observed = string.Join(",", Enumerable.Range(0, 11).Select(i =>
		{
			var field = module.ResolveManagedField("System.Private.CoreLib", "System.Guid", "_" + (char)('a' + i));
			return $"{field.Type.DisplayName}:{raw.FieldOffsets[field.Handle]}";
		}));
		Assert.True(module.IsSupportedStructType(type), $"Size={raw.Size} refs={raw.ReferenceBitmap} fields={observed}");
		Assert.True(module.TryGetReferenceFreeStructLayout(type, "System.Private.CoreLib", out var layout));
		Assert.Equal(16, layout.Size);
		Assert.Equal(0u, layout.ReferenceBitmap);
		Assert.True(module.IsVerifiedGuidLayout(layout));
		Assert.False(module.IsVerifiedGuidLayout(layout with { Size = 8 }));
		Assert.False(module.IsVerifiedGuidLayout(layout with { ReferenceBitmap = 1 }));
		Assert.False(module.IsVerifiedGuidLayout(layout with { FieldOffsets = new Dictionary<System.Reflection.Metadata.FieldDefinitionHandle, int>() }));
		using var ordinary = new CompilationModule(typeof(GuidCoreLibFixtures).Assembly.Location);
		Assert.False(ordinary.IsSupportedStructType(type));
		Assert.False(ordinary.TryGetReferenceFreeStructLayout(type, "System.Runtime", out _));
	}

	[Fact]
	public void GeneratedByteOrderAndValueVectorsMatchHostGuid()
	{
		var random = new Random(0x68c0);
		for (var i = 0; i < 128; i++)
		{
			var bytes = new byte[16];
			random.NextBytes(bytes);
			var host = new Guid(bytes);
			var shadow = new ShadowGuid(bytes);
			Assert.Equal(host.ToByteArray(), shadow.GetBytes());
			Assert.Equal(host.ToByteArray(true), shadow.GetBytes(true));
			Assert.True(shadow.Equals(host));
			Assert.Equal(host.GetHashCode(), shadow.GetValueHashCode());
			Assert.Equal(host.ToByteArray(), new ShadowGuid((ReadOnlySpan<byte>)host.ToByteArray(true), true).GetBytes());
			var otherBytes = new byte[16];
			random.NextBytes(otherBytes);
			Assert.Equal(Math.Sign(host.CompareTo(new Guid(otherBytes))), Math.Sign(shadow.CompareTo(new Guid(otherBytes))));
		}
		Assert.Equal(42, GuidCoreLibFixtures.BinaryEntry());
	}
}

public sealed partial class CompilerExecutionTests
{
	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void GuidBinaryValueSurfaceExecutesOnTarget(M68kCpuTarget target, M68kCpuModel model)
		=> RunGuidSurface(target, model, "BinaryEntry");

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void GuidTextSurfaceExecutesOnTarget(M68kCpuTarget target, M68kCpuModel model)
		=> RunGuidSurface(target, model, "TextEntry");

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void GuidGeneratedCorpusMatchesHostOnTarget(M68kCpuTarget target, M68kCpuModel model)
		=> RunGuidSurface(target, model, "GeneratedEntry", GuidCoreLibFixtures.GeneratedEntry());

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void GuidAdversarialCorpusMatchesHostOnTarget(M68kCpuTarget target, M68kCpuModel model)
		=> RunGuidSurface(target, model, "AdversarialEntry", GuidCoreLibFixtures.AdversarialEntry());

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void NarrowIncomingArgumentSpillsPreserveBothFrameModes(M68kCpuTarget target, M68kCpuModel model)
	{
		Assert.Equal(42, GuidCoreLibFixtures.IncomingScalarAbiEntry());
		var result = RunGuidSurface(target, model, "IncomingScalarAbiEntry");
		foreach (var name in new[] { "IncomingBytes", "IncomingWords" })
		{
			var stats = Assert.Single(result.AllocationStatistics.Where(s => s.Method.EndsWith("::" + name, StringComparison.Ordinal)));
			Assert.True(stats.SpilledValues > 0 && stats.AllocationIterations > 1, $"{name} must exercise argument spills: {stats}");
		}
	}

	private M68kCompilationResult RunGuidSurface(M68kCpuTarget target, M68kCpuModel model, string entry, uint expected = 42)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = $"CopperSharp.Compiler.Tests.GuidCoreLibFixtures::{entry}",
			IncludedExportNames = [],
			ManagedAssemblyPaths = [typeof(M68kRuntime).Assembly.Location, typeof(EnvironmentPal).Assembly.Location],
			Cpu = target,
			FloatingPoint = M68kFloatingPointMode.SoftFloat,
			OutputFormat = M68kOutputFormat.Hunk,
			ExceptionMode = M68kExceptionMode.Full,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions { StartAddress = 0x0010_0000, Size = 32 * 1024 },
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		});
		Assert.Equal(expected, Execute(CreateHunkBus(result), model, HunkLoadAddress + result.EntryPoint,
			initialStackPointer: 0x0020_0000, maxInstructions: 100_000_000));
		return result;
	}
}
