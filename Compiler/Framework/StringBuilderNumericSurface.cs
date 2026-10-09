/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Compiler.Framework;

internal static class StringBuilderNumericSurface
{
	private static FrameworkTypeId P(string name) => FrameworkTypeId.Primitive("System." + name);
	private static FrameworkTypeId N(string name) => FrameworkTypeId.Named("System.Runtime", name);
	private static readonly FrameworkTypeId Void = P("Void"), Int = P("Int32"), Char = P("Char"), Bool = P("Boolean"), Text = P("String");
	private static readonly FrameworkTypeId T = FrameworkTypeId.GenericMethodParameter(0), V = FrameworkTypeId.GenericTypeParameter(0);
	private static FrameworkTypeId Span(FrameworkTypeId type, bool readOnly = false) => FrameworkTypeId.GenericInstantiation(N(readOnly ? "System.ReadOnlySpan`1" : "System.Span`1"), [type]);
	private static FrameworkTypeId List(FrameworkTypeId type) => FrameworkTypeId.GenericInstantiation(N("System.Collections.Generic.ValueListBuilder`1"), [type]);
	private static readonly FrameworkTypeId Info = N("System.Globalization.NumberFormatInfo");
	private static readonly FrameworkTypeId Buffer = FrameworkTypeId.Named("System.Runtime", "NumberBuffer", N("System.Number"));
	internal static readonly IReadOnlyList<FrameworkMemberId> Members = [
		M(N("System.Number"), "TryCopyTo", 0x10, 1, Bool, [Text, Span(T), FrameworkTypeId.ByReference(Int)], [Char]),
		M(Span(Char), "TryCopyTo", 0x20, 0, Bool, [Span(V)]),
		M(List(Char), ".ctor", 0x20, 0, Void, [Span(V)]),
		M(List(Char), "AsSpan", 0x20, 0, Span(V, true), []),
		M(List(Char), "TryCopyTo", 0x20, 0, Bool, [Span(V), FrameworkTypeId.ByReference(Int)]),
		M(N("System.Number"), "ParseFormatSpecifier", 0, 0, Char, [Span(Char, true), FrameworkTypeId.ByReference(Int)]),
		M(N("System.Number"), "CurrencyGroupSizes", 0, 0, FrameworkTypeId.SzArray(Int), [Info]),
		M(N("System.Number"), "PercentGroupSizes", 0, 0, FrameworkTypeId.SzArray(Int), [Info]),
		M(List(Char), "AppendSpan", 0x20, 0, Span(V), [Int]),
		M(List(Char), "AppendSpanWithGrow", 0x20, 0, Span(V), [Int]),
		M(FrameworkTypeId.GenericInstantiation(N("System.IUtfChar`1"), [Char]), "CastFrom", 0, 0, V, [P("Byte")]),
		M(N("System.Number"), "NumberToString", 0x10, 1, Void, [FrameworkTypeId.ByReference(List(T)), FrameworkTypeId.ByReference(Buffer), Char, Int, Info], [Char]),
		M(N("System.Number"), "FormatCurrency", 0x10, 1, Void, [FrameworkTypeId.ByReference(List(T)), FrameworkTypeId.ByReference(Buffer), Int, Info], [Char]),
		M(N("System.Number"), "FormatNumber", 0x10, 1, Void, [FrameworkTypeId.ByReference(List(T)), FrameworkTypeId.ByReference(Buffer), Int, Info], [Char]),
		M(N("System.Number"), "FormatPercent", 0x10, 1, Void, [FrameworkTypeId.ByReference(List(T)), FrameworkTypeId.ByReference(Buffer), Int, Info], [Char]),
		M(N("System.Number"), "FormatFixed", 0x10, 1, Void, [FrameworkTypeId.ByReference(List(T)), FrameworkTypeId.ByReference(Buffer), Int, FrameworkTypeId.SzArray(Int), Span(T, true), Span(T, true)], [Char]),
		M(N("System.Number"), "FormatGeneral", 0x10, 1, Void, [FrameworkTypeId.ByReference(List(T)), FrameworkTypeId.ByReference(Buffer), Int, Info, Char, Bool], [Char]),
		M(N("System.Number"), "FormatScientific", 0x10, 1, Void, [FrameworkTypeId.ByReference(List(T)), FrameworkTypeId.ByReference(Buffer), Int, Info, Char], [Char]),
		M(N("System.Char"), "IsAsciiDigit", 0, 0, Bool, [Char]),
		M(N("System.Char"), "IsAsciiLetter", 0, 0, Bool, [Char]),
		M(N("System.Number"), "NumberToStringFormat", 0x10, 1, Void, [FrameworkTypeId.ByReference(List(T)), FrameworkTypeId.ByReference(Buffer), Span(Char, true), Info], [Char]),
		M(N("System.Number"), "RoundNumber", 0, 0, Void, [FrameworkTypeId.ByReference(Buffer), Int, Bool]),
		M(N("System.Number"), "FindSection", 0, 0, Int, [Span(Char, true), Int]),
		M(N("System.Number"), "NumberGroupSizes", 0, 0, FrameworkTypeId.SzArray(Int), [Info]),
		M(N("System.Number"), "AppendUnknownChar", 0x10, 1, Void, [FrameworkTypeId.ByReference(List(T)), Char], [Char]),
		M(N("System.Number"), "FormatExponent", 0x10, 1, Void, [FrameworkTypeId.ByReference(List(T)), Info, Int, Char, Int, Bool], [Char]),
		M(Buffer, "get_DigitsPtr", 0x20, 0, FrameworkTypeId.Pointer(P("Byte")), []),
		M(List(Char), "Append", 0x20, 0, Void, [V]),
		M(List(Char), "Append", 0x20, 0, Void, [Span(V, true)]),
		M(List(Char), "Insert", 0x20, 0, Void, [Int, Span(V, true)]),
		M(List(Char), "get_Length", 0x20, 0, Int, []),
		M(List(Char), "AddWithResize", 0x20, 0, Void, [V]),
		M(List(Char), "AppendMultiChar", 0x20, 0, Void, [Span(V, true)]),
		M(List(Char), "Grow", 0x20, 0, Void, [Int]),
		M(List(Char), "Dispose", 0x20, 0, Void, []),
		M(N("System.Number"), "UInt32ToDecChars", 0x10, 1, FrameworkTypeId.Pointer(T), [FrameworkTypeId.Pointer(T), P("UInt32"), Int], [Char]),
		M(N("System.Number"), "<RoundNumber>g__ShouldRoundUp|162_0", 0, 0, Bool, [FrameworkTypeId.Pointer(P("Byte")), Int,
			FrameworkTypeId.Named("System.Runtime", "NumberBufferKind", N("System.Number")), Bool]),
		M(N("System.Char"), "IsAscii", 0, 0, Bool, [Char]),
		M(FrameworkTypeId.GenericInstantiation(N("System.IUtfChar`1"), [Char]), "CastFrom", 0, 0, V, [Char]),
		M(FrameworkTypeId.GenericInstantiation(N("System.IUtfChar`1"), [Char]), "CastFrom", 0, 0, V, [P("UInt32")]),
		M(Span(Char, true), "TryCopyTo", 0x20, 0, Bool, [Span(V)]),
		M(Span(Char, true), ".ctor", 0x20, 0, Void, [FrameworkTypeId.Pointer(Void), Int]),
		M(N("System.Math"), "DivRem", 0, 0, FrameworkTypeId.GenericInstantiation(N("System.ValueTuple`2"), [P("UInt32"), P("UInt32")]), [P("UInt32"), P("UInt32")]),
		M(FrameworkTypeId.GenericInstantiation(N("System.ValueTuple`2"), [P("UInt32"), P("UInt32")]), ".ctor", 0x20, 0, Void, [V, FrameworkTypeId.GenericTypeParameter(1)])
	];
	internal static bool Contains(FrameworkMemberId member)
	{
		var canonical = FrameworkImplementationProfile.Canonicalize(member);
		if (canonical.DeclaringType.Equals(FrameworkTypeId.GenericInstantiation(N("System.IUtfChar`1"), [T])))
			canonical = new FrameworkMemberId(FrameworkTypeId.GenericInstantiation(N("System.IUtfChar`1"), [Char]), canonical.Name, canonical.Signature, canonical.MethodTypeArguments);
		if (canonical.DeclaringType.Equals(List(T))) canonical = new FrameworkMemberId(List(Char), canonical.Name, canonical.Signature, canonical.MethodTypeArguments);
		if (canonical.MethodTypeArguments.SequenceEqual(new[] { T }) || canonical.Name == "TryCopyTo" && canonical.MethodTypeArguments.SequenceEqual(new[] { FrameworkTypeId.GenericMethodParameter(1) }))
			canonical = new FrameworkMemberId(canonical.DeclaringType, canonical.Name, canonical.Signature, [Char]);
		return Members.Contains(canonical);
	}
	internal static bool IsOwnedCaller(FrameworkMemberId? caller) => caller?.AssemblyName == "System.Private.CoreLib" && IsDefinition(caller) || IsRuntimeCaller(caller) || StringBuilderFloatingSurface.IsOwnedCaller(caller);
	internal static bool IsDefinition(FrameworkMemberId caller)
	{
		var canonical = FrameworkImplementationProfile.Canonicalize(caller);
		return canonical.MethodTypeArguments.Length == 0 && Members.Any(member =>
			(member.DeclaringType.Kind == FrameworkTypeKind.GenericInstantiation ? member.DeclaringType.ElementType! : member.DeclaringType).Equals(canonical.DeclaringType) &&
			member.Name == canonical.Name && member.Signature.Equals(canonical.Signature));
	}
	internal static bool IsRuntimeCaller(FrameworkMemberId? caller)
	{
		if (StringBuilderDecimalSurface.IsRuntimeCaller(caller)) return true;
		if (caller?.AssemblyName != "CopperSharp.Runtime.Managed") return false;
		var owner = caller.DeclaringType.FullMetadataName;
		var member = FrameworkImplementationProfile.Canonicalize(caller);
		if (owner == "CopperSharp.Runtime.ShadowCustomNumberFormatting")
			return member.Equals(M(caller.DeclaringType, "ValidateCustomFormat", 0, 0, Void, [Span(Char, true)])) ||
				member.Equals(M(caller.DeclaringType, "TryFormatMagnitude", 0, 0, Bool, [P("UInt64"), Bool, Span(Char, true), Info, Span(Char), FrameworkTypeId.ByReference(Int)])) ||
				member.Equals(M(caller.DeclaringType, "FormatMagnitude", 0, 0, Text, [P("UInt64"), Bool, Span(Char, true), Info]));
		if (owner == "CopperSharp.Runtime.ShadowStandardIntegerFormatting")
			return member.Equals(M(caller.DeclaringType, "Parse", 0, 0, Char, [Text, FrameworkTypeId.ByReference(Int)])) ||
				member.Equals(M(caller.DeclaringType, "Parse", 0, 0, Char, [Span(Char, true), FrameworkTypeId.ByReference(Int)]));
		if (owner == "CopperSharp.Runtime.ShadowGroupedIntegerFormatting")
			return member.Equals(M(caller.DeclaringType, "FormatCore", 0, 0, Text,
				[P("UInt32"), P("UInt32"), Bool, Int, Int, Text, Text, Text, Text, Text, FrameworkTypeId.SzArray(Int)]));
		return false;
	}
	private static FrameworkMemberId M(FrameworkTypeId owner, string name, byte header, int arity, FrameworkTypeId result,
		FrameworkTypeId[] parameters, FrameworkTypeId[]? arguments = null) =>
		new(owner, name, new FrameworkMethodSignatureId(header, arity, parameters.Length, result, parameters), arguments);
}
