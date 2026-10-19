/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Compiler.Framework;

internal static class StringBuilderObjectBufferSurface
{
	private static readonly FrameworkTypeId Object = FrameworkTypeId.Primitive("System.Object");
	private static readonly FrameworkTypeId Integer = FrameworkTypeId.Primitive("System.Int32");
	private static readonly FrameworkTypeId First = FrameworkTypeId.GenericMethodParameter(0);
	private static readonly FrameworkTypeId Second = FrameworkTypeId.GenericMethodParameter(1);
	internal static readonly IReadOnlyList<FrameworkMemberId> Constructors = new[] { 2, 3 }.Select(length =>
		new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", length == 2 ? "System.TwoObjects" : "System.ThreeObjects"), ".ctor",
			new FrameworkMethodSignatureId(0x20, 0, length, FrameworkTypeId.Primitive("System.Void"), Enumerable.Repeat(Object, length).ToArray()))).ToArray();
	internal static readonly IReadOnlyList<FrameworkMemberId> Helpers = Constructors.SelectMany(constructor => new[] {
		Helper("InlineArrayAsReadOnlySpan", FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [Second]), [FrameworkTypeId.ByReference(First), Integer], constructor.DeclaringType),
		Helper("InlineArrayElementRef", FrameworkTypeId.ByReference(Second), [FrameworkTypeId.ByReference(First), Integer], constructor.DeclaringType),
		Helper("InlineArrayFirstElementRef", FrameworkTypeId.ByReference(Second), [FrameworkTypeId.ByReference(First)], constructor.DeclaringType)
	}).ToArray();
	private static FrameworkMemberId Helper(string name, FrameworkTypeId result, FrameworkTypeId[] parameters, FrameworkTypeId buffer) =>
		new(FrameworkTypeId.Named("System.Runtime", "<PrivateImplementationDetails>"), name,
			new FrameworkMethodSignatureId(0x10, 2, parameters.Length, result, parameters), [buffer, Object]);
	internal static bool IsConstructor(FrameworkMemberId member) => Constructors.Contains(FrameworkImplementationProfile.Canonicalize(member));
	internal static bool IsHelper(FrameworkMemberId member) => Helpers.Contains(FrameworkImplementationProfile.Canonicalize(member));
	internal static bool IsOwnedCaller(FrameworkMemberId? caller) => caller?.AssemblyName == "System.Private.CoreLib" && IsConstructor(caller);
}
