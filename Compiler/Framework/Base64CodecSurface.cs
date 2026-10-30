/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Framework;

/// <summary>Exact standard Base64 and Convert overloads in pinned .NET 10 CoreLib.</summary>
internal static class Base64CodecSurface
{
	private static readonly FrameworkTypeId Base64 = FrameworkTypeId.Named("System.Runtime", "System.Buffers.Text.Base64");
	private static readonly FrameworkTypeId Convert = FrameworkTypeId.Named("System.Runtime", "System.Convert");
	private static readonly FrameworkTypeId Byte = FrameworkTypeId.Primitive("System.Byte");
	private static readonly FrameworkTypeId Char = FrameworkTypeId.Primitive("System.Char");
	private static readonly FrameworkTypeId Int = FrameworkTypeId.Primitive("System.Int32");
	private static readonly FrameworkTypeId Bool = FrameworkTypeId.Primitive("System.Boolean");
	private static readonly FrameworkTypeId String = FrameworkTypeId.Primitive("System.String");
	private static readonly FrameworkTypeId Status = FrameworkTypeId.Named("System.Runtime", "System.Buffers.OperationStatus");
	private static readonly FrameworkTypeId Options = FrameworkTypeId.Named("System.Runtime", "System.Base64FormattingOptions");
	private static readonly FrameworkTypeId Bytes = FrameworkTypeId.SzArray(Byte);
	private static readonly FrameworkTypeId Chars = FrameworkTypeId.SzArray(Char);
	private static readonly FrameworkTypeId ReadBytes = Span("System.ReadOnlySpan`1", Byte);
	private static readonly FrameworkTypeId WriteBytes = Span("System.Span`1", Byte);
	private static readonly FrameworkTypeId ReadChars = Span("System.ReadOnlySpan`1", Char);
	private static readonly FrameworkTypeId WriteChars = Span("System.Span`1", Char);
	private static readonly FrameworkTypeId OutInt = FrameworkTypeId.ByReference(Int);

	public static bool IsRuntimeStringCreator(FrameworkMemberId? caller) => caller is not null &&
		caller.DeclaringType.Equals(FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowConvertBase64")) &&
		caller.Name == "CreateString" && caller.Signature.Header == 0 && caller.Signature.GenericParameterCount == 0 &&
		caller.MethodTypeArguments.Length == 0 && caller.Signature.RequiredParameterCount == 1 &&
		caller.Signature.ReturnType.Equals(String) && caller.Signature.ParameterTypes.SequenceEqual([ReadChars]);

	public static bool TryCreateShadowBinding(FrameworkMemberId referenced, out FrameworkBinding binding)
	{
		binding = null!;
		var m = FrameworkImplementationProfile.Canonicalize(referenced);
		string? method = null;
		string? type = null;
		if (Exact(m, Base64, "GetMaxEncodedToUtf8Length", Int, [Int]) ||
			Exact(m, Base64, "GetMaxDecodedFromUtf8Length", Int, [Int]) ||
			Exact(m, Base64, "EncodeToUtf8", Status, [ReadBytes, WriteBytes, OutInt, OutInt, Bool]) ||
			Exact(m, Base64, "DecodeFromUtf8", Status, [ReadBytes, WriteBytes, OutInt, OutInt, Bool]) ||
			Exact(m, Base64, "EncodeToUtf8InPlace", Status, [WriteBytes, Int, OutInt]) ||
			Exact(m, Base64, "DecodeFromUtf8InPlace", Status, [WriteBytes, OutInt]) ||
			Exact(m, Base64, "IsValid", Bool, [ReadBytes]) || Exact(m, Base64, "IsValid", Bool, [ReadBytes, OutInt]) ||
			Exact(m, Base64, "IsValid", Bool, [ReadChars]) || Exact(m, Base64, "IsValid", Bool, [ReadChars, OutInt]))
		{
			method = m.Name;
			type = "CopperSharp.Runtime.ShadowBase64";
		}
		else if (Exact(m, Convert, "ToBase64String", String, [Bytes]) ||
			Exact(m, Convert, "ToBase64String", String, [Bytes, Options]) ||
			Exact(m, Convert, "ToBase64String", String, [Bytes, Int, Int]) ||
			Exact(m, Convert, "ToBase64String", String, [Bytes, Int, Int, Options]) ||
			Exact(m, Convert, "ToBase64String", String, [ReadBytes, Options]) ||
			Exact(m, Convert, "ToBase64CharArray", Int, [Bytes, Int, Int, Chars, Int]) ||
			Exact(m, Convert, "ToBase64CharArray", Int, [Bytes, Int, Int, Chars, Int, Options]) ||
			Exact(m, Convert, "TryToBase64Chars", Bool, [ReadBytes, WriteChars, OutInt, Options]) ||
			Exact(m, Convert, "FromBase64String", Bytes, [String]) ||
			Exact(m, Convert, "FromBase64CharArray", Bytes, [Chars, Int, Int]) ||
			Exact(m, Convert, "TryFromBase64String", Bool, [String, WriteBytes, OutInt]) ||
			Exact(m, Convert, "TryFromBase64Chars", Bool, [ReadChars, WriteBytes, OutInt]))
		{
			method = m.Name;
			type = "CopperSharp.Runtime.ShadowConvertBase64";
		}
		if (method is null) return false;
		var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", type!, method);
		binding = new FrameworkBinding(referenced, FrameworkBindingKind.ShadowMethod,
			$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
			new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
				FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
				[FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedStrings, FrameworkFeature.ManagedGc, FrameworkFeature.ManagedExceptions, FrameworkFeature.Spans]),
			Reason: "Pinned standard Base64 semantics use scalar span adapters with explicit status and boundary behavior.",
			ShadowMethod: shadow);
		return true;
	}

	private static FrameworkTypeId Span(string name, FrameworkTypeId element) =>
		FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", name), [element]);
	private static bool Exact(FrameworkMemberId m, FrameworkTypeId owner, string name, FrameworkTypeId result, FrameworkTypeId[] parameters) =>
		m.DeclaringType.Equals(owner) && m.Name == name && m.MethodTypeArguments.Length == 0 &&
		m.Signature.Header == 0 && m.Signature.GenericParameterCount == 0 &&
		m.Signature.RequiredParameterCount == parameters.Length && m.Signature.ReturnType.Equals(result) &&
		m.Signature.ParameterTypes.SequenceEqual(parameters);
}
