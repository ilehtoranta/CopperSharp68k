/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Compiler.Framework;

internal static class StringBuilderEnumerationInterfaces
{
	private static readonly FrameworkTypeId Element = FrameworkTypeId.GenericTypeParameter(0);
	private static FrameworkTypeId Generic(string name, FrameworkTypeId element) => FrameworkTypeId.GenericInstantiation(
		FrameworkTypeId.Named("System.Runtime", "System.Collections.Generic." + name + "`1"), [element]);
	private static FrameworkMemberId Member(FrameworkTypeId owner, string name, FrameworkTypeId result) =>
		new(owner, name, new FrameworkMethodSignatureId(0x20, 0, 0, result, []));
	internal static readonly IReadOnlyList<FrameworkMemberId> Members = [
		Member(FrameworkTypeId.Named("System.Runtime", "System.Collections.IEnumerator"), "MoveNext", FrameworkTypeId.Primitive("System.Boolean")),
		Member(FrameworkTypeId.Named("System.Runtime", "System.IDisposable"), "Dispose", FrameworkTypeId.Primitive("System.Void")),
		..new[] { FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Object"), FrameworkTypeId.Named("System.Runtime", "System.Decimal"),
			FrameworkTypeId.Primitive("System.Single"), FrameworkTypeId.Primitive("System.Double"),
			FrameworkTypeId.Primitive("System.Boolean"), FrameworkTypeId.Primitive("System.Char") }.SelectMany(element => new[] {
			Member(Generic("IEnumerable", element), "GetEnumerator", Generic("IEnumerator", Element)),
			Member(Generic("IEnumerator", element), "get_Current", Element) })
	];
	internal static bool Contains(FrameworkMemberId member) => Members.Contains(FrameworkImplementationProfile.Canonicalize(member));
}
