/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using System.Reflection.Emit;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderMemoryDependencyAdmissionTests
{
	[Fact]
	public void CharacterMemoryDependenciesRetainTheirCallerRulesAndReleasedInput()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = Load(pack);
		var constructor = StringBuilderFrameworkSurface.Members.First(member => member.Name == ".ctor" && member.Signature.ParameterTypes.Length == 0);
		var caller = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.Text.StringBuilder"), constructor.Name, constructor.Signature);
		foreach (var member in Members())
		{
			Assert.True(Admit(member, caller, out var binding), member.DisplayName);
			Assert.True(FrameworkImplementationProfile.IsTargetRuntimeOverride(binding));
			var publicProjection = member.Name == "AsSpan" && member.DeclaringType.FullMetadataName == "System.MemoryExtensions";
			Assert.Equal(publicProjection, Admit(member, null, out _));
			Assert.Equal(publicProjection, Admit(member, constructor, out _));
			Assert.False(Admit(new FrameworkMemberId(member.DeclaringType, "UnlistedName", member.Signature, member.MethodTypeArguments), caller, out _));
			if (member.MethodTypeArguments.Length == 1)
				Assert.False(Admit(new FrameworkMemberId(member.DeclaringType, member.Name, member.Signature, [FrameworkTypeId.Primitive("System.Int32")]), caller, out _));
		}
		var outside = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Object"), "MemberwiseClone",
			new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.Object"), []));
		Assert.False(Admit(outside, caller, out _));
		pack.Replace("packVersion", "10.0.10");
		var adjacent = Load(pack);
		foreach (var member in Members())
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, adjacent, caller, out _));

		bool Admit(FrameworkMemberId member, FrameworkMemberId? owner, out FrameworkBinding binding) =>
			FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog, owner, out binding);
	}

	[Fact]
	public void CharacterSpanFillRequiresAnOwnedBuilderAndHasOneCharacterLeaf()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = Load(pack);
		var character = FrameworkTypeId.Primitive("System.Char");
		var definition = FrameworkTypeId.Named("System.Runtime", "System.Span`1");
		var signature = new FrameworkMethodSignatureId(0x20, 0, 1, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.GenericTypeParameter(0)]);
		var fill = new FrameworkMemberId(FrameworkTypeId.GenericInstantiation(definition, [character]), "Fill", signature);
		var constructor = StringBuilderFrameworkSurface.Members.First(member => member.Name == ".ctor" && member.Signature.ParameterTypes.Length == 0);
		var builder = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.Text.StringBuilder"), constructor.Name, constructor.Signature);
		Assert.True(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(fill, null, catalog, builder, out var binding));
		Assert.Equal(FrameworkBindingKind.PinnedManagedBody, binding.Kind);
		Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(fill, null, catalog, null, out _));
		Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(new FrameworkMemberId(
			FrameworkTypeId.GenericInstantiation(definition, [FrameworkTypeId.Primitive("System.Int32")]), "Fill", signature), null, catalog, builder, out _));
		var caller = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.Span`1"), "Fill", signature);
		var parameter = FrameworkTypeId.GenericMethodParameter(0);
		var leaf = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.SpanHelpers"), "Fill",
			new FrameworkMethodSignatureId(0x10, 1, 3, FrameworkTypeId.Primitive("System.Void"),
				[FrameworkTypeId.ByReference(parameter), FrameworkTypeId.Primitive("System.UIntPtr"), parameter]), [character]);
		Assert.True(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(leaf, catalog, caller, out var intrinsic));
		Assert.Equal("intrinsic:corelib-fill-char", intrinsic.Target);
		Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(Members().First(), catalog, caller, out _));
		pack.Replace("packVersion", "10.0.10");
		Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(fill, null, Load(pack), builder, out _));
	}

	[Fact]
	public void CharacterArrayCopyClosureRetainsItsBoundedExceptionDependencies()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = Load(pack);
		var array = FrameworkTypeId.Named("System.Runtime", "System.Array");
		var caller = new FrameworkMemberId(FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowArray"), "CopyCharacters",
			new FrameworkMethodSignatureId(0, 0, 3, FrameworkTypeId.Primitive("System.Void"), [array, array, FrameworkTypeId.Primitive("System.Int32")]));
		var projection = Members().Single(member => member.Name == "AsSpan" && member.Signature.RequiredParameterCount == 3);
		Assert.True(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(projection, catalog, caller, out var binding));
		Assert.Equal("intrinsic:span-from-array-range-value:char", binding.Target);
		Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(Members().First(), catalog, caller, out _));
		Assert.True(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(projection, catalog,
			new FrameworkMemberId(caller.DeclaringType, "Unlisted", caller.Signature), out _));
		foreach (var member in StringBuilderExceptionSurface.Members)
		{
			var publicConstructor = StringBuilderExceptionSurface.IsPublicCallbackConstructor(member);
			var expected = publicConstructor || member.Name == ".ctor" && (member.DeclaringType.FullMetadataName is
				"System.ArgumentException" or "System.ArgumentNullException" or "System.ArgumentOutOfRangeException" or "System.NotSupportedException") &&
				member.Signature.ParameterTypes.SequenceEqual(new[] { FrameworkTypeId.Primitive("System.String") });
			Assert.Equal(expected, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, caller, out _));
			Assert.Equal(publicConstructor, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog,
				new FrameworkMemberId(caller.DeclaringType, "Unlisted", caller.Signature), out _));
		}
		pack.Replace("packVersion", "10.0.10");
		Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(projection, Load(pack), caller, out _));
	}

	[Fact]
	public void ResourceGetterClosureUsesExactLiteralBodiesAndRequiresOwnedCallers()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = Load(pack);
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack: catalog);
		var constructor = StringBuilderFrameworkSurface.Members.First(member => member.Name == ".ctor" && member.Signature.ParameterTypes.Length == 0);
		var owner = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.Text.StringBuilder"), constructor.Name, constructor.Signature);
		var text = FrameworkTypeId.Primitive("System.String");
		var lookup = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.SR"), "GetResourceString",
			new FrameworkMethodSignatureId(0, 0, 1, text, [text]));
		Assert.Equal(34, FrameworkImplementationProfile.StringBuilderResourceGetters.Count);
		foreach (var name in FrameworkImplementationProfile.StringBuilderResourceGetters)
		{
			var getter = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.SR"), name,
				new FrameworkMethodSignatureId(0, 0, 0, text, []));
			Assert.True(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(getter, null, catalog, owner, out var binding));
			Assert.True(FrameworkImplementationProfile.TryCreatePinnedBinding(getter, null, true, out var experimental));
			Assert.Equal(experimental.EffectSummary.Effects, binding.EffectSummary.Effects);
			Assert.Equal(experimental.EffectSummary.RequiredFeatures, binding.EffectSummary.RequiredFeatures);
			Assert.Equal(FrameworkTypeInitializerPolicy.TargetOwned, binding.TypeInitializerPolicy);
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(getter, null, catalog, null, out _));
			Assert.True(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(lookup, catalog, getter, out var leaf));
			Assert.Equal("shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowSystemResources::GetResourceString", leaf.Target);
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(Members().First(), catalog, getter, out _));
			var method = module.ResolveManagedMethod("System.Private.CoreLib", "System.SR::" + name);
			Assert.Equal(new[] { OpCodes.Ldstr, OpCodes.Call, OpCodes.Ret }, method.Instructions.Select(instruction => instruction.OpCode));
			Assert.Equal(name[4..], module.GetUserString((int)method.Instructions[0].Operand!, method, method.Instructions[0].Offset));
			Assert.Equal(lookup, module.DescribeFrameworkMethodToken((int)method.Instructions[1].Operand!, method, method.Instructions[1].Offset));
		}
		var unknown = new FrameworkMemberId(lookup.DeclaringType, "get_UnlistedResource", new FrameworkMethodSignatureId(0, 0, 0, text, []));
		Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(unknown, null, catalog, owner, out _));
		Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(lookup, catalog, unknown, out _));
	}

	internal static IEnumerable<FrameworkMemberId> Members()
	{
		var element = FrameworkTypeId.GenericMethodParameter(0);
		var reference = FrameworkTypeId.ByReference(element);
		var character = FrameworkTypeId.Primitive("System.Char");
		var integer = FrameworkTypeId.Primitive("System.Int32");
		var none = FrameworkTypeId.Primitive("System.Void");
		yield return Generic("System.Buffer", "Memmove", none, [reference, reference, FrameworkTypeId.Primitive("System.UIntPtr")]);
		yield return Generic("System.GC", "AllocateUninitializedArray", FrameworkTypeId.SzArray(element), [integer, FrameworkTypeId.Primitive("System.Boolean")]);
		yield return Generic("System.Runtime.CompilerServices.Unsafe", "Add", reference, [reference, integer]);
		yield return Generic("System.Runtime.InteropServices.MemoryMarshal", "GetArrayDataReference", reference, [FrameworkTypeId.SzArray(element)]);
		yield return Generic("System.MemoryExtensions", "AsSpan",
			FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Span`1"), [element]), [FrameworkTypeId.SzArray(element), integer]);
		yield return new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.String"), "GetRawStringData",
			new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.ByReference(character), []));
		yield return new FrameworkMemberId(FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [character]), ".ctor",
			new FrameworkMethodSignatureId(0x20, 0, 2, none, [FrameworkTypeId.ByReference(FrameworkTypeId.GenericTypeParameter(0)), integer]));
		yield return new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.String"), "FastAllocateString",
			new FrameworkMethodSignatureId(0, 0, 1, FrameworkTypeId.Primitive("System.String"), [FrameworkTypeId.Primitive("System.IntPtr")]));
		yield return new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.String"), ".ctor",
			new FrameworkMethodSignatureId(0x20, 0, 1, none, [FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [character])]));
		var writable = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Span`1"), [character]);
		var readOnly = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [character]);
		var genericReadOnly = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [element]);
		yield return Generic("System.MemoryExtensions", "IndexOf", integer, [genericReadOnly, genericReadOnly]);
		yield return Generic("System.MemoryExtensions", "IndexOfAny", integer, [genericReadOnly, element, element]);
		yield return new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.MemoryExtensions"), "EqualsOrdinal",
			new FrameworkMethodSignatureId(0, 0, 2, FrameworkTypeId.Primitive("System.Boolean"), [readOnly, readOnly]));
		yield return Generic("System.Runtime.InteropServices.MemoryMarshal", "GetReference", reference, [genericReadOnly]);
		yield return new FrameworkMemberId(writable, ".ctor", new FrameworkMethodSignatureId(0x20, 0, 2, none, [FrameworkTypeId.ByReference(FrameworkTypeId.GenericTypeParameter(0)), integer]));
		foreach (var name in new[] { "System.Span`1", "System.ReadOnlySpan`1" })
		{
			yield return new FrameworkMemberId(FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", name), [character]), ".ctor",
				new FrameworkMethodSignatureId(0x20, 0, 1, none, [FrameworkTypeId.SzArray(FrameworkTypeId.GenericTypeParameter(0))]));
			yield return new FrameworkMemberId(FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", name), [character]), ".ctor",
				new FrameworkMethodSignatureId(0x20, 0, 3, none, [FrameworkTypeId.SzArray(FrameworkTypeId.GenericTypeParameter(0)), integer, integer]));
		}
		yield return Generic("System.MemoryExtensions", "AsSpan", FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Span`1"), [element]),
			[FrameworkTypeId.SzArray(element), integer, integer]);
		yield return Generic("System.MemoryExtensions", "Replace", none,
			[FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Span`1"), [element]), element, element]);
		var array = FrameworkTypeId.Named("System.Runtime", "System.Array");
		yield return new FrameworkMemberId(array, "Copy", new FrameworkMethodSignatureId(0, 0, 3, none, [array, array, integer]));

		FrameworkMemberId Generic(string owner, string name, FrameworkTypeId result, FrameworkTypeId[] parameters) =>
			new(FrameworkTypeId.Named("System.Runtime", owner), name, new FrameworkMethodSignatureId(0x10, 1, parameters.Length, result, parameters), [character]);
	}

	private static FrameworkImplementationPackCatalog Load(FrameworkImplementationPackTests.CoreLibPack pack) =>
		FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
}
