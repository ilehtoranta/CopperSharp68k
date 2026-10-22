/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Compiler.Framework;

internal static class StringBuilderCompositeSurface
{
	private static FrameworkTypeId P(string name) => FrameworkTypeId.Primitive("System." + name);
	private static FrameworkTypeId N(string name) => FrameworkTypeId.Named("System.Runtime", name);
	private static readonly FrameworkTypeId Owner = N("System.Text.CompositeFormat");
	private static readonly FrameworkTypeId Text = P("String"), Integer = P("Int32"), Void = P("Void"), Boolean = P("Boolean");
	internal static readonly FrameworkTypeId Segment = FrameworkTypeId.GenericInstantiation(N("System.ValueTuple`4"), [Text, Integer, Integer, Text]);
	private static readonly FrameworkTypeId Span = FrameworkTypeId.GenericInstantiation(N("System.ReadOnlySpan`1"), [P("Char")]);
	internal static readonly FrameworkTypeId List = FrameworkTypeId.GenericInstantiation(N("System.Collections.Generic.List`1"), [Segment]);
	internal static readonly IReadOnlyList<FrameworkMemberId> Members = [
		M(".ctor", 0x20, Void, [Text, FrameworkTypeId.SzArray(Segment)]),
		M("Parse", 0, Owner, [Text]), M("get_Format", 0x20, Text, []), M("get_MinimumArgumentCount", 0x20, Integer, []),
		M("ValidateNumberOfArgs", 0x20, Void, [Integer]),
		M("TryParseLiterals", 0, Boolean, [Span, List, FrameworkTypeId.ByReference(Integer), FrameworkTypeId.ByReference(N("System.ExceptionResource"))]),
		M("<TryParseLiterals>g__TryMoveNext|12_0", 0, Boolean, [Span, FrameworkTypeId.ByReference(Integer), FrameworkTypeId.ByReference(P("Char"))])
	];
	private static FrameworkMemberId M(string name, byte header, FrameworkTypeId result, FrameworkTypeId[] parameters) =>
		new(Owner, name, new FrameworkMethodSignatureId(header, 0, parameters.Length, result, parameters));
	private static readonly FrameworkTypeId Element = FrameworkTypeId.GenericTypeParameter(0);
	internal static readonly IReadOnlyList<FrameworkMemberId> ListMembers = [
		L(".ctor", Void, []), L("Add", Void, [Element]), L("AddWithResize", Void, [Element]),
		L("ToArray", FrameworkTypeId.SzArray(Element), []), L("Grow", Void, [Integer]),
		L("get_Capacity", Integer, []), L("set_Capacity", Void, [Integer]), L("GetNewCapacity", Integer, [Integer])
	];
	private static FrameworkMemberId L(string name, FrameworkTypeId result, FrameworkTypeId[] parameters) =>
		new(List, name, new FrameworkMethodSignatureId(0x20, 0, parameters.Length, result, parameters));
	internal static readonly IReadOnlyList<FrameworkMemberId> BufferMembers = [
		B(".ctor", Void, [FrameworkTypeId.GenericInstantiation(N("System.Span`1"), [P("Char")])]),
		B("Append", Void, [Span]), B("Append", Void, [P("Char")]), B("GrowAndAppend", Void, [P("Char")]),
		B("Grow", Void, [Integer]), B("Dispose", Void, []), B("ToString", Text, []), B("set_Length", Void, [Integer])
	];
	private static FrameworkMemberId B(string name, FrameworkTypeId result, FrameworkTypeId[] parameters) =>
		new(N("System.Text.ValueStringBuilder"), name, new FrameworkMethodSignatureId(0x20, 0, parameters.Length, result, parameters));
	internal static readonly FrameworkMemberId SegmentConstructor = new(Segment, ".ctor", new FrameworkMethodSignatureId(0x20, 0, 4, Void,
		[FrameworkTypeId.GenericTypeParameter(0), FrameworkTypeId.GenericTypeParameter(1), FrameworkTypeId.GenericTypeParameter(2), FrameworkTypeId.GenericTypeParameter(3)]));
	internal static readonly FrameworkMemberId SpanToString = new(Span, "ToString", new FrameworkMethodSignatureId(0x20, 0, 0, Text, []));
	internal static readonly FrameworkMemberId MutableSpanToString = new(FrameworkTypeId.GenericInstantiation(N("System.Span`1"), [P("Char")]),
		"ToString", new FrameworkMethodSignatureId(0x20, 0, 0, Text, []));
	internal static readonly FrameworkMemberId SpanTryCopyTo = new(Span, "TryCopyTo", new FrameworkMethodSignatureId(0x20, 0, 1, Boolean,
		[FrameworkTypeId.GenericInstantiation(N("System.Span`1"), [Element])]));
	internal static bool IsHelper(FrameworkMemberId member) => ListMembers.Contains(FrameworkImplementationProfile.Canonicalize(member)) ||
		BufferMembers.Contains(FrameworkImplementationProfile.Canonicalize(member)) || SegmentConstructor.Equals(FrameworkImplementationProfile.Canonicalize(member)) ||
		SpanToString.Equals(FrameworkImplementationProfile.Canonicalize(member)) || MutableSpanToString.Equals(FrameworkImplementationProfile.Canonicalize(member)) || IsOpenListMember(FrameworkImplementationProfile.Canonicalize(member));
	private static bool IsOpenListMember(FrameworkMemberId member) => member.DeclaringType.Equals(FrameworkTypeId.GenericInstantiation(List.ElementType!, [Element])) &&
		ListMembers.Any(candidate => candidate.Name == member.Name && candidate.Signature.Equals(member.Signature) && member.MethodTypeArguments.Length == 0);
	internal static bool IsHelperCaller(FrameworkMemberId? caller)
	{
		if (caller?.AssemblyName != "System.Private.CoreLib") return false;
		var canonical = FrameworkImplementationProfile.Canonicalize(caller);
		return BufferMembers.Contains(canonical) || ListMembers.Contains(canonical) || SpanToString.Equals(canonical) || MutableSpanToString.Equals(canonical) || SpanTryCopyTo.Equals(canonical);
	}
	internal static bool Contains(FrameworkMemberId member) => Members.Contains(FrameworkImplementationProfile.Canonicalize(member));
	internal static bool IsPublic(FrameworkMemberId member) => Contains(member) && member.Name is "Parse" or "get_Format" or "get_MinimumArgumentCount";
	internal static bool IsOwnedCaller(FrameworkMemberId? caller) => caller?.AssemblyName == "System.Private.CoreLib" && Contains(caller);
	internal static readonly FrameworkEffectSummary Effects = new(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
		FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
		[FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedArrays, FrameworkFeature.ManagedStrings, FrameworkFeature.Spans, FrameworkFeature.ManagedGc, FrameworkFeature.StringBuilder]);
}
