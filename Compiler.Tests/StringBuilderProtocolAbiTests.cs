/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderProtocolAbiTests
{
	[Theory]
	[InlineData("AppendInterpolatedStringHandler", 3u)]
	[InlineData("ChunkEnumerator", 7u)]
	public void ReleasedProtocolLayoutsDoNotRequireUnlistedBodyAdmission(string nested, uint bitmap)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = Load(pack, false);
		Assert.False(catalog.EnableUnlistedManagedBodies);
		using var module = Open(catalog);
		var type = new CilType(CilTypeKind.ValueType, 0, "System.Text.StringBuilder/" + nested);
		Assert.True(module.TryGetReferenceFreeStructLayout(type, "System.Private.CoreLib", out var layout));
		Assert.Equal("System.Private.CoreLib", layout.ModuleName);
		Assert.Equal(12, layout.Size);
		Assert.Equal(bitmap, layout.ReferenceBitmap);
		Assert.Equal(new[] { 0, 4, 8 }, layout.FieldOffsets.Values.Order());
		Assert.False(module.TryGetReferenceFreeStructLayout(type with { DisplayName = "Application/" + nested }, "System.Private.CoreLib", out _));
		Assert.False(module.TryGetReferenceFreeStructLayout(type with {
			GenericArguments = [new CilType(CilTypeKind.SignedInteger, 4, "int")]
		}, "System.Private.CoreLib", out _));
		foreach (var member in StringBuilderFrameworkSurface.Members.Concat(StringBuilderFrameworkSurface.Helpers).Concat(StringBuilderFrameworkSurface.NestedMembers))
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedBinding(member, null, false, out _));
		var decimalType = new CilType(CilTypeKind.ValueType, 0, "System.Decimal");
		Assert.True(module.TryGetReferenceFreeStructLayout(decimalType, "System.Private.CoreLib", out var decimalLayout));
		Assert.Equal(16, decimalLayout.Size);
		Assert.Equal(0u, decimalLayout.ReferenceBitmap);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void SyntheticHostProtocolLayoutsRetainTheExperimentalGate(bool experimental)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var catalog = Load(pack, experimental);
		Assert.Equal(experimental, catalog.SupportsStringBuilderProtocolAbi);
		using var module = Open(catalog);
		foreach (var nested in new[] { "AppendInterpolatedStringHandler", "ChunkEnumerator" })
			Assert.Equal(experimental, module.TryGetReferenceFreeStructLayout(
				new CilType(CilTypeKind.ValueType, 0, "System.Text.StringBuilder/" + nested), "System.Private.CoreLib", out _));
	}

	[Fact]
	public void AdjacentPackCoordinatesCannotEnableStableProtocolTransport()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		pack.Replace("packVersion", "10.0.10");
		var catalog = Load(pack, false);
		Assert.False(catalog.SupportsStringBuilderProtocolAbi);
		using var module = Open(catalog);
		Assert.False(module.TryGetReferenceFreeStructLayout(
			new CilType(CilTypeKind.ValueType, 0, "System.Text.StringBuilder/ChunkEnumerator"), "System.Private.CoreLib", out _));
	}

	private static FrameworkImplementationPackCatalog Load(FrameworkImplementationPackTests.CoreLibPack pack, bool experimental) =>
		FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = experimental })!;
	private static CompilationModule Open(FrameworkImplementationPackCatalog catalog) =>
		new(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack: catalog);
}
