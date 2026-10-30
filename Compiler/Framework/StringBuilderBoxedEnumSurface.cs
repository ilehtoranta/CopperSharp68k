/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Compiler.Framework;

internal static class StringBuilderBoxedEnumSurface
{
	private static FrameworkTypeId P(string name) => FrameworkTypeId.Primitive("System." + name);
	private static readonly FrameworkTypeId Provider = FrameworkTypeId.Named("System.Runtime", "System.IFormatProvider");
	private static FrameworkTypeId Span(string name) => FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", name), [P("Char")]);
	private static FrameworkMemberId M(string name, FrameworkTypeId result, params FrameworkTypeId[] parameters) =>
		new(FrameworkTypeId.Named("System.Runtime", "System.Enum"), name, new FrameworkMethodSignatureId(0x20, 0, parameters.Length, result, parameters));
	internal static readonly IReadOnlyList<FrameworkMemberId> Members = [
		M("ToString", P("String")), M("ToString", P("String"), P("String")), M("ToString", P("String"), Provider),
		M("ToString", P("String"), P("String"), Provider),
		M("System.ISpanFormattable.TryFormat", P("Boolean"), Span("System.Span`1"), FrameworkTypeId.ByReference(P("Int32")), Span("System.ReadOnlySpan`1"), Provider)
	];
	internal static bool Contains(FrameworkMemberId member) => Members.Contains(FrameworkImplementationProfile.Canonicalize(member));
	private static readonly FrameworkTypeId Metadata = FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowEnumMetadata");
	private static FrameworkMemberId R(string name, FrameworkTypeId result, params FrameworkTypeId[] parameters) =>
		new(FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowEnumFormatting"), name, new FrameworkMethodSignatureId(0, 0, parameters.Length, result, parameters));
	internal static readonly IReadOnlyList<FrameworkMemberId> RuntimeCallers = [
		R("TryFormatBits", P("Boolean"), P("UInt64"), Metadata, Span("System.Span`1"), FrameworkTypeId.ByReference(P("Int32")), Span("System.ReadOnlySpan`1")),
		R("FormatBits", P("String"), P("UInt64"), Metadata, P("String")),
		R("TryWriteName", P("Boolean"), P("String"), Span("System.Span`1"), FrameworkTypeId.ByReference(P("Int32")))
	];
	internal static bool IsRuntimeCaller(FrameworkMemberId? member) => member?.AssemblyName == "CopperSharp.Runtime.Managed" && RuntimeCallers.Contains(FrameworkImplementationProfile.Canonicalize(member));
}
