/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using System.Security.Cryptography;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderImplementationInputTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ReleasedInputRecognitionIsIndependentOfExperimentalAdmission(bool experimental)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = Load(pack, experimental);
		Assert.True(catalog.IsPinnedStringBuilderInput);
		Assert.Equal(experimental, catalog.EnableUnlistedManagedBodies);
		foreach (var member in StringBuilderFrameworkSurface.Members)
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedBinding(member, null, false, out _));
	}

	[Theory]
	[InlineData("packId", "Microsoft.NETCore.App.Runtime.test")]
	[InlineData("packVersion", "10.0.10")]
	[InlineData("runtimeIdentifier", "linux-x64")]
	public void MatchingBodyWithAdjacentPackageCoordinatesIsNotThePinnedInput(string property, string value)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		pack.Replace(property, value);
		Assert.False(Load(pack, true).IsPinnedStringBuilderInput);
	}

	[Fact]
	public void SyntheticHostManifestIsNotThePinnedInput()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		Assert.False(Load(pack, true).IsPinnedStringBuilderInput);
	}

	[Fact]
	public void RehashedBodyWithMatchingMetadataAndCoordinatesIsNotThePinnedInput()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using (var stream = new FileStream(pack.AssemblyPath, FileMode.Append, FileAccess.Write)) stream.WriteByte(0);
		var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pack.AssemblyPath))).ToLowerInvariant();
		pack.ReplaceAssembly("sha256", hash);
		var catalog = Load(pack, true);
		Assert.Equal(new Guid("62eafe31-9268-4ab3-97e6-d291cf7a762b"), Assert.Single(catalog.Provenance.Assemblies).Mvid);
		Assert.Equal(hash, Assert.Single(catalog.Provenance.Assemblies).Sha256);
		Assert.False(catalog.IsPinnedStringBuilderInput);
	}

	[Fact]
	public void IdentityRequiresEveryCompatibilityAxisAndAssemblyIdentityField()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var original = Load(pack, false).Provenance;
		var assembly = Assert.Single(original.Assemblies);
		M68kFrameworkImplementationPackProvenance[] rejected = [
			original with { ManifestSchemaVersion = 2 }, original with { TargetFramework = "net11.0" },
			original with { ReferencePack = "Other.Ref" }, original with { ReferencePackVersion = "10.0.10" },
			original with { ImplementationProfile = "other-profile" }, original with { Assemblies = [] },
			original with { Assemblies = [assembly, assembly] },
			original with { Assemblies = [assembly with { Name = "Other.CoreLib" }] },
			original with { Assemblies = [assembly with { Version = "11.0.0.0" }] },
			original with { Assemblies = [assembly with { PublicKeyToken = "0000000000000000" }] },
			original with { Assemblies = [assembly with { Mvid = Guid.Empty }] },
			original with { Assemblies = [assembly with { Sha256 = new string('0', 64) }] }
		];
		foreach (var input in rejected) Assert.False(StringBuilderImplementationInput.Matches(input));
		Assert.True(StringBuilderImplementationInput.Matches(original with { Assemblies = [assembly with {
			Sha256 = assembly.Sha256.ToUpperInvariant(), PublicKeyToken = assembly.PublicKeyToken.ToUpperInvariant() }] }));
	}

	private static FrameworkImplementationPackCatalog Load(FrameworkImplementationPackTests.CoreLibPack pack, bool experimental) =>
		FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = experimental })!;
}
