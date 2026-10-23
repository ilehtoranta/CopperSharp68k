/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Framework;

/// <summary>Exact, allocation-free System.Text.Unicode.Utf8 surface.</summary>
internal static class Utf8CodecSurface
{
	private static readonly FrameworkTypeId Owner = FrameworkTypeId.Named("System.Runtime", "System.Text.Unicode.Utf8");
	private static readonly FrameworkTypeId Byte = FrameworkTypeId.Primitive("System.Byte");
	private static readonly FrameworkTypeId Char = FrameworkTypeId.Primitive("System.Char");
	private static readonly FrameworkTypeId Int = FrameworkTypeId.Primitive("System.Int32");
	private static readonly FrameworkTypeId Bool = FrameworkTypeId.Primitive("System.Boolean");
	private static readonly FrameworkTypeId Status = FrameworkTypeId.Named("System.Runtime", "System.Buffers.OperationStatus");
	private static readonly FrameworkTypeId ReadBytes = Span("System.ReadOnlySpan`1", Byte);
	private static readonly FrameworkTypeId WriteBytes = Span("System.Span`1", Byte);
	private static readonly FrameworkTypeId ReadChars = Span("System.ReadOnlySpan`1", Char);
	private static readonly FrameworkTypeId WriteChars = Span("System.Span`1", Char);
	private static readonly FrameworkTypeId OutInt = FrameworkTypeId.ByReference(Int);

	public static bool TryCreateShadowBinding(FrameworkMemberId referenced, out FrameworkBinding binding)
	{
		binding = null!;
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		var name = Exact(member, "FromUtf16", Status, [ReadChars, WriteBytes, OutInt, OutInt, Bool, Bool]) ? "FromUtf16" :
			Exact(member, "ToUtf16", Status, [ReadBytes, WriteChars, OutInt, OutInt, Bool, Bool]) ? "ToUtf16" :
			Exact(member, "IsValid", Bool, [ReadBytes]) ? "IsValid" : null;
		if (name is null) return false;
		var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowUtf8", name);
		binding = new FrameworkBinding(referenced, FrameworkBindingKind.ShadowMethod,
			$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
			new FrameworkEffectSummary(FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
				[FrameworkFeature.Spans]),
			Reason: "Portable scalar UTF-8 conversion preserves status, consumed counts and caller-owned spans.",
			ShadowMethod: shadow);
		return true;
	}

	private static FrameworkTypeId Span(string name, FrameworkTypeId element) =>
		FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", name), [element]);

	private static bool Exact(FrameworkMemberId member, string name, FrameworkTypeId result, FrameworkTypeId[] parameters) =>
		member.DeclaringType.Equals(Owner) && member.Name == name && member.MethodTypeArguments.Length == 0 &&
		member.Signature.Header == 0 && member.Signature.GenericParameterCount == 0 &&
		member.Signature.RequiredParameterCount == parameters.Length && member.Signature.ReturnType.Equals(result) &&
		member.Signature.ParameterTypes.SequenceEqual(parameters);
}
