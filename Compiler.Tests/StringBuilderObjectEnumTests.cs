/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Reflection.Emit;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderObjectEnumTests
{
	[Fact]
	public void RuntimeEnumCallerScopeRequiresExactOwnedSignatures()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(typeof(CopperSharp.Runtime.ShadowBoxedEnum).Assembly.Location,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		Assert.False(StringBuilderBoxedEnumSurface.IsRuntimeCaller(null));
		foreach (var member in StringBuilderBoxedEnumSurface.RuntimeCallers)
		{
			var method = module.ResolveManagedMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowEnumFormatting::" + member.Name);
			var actual = module.DescribeFrameworkMethodToken(System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(method.Handle), method, -1);
			Assert.Equal(member, FrameworkImplementationProfile.Canonicalize(actual));
			Assert.True(StringBuilderBoxedEnumSurface.IsRuntimeCaller(actual));
			Assert.False(StringBuilderBoxedEnumSurface.IsRuntimeCaller(new(FrameworkTypeId.Named("Application", "CopperSharp.Runtime.ShadowEnumFormatting"), member.Name, member.Signature)));
			Assert.False(StringBuilderBoxedEnumSurface.IsRuntimeCaller(new(member.DeclaringType, "Unlisted", member.Signature)));
			Assert.False(StringBuilderBoxedEnumSurface.IsRuntimeCaller(new(member.DeclaringType, member.Name, member.Signature, [FrameworkTypeId.Primitive("System.Int32")])));
		}
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void EnumObjectDispatchUsesVerifiedBoxesAndTheReferenceReceiver(bool experimental)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowBoxedEnum).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = experimental }));
		pack.Replace("packVersion", "10.0.10");
		using var adjacent = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		var caller = module.ResolveManagedMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowObjectJoinText::ToString");
		var call = Assert.Single(caller.Instructions.Where(instruction => instruction.OpCode == OpCodes.Callvirt));
		var declaration = module.ResolveMethodToken((int)call.Operand!, caller, call.Offset).Definition!;
		Assert.True(module.IsObjectJoinDispatch(declaration)); Assert.False(adjacent.IsObjectJoinDispatch(declaration));
		Assert.Empty(module.GetObjectJoinDispatchEntries(declaration));
		var producer = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CreateObjectEnum");
		var types = producer.Instructions.Where(instruction => instruction.OpCode == OpCodes.Box)
			.Select(instruction => module.ResolveTypeToken((int)instruction.Operand!, producer, instruction.Offset)).Distinct().ToArray();
		Assert.Equal(9, types.Length);
		foreach (var type in types)
		{
			Assert.True(type.IsEnum);
			Assert.True(module.IsPinnedBoxedEnumType(type, producer.ModuleName));
			Assert.False(module.IsPinnedBoxedEnumType(type with { IsEnum = false }, producer.ModuleName));
			Assert.False(module.IsPinnedBoxedEnumType(type with { Kind = CilTypeKind.FloatingPoint }, producer.ModuleName));
			Assert.False(module.IsPinnedBoxedEnumType(type with { Size = 3 }, producer.ModuleName));
			Assert.Throws<M68kCompilationException>(() => module.IsPinnedBoxedEnumType(type with { Size = type.Size == 8 ? 4 : 8 }, producer.ModuleName));
			var layout = module.RegisterBoxedDispatchLayout(type, producer.ModuleName)!;
			var entry = Assert.Single(module.GetObjectJoinDispatchEntries(declaration).Where(item => item.Layout.Identity == layout.Identity));
			Assert.Equal("CopperSharp.Runtime.ShadowBoxedEnum::ToString", entry.Method.DisplayName);
			Assert.Equal("CopperSharp.Runtime.Managed", entry.Method.ModuleName);
			Assert.True(entry.Method.Signature.Header.IsInstance);
			Assert.Equal(CilTypeKind.ManagedReference, module.GetMethodDeclaringType(entry.Method).Kind);
			Assert.Null(adjacent.RegisterBoxedDispatchLayout(type, producer.ModuleName));
		}
		Assert.Equal(9, module.GetObjectJoinDispatchEntries(declaration).Count);
	}

	[Fact]
	public void BoxedEnumMethodsRequireExactReleasedDefinitions()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack: catalog);
			var owner = module.Reader.TypeDefinitions.Single(handle => module.Reader.GetString(module.Reader.GetTypeDefinition(handle).Namespace) == "System" && module.Reader.GetString(module.Reader.GetTypeDefinition(handle).Name) == "Enum");
			var definitions = module.Reader.GetTypeDefinition(owner).GetMethods()
				.Where(handle => module.Reader.GetString(module.Reader.GetMethodDefinition(handle).Name) is "ToString" or "System.ISpanFormattable.TryFormat")
				.Select(module.GetMethod).Select(method =>
				FrameworkImplementationProfile.Canonicalize(module.DescribeFrameworkMethodToken(System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(method.Handle), method, -1))).ToArray();
			Assert.Equal(5, StringBuilderBoxedEnumSurface.Members.Count);
			foreach (var member in StringBuilderBoxedEnumSurface.Members)
			{
				Assert.Contains(member, definitions);
				Assert.Equal(admitted, Admit(member, out var binding));
				if (admitted) { Assert.Equal("CopperSharp.Runtime.ShadowBoxedEnum", binding.ShadowMethod!.TypeName); Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayThrow | FrameworkEffects.MayCollect)); }
				Assert.False(Admit(new(member.DeclaringType, "Unlisted", member.Signature), out _));
				Assert.False(Admit(new(member.DeclaringType, member.Name, member.Signature, [FrameworkTypeId.Primitive("System.Int32")]), out _));
				Assert.False(Admit(new(FrameworkTypeId.Named("Application", "System.Enum"), member.Name, member.Signature), out _));
			}
			bool Admit(FrameworkMemberId member, out FrameworkBinding binding) => FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out binding);
		}
	}
}
