/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Compiler.Framework;

internal static class StringBuilderFormattingInterfaces
{
	private static readonly FrameworkTypeId Text = FrameworkTypeId.Primitive("System.String");
	private static readonly FrameworkTypeId Object = FrameworkTypeId.Primitive("System.Object");
	private static readonly FrameworkTypeId Integer = FrameworkTypeId.Primitive("System.Int32");
	private static readonly FrameworkTypeId Provider = FrameworkTypeId.Named("System.Runtime", "System.IFormatProvider");
	internal static readonly IReadOnlyList<FrameworkMemberId> Members = [
		Member("System.IFormatProvider", "GetFormat", Object, [FrameworkTypeId.Named("System.Runtime", "System.Type")]),
		Member("System.ICustomFormatter", "Format", Text, [Text, Object, Provider]),
		Member("System.IFormattable", "ToString", Text, [Text, Provider]),
		Member("System.ISpanFormattable", "TryFormat", FrameworkTypeId.Primitive("System.Boolean"), [
			FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Span`1"), [FrameworkTypeId.Primitive("System.Char")]),
			FrameworkTypeId.ByReference(Integer),
			FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Char")]), Provider])
	];
	internal static bool Contains(FrameworkMemberId member) => Members.Contains(FrameworkImplementationProfile.Canonicalize(member));
	private static FrameworkMemberId Member(string owner, string name, FrameworkTypeId result, FrameworkTypeId[] parameters) =>
		new(FrameworkTypeId.Named("System.Runtime", owner), name, new FrameworkMethodSignatureId(0x20, 0, parameters.Length, result, parameters));
}
