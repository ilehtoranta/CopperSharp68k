/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Framework;

/// <summary>Exact hexadecimal System.Convert overloads from pinned CoreLib.</summary>
internal static class HexCodecSurface
{
	private static readonly FrameworkTypeId Owner = FrameworkTypeId.Named("System.Runtime", "System.Convert");
	private static readonly FrameworkTypeId String = FrameworkTypeId.Primitive("System.String");
	private static readonly FrameworkTypeId Byte = FrameworkTypeId.Primitive("System.Byte");
	private static readonly FrameworkTypeId Char = FrameworkTypeId.Primitive("System.Char");
	private static readonly FrameworkTypeId Int = FrameworkTypeId.Primitive("System.Int32");
	private static readonly FrameworkTypeId Bool = FrameworkTypeId.Primitive("System.Boolean");
	private static readonly FrameworkTypeId Status = FrameworkTypeId.Named("System.Runtime", "System.Buffers.OperationStatus");
	private static readonly FrameworkTypeId Array = FrameworkTypeId.SzArray(Byte);
	private static readonly FrameworkTypeId ReadBytes = Span("System.ReadOnlySpan`1", Byte);
	private static readonly FrameworkTypeId WriteBytes = Span("System.Span`1", Byte);
	private static readonly FrameworkTypeId ReadChars = Span("System.ReadOnlySpan`1", Char);
	private static readonly FrameworkTypeId WriteChars = Span("System.Span`1", Char);
	private static readonly FrameworkTypeId OutInt = FrameworkTypeId.ByReference(Int);
	private static readonly FrameworkTypeId[] ArraySlice = [Array, Int, Int];
	private static readonly FrameworkTypeId[] WriteCharsSignature = [ReadBytes, WriteChars, OutInt];
	private static readonly FrameworkTypeId[] WriteUtf8Signature = [ReadBytes, WriteBytes, OutInt];
	private static readonly FrameworkTypeId[] ParseStringDestination = [String, WriteBytes, OutInt, OutInt];
	private static readonly FrameworkTypeId[] ParseCharsDestination = [ReadChars, WriteBytes, OutInt, OutInt];
	private static readonly FrameworkTypeId[] ParseUtf8Destination = [ReadBytes, WriteBytes, OutInt, OutInt];

	public static bool IsRuntimeStringCreator(FrameworkMemberId? caller) => caller is not null &&
		caller.DeclaringType.Equals(FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowConvertHex")) &&
		caller.Name == "CreateString" && caller.Signature.Header == 0 && caller.Signature.GenericParameterCount == 0 &&
		caller.MethodTypeArguments.Length == 0 && caller.Signature.RequiredParameterCount == 1 &&
		caller.Signature.ReturnType.Equals(String) && caller.Signature.ParameterTypes.SequenceEqual([ReadChars]);

	public static bool TryCreateShadowBinding(FrameworkMemberId referenced, out FrameworkBinding binding)
	{
		binding = null!;
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		string? name = null;
		if (Exact(member, "ToHexString", String, [Array]) || Exact(member, "ToHexString", String, ArraySlice) ||
			Exact(member, "ToHexString", String, [ReadBytes]) ||
			Exact(member, "TryToHexString", Bool, WriteCharsSignature) ||
			Exact(member, "TryToHexString", Bool, WriteUtf8Signature)) name = member.Name;
		else if (Exact(member, "ToHexStringLower", String, [Array]) || Exact(member, "ToHexStringLower", String, ArraySlice) ||
			Exact(member, "ToHexStringLower", String, [ReadBytes]) ||
			Exact(member, "TryToHexStringLower", Bool, WriteCharsSignature) ||
			Exact(member, "TryToHexStringLower", Bool, WriteUtf8Signature)) name = member.Name;
		else if (Exact(member, "FromHexString", Array, [String]) || Exact(member, "FromHexString", Array, [ReadChars]) ||
			Exact(member, "FromHexString", Array, [ReadBytes]) ||
			Exact(member, "FromHexString", Status, ParseStringDestination) ||
			Exact(member, "FromHexString", Status, ParseCharsDestination) ||
			Exact(member, "FromHexString", Status, ParseUtf8Destination)) name = member.Name;
		if (name is null) return false;
		var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowConvertHex", name);
		binding = new FrameworkBinding(referenced, FrameworkBindingKind.ShadowMethod,
			$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
			new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
				FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
				[FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedStrings, FrameworkFeature.ManagedGc, FrameworkFeature.ManagedExceptions, FrameworkFeature.Spans]),
			Reason: "Scalar hexadecimal conversion preserves exact output, status and partial conversion counts.",
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
