/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Compiler.Framework;

internal static class StringBuilderFloatingSurface
{
	private static FrameworkTypeId P(string name) => FrameworkTypeId.Primitive("System." + name);
	private static FrameworkTypeId N(string name) => FrameworkTypeId.Named("System.Runtime", name);
	private static FrameworkTypeId Span(FrameworkTypeId element, bool readOnly = false) => FrameworkTypeId.GenericInstantiation(N(readOnly ? "System.ReadOnlySpan`1" : "System.Span`1"), [element]);
	private static readonly FrameworkTypeId Text = P("String"), Char = P("Char"), Int = P("Int32"), Bool = P("Boolean"), Provider = N("System.IFormatProvider"), Info = N("System.Globalization.NumberFormatInfo");
	private static readonly FrameworkTypeId T = FrameworkTypeId.GenericMethodParameter(0), C = FrameworkTypeId.GenericMethodParameter(1);
	private static readonly FrameworkTypeId Number = N("System.Number"), Diy = FrameworkTypeId.Named("System.Runtime", "DiyFp", Number), Grisu = FrameworkTypeId.Named("System.Runtime", "Grisu3", Number);
	private static readonly FrameworkTypeId DiyRef = FrameworkTypeId.ByReference(Diy), IntRef = FrameworkTypeId.ByReference(Int), ULong = P("UInt64"), UInt = P("UInt32"), Void = P("Void"), Bytes = Span(P("Byte"));
	private static readonly FrameworkTypeId Big = FrameworkTypeId.Named("System.Runtime", "BigInteger", Number), BigRef = FrameworkTypeId.ByReference(Big);
	private static FrameworkMemberId M(FrameworkTypeId owner, string name, byte header, int arity, FrameworkTypeId result, FrameworkTypeId[] parameters, params FrameworkTypeId[] arguments) =>
		new(owner, name, new FrameworkMethodSignatureId(header, arity, parameters.Length, result, parameters), arguments);
	internal static readonly IReadOnlyList<FrameworkMemberId> PublicMembers = new[] { "System.Single", "System.Double" }.SelectMany(owner => new[] {
		M(N(owner), "ToString", 0x20, 0, Text, []), M(N(owner), "ToString", 0x20, 0, Text, [Text]),
		M(N(owner), "ToString", 0x20, 0, Text, [Provider]), M(N(owner), "ToString", 0x20, 0, Text, [Text, Provider]),
		M(N(owner), "TryFormat", 0x20, 0, Bool, [Span(Char), FrameworkTypeId.ByReference(Int), Span(Char, true), Provider])
	}).ToArray();
	internal static readonly IReadOnlyList<FrameworkMemberId> Helpers = [
		M(Diy, ".ctor", 0x20, 0, Void, [ULong, Int]), M(Diy, "Normalize", 0x20, 0, Diy, []),
		M(Diy, "Multiply", 0x20, 0, Diy, [DiyRef]), M(Diy, "Subtract", 0x20, 0, Diy, [DiyRef]),
		M(Diy, "GetBoundaries", 0x20, 0, Void, [Int, DiyRef, DiyRef]),
		M(Grisu, "TryRunCounted", 0, 0, Bool, [DiyRef, Int, Bytes, IntRef, IntRef]),
		M(Grisu, "TryRunShortest", 0, 0, Bool, [DiyRef, DiyRef, DiyRef, Bytes, IntRef, IntRef]),
		M(Grisu, "TryDigitGenCounted", 0, 0, Bool, [DiyRef, Int, Bytes, IntRef, IntRef]),
		M(Grisu, "TryDigitGenShortest", 0, 0, Bool, [DiyRef, DiyRef, DiyRef, Bytes, IntRef, IntRef]),
		M(Grisu, "BiggestPowerTen", 0, 0, UInt, [UInt, Int, IntRef]),
		M(Grisu, "GetCachedPowerForBinaryExponentRange", 0, 0, Diy, [Int, Int, IntRef]),
		M(Grisu, "TryRoundWeedCounted", 0, 0, Bool, [Bytes, Int, ULong, ULong, ULong, IntRef]),
		M(Grisu, "TryRoundWeedShortest", 0, 0, Bool, [Bytes, Int, ULong, ULong, ULong, ULong, ULong]),
		M(Grisu, "get_CachedPowersBinaryExponent", 0, 0, Span(P("Int16"), true), []),
		M(Grisu, "get_CachedPowersDecimalExponent", 0, 0, Span(P("Int16"), true), []),
		M(Grisu, "get_CachedPowersSignificand", 0, 0, Span(ULong, true), []),
		M(Grisu, "get_SmallPowersOfTen", 0, 0, Span(UInt, true), []),
		M(Number, "Dragon4", 0, 0, UInt, [ULong, Int, UInt, Bool, Int, Bool, Bytes, IntRef]),
		M(Number, "GetFloatingPointMaxDigitsAndPrecision", 0, 0, Int, [Char, IntRef, Info, FrameworkTypeId.ByReference(Bool)]),
		M(FrameworkTypeId.Named("System.Runtime", "NumberBuffer", Number), ".ctor", 0x20, 0, Void, [FrameworkTypeId.Named("System.Runtime", "NumberBufferKind", Number), FrameworkTypeId.Pointer(P("Byte")), Int]),
		M(FrameworkTypeId.Named("System.Runtime", "NumberBuffer", Number), ".ctor", 0x20, 0, Void, [FrameworkTypeId.Named("System.Runtime", "NumberBufferKind", Number), Bytes]),
		M(Big, "Add", 0, 0, Void, [BigRef, BigRef, BigRef]), M(Big, "Compare", 0, 0, Int, [BigRef, BigRef]),
		M(Big, "GetBlock", 0x20, 0, UInt, [UInt]), M(Big, "GetLength", 0x20, 0, Int, []),
		M(Big, "HeuristicDivide", 0, 0, UInt, [BigRef, BigRef]), M(Big, "IsZero", 0x20, 0, Bool, []),
		M(Big, "Multiply", 0x20, 0, Void, [BigRef]), M(Big, "Multiply", 0, 0, Void, [BigRef, UInt, BigRef]),
		M(Big, "Multiply10", 0x20, 0, Void, []), M(Big, "MultiplyPow10", 0x20, 0, Void, [UInt]),
		M(Big, "Pow10", 0, 0, Void, [UInt, BigRef]), M(Big, "Pow2", 0, 0, Void, [UInt, BigRef]),
		M(Big, "SetUInt32", 0, 0, Void, [BigRef, UInt]), M(Big, "SetUInt64", 0, 0, Void, [BigRef, ULong]),
		M(Big, "ShiftLeft", 0x20, 0, Void, [UInt]),
		M(Big, "Clear", 0x20, 0, Void, [UInt]), M(Big, "DivRem32", 0, 0, UInt, [UInt, FrameworkTypeId.ByReference(UInt)]),
		M(Big, "Multiply", 0x20, 0, Void, [UInt]), M(Big, "Multiply", 0, 0, Void, [BigRef, BigRef, BigRef]),
		M(Big, "SetValue", 0, 0, Void, [BigRef, BigRef]), M(Big, "SetZero", 0, 0, Void, [BigRef]),
		M(Big, "ToUInt32", 0x20, 0, UInt, []),
		M(Big, "get_Pow10BigNumTable", 0, 0, Span(UInt, true), []),
		M(Big, "get_Pow10BigNumTableIndices", 0, 0, Span(Int, true), []),
		M(Big, "get_Pow10UInt32Table", 0, 0, Span(UInt, true), []),
		M(N("System.Numerics.BitOperations"), "LeadingZeroCount", 0, 0, Int, [ULong]),
		M(N("System.Numerics.BitOperations"), "LeadingZeroCount", 0, 0, Int, [UInt]),
		M(N("System.Numerics.BitOperations"), "Log2SoftwareFallback", 0, 0, Int, [UInt]),
		M(N("System.Numerics.BitOperations"), "get_Log2DeBruijn", 0, 0, Span(P("Byte"), true), []),
		M(N("System.Numerics.BitOperations"), "Log2", 0, 0, Int, [UInt]),
		M(N("System.Numerics.BitOperations"), "Log2", 0, 0, Int, [ULong])
	];
	internal static bool ContainsHelper(FrameworkMemberId member) => Helpers.Contains(FrameworkImplementationProfile.Canonicalize(member));
	internal static readonly FrameworkMemberId BytePointerConstructor = M(Span(P("Byte"), true), ".ctor", 0x20, 0, Void, [FrameworkTypeId.Pointer(Void), Int]);
	internal static bool IsByteTablePointerCall(FrameworkMemberId member, FrameworkMemberId? caller) =>
		caller?.AssemblyName == "System.Private.CoreLib" && caller.Name == "get_Log2DeBruijn" && ContainsHelper(caller) &&
		FrameworkImplementationProfile.Canonicalize(member).Equals(BytePointerConstructor);
	internal static bool IsBigIntegerCaller(FrameworkMemberId? caller, string name) =>
		caller?.AssemblyName == "System.Private.CoreLib" && caller.Name == name && ContainsHelper(caller) &&
		FrameworkImplementationProfile.Canonicalize(caller).DeclaringType.Equals(Big);
	internal static bool IsCachedDataCall(FrameworkMemberId member, FrameworkMemberId? caller)
	{
		if (caller?.AssemblyName != "System.Private.CoreLib" || !ContainsHelper(caller)) return false;
		var owner = FrameworkImplementationProfile.Canonicalize(caller);
		var element = owner.Name switch {
			"get_CachedPowersBinaryExponent" or "get_CachedPowersDecimalExponent" => P("Int16"),
			"get_CachedPowersSignificand" => ULong,
			"get_SmallPowersOfTen" => UInt,
			"get_Pow10BigNumTable" or "get_Pow10UInt32Table" => UInt,
			"get_Pow10BigNumTableIndices" => Int,
			"get_Log2DeBruijn" => P("Byte"),
			_ => null
		};
		return element is not null && FrameworkImplementationProfile.Canonicalize(member).Equals(
			M(N("System.Runtime.CompilerServices.RuntimeHelpers"), "CreateSpan", 0x10, 1,
				Span(T, true), [N("System.RuntimeFieldHandle")], element));
	}
	internal static readonly IReadOnlyList<FrameworkMemberId> BitProjectionCallers = new[] { "Single", "Double" }.Select(precision =>
		M(N("System." + precision), "System.IBinaryFloatParseAndFormatInfo<System." + precision + ">.FloatToBits", 0, 0, ULong, [P(precision)])).ToArray();
	internal static readonly IReadOnlyList<FrameworkMemberId> BitProjections = [
		M(N("System.BitConverter"), "SingleToUInt32Bits", 0, 0, UInt, [P("Single")]),
		M(N("System.BitConverter"), "DoubleToUInt64Bits", 0, 0, ULong, [P("Double")])
	];
	internal static bool IsBitProjectionCall(FrameworkMemberId member, FrameworkMemberId? caller) =>
		caller?.AssemblyName == "System.Private.CoreLib" && BitProjectionCallers.Any(owner =>
			owner.Equals(FrameworkImplementationProfile.Canonicalize(caller)) &&
			BitProjections.Any(projection => projection.Signature.ParameterTypes.SequenceEqual(owner.Signature.ParameterTypes) &&
				projection.Equals(FrameworkImplementationProfile.Canonicalize(member))));
	internal static bool TryCreateBitProjectionBinding(FrameworkMemberId member, out FrameworkBinding binding)
	{
		binding = null!;
		var canonical = FrameworkImplementationProfile.Canonicalize(member);
		if (!BitProjections.Contains(canonical)) return false;
		var shadow = canonical.Name == "DoubleToUInt64Bits"
			? new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowFloatingPointBits", "DoubleToUInt64") : null;
		binding = new FrameworkBinding(member, shadow is null ? FrameworkBindingKind.Intrinsic : FrameworkBindingKind.ShadowMethod,
			shadow is null ? "intrinsic:runtime-bitcast-32" : $"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
			new FrameworkEffectSummary(FrameworkEffects.None, [FrameworkFeature.Numerics]),
			Reason: "The exact unsigned IEEE projection preserves the target floating payload bits.", ShadowMethod: shadow);
		return true;
	}
	private static readonly FrameworkTypeId V = FrameworkTypeId.GenericTypeParameter(0);
	private static FrameworkTypeId G(string owner, params FrameworkTypeId[] arguments) => FrameworkTypeId.GenericInstantiation(N(owner), arguments);
	internal static readonly IReadOnlyList<FrameworkMemberId> Interfaces = new[] {
		M(G("System.Numerics.INumberBase`1", T), "IsFinite", 0, 0, Bool, [V]),
		M(G("System.Numerics.INumberBase`1", T), "IsNaN", 0, 0, Bool, [V]),
		M(G("System.Numerics.INumberBase`1", T), "IsNegative", 0, 0, Bool, [V]),
		M(G("System.Numerics.IUnaryNegationOperators`2", T, T), "op_UnaryNegation", 0, 0, FrameworkTypeId.GenericTypeParameter(1), [V]),
		M(G("System.Numerics.IEqualityOperators`3", T, T, Bool), "op_Inequality", 0, 0, FrameworkTypeId.GenericTypeParameter(2), [V, FrameworkTypeId.GenericTypeParameter(1)]),
		M(G("System.IBinaryFloatParseAndFormatInfo`1", T), "FloatToBits", 0, 0, ULong, [V]),
		M(G("System.IBinaryFloatParseAndFormatInfo`1", T), "get_DenormalMantissaBits", 0, 0, P("UInt16"), []),
		M(G("System.IBinaryFloatParseAndFormatInfo`1", T), "get_DenormalMantissaMask", 0, 0, ULong, [])
	}.Concat(new[] { "ExponentBias", "InfinityExponent", "MaxPrecisionCustomFormat", "MaxRoundTripDigits", "MinBinaryExponent", "NumberBufferLength" }
		.Select(name => M(G("System.IBinaryFloatParseAndFormatInfo`1", T), "get_" + name, 0, 0, Int, []))).ToArray();
	internal static bool ContainsInterface(FrameworkMemberId member)
	{
		var canonical = FrameworkImplementationProfile.Canonicalize(member);
		if (Interfaces.Contains(canonical)) return true;
		// Member references retain their open signature, but their declaring
		// interface is closed when resolved in a float/double specialization.
		return Interfaces.Any(candidate => new[] { P("Single"), P("Double") }.Any(precision =>
			canonical.Equals(new FrameworkMemberId(
				FrameworkTypeId.GenericInstantiation(candidate.DeclaringType.ElementType!,
					candidate.DeclaringType.GenericArguments.Select(argument => argument.Equals(T) ? precision : argument).ToArray()),
				candidate.Name, candidate.Signature))));
	}
	internal static readonly IReadOnlyList<FrameworkMemberId> ScalarPredicates = new[] { "Single", "Double" }.SelectMany(precision => new[] { "IsFinite", "IsNaN", "IsNegative" }
		.Select(name => M(N("System." + precision), name, 0, 0, Bool, [P(precision)]))).ToArray();
	internal static bool ContainsScalarPredicate(FrameworkMemberId member) => ScalarPredicates.Contains(FrameworkImplementationProfile.Canonicalize(member));
	internal static bool TryCreateScalarPredicateBinding(FrameworkMemberId member, out FrameworkBinding binding)
	{
		binding = null!;
		if (!ContainsScalarPredicate(member)) return false;
		var owner = FrameworkImplementationProfile.Canonicalize(member).DeclaringType.MetadataName;
		var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", owner == "System.Single" ? "CopperSharp.Runtime.ShadowSingle" : "CopperSharp.Runtime.ShadowDouble", member.Name);
		binding = new FrameworkBinding(member, FrameworkBindingKind.ShadowMethod, $"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
			new FrameworkEffectSummary(FrameworkEffects.None, [FrameworkFeature.Numerics]), Reason: "Verified floating predicates use the target's IEEE payload bits.", ShadowMethod: shadow,
			TypeInitializerPolicy: FrameworkTypeInitializerPolicy.TargetOwned);
		return true;
	}
	internal static readonly IReadOnlyList<FrameworkMemberId> Formatters = new[] { P("Single"), P("Double") }.SelectMany(precision => new[] {
		M(N("System.Number"), "FormatFloat", 0x10, 1, Text, [T, Text, Info], precision),
		M(N("System.Number"), "TryFormatFloat", 0x10, 2, Bool, [T, Span(Char, true), Info, Span(C), FrameworkTypeId.ByReference(Int)], precision, Char),
		M(N("System.Number"), "FormatFloat", 0x10, 2, Text, [FrameworkTypeId.ByReference(FrameworkTypeId.GenericInstantiation(N("System.Collections.Generic.ValueListBuilder`1"), [C])), T, Span(Char, true), Info], precision, Char),
		M(FrameworkTypeId.Named("System.Runtime", "Grisu3", N("System.Number")), "TryRun", 0x10, 1, Bool, [T, Int, FrameworkTypeId.ByReference(FrameworkTypeId.Named("System.Runtime", "NumberBuffer", N("System.Number")))], precision),
		M(N("System.Number"), "Dragon4", 0x10, 1, P("Void"), [T, Int, Bool, FrameworkTypeId.ByReference(FrameworkTypeId.Named("System.Runtime", "NumberBuffer", N("System.Number")))], precision),
		M(FrameworkTypeId.Named("System.Runtime", "DiyFp", N("System.Number")), "Create", 0x10, 1, FrameworkTypeId.Named("System.Runtime", "DiyFp", N("System.Number")), [T], precision),
		M(FrameworkTypeId.Named("System.Runtime", "DiyFp", N("System.Number")), "CreateAndGetBoundaries", 0x10, 1, FrameworkTypeId.Named("System.Runtime", "DiyFp", N("System.Number")), [T, FrameworkTypeId.ByReference(FrameworkTypeId.Named("System.Runtime", "DiyFp", N("System.Number"))), FrameworkTypeId.ByReference(FrameworkTypeId.Named("System.Runtime", "DiyFp", N("System.Number")))], precision),
		M(N("System.Number"), "ExtractFractionAndBiasedExponent", 0x10, 1, P("UInt64"), [T, FrameworkTypeId.ByReference(Int)], precision)
	}).ToArray();
	internal static bool IsPublic(FrameworkMemberId member) => PublicMembers.Contains(FrameworkImplementationProfile.Canonicalize(member));
	internal static bool ContainsFormatter(FrameworkMemberId member) => Formatters.Contains(FrameworkImplementationProfile.Canonicalize(member));
	internal static bool IsOwnedCaller(FrameworkMemberId? caller)
	{
		if (caller?.AssemblyName != "System.Private.CoreLib") return false;
		var member = FrameworkImplementationProfile.Canonicalize(caller);
		return IsPublic(member) || ContainsHelper(member) || Formatters.Any(formatter => member.DeclaringType.Equals(formatter.DeclaringType) && member.Name == formatter.Name && member.Signature.Equals(formatter.Signature) &&
			(member.MethodTypeArguments.Length == 0 || member.MethodTypeArguments.SequenceEqual(formatter.MethodTypeArguments)));
	}
}
