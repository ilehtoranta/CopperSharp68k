/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Compiler.Framework;

internal static class StringBuilderConcatenationSurface
{
	private static readonly FrameworkTypeId Text = FrameworkTypeId.Primitive("System.String");
	internal static readonly IReadOnlyList<FrameworkMemberId> Members = new[] { 3, 4 }.Select(count =>
		new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.String"), "Concat",
			new FrameworkMethodSignatureId(0, 0, count, Text, Enumerable.Repeat(Text, count).ToArray()))).ToArray();
	internal static bool Contains(FrameworkMemberId member) => Members.Contains(FrameworkImplementationProfile.Canonicalize(member));
	internal static readonly IReadOnlyList<FrameworkMemberId> Helpers = [
		new(FrameworkTypeId.Named("System.Runtime", "System.String"), "CopyStringContent",
			new FrameworkMethodSignatureId(0, 0, 3, FrameworkTypeId.Primitive("System.Void"), [Text, FrameworkTypeId.Primitive("System.Int32"), Text])),
		new(FrameworkTypeId.Named("System.Runtime", "System.String"), "IsNullOrEmpty",
			new FrameworkMethodSignatureId(0, 0, 1, FrameworkTypeId.Primitive("System.Boolean"), [Text])),
		new(FrameworkTypeId.Named("System.Runtime", "System.ThrowHelper"), "ThrowOutOfMemoryException_StringTooLong",
			new FrameworkMethodSignatureId(0, 0, 0, FrameworkTypeId.Primitive("System.Void"), [])),
		new(FrameworkTypeId.Named("System.Runtime", "System.OutOfMemoryException"), ".ctor",
			new FrameworkMethodSignatureId(0x20, 0, 1, FrameworkTypeId.Primitive("System.Void"), [Text])),
		new(FrameworkTypeId.Named("System.Runtime", "System.SR"), "get_OutOfMemory_StringTooLong",
			new FrameworkMethodSignatureId(0, 0, 0, Text, []))
	];
	internal static bool ContainsHelper(FrameworkMemberId member) => Helpers.Contains(FrameworkImplementationProfile.Canonicalize(member));
	internal static bool IsOwnedCaller(FrameworkMemberId? caller) => caller?.AssemblyName == "System.Private.CoreLib" && (Contains(caller) || ContainsHelper(caller));
	internal static bool IsMemoryCaller(FrameworkMemberId? caller) => caller?.AssemblyName == "System.Private.CoreLib" &&
		(Contains(caller) || caller.Name == "CopyStringContent" && ContainsHelper(caller));
}
