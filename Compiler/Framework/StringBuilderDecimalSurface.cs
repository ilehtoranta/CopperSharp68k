/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Compiler.Framework;

internal static class StringBuilderDecimalSurface
{
	internal static readonly FrameworkEffectSummary Effects = new(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
		FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
		[FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedArrays, FrameworkFeature.ManagedStrings, FrameworkFeature.Spans, FrameworkFeature.ManagedGc, FrameworkFeature.StringBuilder]);
	private static FrameworkTypeId P(string name) => FrameworkTypeId.Primitive("System." + name);
	private static FrameworkTypeId N(string name) => FrameworkTypeId.Named("System.Runtime", name);
	private static FrameworkTypeId S(string name, bool readOnly = false) => FrameworkTypeId.GenericInstantiation(N(readOnly ? "System.ReadOnlySpan`1" : "System.Span`1"), [P(name)]);
	private static readonly FrameworkTypeId Decimal = N("System.Decimal"), Void = P("Void"), Text = P("String"), Int = P("Int32"), Provider = N("System.IFormatProvider");
	internal static readonly IReadOnlyList<FrameworkMemberId> PublicMembers = [
		M(".ctor", 0x20, Void, [Int, Int, Int, P("Boolean"), P("Byte")]),
		M(".ctor", 0x20, Void, [Int]), M(".ctor", 0x20, Void, [P("UInt32")]),
		M(".ctor", 0x20, Void, [P("Int64")]), M(".ctor", 0x20, Void, [P("UInt64")]),
		M("ToString", 0x20, Text, []), M("ToString", 0x20, Text, [Text]),
		M("ToString", 0x20, Text, [Provider]), M("ToString", 0x20, Text, [Text, Provider]),
		M("TryFormat", 0x20, P("Boolean"), [S("Char"), FrameworkTypeId.ByReference(Int), S("Char", true), Provider]),
		M("GetBits", 0, Int, [Decimal, S("Int32")]),
		M("GetBits", 0, FrameworkTypeId.SzArray(Int), [Decimal])
	];
	internal static readonly IReadOnlyList<FrameworkMemberId> PrivateMembers = [
		M("get_High", 0x20, P("UInt32"), []), M("get_Low", 0x20, P("UInt32"), []),
		M("get_Mid", 0x20, P("UInt32"), []), M("get_Low64", 0x20, P("UInt64"), [])
	];
	private static FrameworkMemberId M(string name, byte header, FrameworkTypeId result, FrameworkTypeId[] parameters) =>
		new(Decimal, name, new FrameworkMethodSignatureId(header, 0, parameters.Length, result, parameters));
	internal static bool IsPublic(FrameworkMemberId member) => PublicMembers.Contains(FrameworkImplementationProfile.Canonicalize(member));
	internal static bool Contains(FrameworkMemberId member) => IsPublic(member) || PrivateMembers.Contains(FrameworkImplementationProfile.Canonicalize(member));
	internal static bool IsOwnedCaller(FrameworkMemberId? caller) => caller?.AssemblyName == "System.Private.CoreLib" && Contains(caller);
	internal static bool IsValidationCaller(FrameworkMemberId? caller) => caller?.AssemblyName == "System.Private.CoreLib" && IsValidationDependency(caller);
	internal static bool IsValidationDependency(FrameworkMemberId member)
	{
		var canonical = FrameworkImplementationProfile.Canonicalize(member);
		var parameter = FrameworkTypeId.GenericMethodParameter(0);
		return (canonical.DeclaringType.Equals(N("System.ArgumentOutOfRangeException")) && canonical.Name is "ThrowIfGreaterThan" or "ThrowGreater" &&
			canonical.Signature.Equals(new FrameworkMethodSignatureId(0x10, 1, 3, Void, [parameter, parameter, Text])) &&
			(canonical.MethodTypeArguments.Length == 0 || canonical.MethodTypeArguments.SequenceEqual(new[] { Int }) || canonical.MethodTypeArguments.SequenceEqual(new[] { parameter }))) ||
			canonical.Equals(new FrameworkMemberId(N("System.ThrowHelper"), "ThrowArgumentException_DestinationTooShort", new FrameworkMethodSignatureId(0, 0, 0, Void, [])));
	}
	internal static bool IsComparisonDependency(FrameworkMemberId member) => FrameworkImplementationProfile.Canonicalize(member).Equals(
		new FrameworkMemberId(FrameworkTypeId.GenericInstantiation(N("System.IComparable`1"), [FrameworkTypeId.GenericMethodParameter(0)]), "CompareTo",
			new FrameworkMethodSignatureId(0x20, 0, 1, Int, [FrameworkTypeId.GenericTypeParameter(0)]))) ||
		FrameworkImplementationProfile.Canonicalize(member).Equals(new FrameworkMemberId(FrameworkTypeId.GenericInstantiation(N("System.IComparable`1"), [Int]), "CompareTo",
			new FrameworkMethodSignatureId(0x20, 0, 1, Int, [FrameworkTypeId.GenericTypeParameter(0)]))) ||
		FrameworkImplementationProfile.Canonicalize(member).Equals(new FrameworkMemberId(N("System.Int32"), "CompareTo", new FrameworkMethodSignatureId(0x20, 0, 1, Int, [Int])));
	internal static bool IsRuntimeCaller(FrameworkMemberId? caller)
	{
		if (caller?.AssemblyName != "CopperSharp.Runtime.Managed" || caller.DeclaringType.FullMetadataName != "CopperSharp.Runtime.ShadowDecimalFormatting") return false;
		var member = FrameworkImplementationProfile.Canonicalize(caller);
		var info = N("System.Globalization.NumberFormatInfo");
		return member.Equals(new FrameworkMemberId(caller.DeclaringType, "FormatDecimal", new FrameworkMethodSignatureId(0, 0, 3, Text, [Decimal, S("Char", true), info]))) ||
			member.Equals(new FrameworkMemberId(caller.DeclaringType, "TryFormatDecimal", new FrameworkMethodSignatureId(0, 0, 5, P("Boolean"), [Decimal, S("Char", true), info, S("Char"), FrameworkTypeId.ByReference(Int)]))) ||
			member.Equals(new FrameworkMemberId(caller.DeclaringType, "CreateNumber", new FrameworkMethodSignatureId(0, 0, 2,
				FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowNumberBuffer"), [Decimal, S("Byte")])));
	}
}
