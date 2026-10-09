/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using System.Reflection.Emit;
using System.Reflection.Metadata.Ecma335;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderArrayMemoryAdmissionTests
{
	[Fact]
	public void ArrayMemoryFactoriesRequirePinnedCharacterSignatures()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack: catalog);
			var type = module.Reader.TypeDefinitions.Select(module.Reader.GetTypeDefinition).Single(type =>
				module.Reader.GetString(type.Namespace) == "System" && module.Reader.GetString(type.Name) == "MemoryExtensions");
			var count = 0;
			foreach (var handle in type.GetMethods().Where(handle => module.Reader.GetString(module.Reader.GetMethodDefinition(handle).Name) == "AsMemory"))
			{
				var body = module.GetMethod(handle);
				var definition = module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(handle), body, 0);
				if (definition.Signature.GenericParameterCount != 1 || definition.Signature.ParameterTypes[0].Kind != FrameworkTypeKind.SzArray ||
					definition.Signature.ParameterTypes.Any(type => type.FullMetadataName == "System.Index" || type.FullMetadataName == "System.Range")) continue;
				var member = new FrameworkMemberId(definition.DeclaringType, definition.Name, definition.Signature, [FrameworkTypeId.Primitive("System.Char")]);
				if (!FrameworkImplementationProfile.IsCharacterArrayMemoryProjection(member)) continue;
				count++;
				File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, $"array-memory-projection-{body.Signature.ParameterTypes.Length}.txt"),
					body.Instructions.Select(instruction => $"{instruction.Offset:X4}: {instruction.OpCode} {instruction.Operand}"));
				Assert.Equal(admitted, Admit(member, out var binding));
				if (admitted)
				{
					Assert.Equal(FrameworkBindingKind.PinnedManagedBody, binding.Kind);
					Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayThrow | FrameworkEffects.MayCollect | FrameworkEffects.ReadsManagedMemory));
					var canonical = FrameworkImplementationProfile.Canonicalize(member);
					foreach (var assembly in new[] { "System.Runtime", "System.Memory" })
						Assert.True(Admit(new(FrameworkTypeId.Named(assembly, "System.MemoryExtensions"), member.Name, canonical.Signature, member.MethodTypeArguments), out _));
				}
				Assert.False(Admit(new(member.DeclaringType, member.Name, member.Signature, [FrameworkTypeId.Primitive("System.Int32")]), out _));
				Assert.False(Admit(new(member.DeclaringType, "Unlisted", member.Signature, member.MethodTypeArguments), out _));
				Assert.False(Admit(new(FrameworkTypeId.Named("Application", "System.MemoryExtensions"), member.Name, member.Signature, member.MethodTypeArguments), out _));
			}
			Assert.Equal(3, count);
			var memoryType = module.Reader.TypeDefinitions.Select(module.Reader.GetTypeDefinition).Single(type =>
				module.Reader.GetString(type.Namespace) == "System" && module.Reader.GetString(type.Name) == "Memory`1");
			var startConstructor = memoryType.GetMethods().Select(module.GetMethod).Single(method => method.Name == ".ctor" && method.Signature.ParameterTypes.Length == 2 &&
				method.Signature.ParameterTypes[0].ElementType is not null && method.Signature.ParameterTypes[1].DisplayName == "int");
			File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "array-memory-start-constructor.txt"), startConstructor.Instructions.Select(instruction =>
				$"{instruction.Offset:X4}: {instruction.OpCode} {instruction.Operand}" + (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt || instruction.OpCode == OpCodes.Newobj
					? " " + module.DescribeFrameworkMethodToken((int)instruction.Operand!, startConstructor, instruction.Offset).DisplayName : "")));
			bool Admit(FrameworkMemberId member, out FrameworkBinding binding) =>
				FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out binding);
		}
	}

	[Fact]
	public void ArrayMemoryFixtureMatchesCoreLib() => Assert.Equal(42, CompilerFixtures.StringBuilderStableArrayMemoryEntry());

	[Fact]
	public void InternalStartConstructorRequiresItsVerifiedCharacterFactoryCaller()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack: catalog);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::StringBuilderStableArrayMemoryEntry");
		var factoryCall = entry.Instructions.First(instruction => instruction.OpCode == OpCodes.Call &&
			module.DescribeMethodToken((int)instruction.Operand!, entry, instruction.Offset) is { Name: "AsMemory", ParameterTypes.Length: 2 });
		var factory = module.ResolveMethodToken((int)factoryCall.Operand!, entry, factoryCall.Offset).Definition!;
		var constructorCall = factory.Instructions.Single(instruction => instruction.OpCode == OpCodes.Newobj);
		var member = module.DescribeFrameworkMethodToken((int)constructorCall.Operand!, factory, constructorCall.Offset);
		Assert.True(module.TryCreatePinnedArrayMemoryStartConstructorBinding(member, factory, out var binding));
		Assert.Equal("intrinsic:memory-from-array-start:char", binding.Target);
		Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayThrow));
		Assert.False(module.TryCreatePinnedArrayMemoryStartConstructorBinding(member, null, out _));
		Assert.False(module.TryCreatePinnedArrayMemoryStartConstructorBinding(member, entry, out _));
		Assert.False(module.TryCreatePinnedArrayMemoryStartConstructorBinding(member, factory with { MethodTypeArguments = [] }, out _));
		Assert.False(module.TryCreatePinnedArrayMemoryStartConstructorBinding(member, factory with { MethodTypeArguments = [new CilType(CilTypeKind.SignedInteger, 4, "int")] }, out _));
		Assert.False(module.TryCreatePinnedArrayMemoryStartConstructorBinding(new(member.DeclaringType, "Unlisted", member.Signature), factory, out _));
		pack.Replace("packVersion", "10.0.10");
		var adjacent = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		using var otherModule = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack: adjacent);
		Assert.False(otherModule.TryCreatePinnedArrayMemoryStartConstructorBinding(member, factory, out _));
	}
}

public static partial class CompilerFixtures
{
	public static int StringBuilderStableArrayMemoryEntry()
	{
		var memory = MakeBuilderSpanArray().AsMemory();
		ReadOnlyMemory<char> tail = MakeBuilderSpanArray().AsMemory(2);
		var middle = MakeBuilderSpanArray().AsMemory(1, 2);
		GC.Collect();
		memory.Span[0] = 'Z';
		var builder = new System.Text.StringBuilder(1).Append(memory).Append(tail).Append(middle);
		var snapshot = builder.ToString();
		memory.Span[0] = 'Y';
		GC.Collect();
		if (snapshot != "Z\0\u03a9\ud800\u03a9\ud800\0\u03a9" || builder.ToString() != snapshot || memory.Length != 4 || tail.Span[0] != '\u03a9') return 1;
		builder.Clear().Append(memory.Slice(1).Slice(0, 2));
		GC.Collect();
		if (builder.ToString() != "\0\u03a9" || middle.Span[1] != '\u03a9') return 2;
		char[]? absent = null;
		if (!absent.AsMemory().IsEmpty || !absent.AsMemory(0).IsEmpty || !absent.AsMemory(0, 0).IsEmpty ||
			!MakeBuilderSpanArray().AsMemory(4).IsEmpty || !MakeBuilderSpanArray().AsMemory(4, 0).IsEmpty) return 3;
		var failures = 0;
		try { _ = absent.AsMemory(1); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = absent.AsMemory(0, 1); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = MakeBuilderSpanArray().AsMemory(-1); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = MakeBuilderSpanArray().AsMemory(5); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = MakeBuilderSpanArray().AsMemory(-1, 1); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = MakeBuilderSpanArray().AsMemory(0, -1); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = MakeBuilderSpanArray().AsMemory(3, 2); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = MakeBuilderSpanArray().AsMemory(int.MaxValue, int.MaxValue); } catch (ArgumentOutOfRangeException) { failures++; }
		GC.Collect();
		return failures == 8 && memory.Span[0] == 'Y' && middle.Span[0] == '\0' && tail.Span[1] == '\ud800' ? 42 : 4;
	}
}
