/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Compiler.Framework;

internal static class StringBuilderInterpolatedHandlerSurface
{
	private static FrameworkTypeId P(string name) => FrameworkTypeId.Primitive("System." + name);
	private static FrameworkTypeId N(string name) => FrameworkTypeId.Named("System.Runtime", name);
	private static readonly FrameworkTypeId Owner = N("System.Runtime.CompilerServices.DefaultInterpolatedStringHandler");
	private static readonly FrameworkTypeId Text = P("String"), Integer = P("Int32"), Void = P("Void"), Value = FrameworkTypeId.GenericMethodParameter(0);
	private static readonly FrameworkTypeId Provider = N("System.IFormatProvider");
	internal static readonly IReadOnlyList<FrameworkMemberId> Members = [
		M(".ctor", 0x20, 0, Void, [Integer, Integer, Provider, Span(false)]),
		M("AppendFormatted", 0x30, 1, Void, [Value, Text]),
		M("AppendCustomFormatter", 0x30, 1, Void, [Value, Text]),
		M("AppendLiteral", 0x20, 0, Void, [Text]),
		M("Clear", 0x20, 0, Void, []),
		M("get_Text", 0x20, 0, Span(true), []),
		M("Grow", 0x20, 0, Void, []),
		M("Grow", 0x20, 0, Void, [Integer]),
		M("GrowCore", 0x20, 0, Void, [P("UInt32")]),
		M("GrowThenCopyString", 0x20, 0, Void, [Text]),
		M("HasCustomFormatter", 0, 0, P("Boolean"), [Provider])
	];
	private static FrameworkTypeId Span(bool readOnly) => FrameworkTypeId.GenericInstantiation(N(readOnly ? "System.ReadOnlySpan`1" : "System.Span`1"), [P("Char")]);
	private static FrameworkMemberId M(string name, byte header, int arity, FrameworkTypeId result, FrameworkTypeId[] parameters) =>
		new(Owner, name, new FrameworkMethodSignatureId(header, arity, parameters.Length, result, parameters));
	internal static bool Contains(FrameworkMemberId member)
	{
		member = FrameworkImplementationProfile.Canonicalize(member);
		return Members.Any(candidate => candidate.DeclaringType.Equals(member.DeclaringType) && candidate.Name == member.Name &&
			candidate.Signature.Equals(member.Signature) && (member.MethodTypeArguments.Length == 0 || member.MethodTypeArguments.Length == candidate.Signature.GenericParameterCount));
	}
	internal static bool IsOwnedCaller(FrameworkMemberId? caller) => caller?.AssemblyName == "System.Private.CoreLib" && Contains(caller);
}
