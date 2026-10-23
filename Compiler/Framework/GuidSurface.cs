/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Framework;

/// <summary>Exact deterministic Guid APIs; randomness and OS services are excluded.</summary>
internal static class GuidSurface
{
	private static readonly FrameworkTypeId Guid = FrameworkTypeId.Named("System.Runtime", "System.Guid");
	private static readonly FrameworkTypeId String = FrameworkTypeId.Primitive("System.String");
	private static readonly FrameworkTypeId Void = FrameworkTypeId.Primitive("System.Void");
	private static readonly FrameworkTypeId Boolean = FrameworkTypeId.Primitive("System.Boolean");
	private static readonly FrameworkTypeId Int32 = FrameworkTypeId.Primitive("System.Int32");
	private static readonly FrameworkTypeId Int16 = FrameworkTypeId.Primitive("System.Int16");
	private static readonly FrameworkTypeId Byte = FrameworkTypeId.Primitive("System.Byte");
	private static readonly FrameworkTypeId Bytes = FrameworkTypeId.SzArray(Byte);
	private static readonly FrameworkTypeId ReadOnlyBytes = Span("System.ReadOnlySpan`1", Byte);
	private static readonly FrameworkTypeId WriteBytes = Span("System.Span`1", Byte);
	private static readonly FrameworkTypeId ReadOnlyChars = Span("System.ReadOnlySpan`1", FrameworkTypeId.Primitive("System.Char"));
	private static readonly FrameworkTypeId WriteChars = Span("System.Span`1", FrameworkTypeId.Primitive("System.Char"));
	private static readonly FrameworkTypeId OutGuid = FrameworkTypeId.ByReference(Guid);
	private static readonly FrameworkTypeId OutInt = FrameworkTypeId.ByReference(Int32);
	private static readonly FrameworkTypeId UInt32 = FrameworkTypeId.Primitive("System.UInt32");
	internal static bool IsRuntimeFormatter(FrameworkMemberId? caller) => caller is not null &&
		caller.DeclaringType.Equals(FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowGuid")) &&
		caller.Name == "Format" && caller.Signature.Header == 0x20 && caller.Signature.GenericParameterCount == 0 &&
		caller.MethodTypeArguments.Length == 0 && caller.Signature.RequiredParameterCount == 1 &&
		caller.Signature.ReturnType.Equals(String) && caller.Signature.ParameterTypes.SequenceEqual([String]);

	public static bool TryCreatePinnedBinding(FrameworkMemberId referenced, FrameworkMemberId? caller, out FrameworkBinding binding)
	{
		binding = null!;
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		var components = Exact(member, true, ".ctor", Void, [Int32, Int16, Int16, Byte, Byte, Byte, Byte, Byte, Byte, Byte, Byte]);
		var comparison = Exact(member, true, "CompareTo", Int32, [Guid]);
		var helper = caller?.AssemblyName == "System.Private.CoreLib" &&
			Exact(FrameworkImplementationProfile.Canonicalize(caller), true, "CompareTo", Int32, [Guid]) &&
			Exact(member, false, "GetResult", Int32, [UInt32, UInt32]);
		if (!(components || comparison || helper) || !FrameworkImplementationProfile.TryCreatePinnedBinding(referenced, null, true, out binding)) return false;
		binding = binding with
		{
			EffectSummary = new FrameworkEffectSummary(components ? FrameworkEffects.WritesManagedMemory : comparison ? FrameworkEffects.ReadsManagedMemory : FrameworkEffects.None, []),
			Reason = "The exact Guid component constructor and typed comparison execute the pinned managed body over the verified payload."
		};
		return true;
	}

	public static bool TryCreateShadowBinding(FrameworkMemberId referenced, out FrameworkBinding binding)
	{
		binding = null!;
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		string? name = null;
		if (Exact(member, true, ".ctor", Void, [Int32, Int16, Int16, Byte, Byte, Byte, Byte, Byte, Byte, Byte, Byte]) ||
			Exact(member, true, ".ctor", Void, [Bytes]) || Exact(member, true, ".ctor", Void, [ReadOnlyBytes]) ||
			Exact(member, true, ".ctor", Void, [ReadOnlyBytes, Boolean])) name = ".ctor";
		else if (Exact(member, true, "Equals", Boolean, [Guid])) name = "Equals";
		else if (Exact(member, true, "CompareTo", Int32, [Guid])) name = "CompareTo";
		else if (Exact(member, true, "GetHashCode", Int32, [])) name = "GetValueHashCode";
		else if (Exact(member, false, "op_Equality", Boolean, [Guid, Guid])) name = "AreEqual";
		else if (Exact(member, false, "op_Inequality", Boolean, [Guid, Guid])) name = "AreNotEqual";
		else if (Exact(member, true, "ToByteArray", Bytes, []) || Exact(member, true, "ToByteArray", Bytes, [Boolean])) name = "GetBytes";
		else if (Exact(member, true, "TryWriteBytes", Boolean, [WriteBytes]) ||
			Exact(member, true, "TryWriteBytes", Boolean, [WriteBytes, Boolean, OutInt])) name = "TryWriteBytes";
		else if (Exact(member, true, "ToString", String, [])) name = "FormatDefault";
		else if (Exact(member, true, "ToString", String, [String])) name = "Format";
		else if (Exact(member, true, "TryFormat", Boolean, [WriteChars, OutInt, ReadOnlyChars])) name = "TryFormat";
		else if (Exact(member, false, "Parse", Guid, [String])) name = "Parse";
		else if (Exact(member, false, "TryParse", Boolean, [String, OutGuid])) name = "TryParse";
		else if (Exact(member, false, "ParseExact", Guid, [String, String])) name = "ParseExact";
		else if (Exact(member, false, "TryParseExact", Boolean, [String, String, OutGuid])) name = "TryParseExact";
		if (name is null) return false;
		var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowGuid", name);
		binding = new FrameworkBinding(referenced, FrameworkBindingKind.ShadowMethod,
			$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
			new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
				FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
				[FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedStrings, FrameworkFeature.ManagedGc, FrameworkFeature.ManagedExceptions, FrameworkFeature.Spans]),
			Reason: "Deterministic Guid operations use the verified sixteen-byte payload and explicit logical byte order on the big-endian target.",
			ShadowMethod: shadow);
		return true;
	}

	private static FrameworkTypeId Span(string name, FrameworkTypeId element) =>
		FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", name), [element]);

	private static bool Exact(FrameworkMemberId member, bool instance, string name, FrameworkTypeId result, FrameworkTypeId[] parameters) =>
		member.DeclaringType.Equals(Guid) && member.Name == name && member.MethodTypeArguments.Length == 0 &&
		member.Signature.Header == (instance ? 0x20 : 0) && member.Signature.GenericParameterCount == 0 &&
		member.Signature.RequiredParameterCount == parameters.Length && member.Signature.ReturnType.Equals(result) &&
		member.Signature.ParameterTypes.SequenceEqual(parameters);
}
