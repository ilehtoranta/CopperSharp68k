/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Compiler.Framework;

internal static class StringBuilderNumberSettingsSurface
{
	private static FrameworkTypeId P(string name) => FrameworkTypeId.Primitive("System." + name);
	private static readonly FrameworkTypeId Owner = FrameworkTypeId.Named("System.Runtime", "System.Globalization.NumberFormatInfo");
	internal static readonly IReadOnlyList<FrameworkMemberId> PublicMembers = [
		..new[] { "NegativeSign", "PositiveSign", "NumberDecimalSeparator", "NumberGroupSeparator", "CurrencyDecimalSeparator", "CurrencyGroupSeparator",
			"CurrencySymbol", "PercentDecimalSeparator", "PercentGroupSeparator", "PercentSymbol", "PerMilleSymbol", "NaNSymbol", "NegativeInfinitySymbol", "PositiveInfinitySymbol" }
			.Select(name => M("set_" + name, 0x20, [P("String")])),
		..new[] { "NumberDecimalDigits", "NumberNegativePattern", "CurrencyDecimalDigits", "CurrencyNegativePattern", "CurrencyPositivePattern",
			"PercentDecimalDigits", "PercentNegativePattern", "PercentPositivePattern" }.Select(name => M("set_" + name, 0x20, [P("Int32")])),
		..new[] { "NumberGroupSizes", "CurrencyGroupSizes", "PercentGroupSizes" }.Select(name => M("set_" + name, 0x20, [FrameworkTypeId.SzArray(P("Int32"))])),
		..new[] { "NumberGroupSizes", "CurrencyGroupSizes", "PercentGroupSizes" }.Select(name => new FrameworkMemberId(Owner, "get_" + name,
			new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.SzArray(P("Int32")), [])))
	];
	internal static readonly IReadOnlyList<FrameworkMemberId> Helpers = [
		M("VerifyWritable", 0x20, []), M("InitializeInvariantAndNegativeSignFlags", 0x20, []),
		M("CheckGroupSize", 0, [P("String"), FrameworkTypeId.SzArray(P("Int32"))])
	];
	private static FrameworkMemberId M(string name, byte header, FrameworkTypeId[] parameters) =>
		new(Owner, name, new FrameworkMethodSignatureId(header, 0, parameters.Length, P("Void"), parameters));
	internal static bool IsPublic(FrameworkMemberId member) => PublicMembers.Contains(FrameworkImplementationProfile.Canonicalize(member));
	internal static bool IsHelper(FrameworkMemberId member) => Helpers.Contains(FrameworkImplementationProfile.Canonicalize(member));
	internal static bool IsRangeThrow(FrameworkMemberId member)
	{
		var canonical = FrameworkImplementationProfile.Canonicalize(member);
		var argument = FrameworkTypeId.GenericMethodParameter(0);
		return canonical.DeclaringType.Equals(FrameworkTypeId.Named("System.Runtime", "System.ThrowHelper")) && canonical.Name == "ThrowArgumentOutOfRange_Range" &&
			canonical.Signature.Equals(new FrameworkMethodSignatureId(0x10, 1, 4, P("Void"), [P("String"), argument, argument, argument])) &&
			(canonical.MethodTypeArguments.Length == 0 || canonical.MethodTypeArguments.SequenceEqual([P("Int32")]) || canonical.MethodTypeArguments.SequenceEqual([argument]));
	}
	internal static bool IsRangeCaller(FrameworkMemberId? caller) => caller?.AssemblyName == "System.Private.CoreLib" && IsRangeThrow(caller);
	internal static bool IsOwnedCaller(FrameworkMemberId? caller) => caller?.AssemblyName == "System.Private.CoreLib" && (IsPublic(caller) || IsHelper(caller));
	internal static readonly FrameworkEffectSummary Effects = new(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
		FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
		[FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedArrays, FrameworkFeature.ManagedStrings, FrameworkFeature.ManagedExceptions, FrameworkFeature.ManagedGc]);
}
