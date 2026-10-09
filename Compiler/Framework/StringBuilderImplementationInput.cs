/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Compiler.Framework;

/// <summary>
/// Compiler-owned identity of the released CIL input validated for StringBuilder.
/// Recognizing this input does not itself admit any method or change lowering.
/// </summary>
internal static class StringBuilderImplementationInput
{
	private const string Sha256 = "dc1945de746f94987ec705a1f27d512abf72a41a1da37e414d1951e4e823037a";
	private static readonly Guid Mvid = new("62eafe31-9268-4ab3-97e6-d291cf7a762b");

	public static bool Matches(M68kFrameworkImplementationPackProvenance input) =>
		input.ManifestSchemaVersion == FrameworkImplementationPackLoader.SchemaVersion &&
		input.PackId == "Microsoft.NETCore.App.Runtime.win-x64" && input.PackVersion == "10.0.9" &&
		input.RuntimeIdentifier == "win-x64" && input.TargetFramework == FrameworkImplementationPackLoader.TargetFramework &&
		input.ReferencePack == FrameworkImplementationPackLoader.ReferencePack &&
		input.ReferencePackVersion == FrameworkImplementationPackLoader.ReferencePackVersion &&
		input.ImplementationProfile == FrameworkImplementationPackLoader.CoreLibProfile &&
		input.Assemblies is [var assembly] && assembly.Name == "System.Private.CoreLib" && assembly.Version == "10.0.0.0" &&
		string.Equals(assembly.PublicKeyToken, "7cec85d7bea7798e", StringComparison.OrdinalIgnoreCase) &&
		assembly.Mvid == Mvid && string.Equals(assembly.Sha256, Sha256, StringComparison.OrdinalIgnoreCase);
}
