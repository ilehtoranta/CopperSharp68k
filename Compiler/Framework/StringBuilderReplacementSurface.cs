/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Compiler.Framework;

internal static class StringBuilderReplacementSurface
{
	private static readonly FrameworkTypeId Integer = FrameworkTypeId.Primitive("System.Int32");
	private static readonly FrameworkTypeId Element = FrameworkTypeId.GenericTypeParameter(0);
	private static readonly FrameworkTypeId Void = FrameworkTypeId.Primitive("System.Void");
	private static readonly FrameworkTypeId List = FrameworkTypeId.GenericInstantiation(
		FrameworkTypeId.Named("System.Runtime", "System.Collections.Generic.ValueListBuilder`1"), [Integer]);
	private static FrameworkTypeId Span(bool readOnly) => FrameworkTypeId.GenericInstantiation(
		FrameworkTypeId.Named("System.Runtime", readOnly ? "System.ReadOnlySpan`1" : "System.Span`1"), [Element]);
	internal static readonly IReadOnlyList<FrameworkMemberId> Members = [
		Method(".ctor", Void, [Span(false)]), Method("Append", Void, [Element]), Method("AddWithResize", Void, [Element]),
		Method("Grow", Void, [Integer]), Method("Dispose", Void, []), Method("AsSpan", Span(true), []),
		Method("get_Length", Integer, []), Method("set_Length", Void, [Integer])
	];
	private static FrameworkMemberId Method(string name, FrameworkTypeId result, FrameworkTypeId[] parameters) =>
		new(List, name, new FrameworkMethodSignatureId(0x20, 0, parameters.Length, result, parameters));
	internal static bool Contains(FrameworkMemberId member) => Members.Contains(FrameworkImplementationProfile.Canonicalize(member));
	internal static bool IsOwnedCaller(FrameworkMemberId? caller) => caller?.AssemblyName == "System.Private.CoreLib" &&
		caller.MethodTypeArguments.Length == 0 && Members.Any(member => member.DeclaringType.ElementType!.Equals(
			FrameworkImplementationProfile.Canonicalize(caller).DeclaringType) && member.Name == caller.Name &&
			member.Signature.Equals(FrameworkImplementationProfile.Canonicalize(caller).Signature));
}
