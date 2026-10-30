/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderConcatenationAdmissionTests
{
	[Fact]
	public void ConcatenationHelpersRequireReleasedDefinitionsAndOwnedCallers()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(pack.AssemblyPath);
		var actual = module.Reader.MethodDefinitions.Where(handle => {
			var definition = module.Reader.GetMethodDefinition(handle);
			var owner = module.Reader.GetTypeDefinition(definition.GetDeclaringType());
			var name = module.Reader.GetString(definition.Name);
			return module.Reader.GetString(owner.Name) switch {
				"String" => name is "CopyStringContent" or "IsNullOrEmpty",
				"ThrowHelper" => name == "ThrowOutOfMemoryException_StringTooLong",
				"OutOfMemoryException" => name == ".ctor",
				"SR" => name == "get_OutOfMemory_StringTooLong", _ => false
			};
		}).Select(module.GetMethod).Select(method => module.DescribeFrameworkMethodToken(
			System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(method.Handle), method, -1))
			.Where(StringBuilderConcatenationSurface.ContainsHelper).ToArray();
		Assert.Equal(5, actual.Length);
		Assert.Equal(StringBuilderConcatenationSurface.Helpers.OrderBy(member => member.DisplayName), actual.Select(FrameworkImplementationProfile.Canonicalize).OrderBy(member => member.DisplayName));
		var ownerName = "System.Private.CoreLib";
		var declared = StringBuilderConcatenationSurface.Members[0];
		var caller = new FrameworkMemberId(FrameworkTypeId.Named(ownerName, "System.String"), declared.Name, declared.Signature);
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			foreach (var helper in actual)
			{
				Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(helper, null, catalog, caller, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(helper, null, catalog, null, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(helper, null, catalog, new(caller.DeclaringType, "AdjacentCaller", caller.Signature), out _));
			}
		}
	}

	[Fact]
	public void ConcatenationLeavesResolveFromTheirExactOwnedBodies()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		using var module = new CompilationModule(pack.AssemblyPath,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowArray).Assembly.Location], frameworkImplementationPack: catalog);
		pack.Replace("packVersion", "10.0.10");
		var adjacent = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		var leaves = new HashSet<string>();
		foreach (var handle in module.Reader.MethodDefinitions)
		{
			var definition = module.Reader.GetMethodDefinition(handle);
			var name = module.Reader.GetString(definition.Name);
			if (name is not ("Concat" or "CopyStringContent")) continue;
			if (module.Reader.GetString(module.Reader.GetTypeDefinition(definition.GetDeclaringType()).Name) != "String") continue;
			var method = module.GetMethod(handle);
			var caller = module.DescribeFrameworkMethodToken(System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(handle), method, -1);
			if (!StringBuilderConcatenationSurface.IsOwnedCaller(caller)) continue;
			foreach (var call in method.Instructions.Where(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Call))
			{
				var member = module.DescribeFrameworkMethodToken((int)call.Operand!, method, call.Offset);
				if (member.Name is not ("FastAllocateString" or "GetRawStringData" or "Memmove" or "Add")) continue;
				Assert.True(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog, caller, out var binding), member.DisplayName);
				Assert.Equal(binding.Target, module.ResolveMethodToken((int)call.Operand!, method, call.Offset).FrameworkBinding!.Target);
				Assert.NotNull(binding.Target);
				leaves.Add(binding.Target);
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, adjacent, caller, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog, null, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog, new(caller.DeclaringType, "AdjacentCaller", caller.Signature), out _));
			}
		}
		Assert.Equal(new[] { "intrinsic:corelib-add-char-ref", "intrinsic:corelib-memmove-char", "intrinsic:runtime-allocate-string" }, leaves.OrderBy(value => value, StringComparer.Ordinal));
	}

	[Fact]
	public void ConcatenationRequiresExactReleasedStringOverloads()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(pack.AssemblyPath);
		var owner = module.Reader.TypeDefinitions.Single(handle => module.Reader.GetString(module.Reader.GetTypeDefinition(handle).Namespace) == "System" &&
			module.Reader.GetString(module.Reader.GetTypeDefinition(handle).Name) == "String");
		var actual = module.Reader.GetTypeDefinition(owner).GetMethods()
			.Where(handle => module.Reader.GetString(module.Reader.GetMethodDefinition(handle).Name) == "Concat").Select(module.GetMethod)
			.Select(method => module.DescribeFrameworkMethodToken(System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(method.Handle), method, -1))
			.Where(StringBuilderConcatenationSurface.Contains).Select(FrameworkImplementationProfile.Canonicalize).ToArray();
		Assert.Equal(StringBuilderConcatenationSurface.Members.OrderBy(member => member.Signature.RequiredParameterCount), actual.OrderBy(member => member.Signature.RequiredParameterCount));
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			foreach (var member in actual)
			{
				Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out var binding));
				if (admitted) Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect | FrameworkEffects.MayThrow));
				Assert.False(StringBuilderConcatenationSurface.Contains(new(member.DeclaringType, "AdjacentConcat", member.Signature)));
				Assert.False(StringBuilderConcatenationSurface.Contains(new(FrameworkTypeId.Named("Application", "System.String"), member.Name, member.Signature)));
				Assert.False(StringBuilderConcatenationSurface.Contains(new(member.DeclaringType, member.Name,
					new FrameworkMethodSignatureId(0x20, 0, member.Signature.RequiredParameterCount, member.Signature.ReturnType, member.Signature.ParameterTypes))));
			}
		}
	}
}
