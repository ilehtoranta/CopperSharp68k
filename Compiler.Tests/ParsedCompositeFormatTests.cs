/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class ParsedCompositeFormatTests
{
	[Theory]
	[InlineData("Grow")]
	[InlineData("Dispose")]
	public void CharacterBufferStorageOverridesRequireExactPrivateSignatures(string name)
	{
		var integer = FrameworkTypeId.Primitive("System.Int32");
		var result = FrameworkTypeId.Primitive("System.Void");
		var parameters = name == "Grow" ? new[] { integer } : Array.Empty<FrameworkTypeId>();
		var owner = FrameworkTypeId.Named("System.Private.CoreLib", "System.Text.ValueStringBuilder");
		var signature = new FrameworkMethodSignatureId(0x20, 0, parameters.Length, result, parameters);
		var member = new FrameworkMemberId(owner, name, signature);
		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
		Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
		Assert.Equal("CopperSharp.Runtime.ShadowValueStringBuilder", binding.ShadowMethod!.TypeName);
		Assert.Equal(name == "Grow", binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayCollect));
		foreach (var assembly in new[] { "System.Runtime", "Application" })
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(
				FrameworkTypeId.Named(assembly, owner.MetadataName!), name, signature), true, out _));
		foreach (var wrong in new[] {
			new FrameworkMethodSignatureId(0, 0, parameters.Length, result, parameters),
			new FrameworkMethodSignatureId(0x20, 1, parameters.Length, result, parameters),
			new FrameworkMethodSignatureId(0x20, 0, parameters.Length, integer, parameters),
			new FrameworkMethodSignatureId(0x20, 0, parameters.Length + 1, result, [.. parameters, integer]) })
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(owner, name, wrong), true, out _));
		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(owner, name, signature, [integer]), true, out _));
	}

	[Fact]
	public void CharacterBufferAndParsedSegmentLayoutsRetainTheirPreciseRoots()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowValueStringBuilder).Assembly.Location], frameworkImplementationPack: catalog);
		using var storageModule = new CompilationModule(typeof(CopperSharp.Runtime.ShadowValueStringBuilder).Assembly.Location, frameworkImplementationPack: catalog);
		foreach (var (assembly, name) in new[] {
			("System.Private.CoreLib", "System.Text.ValueStringBuilder"),
			("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowValueStringBuilder") })
		{
			Assert.True(storageModule.TryGetReferenceFreeStructLayout(new CilType(CilTypeKind.ValueType, 0, name), assembly, out var layout));
			Assert.Equal(assembly, layout.ModuleName);
			Assert.Equal(20, layout.Size); Assert.Equal(9u, layout.ReferenceBitmap);
			Assert.Equal(new[] { 0, 4, 16 }, layout.FieldOffsets.Values.Order().ToArray());
		}
		var caller = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::RetainCompositeSegment");
		var segment = caller.Signature.ReturnType;
		Assert.True(module.TryGetReferenceFreeStructLayout(segment, caller.ModuleName, out var segments));
		Assert.Equal("System.Private.CoreLib", segments.ModuleName);
		Assert.Equal(16, segments.Size); Assert.Equal(9u, segments.ReferenceBitmap);
		Assert.Equal(new[] { 0, 4, 8, 12 }, segments.FieldOffsets.Values.Order().ToArray());
		var wrong = segment with { GenericArguments = segment.GenericArguments.SetItem(1, new CilType(CilTypeKind.UnsignedInteger, 4, "uint")) };
		Assert.False(CompilationModule.IsCompositeFormatSegment(wrong));
	}

	[Fact]
	public void ParsedSegmentCopyIsScopedToVerifiedCoreLibListCallers()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowArray).Assembly.Location], frameworkImplementationPack: catalog);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibStringBuilderParsedCompositeFormatEntry");
		var parseCall = entry.Instructions.First(instruction => instruction.OpCode == OpCodes.Call &&
			module.DescribeMethodToken((int)instruction.Operand!, entry, instruction.Offset)?.Name == "Parse");
		var parse = module.ResolveMethodToken((int)parseCall.Operand!, entry, parseCall.Offset).Definition!;
		var listCall = parse.Instructions.Single(instruction => instruction.OpCode == OpCodes.Callvirt &&
			module.DescribeMethodToken((int)instruction.Operand!, parse, instruction.Offset)?.Name == "ToArray");
		var caller = module.ResolveMethodToken((int)listCall.Operand!, parse, listCall.Offset).Definition!;
		var copy = caller.Instructions.Single(instruction => instruction.OpCode == OpCodes.Call &&
			module.DescribeMethodToken((int)instruction.Operand!, caller, instruction.Offset)?.Name == "Copy");
		var member = module.DescribeFrameworkMethodToken((int)copy.Operand!, caller, copy.Offset);
		Assert.True(module.TryCreateExperimentalCompositeSegmentCopyBinding(member, caller, out var binding));
		Assert.Equal("CopyCompositeSegments", binding.ShadowMethod!.MethodName);
		Assert.Equal("CopyCompositeSegments", module.ResolveMethodToken((int)copy.Operand!, caller, copy.Offset).Definition!.Name);
		Assert.False(module.TryCreateExperimentalCompositeSegmentCopyBinding(member, caller with { ModuleName = "Application" }, out _));
		Assert.False(module.TryCreateExperimentalCompositeSegmentCopyBinding(member, caller with { Name = "Unrelated" }, out _));
		Assert.False(module.TryCreateExperimentalCompositeSegmentCopyBinding(member, caller with { ConstructedDeclaringType = null }, out _));
		Assert.False(module.TryCreateExperimentalCompositeSegmentCopyBinding(new FrameworkMemberId(
			FrameworkTypeId.Named("Application", "System.Array"), member.Name, member.Signature), caller, out _));
	}

	[Fact]
	public void ClosedTypeEqualityFoldsBothOutcomesAndPreservesInteriorEntries()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack: catalog);
		using var ordinary = new CompilationModule(typeof(CompilerFixtures).Assembly.Location);
		var caller = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CompositeTypeEqualityEntry");
		var expected = new[] { 17, 29, 43, 31 }; var index = 0;
		foreach (var call in caller.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call))
		{
			var closed = module.ResolveMethodToken((int)call.Operand!, caller, call.Offset).Definition!;
			var method = module.GetMethod(closed.Handle) with { MethodTypeArguments = closed.MethodTypeArguments };
			var folded = CilTypePredicateSpecializer.Specialize(method, module);
			Assert.DoesNotContain(folded.Instructions, instruction => instruction.OpCode == OpCodes.Ldtoken);
			Assert.Equal(expected[index++], Convert.ToInt32(Assert.Single(folded.Instructions, instruction => instruction.OpCode == OpCodes.Ldc_I4_S).Operand));
			Assert.Same(method, CilTypePredicateSpecializer.Specialize(method, ordinary));
			foreach (var interior in method.Instructions.Skip(1).Take(5))
			{
				var entered = method with { Instructions = [new CilInstruction(-1, OpCodes.Br, interior.Offset, 0), .. method.Instructions] };
				Assert.Same(entered, CilTypePredicateSpecializer.Specialize(entered, module));
			}
		}
		Assert.Equal(4, index);
	}
}
