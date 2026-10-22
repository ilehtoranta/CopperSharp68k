/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Compiler.Framework;

internal static class StringBuilderDecimalListSurface
{
	private static FrameworkTypeId P(string name) => FrameworkTypeId.Primitive("System." + name);
	internal static readonly FrameworkTypeId Decimal = FrameworkTypeId.Named("System.Runtime", "System.Decimal");
	private static readonly FrameworkTypeId Element = FrameworkTypeId.GenericTypeParameter(0);
	internal static readonly FrameworkTypeId List = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Collections.Generic.List`1"), [Decimal]);
	internal static readonly FrameworkTypeId Enumerator = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "Enumerator", List.ElementType), [Decimal]);
	private static readonly FrameworkTypeId OpenEnumerator = FrameworkTypeId.GenericInstantiation(Enumerator.ElementType!, [Element]);
	private static FrameworkMemberId L(string name, FrameworkTypeId result, params FrameworkTypeId[] parameters) => M(List, name, result, parameters);
	private static FrameworkMemberId E(string name, FrameworkTypeId result, params FrameworkTypeId[] parameters) => M(Enumerator, name, result, parameters);
	private static FrameworkMemberId M(FrameworkTypeId owner, string name, FrameworkTypeId result, FrameworkTypeId[] parameters) =>
		new(owner, name, new FrameworkMethodSignatureId(0x20, 0, parameters.Length, result, parameters));
	internal static readonly IReadOnlyList<FrameworkMemberId> PublicMembers = [
		L(".ctor", P("Void")), L(".ctor", P("Void"), P("Int32")), L("Add", P("Void"), Element), L("Clear", P("Void")),
		L("set_Item", P("Void"), P("Int32"), Element), L("GetEnumerator", OpenEnumerator),
		E("MoveNext", P("Boolean")), E("get_Current", Element), E("Dispose", P("Void"))
	];
	internal static readonly IReadOnlyList<FrameworkMemberId> Helpers = [
		L("AddWithResize", P("Void"), Element), L("Grow", P("Void"), P("Int32")), L("GetNewCapacity", P("Int32"), P("Int32")),
		L("get_Capacity", P("Int32")), L("set_Capacity", P("Void"), P("Int32")),
		L("get_Count", P("Int32")),
		L("System.Collections.Generic.IEnumerable<T>.GetEnumerator", FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Collections.Generic.IEnumerator`1"), [Element])),
		L("System.Collections.IEnumerable.GetEnumerator", FrameworkTypeId.Named("System.Runtime", "System.Collections.IEnumerator")),
		E("System.Collections.IEnumerator.Reset", P("Void")), E("System.Collections.IEnumerator.get_Current", P("Object")),
		E(".ctor", P("Void"), FrameworkTypeId.GenericInstantiation(List.ElementType!, [Element]))
	];
	private static FrameworkMemberId Canonical(FrameworkMemberId member)
	{
		member = FrameworkImplementationProfile.Canonicalize(member);
		if (member.AssemblyName != "System.Collections") return member;
		return new FrameworkMemberId(Normalize(member.DeclaringType), member.Name,
			new FrameworkMethodSignatureId(member.Signature.Header, member.Signature.GenericParameterCount, member.Signature.RequiredParameterCount,
				Normalize(member.Signature.ReturnType), member.Signature.ParameterTypes.Select(Normalize).ToArray()), member.MethodTypeArguments);
	}
	private static FrameworkTypeId Normalize(FrameworkTypeId type) => type.Kind == FrameworkTypeKind.GenericInstantiation
		? FrameworkTypeId.GenericInstantiation(Normalize(type.ElementType!), type.GenericArguments.Select(Normalize).ToArray())
		: type.Kind == FrameworkTypeKind.Named && type.AssemblyName == "System.Collections" &&
			type.FullMetadataName is "System.Collections.Generic.List`1" or "System.Collections.Generic.List`1+Enumerator"
				? FrameworkTypeId.Named("System.Runtime", type.MetadataName!, type.DeclaringType is null ? null : Normalize(type.DeclaringType)) : type;
	internal static bool IsPublic(FrameworkMemberId member) => PublicMembers.Contains(Canonical(member));
	internal static bool IsHelper(FrameworkMemberId member)
	{
		var canonical = Canonical(member);
		if (Helpers.Contains(canonical)) return true;
		return canonical.DeclaringType.Kind == FrameworkTypeKind.GenericInstantiation && canonical.DeclaringType.GenericArguments.SequenceEqual([Element]) &&
			Helpers.Concat(PublicMembers).Any(candidate => candidate.DeclaringType.ElementType!.Equals(canonical.DeclaringType.ElementType) &&
				candidate.Name == canonical.Name && candidate.Signature.Equals(canonical.Signature) && canonical.MethodTypeArguments.Length == 0);
	}
	internal static bool IsOwnedCaller(FrameworkMemberId? caller) => caller?.AssemblyName == "System.Private.CoreLib" &&
		(PublicMembers.Contains(Canonical(caller)) || Helpers.Contains(Canonical(caller)));
	internal static readonly IReadOnlyList<FrameworkMemberId> ExceptionHelpers = [
		new(FrameworkTypeId.Named("System.Runtime", "System.ThrowHelper"), "ThrowArgumentOutOfRange_IndexMustBeLessException", new FrameworkMethodSignatureId(0, 0, 0, P("Void"), [])),
		new(FrameworkTypeId.Named("System.Runtime", "System.ThrowHelper"), "ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion", new FrameworkMethodSignatureId(0, 0, 0, P("Void"), [])),
		new(FrameworkTypeId.Named("System.Runtime", "System.ThrowHelper"), "ThrowInvalidOperationException_EnumCurrent", new FrameworkMethodSignatureId(0, 0, 1, P("Void"), [P("Int32")])),
		new(FrameworkTypeId.Named("System.Runtime", "System.ThrowHelper"), "GetInvalidOperationException_EnumCurrent", new FrameworkMethodSignatureId(0, 0, 1, FrameworkTypeId.Named("System.Runtime", "System.InvalidOperationException"), [P("Int32")]))
	];
	internal static bool IsExceptionHelper(FrameworkMemberId member) => ExceptionHelpers.Contains(FrameworkImplementationProfile.Canonicalize(member));
	internal static bool IsExceptionCaller(FrameworkMemberId? caller) => caller?.AssemblyName == "System.Private.CoreLib" && IsExceptionHelper(caller);
	internal static readonly FrameworkEffectSummary Effects = new(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
		FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
		[FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedArrays, FrameworkFeature.ManagedGc, FrameworkFeature.ManagedExceptions]);
}
