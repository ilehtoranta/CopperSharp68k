/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using System.Reflection.Emit;
using System.Reflection.Metadata.Ecma335;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderCharacterTextAdmissionTests
{
	[Fact]
	public void CharacterTextRequiresItsReleasedOverrideAndExactMemoryLeaves()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack: catalog);
			var type = module.Reader.TypeDefinitions.Single(handle => module.Reader.GetString(module.Reader.GetTypeDefinition(handle).Name) == "Char");
			var methods = module.Reader.GetTypeDefinition(type).GetMethods().Select(module.GetMethod).Where(method => method.Name == "ToString").ToArray();
			var instance = methods.Single(method => method.Signature.Header.IsInstance && method.Signature.ParameterTypes.Length == 0);
			var scalar = methods.Single(method => !method.Signature.Header.IsInstance && method.Signature.ParameterTypes is [{ DisplayName: "char" }]);
			var owner = module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(instance.Handle), instance, -1);
			var member = module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(scalar.Handle), scalar, -1);
			Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, owner, out var binding));
			if (admitted) Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect | FrameworkEffects.MayThrow));
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out _));
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, FrameworkImplementationProfile.Canonicalize(owner), out _));
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, new(owner.DeclaringType, "AdjacentToString", owner.Signature), out _));
			var construction = Assert.Single(scalar.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call));
			var constructionMember = module.DescribeFrameworkMethodToken((int)construction.Operand!, scalar, construction.Offset);
			Assert.Equal("CreateFromChar", constructionMember.Name);
			Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(constructionMember, null, catalog, member, out _));
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(constructionMember, null, catalog, owner, out _));
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(constructionMember, null, catalog, null, out _));
			var strings = module.Reader.TypeDefinitions.Single(handle => module.Reader.GetString(module.Reader.GetTypeDefinition(handle).Name) == "String");
			var factory = module.Reader.GetTypeDefinition(strings).GetMethods().Where(handle =>
				module.Reader.GetString(module.Reader.GetMethodDefinition(handle).Name) == "CreateFromChar")
				.Select(module.GetMethod).Single(method => method.Signature.ParameterTypes is [{ DisplayName: "char" }]);
			var calls = factory.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call).ToArray();
			Assert.Single(calls);
			foreach (var call in calls)
			{
				var leaf = module.DescribeFrameworkMethodToken((int)call.Operand!, factory, call.Offset);
				Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(leaf, catalog, constructionMember, out var leafBinding));
				if (admitted)
				{
					Assert.Equal("intrinsic:runtime-allocate-string", leafBinding.Target);
					Assert.Equal(leafBinding.Target, module.ResolveMethodToken((int)call.Operand!, factory, call.Offset).FrameworkBinding!.Target);
				}
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(leaf, catalog, null, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(leaf, catalog, FrameworkImplementationProfile.Canonicalize(constructionMember), out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(leaf, catalog, member, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(leaf, catalog, owner, out _));
			}
		}
	}
}
