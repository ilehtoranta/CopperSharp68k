/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class FrameworkImplementationPackTests
{
	private static readonly string FixtureAssembly = typeof(CompilerFixtures).Assembly.Location;

	[Fact]
	public void ExperimentalNumberGroupSizesReaderRequiresTheExactRuntimeSignature()
	{
		var type = FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowGroupedIntegerFormatting");
		var array = FrameworkTypeId.SzArray(FrameworkTypeId.Primitive("System.Int32"));
		foreach (var readerName in new[] { "NumberGroupSizes", "CurrencyGroupSizes", "PercentGroupSizes" })
		foreach (var assembly in new[] { "System.Runtime", "System.Private.CoreLib" })
		{
			var info = FrameworkTypeId.Named(assembly, "System.Globalization.NumberFormatInfo");
			FrameworkMemberId Member(FrameworkTypeId? declaring = null, string? name = null, byte header = 0,
				int generic = 0, int required = 1, FrameworkTypeId? result = null, FrameworkTypeId[]? parameters = null,
				FrameworkTypeId[]? arguments = null) => new(declaring ?? type, name ?? readerName,
					new FrameworkMethodSignatureId(header, generic, required, result ?? array, parameters ?? [info]), arguments ?? []);
			bool Admit(FrameworkMemberId member, bool enabled = true) => FrameworkImplementationProfile.IsExperimentalNumberGroupSizesReader(member, enabled);
			Assert.True(Admit(Member())); Assert.False(Admit(Member(), false));
			Assert.False(Admit(Member(declaring: FrameworkTypeId.Named("User.Assembly", type.MetadataName!))));
			Assert.False(Admit(Member(name: "Other"))); Assert.False(Admit(Member(header: 0x20)));
			Assert.False(Admit(Member(generic: 1))); Assert.False(Admit(Member(required: 0)));
			Assert.False(Admit(Member(result: FrameworkTypeId.ByReference(array))));
			Assert.False(Admit(Member(parameters: []))); Assert.False(Admit(Member(parameters: [info, info])));
			Assert.False(Admit(Member(parameters: [FrameworkTypeId.Named("User.Assembly", info.MetadataName!)])));
			Assert.False(Admit(Member(arguments: [array])));
		}
	}

	[Fact]
	public void ExperimentalConstrainedStringToStringSelectsOnlyTheSealedStringBody()
	{
		using var pack = CoreLibPack.Create();
		var path = CoreLibCharacterFixtureBuilder.Create(pack.Directory);
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(path, frameworkImplementationPack: catalog);
		CilMethod? objectDeclaration = null;
		foreach (var name in new[] { "ConstrainedStringToString", "ConstrainedObjectToString", "ConstrainedArrayToString" })
		{
			var entry = module.ResolveEntryPoint($"CoreLibCharacterProbe::{name}");
			var call = entry.Instructions.Single(static instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Callvirt);
			var target = module.ResolveMethodToken((int)call.Operand!, entry, call.Offset).Definition!;
			Assert.Equal(name == "ConstrainedStringToString" ? "System.String::ToString" : "System.Object::ToString", target.DisplayName);
			if (name == "ConstrainedStringToString") Assert.True(target.DeclaringTypeIsSealed);
			else objectDeclaration = target;
		}
		var stringEntry = module.ResolveEntryPoint("CoreLibCharacterProbe::ConstrainedStringToString");
		var stringCall = stringEntry.Instructions.Single(static instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Callvirt);
		Assert.True(module.TryResolveExperimentalConstrainedStringToString(stringEntry,
			stringCall.ConstrainedTypeToken!.Value, stringCall.Offset, objectDeclaration!, out _));
		Assert.False(module.TryResolveExperimentalConstrainedStringToString(stringEntry,
			stringCall.ConstrainedTypeToken.Value, stringCall.Offset, objectDeclaration! with { ModuleName = "User.Assembly" }, out _));
		Assert.False(module.TryResolveExperimentalConstrainedStringToString(stringEntry,
			stringCall.ConstrainedTypeToken.Value, stringCall.Offset, objectDeclaration! with { DisplayName = "System.Object::GetHashCode" }, out _));
		var disabledCatalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath));
		using var disabled = new CompilationModule(path, frameworkImplementationPack: disabledCatalog);
		var disabledEntry = disabled.ResolveEntryPoint("CoreLibCharacterProbe::ConstrainedStringToString");
		var disabledCall = disabledEntry.Instructions.Single(static instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Callvirt);
		Assert.False(disabled.TryResolveExperimentalConstrainedStringToString(disabledEntry,
			disabledCall.ConstrainedTypeToken!.Value, disabledCall.Offset, objectDeclaration!, out _));
	}

	[Theory]
	[InlineData("Number")]
	[InlineData("Currency")]
	[InlineData("Percent")]
	public void ExperimentalNumberGroupCloningIsScopedToVerifiedInt32ArrayAccessors(string kind)
	{
		using var pack = CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(FixtureAssembly,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowArray).Assembly.Location], frameworkImplementationPack: catalog);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::" + (kind == "Number" ? "CoreLibGroupedProviderContractsEntry" : $"CoreLib{kind}ProviderContractsEntry"));
		foreach (var name in new[] { $"get_{kind}GroupSizes", $"set_{kind}GroupSizes" })
		{
			var call = entry.Instructions.First(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Callvirt &&
				module.DescribeFrameworkMethodToken((int)instruction.Operand!, entry, instruction.Offset).Name == name);
			var accessor = module.ResolveMethodToken((int)call.Operand!, entry, call.Offset).Definition!;
			var clone = accessor.Instructions.Single(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Callvirt &&
				module.DescribeFrameworkMethodToken((int)instruction.Operand!, accessor, instruction.Offset).Name == "Clone");
			var member = module.DescribeFrameworkMethodToken((int)clone.Operand!, accessor, clone.Offset);
			Assert.True(module.TryCreateExperimentalNumberGroupCloneBinding(member, accessor, out var binding));
			Assert.Equal("CloneInt32", binding.ShadowMethod!.MethodName);
			Assert.Equal("CopperSharp.Runtime.ShadowArray::CloneInt32", module.ResolveMethodToken((int)clone.Operand!, accessor, clone.Offset).Definition!.DisplayName);
			Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayCollect));
			Assert.False(module.TryCreateExperimentalNumberGroupCloneBinding(member, null, out _));
			Assert.False(module.TryCreateExperimentalNumberGroupCloneBinding(member, entry, out _));
			Assert.False(module.TryCreateExperimentalNumberGroupCloneBinding(member, accessor with { ModuleName = "User.Assembly" }, out _));
			Assert.False(module.TryCreateExperimentalNumberGroupCloneBinding(member, accessor with { DisplayName = "System.Globalization.NumberFormatInfo::get_NativeDigits" }, out _));
			Assert.False(module.TryCreateExperimentalNumberGroupCloneBinding(member, accessor with { MethodTypeArguments = [new CilType(CilTypeKind.SignedInteger, 4, "int")] }, out _));
			var wrong = name.StartsWith("get_", StringComparison.Ordinal)
				? accessor with { Signature = new MethodSignature<CilType>(accessor.Signature.Header, accessor.Signature.ReturnType with { Kind = CilTypeKind.ValueType }, 0, 0, []) }
				: accessor with { Signature = new MethodSignature<CilType>(accessor.Signature.Header, accessor.Signature.ReturnType, 1, 0, [accessor.Signature.ParameterTypes[0] with { Kind = CilTypeKind.ValueType }]) };
			Assert.False(module.TryCreateExperimentalNumberGroupCloneBinding(member, wrong, out _));
			foreach (var changed in new[]
			{
				new FrameworkMemberId(FrameworkTypeId.Named("User.Assembly", "System.Array"), member.Name, member.Signature),
				new FrameworkMemberId(member.DeclaringType, "Other", member.Signature),
				new FrameworkMemberId(member.DeclaringType, member.Name, new FrameworkMethodSignatureId(0, 0, 0, member.Signature.ReturnType, [])),
				new FrameworkMemberId(member.DeclaringType, member.Name, new FrameworkMethodSignatureId(0x20, 0, 1, member.Signature.ReturnType, [FrameworkTypeId.Primitive("System.Int32")])),
				new FrameworkMemberId(member.DeclaringType, member.Name, new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.String"), []))
			}) Assert.False(module.TryCreateExperimentalNumberGroupCloneBinding(changed, accessor, out _));
			var description = module.DescribeMethodToken((int)clone.Operand!, accessor, clone.Offset)!;
			var resolved = module.ResolveMethodToken((int)clone.Operand!, accessor, clone.Offset);
			Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, Net10FrameworkContract.Default.Classify(member, description, resolved, null,
				useVerifiedCoreLibIdentity: true, callSiteOverride: binding).Status);
			Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Net10FrameworkContract.Default.Classify(member, description, resolved, null,
				useVerifiedCoreLibIdentity: false, callSiteOverride: binding).Status);
			Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Net10FrameworkContract.Default.Classify(member, description, resolved, null,
				useVerifiedCoreLibIdentity: true).Status);
			using var disabled = new CompilationModule(FixtureAssembly);
			Assert.False(disabled.TryCreateExperimentalNumberGroupCloneBinding(member, accessor, out _));
		}
		var settings = module.ResolveManagedMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowGroupedIntegerFormatting::" + (kind == "Number" ? "Settings" : $"{kind}Settings"));
		var readerCall = settings.Instructions.First(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Call &&
			module.DescribeFrameworkMethodToken((int)instruction.Operand!, settings, instruction.Offset).Name == $"{kind}GroupSizes");
		var reader = module.ResolveMethodToken((int)readerCall.Operand!, settings, readerCall.Offset).Definition!;
		Assert.Equal("System.Private.CoreLib", reader.ModuleName); Assert.Equal($"System.Number::{kind}GroupSizes", reader.DisplayName);
		Assert.Equal(3, reader.Instructions.Count);
		Assert.Null(module.GetTriggeredTypeInitializer(settings, readerCall));
	}

	[Theory]
	[InlineData("string")]
	[InlineData("object")]
	[InlineData("int")]
	[InlineData("System.Decimal")]
	[InlineData("float")]
	[InlineData("double")]
	public void ExperimentalJoinEnumerationBindingsRequireTheExactCoreLibCaller(string elementName)
	{
		using var pack = CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(FixtureAssembly, managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowDecimalFormatting).Assembly.Location], frameworkImplementationPack: catalog);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::" + (elementName switch { "string" => "CoreLibStringBuilderEnumerableJoinContractEntry", "int" => "CoreLibStringBuilderInt32EnumerableJoinContractEntry", "System.Decimal" => "CoreLibStringBuilderDecimalEnumerableJoinEntry", "float" or "double" => "CoreLibStringBuilderFloatingEnumerableEntry", _ => "CoreLibStringBuilderObjectEnumerableJoinContractEntry" }));
		var wrapper = entry.Instructions.Where(static instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Callvirt)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, entry, instruction.Offset).Definition)
			.First(method => method?.DisplayName == $"System.Text.StringBuilder::AppendJoin<{elementName}>")!;
		var core = wrapper.Instructions.Where(static instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Call)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, wrapper, instruction.Offset).Definition)
			.First(method => method?.DisplayName == $"System.Text.StringBuilder::AppendJoinCore<{elementName}>")!;
		var text = elementName == "System.Decimal" ? FrameworkTypeId.Named("System.Runtime", "System.Decimal") : FrameworkTypeId.Primitive(elementName switch { "string" => "System.String", "int" => "System.Int32", "float" => "System.Single", "double" => "System.Double", _ => "System.Object" });
		var parameter = FrameworkTypeId.GenericTypeParameter(0);
		foreach (var assembly in new[] { "System.Runtime", "System.Private.CoreLib" })
		foreach (var operation in new[] { "GetEnumerator", "get_Current", "MoveNext", "Dispose" })
		{
			var enumerableDefinition = FrameworkTypeId.Named(assembly, "System.Collections.Generic.IEnumerable`1");
			var enumeratorDefinition = FrameworkTypeId.Named(assembly, "System.Collections.Generic.IEnumerator`1");
			var type = operation == "GetEnumerator" ? FrameworkTypeId.GenericInstantiation(enumerableDefinition, [FrameworkTypeId.GenericMethodParameter(0)])
				: operation == "get_Current" ? FrameworkTypeId.GenericInstantiation(enumeratorDefinition, [FrameworkTypeId.GenericMethodParameter(0)])
				: FrameworkTypeId.Named(assembly, operation == "MoveNext" ? "System.Collections.IEnumerator" : "System.IDisposable");
			var result = operation == "GetEnumerator" ? FrameworkTypeId.GenericInstantiation(enumeratorDefinition, [parameter])
				: operation == "get_Current" ? parameter : FrameworkTypeId.Primitive(operation == "MoveNext" ? "System.Boolean" : "System.Void");
			var signature = new FrameworkMethodSignatureId(0x20, 0, 0, result, []);
			var member = new FrameworkMemberId(type, operation, signature);
			Assert.True(module.TryCreateExperimentalJoinEnumerationBinding(member, core, out var binding));
			Assert.Equal(operation, binding.ShadowMethod!.MethodName);
			Assert.Equal($"CopperSharp.Runtime.Shadow{(elementName switch { "string" => "String", "int" => "Int32", "System.Decimal" => "Decimal", "float" => "Single", "double" => "Double", _ => "Object" })}Join{(operation == "GetEnumerator" ? "Enumeration" : "Enumerator")}", binding.ShadowMethod.TypeName);
			Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, core with { MethodTypeArguments = [core.MethodTypeArguments[0] with { Size = elementName == "double" ? 4 : 8 }] }, out _));
			if (elementName is "int" or "System.Decimal" or "float" or "double") Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, core with { MethodTypeArguments = [core.MethodTypeArguments[0] with { Kind = CilTypeKind.ManagedReference }] }, out _));
			Assert.Equal(operation == "GetEnumerator" || elementName is "string" or "object" or "int" or "System.Decimal" or "float" or "double", binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayCollect));
			if (elementName is "string" or "object" or "int" or "System.Decimal" or "float" or "double")
			{
				Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayAllocate));
				Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayThrow));
				Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.WritesManagedMemory));
			}
			Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, entry, out _));
			Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, core with { ModuleName = "User.Assembly" }, out _));
			Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, core with { DisplayName = "Other.Type::AppendJoinCore<string>" }, out _));
			Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, core with { MethodTypeArguments = [new CilType(CilTypeKind.ManagedReference, 4, elementName == "string" ? "object" : "string")] }, out _));
			Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, wrapper, out _));
			Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, core with
			{
				Signature = new MethodSignature<CilType>(core.Signature.Header, core.Signature.ReturnType, 3, 1,
					[.. core.Signature.ParameterTypes.Take(2), core.Signature.ParameterTypes[2] with { Kind = CilTypeKind.ValueType }])
			}, out _));
			Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, core with
			{
				Signature = new System.Reflection.Metadata.MethodSignature<CilType>(core.Signature.Header,
					core.Signature.ReturnType, 3, 1, [.. core.Signature.ParameterTypes.Take(2), new CilType(CilTypeKind.ValueType, 8, "System.ReadOnlySpan`1<string>")])
			}, out _));
			Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(new FrameworkMemberId(type, operation, signature, [text]), core, out _));
			Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(new FrameworkMemberId(type, operation,
				new FrameworkMethodSignatureId(0, 0, 0, result, [])), core, out _));
			Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(new FrameworkMemberId(type, operation,
				new FrameworkMethodSignatureId(0x20, 0, 1, result, [text])), core, out _));
			Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(new FrameworkMemberId(FrameworkTypeId.Named("User.Assembly", "System.IDisposable"), operation, signature), core, out _));
			Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(new FrameworkMemberId(type, operation,
				new FrameworkMethodSignatureId(0x20, 1, 0, result, [])), core, out _));
			Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(new FrameworkMemberId(type, operation,
				new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.Int64"), [])), core, out _));
			if (operation == "GetEnumerator")
			{
				var description = new CilMethodReferenceIdentity(assembly, "System.Collections.Generic.IEnumerable`1", operation, false, 0, "IEnumerator", [], []);
				FrameworkBindingDecision Classify(FrameworkBinding candidate, bool verified = true, bool scoped = true) =>
					Net10FrameworkContract.Default.Classify(member, description, MethodReference.ForBinding(candidate, default), null,
						useVerifiedCoreLibIdentity: verified, callSiteOverride: scoped ? binding : null);
				Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, Classify(binding).Status);
				Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Classify(binding, verified: false).Status);
				Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Classify(binding, scoped: false).Status);
				Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Classify(binding with { Target = "different.target" }).Status);
				Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Classify(binding with { ShadowMethod = binding.ShadowMethod with { MethodName = "Other" } }).Status);
				Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Classify(binding with { PreservesVirtualDispatch = true }).Status);
				Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Classify(binding with { EffectSummary = new FrameworkEffectSummary(FrameworkEffects.ReadsManagedMemory, binding.EffectSummary.RequiredFeatures) }).Status);
				Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Classify(binding with { EffectSummary = new FrameworkEffectSummary(binding.EffectSummary.Effects, []) }).Status);
			}
			using var disabled = new CompilationModule(FixtureAssembly);
			Assert.False(disabled.TryCreateExperimentalJoinEnumerationBinding(member, core, out _));
		}
	}

	[Theory]
	[InlineData("Append")]
	[InlineData("Insert")]
	public void ExperimentalObjectAppendOrInsertConversionRequiresTheExactCoreLibCaller(string operation)
	{
		using var pack = CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(FixtureAssembly, frameworkImplementationPack: catalog);
		var entry = module.ResolveEntryPoint($"CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibStringBuilder{operation}ObjectContractEntry");
		var caller = entry.Instructions.Where(static instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Callvirt)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, entry, instruction.Offset).Definition)
			.First(method => method?.DisplayName == $"System.Text.StringBuilder::{operation}" &&
				method.Signature.ParameterTypes.Last().DisplayName == "object")!;
		var count = caller.Signature.RequiredParameterCount;
		var text = FrameworkTypeId.Primitive("System.String");
		var signature = new FrameworkMethodSignatureId(0x20, 0, 0, text, []);
		var member = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Object"), "ToString", signature);
		Assert.True(module.TryCreateExperimentalObjectJoinTextBinding(member, caller, out var binding));
		Assert.Equal("CopperSharp.Runtime.ShadowObjectJoinText", binding.ShadowMethod!.TypeName);
		Assert.False(binding.PreservesVirtualDispatch);
		Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayCollect));
		Assert.False(module.TryCreateExperimentalObjectJoinTextBinding(member, entry, out _));
		Assert.False(module.TryCreateExperimentalObjectJoinTextBinding(member, null, out _));
		Assert.False(module.TryCreateExperimentalObjectJoinTextBinding(member, caller with { ModuleName = "User.Assembly" }, out _));
		Assert.False(module.TryCreateExperimentalObjectJoinTextBinding(member, caller with { DisplayName = "System.Text.StringBuilder::AppendLine" }, out _));
		Assert.False(module.TryCreateExperimentalObjectJoinTextBinding(member, caller with { MethodTypeArguments = [new CilType(CilTypeKind.ManagedReference, 4, "object")] }, out _));
		foreach (var wrong in new[]
		{
			new MethodSignature<CilType>(new SignatureHeader(0), caller.Signature.ReturnType, count, 0, caller.Signature.ParameterTypes),
			new MethodSignature<CilType>(caller.Signature.Header, caller.Signature.ReturnType, count, 1, caller.Signature.ParameterTypes),
			new MethodSignature<CilType>(caller.Signature.Header, caller.Signature.ReturnType, count + 1, 0, caller.Signature.ParameterTypes),
			new MethodSignature<CilType>(caller.Signature.Header, new CilType(CilTypeKind.ManagedReference, 4, "string"), count, 0, caller.Signature.ParameterTypes),
			new MethodSignature<CilType>(caller.Signature.Header, caller.Signature.ReturnType, count, 0,
				[.. caller.Signature.ParameterTypes.Take(count - 1), new CilType(CilTypeKind.ManagedReference, 4, "string")]),
			new MethodSignature<CilType>(caller.Signature.Header, caller.Signature.ReturnType, count, 0,
				[.. caller.Signature.ParameterTypes.Take(count - 1), new CilType(CilTypeKind.ValueType, 4, "object")])
		}) Assert.False(module.TryCreateExperimentalObjectJoinTextBinding(member, caller with { Signature = wrong }, out _));
		if (operation == "Insert")
		foreach (var indexType in new[] { new CilType(CilTypeKind.SignedInteger, 8, "int"),
			new CilType(CilTypeKind.UnsignedInteger, 4, "int"), new CilType(CilTypeKind.SignedInteger, 4, "long"),
			new CilType(CilTypeKind.ManagedReference, 4, "int") })
			Assert.False(module.TryCreateExperimentalObjectJoinTextBinding(member, caller with
			{
				Signature = new MethodSignature<CilType>(caller.Signature.Header, caller.Signature.ReturnType, 2, 0,
					[indexType, caller.Signature.ParameterTypes[1]])
			}, out _));
		var description = new CilMethodReferenceIdentity("System.Runtime", "System.Object", "ToString", false, 0, "string", [], []);
		FrameworkBindingDecision Classify(bool verified, bool scoped) => Net10FrameworkContract.Default.Classify(member, description,
			MethodReference.ForBinding(binding, default), null, useVerifiedCoreLibIdentity: verified, callSiteOverride: scoped ? binding : null);
		Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, Classify(true, true).Status);
		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Classify(false, true).Status);
		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Classify(true, false).Status);
		using var disabled = new CompilationModule(FixtureAssembly,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		Assert.False(disabled.TryCreateExperimentalObjectJoinTextBinding(member, caller, out _));
	}

	[Fact]
	public void ExperimentalObjectJoinConversionRequiresTheExactCoreLibCaller()
	{
		using var pack = CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(FixtureAssembly, frameworkImplementationPack: catalog);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibStringBuilderObjectJoinContractEntry");
		var wrapper = entry.Instructions.Where(static instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Callvirt)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, entry, instruction.Offset).Definition)
			.First(static method => method?.DisplayName == "System.Text.StringBuilder::AppendJoin" &&
				method.Signature.ParameterTypes.Last().DisplayName == "object[]")!;
		var core = wrapper.Instructions.Where(static instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Call)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, wrapper, instruction.Offset).Definition)
			.First(static method => method?.DisplayName == "System.Text.StringBuilder::AppendJoinCore<object>")!;
		var text = FrameworkTypeId.Primitive("System.String");
		foreach (var assembly in new[] { "System.Runtime", "System.Private.CoreLib" })
		{
			var type = FrameworkTypeId.Named(assembly, "System.Object");
			var signature = new FrameworkMethodSignatureId(0x20, 0, 0, text, []);
			var member = new FrameworkMemberId(type, "ToString", signature);
			Assert.True(module.TryCreateExperimentalObjectJoinTextBinding(member, core, out var binding));
			Assert.Equal("CopperSharp.Runtime.ShadowObjectJoinText", binding.ShadowMethod!.TypeName);
			Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayCollect));
			Assert.False(binding.PreservesVirtualDispatch);
			Assert.False(module.TryCreateExperimentalObjectJoinTextBinding(member, entry, out _));
			Assert.False(module.TryCreateExperimentalObjectJoinTextBinding(member, wrapper, out _));
			Assert.False(module.TryCreateExperimentalObjectJoinTextBinding(member, core with { ModuleName = "User.Assembly" }, out _));
			Assert.False(module.TryCreateExperimentalObjectJoinTextBinding(member, core with { DisplayName = "System.Text.StringBuilder::AppendJoinCore<string>" }, out _));
			Assert.False(module.TryCreateExperimentalObjectJoinTextBinding(member, core with { MethodTypeArguments = [new CilType(CilTypeKind.ManagedReference, 4, "string")] }, out _));
			Assert.False(module.TryCreateExperimentalObjectJoinTextBinding(member, core with
			{
				Signature = new System.Reflection.Metadata.MethodSignature<CilType>(core.Signature.Header, core.Signature.ReturnType, 2, 1, core.Signature.ParameterTypes)
			}, out _));
			Assert.True(module.TryCreateExperimentalObjectJoinTextBinding(member, core with
			{
				Signature = new System.Reflection.Metadata.MethodSignature<CilType>(core.Signature.Header, core.Signature.ReturnType, 3, 1,
					[.. core.Signature.ParameterTypes.Take(2), new CilType(CilTypeKind.ManagedReference, 4, "System.Collections.Generic.IEnumerable`1<object>")])
			}, out _));
			Assert.False(module.TryCreateExperimentalObjectJoinTextBinding(new FrameworkMemberId(type, "GetHashCode", signature), core, out _));
			Assert.False(module.TryCreateExperimentalObjectJoinTextBinding(new FrameworkMemberId(type, "ToString", signature, [text]), core, out _));
			Assert.False(module.TryCreateExperimentalObjectJoinTextBinding(new FrameworkMemberId(FrameworkTypeId.Named("User.Assembly", "System.Object"), "ToString", signature), core, out _));
			foreach (var wrong in new[]
			{
				new FrameworkMethodSignatureId(0, 0, 0, text, []),
				new FrameworkMethodSignatureId(0x20, 1, 0, text, []),
				new FrameworkMethodSignatureId(0x20, 0, 1, text, [text]),
				new FrameworkMethodSignatureId(0x20, 0, 1, text, []),
				new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.Object"), [])
			}) Assert.False(module.TryCreateExperimentalObjectJoinTextBinding(new FrameworkMemberId(type, "ToString", wrong), core, out _));
			var description = new CilMethodReferenceIdentity(assembly, "System.Object", "ToString", false, 0, "string", [], []);
			FrameworkBindingDecision Classify(FrameworkBinding candidate, bool verified = true, bool scoped = true) =>
				Net10FrameworkContract.Default.Classify(member, description, MethodReference.ForBinding(candidate, default), null,
					useVerifiedCoreLibIdentity: verified, callSiteOverride: scoped ? binding : null);
			Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, Classify(binding).Status);
			Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Classify(binding, verified: false).Status);
			Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Classify(binding, scoped: false).Status);
			Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Classify(binding with { Target = "different.target" }).Status);
			Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Classify(binding with { PreservesVirtualDispatch = true }).Status);
			Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Classify(binding with { ShadowMethod = binding.ShadowMethod with { MethodName = "Other" } }).Status);
			Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Classify(binding with { EffectSummary = new FrameworkEffectSummary(binding.EffectSummary.Effects, []) }).Status);
			Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Classify(binding with { EffectSummary = new FrameworkEffectSummary(FrameworkEffects.ReadsManagedMemory, binding.EffectSummary.RequiredFeatures) }).Status);
			using var disabled = new CompilationModule(FixtureAssembly,
				frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
			Assert.False(disabled.TryCreateExperimentalObjectJoinTextBinding(member, core, out _));
		}
	}

	[Fact]
	public void ExperimentalObjectJoinDispatchKeepsOnlyReachableApplicationOverrides()
	{
		using var pack = CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(FixtureAssembly,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowObjectJoinText).Assembly.Location], frameworkImplementationPack: catalog);
		var caller = module.ResolveManagedMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowObjectJoinText::ToString");
		var call = caller.Instructions.Single(static instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Callvirt);
		var declaration = module.ResolveMethodToken((int)call.Operand!, caller, call.Offset).Definition!;
		Assert.True(module.IsObjectJoinDispatch(declaration));
		Assert.Equal(declaration, Assert.Single(module.GetVirtualImplementations(declaration)));
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibStringBuilderCustomObjectJoinEntry");
		foreach (var allocation in entry.Instructions.Where(static instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Newobj))
		{
			var constructor = module.ResolveMethodToken((int)allocation.Operand!, entry, allocation.Offset).Definition!;
			module.RegisterReachableDispatchLayout(module.GetTypeLayout(constructor));
		}
		var entries = module.GetObjectJoinDispatchEntries(declaration);
		Assert.True(entries.Count == 9, string.Join('\n', entries.Select(e => e.Layout.DisplayName + " -> " + e.Method.DisplayName)));
		Assert.All(entries, static e => Assert.Equal("CopperSharp.Compiler.Tests", e.Layout.ModuleName));
		foreach (var name in new[] { "InheritedObjectJoinValue", "HidingObjectJoinValue", "OverrideHidingObjectJoinValue" })
			Assert.Contains(entries, e => e.Layout.DisplayName == name && e.Method.DisplayName.Contains("BaseObjectJoinValue::ToString", StringComparison.Ordinal));
		Assert.Equal(2, entries.Count(static e => e.Layout.ConstructedType is not null));
		Assert.DoesNotContain(entries, static e => e.Layout.DisplayName.Contains("UnallocatedObjectJoinValue", StringComparison.Ordinal));
		Assert.DoesNotContain(entries, static e => e.Layout.DisplayName.Contains("NestedOnlyObjectJoinValue", StringComparison.Ordinal));
		var text = FrameworkTypeId.Primitive("System.String");
		var signature = new FrameworkMethodSignatureId(0x20, 0, 0, text, []);
		var member = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Object"), "ToString", signature);
		Assert.True(module.TryCreateExperimentalObjectJoinDispatchBinding(member, caller, out var binding));
		Assert.True(binding.PreservesVirtualDispatch);
		Assert.True(module.TryCreateExperimentalObjectJoinDispatchBinding(new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.Object"), "ToString", signature), caller, out _));
		Assert.False(module.TryCreateExperimentalObjectJoinDispatchBinding(member, caller with
		{
			Signature = new MethodSignature<CilType>(new SignatureHeader(0), caller.Signature.ReturnType, 0, 0, [])
		}, out _));
		Assert.False(module.TryCreateExperimentalObjectJoinDispatchBinding(member, entry, out _));
		Assert.False(module.TryCreateExperimentalObjectJoinDispatchBinding(member, caller with { ModuleName = "User.Assembly" }, out _));
		Assert.False(module.TryCreateExperimentalObjectJoinDispatchBinding(member, caller with { DisplayName = "Other.Type::ToString" }, out _));
		Assert.False(module.TryCreateExperimentalObjectJoinDispatchBinding(new FrameworkMemberId(member.DeclaringType, "GetHashCode", signature), caller, out _));
		Assert.False(module.TryCreateExperimentalObjectJoinDispatchBinding(new FrameworkMemberId(member.DeclaringType, "ToString", signature, [text]), caller, out _));
		Assert.False(module.TryCreateExperimentalObjectJoinDispatchBinding(new FrameworkMemberId(FrameworkTypeId.Named("User.Assembly", "System.Object"), "ToString", signature), caller, out _));
		foreach (var wrong in new[]
		{
			new FrameworkMethodSignatureId(0, 0, 0, text, []), new FrameworkMethodSignatureId(0x20, 1, 0, text, []),
			new FrameworkMethodSignatureId(0x20, 0, 1, text, [text]), new FrameworkMethodSignatureId(0x20, 0, 1, text, []),
			new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.Object"), [])
		}) Assert.False(module.TryCreateExperimentalObjectJoinDispatchBinding(new FrameworkMemberId(member.DeclaringType, "ToString", wrong), caller, out _));
		var description = new CilMethodReferenceIdentity("System.Runtime", "System.Object", "ToString", false, 0, "string", [], []);
		FrameworkBindingDecision Classify(FrameworkBinding candidate, bool verified = true, bool scoped = true) =>
			Net10FrameworkContract.Default.Classify(member, description, MethodReference.ForBinding(candidate, default), null,
				useVerifiedCoreLibIdentity: verified, callSiteOverride: scoped ? binding : null);
		Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, Classify(binding).Status);
		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Classify(binding, verified: false).Status);
		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Classify(binding, scoped: false).Status);
		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Classify(binding with { Target = "different.target" }).Status);
		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Classify(binding with { PreservesVirtualDispatch = false }).Status);
		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, Classify(binding with { EffectSummary = new FrameworkEffectSummary(binding.EffectSummary.Effects, []) }).Status);
		using var disabled = new CompilationModule(FixtureAssembly,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		Assert.False(disabled.TryCreateExperimentalObjectJoinDispatchBinding(member, caller, out _));
	}

	[Theory]
	[InlineData("string")]
	[InlineData("object")]
	[InlineData("int")]
	[InlineData("System.Decimal")]
	public void ExperimentalJoinEnumeratorRetainsPreciseReferences(string elementName)
	{
		using var pack = CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(FixtureAssembly,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowStringJoinEnumerator).Assembly.Location], frameworkImplementationPack: catalog);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::" + (elementName switch
		{
			"string" => "CoreLibStringJoinEnumeratorLifetimeAndMutationEntry",
			"int" => "CoreLibStringBuilderInt32EnumerableJoinLifetimeEntry",
			"System.Decimal" => "CoreLibStringBuilderDecimalJoinLifetimeEntry",
			_ => "CoreLibStringBuilderObjectEnumerableJoinLifetimeEntry"
		}));
		var factory = entry.Instructions.Where(static instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Call)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, entry, instruction.Offset).Definition)
			.First(method => method?.DisplayName == "CopperSharp.Runtime.Shadow" + (elementName switch { "string" => "String", "int" => "Int32", "System.Decimal" => "Decimal", _ => "Object" }) + "JoinEnumeration::GetEnumerator")!;
		var identity = module.ResolveRuntimeTypeIdentity(factory.Signature.ReturnType, factory.ModuleName);
		var layout = module.GetRuntimeTypeLayout(identity);
		Assert.Equal("CopperSharp.Runtime.Managed", layout.ModuleName);
		Assert.Equal(elementName == "System.Decimal" ? 44 : 32, layout.Size);
		Assert.Equal(elementName == "System.Decimal" ? 0x103u : elementName == "int" ? 0x23u : 0x33u, layout.ReferenceBitmap);
		Assert.Equal(new[] { 8, 12, 16, 20, 24, elementName == "System.Decimal" ? 40 : 28 }, layout.FieldOffsets.Values.Order());
	}

	[Fact]
	public void PinnedNestedMembersRetainTheirCompleteDeclaringTypeName()
	{
		var outer = FrameworkTypeId.Named("System.Runtime", "System.Text.StringBuilder");
		var nested = FrameworkTypeId.Named("System.Runtime", "ChunkEnumerator", outer);
		var deep = FrameworkTypeId.Named("System.Runtime", "ManyChunkInfo", nested);
		Assert.Equal("System.Text.StringBuilder+ChunkEnumerator+ManyChunkInfo", deep.FullMetadataName);
		var member = new FrameworkMemberId(nested, "MoveNext",
			new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.Boolean"), []));
		Assert.False(FrameworkImplementationProfile.TryCreatePinnedBinding(member, null, false, out _));
		Assert.True(FrameworkImplementationProfile.TryCreatePinnedBinding(member, null, true, out var binding));
		Assert.Equal("pinned:[System.Private.CoreLib]System.Text.StringBuilder+ChunkEnumerator::MoveNext", binding.Target);
		Assert.True(FrameworkImplementationProfile.IsPinnedBinding(binding));
	}

	[Fact]
	public void ExperimentalChunkEnumeratorTransportRetainsThreePreciseReferences()
	{
		using var pack = CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		{
			EnableUnlistedManagedBodies = true
		});
		using var module = new CompilationModule(FixtureAssembly, frameworkImplementationPack: catalog);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibStringBuilderChunksEntry");
		var type = entry.Locals.First(static type => type.DisplayName.Replace('/', '+') == "System.Text.StringBuilder+ChunkEnumerator");
		Assert.True(module.TryGetReferenceFreeStructLayout(type, entry.ModuleName, out var layout));
		Assert.Equal("System.Private.CoreLib", layout.ModuleName);
		Assert.Equal(12, layout.Size);
		Assert.Equal(7u, layout.ReferenceBitmap);
		Assert.Equal(new[] { 0, 4, 8 }, layout.FieldOffsets.Values.Order());
		Assert.False(module.TryGetReferenceFreeStructLayout(type with { DisplayName = "Different.ChunkEnumerator" }, entry.ModuleName, out _));
		using var disabled = new CompilationModule(FixtureAssembly);
		Assert.False(disabled.TryGetReferenceFreeStructLayout(type, entry.ModuleName, out _));
	}

	[Theory]
	[InlineData("Memmove")]
	[InlineData("Add")]
	[InlineData("GetArrayDataReference")]
	[InlineData("GetSpanReference")]
	[InlineData("GetReadOnlySpanReference")]
	[InlineData("GetRawStringData")]
	[InlineData("FastAllocateString")]
	[InlineData("StringFromSpan")]
	[InlineData("AllocateUninitializedArray")]
	[InlineData("GetDefaultMessage")]
	[InlineData("FormatOne")]
	[InlineData("FormatTwo")]
	[InlineData("FormatThree")]
	[InlineData("FormatFour")]
	[InlineData("FormatDecimal")]
	[InlineData("TryFormatDecimal")]
	[InlineData("TryFormatInt32")]
	[InlineData("FormatInt32")]
	[InlineData("Int32ToDecStr")]
	[InlineData("TryFormatUInt32")]
	[InlineData("FormatUInt32")]
	[InlineData("UInt32ToDecStr")]
	[InlineData("TryFormatInt64")]
	[InlineData("FormatInt64")]
	[InlineData("Int64ToDecStr")]
	[InlineData("TryFormatUInt64")]
	[InlineData("FormatUInt64")]
	[InlineData("UInt64ToDecStr")]
	[InlineData("AsSpanArray")]
	[InlineData("AsSpanStart")]
	[InlineData("AsSpanRange")]
	[InlineData("AppendLine")]
	[InlineData("AppendLineString")]
	[InlineData("CopyCharacters")]
	[InlineData("CopyIntegers")]
	[InlineData("ClearBytes")]
	[InlineData("ClearArrays")]
	[InlineData("ClearWithoutReferences")]
	[InlineData("AddIntegers")]
	[InlineData("AddBytes")]
	[InlineData("AddBytesNativeSigned")]
	[InlineData("AddBytesNativeUnsigned")]
	[InlineData("ByteOffsetsSigned")]
	[InlineData("ByteOffsetsUnsigned")]
	[InlineData("AddNativeIntegers")]
	[InlineData("AddNativeIntegersSigned")]
	[InlineData("AddNativeIntegersUnsigned")]
	[InlineData("FillCharacters")]
	[InlineData("ReplaceCharacters")]
	[InlineData("IndexOfCharacters")]
	[InlineData("EqualsOrdinalCharacters")]
	public void ExperimentalStringBuilderHelpersRequireExactSignaturesAndOptIn(string helper)
	{
		var element = FrameworkTypeId.GenericMethodParameter(0);
		var elementRef = FrameworkTypeId.ByReference(element);
		var text = FrameworkTypeId.Primitive("System.String");
		var obj = FrameworkTypeId.Primitive("System.Object");
		var integer = FrameworkTypeId.Primitive("System.Int32");
		var provider = FrameworkTypeId.Named("System.Runtime", "System.IFormatProvider");
		var spanOfElement = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Span`1"), [element]);
		var characterFormat = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"),
			[FrameworkTypeId.Primitive("System.Char")]);
		var (type, name, header, genericCount, result, parameters) = helper switch
		{
			"Memmove" => ("System.Buffer", helper, (byte)0x10, 1,
				FrameworkTypeId.Primitive("System.Void"),
				new[] { elementRef, elementRef, FrameworkTypeId.Primitive("System.UIntPtr") }),
			"Add" => ("System.Runtime.CompilerServices.Unsafe", helper, (byte)0x10, 1,
				elementRef, new[] { elementRef, FrameworkTypeId.Primitive("System.Int32") }),
			"GetArrayDataReference" => ("System.Runtime.InteropServices.MemoryMarshal", helper, (byte)0x10, 1,
				elementRef, new[] { FrameworkTypeId.SzArray(element) }),
			"GetSpanReference" => ("System.Runtime.InteropServices.MemoryMarshal", "GetReference", (byte)0x10, 1,
				elementRef, new[] { spanOfElement }),
			"GetReadOnlySpanReference" => ("System.Runtime.InteropServices.MemoryMarshal", "GetReference", (byte)0x10, 1,
				elementRef, new[] { FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [element]) }),
			"GetRawStringData" => ("System.String", helper, (byte)0x20, 0,
				FrameworkTypeId.ByReference(FrameworkTypeId.Primitive("System.Char")), Array.Empty<FrameworkTypeId>()),
			"FastAllocateString" => ("System.String", helper, (byte)0, 0,
				text, new[] { FrameworkTypeId.Primitive("System.IntPtr") }),
			"StringFromSpan" => ("System.String", ".ctor", (byte)0x20, 0,
				FrameworkTypeId.Primitive("System.Void"), new[] { characterFormat }),
			"AllocateUninitializedArray" => ("System.GC", helper, (byte)0x10, 1,
				FrameworkTypeId.SzArray(element),
				new[] { FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Boolean") }),
			"GetDefaultMessage" => ("System.OutOfMemoryException", helper, (byte)0, 0,
				text, Array.Empty<FrameworkTypeId>()),
			"FormatOne" => ("System.SR", "Format", (byte)0, 0, text, new[] { text, obj }),
			"FormatTwo" => ("System.SR", "Format", (byte)0, 0, text, new[] { text, obj, obj }),
			"FormatThree" => ("System.SR", "Format", (byte)0, 0, text, new[] { text, obj, obj, obj }),
			"FormatFour" => ("System.SR", "Format", (byte)0, 0, text, new[] { text, obj, obj, obj, obj }),
			"FormatDecimal" => ("System.Number", helper, (byte)0, 0, text,
				new[] { FrameworkTypeId.Named("System.Runtime", "System.Decimal"), characterFormat, FrameworkTypeId.Named("System.Runtime", "System.Globalization.NumberFormatInfo") }),
			"TryFormatDecimal" => ("System.Number", helper, (byte)0x10, 1, FrameworkTypeId.Primitive("System.Boolean"),
				new[] { FrameworkTypeId.Named("System.Runtime", "System.Decimal"), characterFormat, FrameworkTypeId.Named("System.Runtime", "System.Globalization.NumberFormatInfo"), spanOfElement, FrameworkTypeId.ByReference(integer) }),
			"TryFormatInt32" => ("System.Number", helper, (byte)0x10, 1, FrameworkTypeId.Primitive("System.Boolean"),
				new[] { integer, integer, characterFormat, provider, spanOfElement, FrameworkTypeId.ByReference(integer) }),
			"FormatInt32" => ("System.Number", helper, (byte)0, 0, text, new[] { integer, integer, text, provider }),
			"Int32ToDecStr" => ("System.Number", helper, (byte)0, 0, text, new[] { integer }),
			"TryFormatUInt32" => ("System.Number", helper, (byte)0x10, 1, FrameworkTypeId.Primitive("System.Boolean"),
				new[] { FrameworkTypeId.Primitive("System.UInt32"), characterFormat, provider, spanOfElement, FrameworkTypeId.ByReference(integer) }),
			"FormatUInt32" => ("System.Number", helper, (byte)0, 0, text, new[] { FrameworkTypeId.Primitive("System.UInt32"), text, provider }),
			"UInt32ToDecStr" => ("System.Number", helper, (byte)0, 0, text, new[] { FrameworkTypeId.Primitive("System.UInt32") }),
			"TryFormatInt64" or "TryFormatUInt64" => ("System.Number", helper, (byte)0x10, 1, FrameworkTypeId.Primitive("System.Boolean"),
				new[] { FrameworkTypeId.Primitive(helper == "TryFormatInt64" ? "System.Int64" : "System.UInt64"), characterFormat, provider, spanOfElement, FrameworkTypeId.ByReference(integer) }),
			"FormatInt64" or "FormatUInt64" => ("System.Number", helper, (byte)0, 0, text,
				new[] { FrameworkTypeId.Primitive(helper == "FormatInt64" ? "System.Int64" : "System.UInt64"), text, provider }),
			"Int64ToDecStr" or "UInt64ToDecStr" => ("System.Number", helper, (byte)0, 0, text,
				new[] { FrameworkTypeId.Primitive(helper == "Int64ToDecStr" ? "System.Int64" : "System.UInt64") }),
			"AsSpanArray" => ("System.MemoryExtensions", "AsSpan", (byte)0x10, 1, spanOfElement, new[] { FrameworkTypeId.SzArray(element) }),
			"AsSpanStart" => ("System.MemoryExtensions", "AsSpan", (byte)0x10, 1, spanOfElement, new[] { FrameworkTypeId.SzArray(element), integer }),
			"AsSpanRange" => ("System.MemoryExtensions", "AsSpan", (byte)0x10, 1, spanOfElement, new[] { FrameworkTypeId.SzArray(element), integer, integer }),
			"AppendLine" => ("System.Text.StringBuilder", "AppendLine", (byte)0x20, 0,
				FrameworkTypeId.Named("System.Runtime", "System.Text.StringBuilder"), Array.Empty<FrameworkTypeId>()),
			"AppendLineString" => ("System.Text.StringBuilder", "AppendLine", (byte)0x20, 0,
				FrameworkTypeId.Named("System.Runtime", "System.Text.StringBuilder"), new[] { text }),
			"CopyCharacters" => ("System.Array", "Copy", (byte)0, 0,
				FrameworkTypeId.Primitive("System.Void"), new[] {
					FrameworkTypeId.Named("System.Runtime", "System.Array"), FrameworkTypeId.Named("System.Runtime", "System.Array"), integer }),
			"CopyIntegers" => ("System.Array", "Copy", (byte)0, 0,
				FrameworkTypeId.Primitive("System.Void"), new[] {
					FrameworkTypeId.Named("System.Runtime", "System.Array"), integer, FrameworkTypeId.Named("System.Runtime", "System.Array"), integer, integer }),
			"ClearBytes" => ("System.Buffer", "ZeroMemoryInternal", (byte)0, 0,
				FrameworkTypeId.Primitive("System.Void"), new[] { FrameworkTypeId.Pointer(FrameworkTypeId.Primitive("System.Void")), FrameworkTypeId.Primitive("System.UIntPtr") }),
			"ClearArrays" => ("System.Array", "Clear", (byte)0, 0,
				FrameworkTypeId.Primitive("System.Void"), new[] { FrameworkTypeId.Named("System.Runtime", "System.Array"), integer, integer }),
			"ClearWithoutReferences" => ("System.SpanHelpers", helper, (byte)0, 0,
				FrameworkTypeId.Primitive("System.Void"), new[] { FrameworkTypeId.ByReference(FrameworkTypeId.Primitive("System.Byte")), FrameworkTypeId.Primitive("System.UIntPtr") }),
			"AddIntegers" or "AddBytes" or "AddNativeIntegers" => ("System.Runtime.CompilerServices.Unsafe", "Add", (byte)0x10, 1,
				elementRef, new[] { elementRef, integer }),
			"AddBytesNativeSigned" or "AddBytesNativeUnsigned" or "AddNativeIntegersSigned" or "AddNativeIntegersUnsigned" or "ByteOffsetsSigned" or "ByteOffsetsUnsigned" =>
				("System.Runtime.CompilerServices.Unsafe", helper.StartsWith("ByteOffsets", StringComparison.Ordinal) ? "AddByteOffset" : "Add", (byte)0x10, 1,
				 elementRef, new[] { elementRef, FrameworkTypeId.Primitive(helper.EndsWith("Unsigned", StringComparison.Ordinal) ? "System.UIntPtr" : "System.IntPtr") }),
			"FillCharacters" => ("System.SpanHelpers", "Fill", (byte)0x10, 1,
				FrameworkTypeId.Primitive("System.Void"), new[] { elementRef, FrameworkTypeId.Primitive("System.UIntPtr"), element }),
			"ReplaceCharacters" => ("System.MemoryExtensions", "Replace", (byte)0x10, 1,
				FrameworkTypeId.Primitive("System.Void"), new[] { spanOfElement, element, element }),
			"IndexOfCharacters" => ("System.MemoryExtensions", "IndexOf", (byte)0x10, 1,
				integer, new[] { FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [element]),
					FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [element]) }),
			"EqualsOrdinalCharacters" => ("System.MemoryExtensions", "EqualsOrdinal", (byte)0, 0,
				FrameworkTypeId.Primitive("System.Boolean"), new[] { characterFormat, characterFormat }),
			_ => throw new ArgumentOutOfRangeException(nameof(helper))
		};
		var assemblies = helper.StartsWith("AsSpan", StringComparison.Ordinal) || helper is "GetSpanReference" or "GetReadOnlySpanReference" or "ReplaceCharacters" or "IndexOfCharacters" or "EqualsOrdinalCharacters"
			? new[] { "System.Runtime", "System.Private.CoreLib", "System.Memory" }
			: new[] { "System.Runtime", "System.Private.CoreLib" };
		foreach (var assembly in assemblies)
		{
			var signature = new FrameworkMethodSignatureId(header, genericCount, parameters.Length, result, parameters);
			var elementType = FrameworkTypeId.Primitive(helper == "AddIntegers" ? "System.Int32"
				: helper.StartsWith("AddNativeIntegers", StringComparison.Ordinal) ? "System.IntPtr"
				: helper.StartsWith("AddBytes", StringComparison.Ordinal) || helper.StartsWith("ByteOffsets", StringComparison.Ordinal) ? "System.Byte" : "System.Char");
			FrameworkTypeId[] arguments = genericCount == 0 ? [] : [elementType];
			var member = new FrameworkMemberId(FrameworkTypeId.Named(assembly, type), name, signature, arguments);
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
			Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
			Assert.True(FrameworkImplementationProfile.IsTargetRuntimeOverride(binding));
			if (helper == "EqualsOrdinalCharacters")
			{
				Assert.Equal("CopperSharp.Runtime.ShadowCharacterSpans", binding.ShadowMethod!.TypeName);
				Assert.Equal("EqualsOrdinal", binding.ShadowMethod.MethodName);
				Assert.Equal(FrameworkEffects.ReadsManagedMemory, binding.EffectSummary.Effects);
				foreach (var wrongSpan in new[] {
					FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Byte")]),
					FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Span`1"), [FrameworkTypeId.Primitive("System.Char")]) })
				foreach (var parameter in new[] { 0, 1 })
				{
					var wrongParameters = parameters.ToArray(); wrongParameters[parameter] = wrongSpan;
					Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(member.DeclaringType, name,
						new FrameworkMethodSignatureId(header, 0, 2, result, wrongParameters)), true, out _));
				}
				Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(member.DeclaringType, "EqualsOrdinalIgnoreCase", signature), true, out _));
			}

			var wrongSignatures = new[]
			{
				new FrameworkMethodSignatureId((byte)(header ^ 0x20), genericCount, parameters.Length, result, parameters),
				new FrameworkMethodSignatureId(header, genericCount + 1, parameters.Length, result, parameters),
				new FrameworkMethodSignatureId(header, genericCount, parameters.Length + 1, result, parameters),
				new FrameworkMethodSignatureId(header, genericCount, parameters.Length,
					FrameworkTypeId.Primitive(result.Equals(FrameworkTypeId.Primitive("System.Boolean")) ? "System.String" : "System.Boolean"), parameters)
			};
			foreach (var wrong in wrongSignatures)
				Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
					new FrameworkMemberId(member.DeclaringType, name, wrong, arguments), true, out _));
			if (parameters.Length != 0)
				Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
					new FrameworkMemberId(member.DeclaringType, name,
						new FrameworkMethodSignatureId(header, genericCount, parameters.Length, result,
							[.. parameters.Take(parameters.Length - 1), FrameworkTypeId.Primitive(
								parameters[^1].Equals(FrameworkTypeId.Primitive("System.Boolean")) ? "System.String" : "System.Boolean")]), arguments), true, out _));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
				new FrameworkMemberId(FrameworkTypeId.Named("User.Assembly", type), name, signature, arguments), true, out _));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
				new FrameworkMemberId(member.DeclaringType, name, signature,
					[FrameworkTypeId.Primitive("System.Double")]), true, out _));
			if (helper is "TryFormatUInt32" or "FormatUInt32" or "UInt32ToDecStr" || helper.Contains("64", StringComparison.Ordinal) || helper.EndsWith("Decimal", StringComparison.Ordinal))
			{
				foreach (var wrongValueType in new[] { "System.Int32", "System.UInt32", "System.Int64", "System.UInt64" })
				{
					var wrongValue = FrameworkTypeId.Primitive(wrongValueType);
					if (wrongValue.Equals(parameters[0])) continue;
					Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
						new FrameworkMemberId(member.DeclaringType, name,
							new FrameworkMethodSignatureId(header, genericCount, parameters.Length, result,
								[wrongValue, .. parameters.Skip(1)]), arguments), true, out _));
				}
				if (helper.EndsWith("ToDecStr", StringComparison.Ordinal))
					Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
						new FrameworkMemberId(member.DeclaringType, name,
							new FrameworkMethodSignatureId(header, genericCount, 2, result, [.. parameters, integer])), true, out _));
			}
		}
	}

	[Theory]
	[InlineData("CoreLibSpanStringConstructionEntry", "FromCharacters")]
	[InlineData("CoreLibPointerStringConstructionEntry", "FromNullTerminatedCharacters")]
	public void ExperimentalStringConstructionUsesAFactoryWithThePublicConstructorSignature(string entryName, string helperName)
	{
		using var pack = CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(FixtureAssembly,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowStringData).Assembly.Location], frameworkImplementationPack: catalog);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::" + entryName);
		var construction = entry.Instructions.Where(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Newobj)
			.First(instruction => module.ResolveMethodToken((int)instruction.Operand!, entry, instruction.Offset).IsConstructorFactory);
		var factory = module.ResolveMethodToken((int)construction.Operand!, entry, construction.Offset);
		Assert.True(factory.Signature.Header.IsInstance);
		Assert.True(factory.Signature.ReturnType.IsVoid);
		Assert.Single(factory.Signature.ParameterTypes);
		Assert.False(factory.Definition!.Signature.Header.IsInstance);
		Assert.Equal("string", factory.Definition.Signature.ReturnType.DisplayName);
		Assert.Equal("CopperSharp.Runtime.ShadowStringData::" + helperName, factory.Definition.DisplayName);
		var invalidCaller = entry with { Instructions = entry.Instructions.Select(instruction => instruction.Offset == construction.Offset
			? instruction with { OpCode = System.Reflection.Emit.OpCodes.Call } : instruction).ToArray() };
		using var invalidModule = new CompilationModule(FixtureAssembly,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowStringData).Assembly.Location], frameworkImplementationPack: catalog);
		var error = Assert.Throws<M68kCompilationException>(() => invalidModule.ResolveMethodToken((int)construction.Operand!, invalidCaller, construction.Offset));
		Assert.Equal(M68kDiagnosticIds.UnsupportedInstruction, error.DiagnosticId);
		Assert.Contains("require newobj", error.Message);
	}

	[Fact]
	public void ExperimentalObjectArgumentBuffersUseEveryReferenceSlotAndVerifiedCallerHelpers()
	{
		using var pack = CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(FixtureAssembly,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowStringData).Assembly.Location], frameworkImplementationPack: catalog);
		foreach (var (name, size, bitmap) in new[] { ("System.TwoObjects", 8, 3u), ("System.ThreeObjects", 12, 7u) })
		{
			Assert.True(module.TryGetReferenceFreeStructLayout(new CilType(CilTypeKind.ValueType, 4, name), module.AssemblyName, out var layout));
			Assert.Equal("System.Private.CoreLib", layout.ModuleName);
			Assert.Equal(size, layout.Size); Assert.Equal(bitmap, layout.ReferenceBitmap);
		}
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibStringBuilderCompositeFormatContractEntry");
		var buffer = entry.Locals.Single(type => type.DisplayName.StartsWith("System.Runtime.CompilerServices.InlineArray4", StringComparison.Ordinal));
		Assert.True(module.TryGetReferenceFreeStructLayout(buffer, entry.ModuleName, out var bufferLayout));
		Assert.Equal(16, bufferLayout.Size); Assert.Equal(15u, bufferLayout.ReferenceBitmap);
		var calls = entry.Instructions.Where(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Call)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, entry, instruction.Offset)).ToArray();
		Assert.Equal(4, calls.Count(call => call.ImportName == "intrinsic:corelib-add-int32-ref"));
		var span = Assert.Single(calls.Where(call => call.ImportName == "intrinsic:readonly-span-from-ref-length:object"));
		Assert.Equal("System.ReadOnlySpan`1<object>", span.ConstructedDeclaringType!.DisplayName);
		Assert.Null(span.Definition);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ExperimentalObjectInlineArraySubstitutionRequiresTheCompleteHelperBody(bool extraInstruction)
	{
		using var pack = CoreLibPack.Create();
		var path = CoreLibCharacterFixtureBuilder.CreateObjectInlineArrayHelper(pack.Directory, extraInstruction);
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(path, frameworkImplementationPack: catalog);
		var entry = module.ResolveEntryPoint("ObjectInlineArrayProbe::Entry");
		var call = entry.Instructions.Single(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Call);
		if (extraInstruction)
		{
			var error = Assert.Throws<M68kCompilationException>(() => module.ResolveMethodToken((int)call.Operand!, entry, call.Offset));
			Assert.Equal(M68kDiagnosticIds.UnsupportedSignature, error.DiagnosticId);
		}
		else
		{
			var target = module.ResolveMethodToken((int)call.Operand!, entry, call.Offset);
			Assert.Equal("intrinsic:readonly-span-from-ref-length:object", target.ImportName);
			Assert.Null(target.Definition);
		}
	}

	[Theory]
	[InlineData("TypeFromHandle")]
	[InlineData("TypeIsEnum")]
	[InlineData("ObjectGetType")]
	[InlineData("TypeEquality")]
	[InlineData("TypeInequality")]
	[InlineData("ObjectSpan")]
	[InlineData("FirstObject")]
	[InlineData("ObjectAt")]
	[InlineData("ObjectBufferSpan")]
	[InlineData("AsRef")]
	[InlineData("As")]
	[InlineData("AddObjects")]
	[InlineData("AddCharactersNative")]
	[InlineData("IndexOfAnyCharacters")]
	public void ExperimentalFormattingDependenciesRequireExactSignaturesAndOptIn(string helper)
	{
		var first = FrameworkTypeId.GenericMethodParameter(0);
		var second = FrameworkTypeId.GenericMethodParameter(1);
		var integer = FrameworkTypeId.Primitive("System.Int32");
		var boolean = FrameworkTypeId.Primitive("System.Boolean");
		var character = FrameworkTypeId.Primitive("System.Char");
		var obj = FrameworkTypeId.Primitive("System.Object");
		var type = FrameworkTypeId.Named("System.Runtime", "System.Type");
		FrameworkTypeId Span(FrameworkTypeId element) => FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [element]);
		var (owner, name, genericCount, result, parameters, arguments) = helper switch
		{
			"TypeFromHandle" => ("System.Type", "GetTypeFromHandle", 0, type, new[] { FrameworkTypeId.Named("System.Runtime", "System.RuntimeTypeHandle") }, Array.Empty<FrameworkTypeId>()),
			"TypeIsEnum" => ("System.Type", "get_IsEnum", 0, boolean, Array.Empty<FrameworkTypeId>(), Array.Empty<FrameworkTypeId>()),
			"ObjectGetType" => ("System.Object", "GetType", 0, type, Array.Empty<FrameworkTypeId>(), Array.Empty<FrameworkTypeId>()),
			"TypeEquality" or "TypeInequality" => ("System.Type", helper == "TypeEquality" ? "op_Equality" : "op_Inequality", 0, boolean, new[] { type, type }, Array.Empty<FrameworkTypeId>()),
			"ObjectSpan" => ("System.Runtime.InteropServices.MemoryMarshal", "CreateReadOnlySpan", 1, Span(first), new[] { FrameworkTypeId.ByReference(first), integer }, new[] { obj }),
			"FirstObject" => ("<PrivateImplementationDetails>", "InlineArrayFirstElementRef", 2, FrameworkTypeId.ByReference(second), new[] { FrameworkTypeId.ByReference(first) }, new[] { FrameworkTypeId.Named("System.Runtime", "System.ThreeObjects"), obj }),
			"ObjectAt" => ("<PrivateImplementationDetails>", "InlineArrayElementRef", 2, FrameworkTypeId.ByReference(second), new[] { FrameworkTypeId.ByReference(first), integer }, new[] { FrameworkTypeId.Named("System.Runtime", "System.TwoObjects"), obj }),
			"ObjectBufferSpan" => ("<PrivateImplementationDetails>", "InlineArrayAsReadOnlySpan", 2, Span(second), new[] { FrameworkTypeId.ByReference(first), integer }, new[] { FrameworkTypeId.Named("System.Runtime", "System.ThreeObjects"), obj }),
			"AsRef" => ("System.Runtime.CompilerServices.Unsafe", "AsRef", 1, FrameworkTypeId.ByReference(first), new[] { FrameworkTypeId.ByReference(first) }, new[] { obj }),
			"As" => ("System.Runtime.CompilerServices.Unsafe", "As", 2, FrameworkTypeId.ByReference(second), new[] { FrameworkTypeId.ByReference(first) }, new[] { integer, obj }),
			"AddObjects" => ("System.Runtime.CompilerServices.Unsafe", "Add", 1, FrameworkTypeId.ByReference(first), new[] { FrameworkTypeId.ByReference(first), FrameworkTypeId.Primitive("System.UIntPtr") }, new[] { obj }),
			"AddCharactersNative" => ("System.Runtime.CompilerServices.Unsafe", "Add", 1, FrameworkTypeId.ByReference(first), new[] { FrameworkTypeId.ByReference(first), FrameworkTypeId.Primitive("System.IntPtr") }, new[] { character }),
			"IndexOfAnyCharacters" => ("System.MemoryExtensions", "IndexOfAny", 1, integer, new[] { Span(first), first, first }, new[] { character }),
			_ => throw new ArgumentOutOfRangeException(nameof(helper))
		};
		var header = (byte)(helper is "TypeIsEnum" or "ObjectGetType" ? 0x20 : genericCount == 0 ? 0 : 0x10);
		foreach (var assembly in new[] { "System.Runtime", "System.Private.CoreLib" })
		{
			var declaringType = FrameworkTypeId.Named(assembly, owner);
			var signature = new FrameworkMethodSignatureId(header, genericCount, parameters.Length, result, parameters);
			var member = new FrameworkMemberId(declaringType, name, signature, arguments);
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
			Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
			Assert.True(FrameworkImplementationProfile.IsTargetRuntimeOverride(binding));
			Assert.False(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect));
			foreach (var wrong in new[]
			{
				new FrameworkMethodSignatureId((byte)(header ^ 0x20), genericCount, parameters.Length, result, parameters),
				new FrameworkMethodSignatureId(header, genericCount + 1, parameters.Length, result, parameters),
				new FrameworkMethodSignatureId(header, genericCount, parameters.Length + 1, result, parameters),
				new FrameworkMethodSignatureId(header, genericCount, parameters.Length, FrameworkTypeId.Primitive("System.String"), parameters),
				new FrameworkMethodSignatureId(header, genericCount, parameters.Length, result, [FrameworkTypeId.Primitive("System.String"), .. parameters.Skip(1)])
			}) Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(declaringType, name, wrong, arguments), true, out _));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(FrameworkTypeId.Named("User.Assembly", owner), name, signature, arguments), true, out _));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(declaringType, name, signature, [.. arguments, obj]), true, out _));
			if (owner == "<PrivateImplementationDetails>")
				foreach (var wrongBuffer in new[] { FrameworkTypeId.Named("System.Runtime", "System.OneObject"), FrameworkTypeId.Named("User.Assembly", "System.ThreeObjects") })
					Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(declaringType, name, signature, [wrongBuffer, obj]), true, out _));
		}
	}

	[Theory]
	[InlineData("Grow")]
	[InlineData("Dispose")]
	public void ExperimentalIntegerAndCharacterListHelpersRequireExactConstructionAndOptIn(string name)
	{
		var integer = FrameworkTypeId.Primitive("System.Int32");
		var parameters = name == "Grow" ? new[] { integer } : Array.Empty<FrameworkTypeId>();
		var signature = new FrameworkMethodSignatureId(0x20, 0, parameters.Length, FrameworkTypeId.Primitive("System.Void"), parameters);
		foreach (var assembly in new[] { "System.Runtime", "System.Private.CoreLib" })
		foreach (var element in new[] { integer, FrameworkTypeId.Primitive("System.Char") })
		{
			var type = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(assembly, "System.Collections.Generic.ValueListBuilder`1"), [element]);
			var member = new FrameworkMemberId(type, name, signature);
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
			Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
			Assert.Equal(name == "Grow", binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayCollect));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(type, name, signature, [integer]), true, out _));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(type, name,
				new FrameworkMethodSignatureId(0, 0, parameters.Length, signature.ReturnType, parameters)), true, out _));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(type, name,
				new FrameworkMethodSignatureId(0x20, 0, parameters.Length + 1, signature.ReturnType, [..parameters, integer])), true, out _));
			foreach (var wrong in new[] {
				FrameworkTypeId.GenericInstantiation(type.ElementType!, [FrameworkTypeId.Primitive("System.Byte")]),
				FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("User.Assembly", "System.Collections.Generic.ValueListBuilder`1"), [integer]) })
				Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(wrong, name, signature), true, out _));
		}
	}

	[Theory]
	[InlineData(false, "ArrayRange")]
	[InlineData(true, "ArrayRange")]
	[InlineData(true, "Byref")]
	[InlineData(false, "Byref")]
	[InlineData(false, "Array")]
	[InlineData(true, "Array")]
	public void ExperimentalCharacterSpanConstructorsRequireExactSignaturesAndOptIn(bool readOnly, string construction)
	{
		var integer = FrameworkTypeId.Primitive("System.Int32");
		var element = FrameworkTypeId.GenericTypeParameter(0);
		var arrayOnly = construction == "Array";
		var parameters = arrayOnly ? new[] { FrameworkTypeId.SzArray(element) } : construction == "Byref"
			? new[] { FrameworkTypeId.ByReference(element), integer }
			: new[] { FrameworkTypeId.SzArray(element), integer, integer };
		var signature = new FrameworkMethodSignatureId(0x20, 0, parameters.Length,
			FrameworkTypeId.Primitive("System.Void"), parameters);
		foreach (var assembly in new[] { "System.Runtime", "System.Private.CoreLib" })
		{
			var type = FrameworkTypeId.GenericInstantiation(
				FrameworkTypeId.Named(assembly, readOnly ? "System.ReadOnlySpan`1" : "System.Span`1"),
				[FrameworkTypeId.Primitive("System.Char")]);
			var member = new FrameworkMemberId(type, ".ctor", signature);
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
			Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
			Assert.Equal(!arrayOnly, binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayThrow));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
				new FrameworkMemberId(type, ".ctor", new FrameworkMethodSignatureId(0, 0, parameters.Length,
					FrameworkTypeId.Primitive("System.Void"), parameters)), true, out _));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
				new FrameworkMemberId(type, ".ctor", signature, [FrameworkTypeId.Primitive("System.Char")]), true, out _));
			foreach (var mismatch in new[]
			{
				FrameworkTypeId.GenericInstantiation(type.ElementType!, [FrameworkTypeId.Primitive("System.Byte")]),
				FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("User.Assembly", type.ElementType!.MetadataName!),
					[FrameworkTypeId.Primitive("System.Char")])
			})
				Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
					new FrameworkMemberId(mismatch, ".ctor", signature), true, out _));
		}
	}

	[Theory]
	[InlineData("String")]
	[InlineData("Object")]
	public void ExperimentalReadonlyReferenceSpanConstructorsRequireExactSignaturesAndOptIn(string elementName)
	{
		var text = FrameworkTypeId.Primitive("System." + elementName);
		var integer = FrameworkTypeId.Primitive("System.Int32");
		var genericArray = FrameworkTypeId.SzArray(FrameworkTypeId.GenericTypeParameter(0));
		foreach (var assembly in new[] { "System.Runtime", "System.Private.CoreLib" })
		foreach (var ranged in new[] { false, true })
		{
			var type = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(assembly, "System.ReadOnlySpan`1"), [text]);
			var parameters = ranged ? new[] { genericArray, integer, integer } : new[] { genericArray };
			var signature = new FrameworkMethodSignatureId(0x20, 0, parameters.Length, FrameworkTypeId.Primitive("System.Void"), parameters);
			var member = new FrameworkMemberId(type, ".ctor", signature);
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
			Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
			Assert.Equal(ranged ? "intrinsic:span-from-array-range:" + elementName.ToLowerInvariant() : "intrinsic:readonly-span-from-array-ctor:" + elementName.ToLowerInvariant(), binding.Target);
			Assert.Equal(ranged, binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayThrow));
			Assert.False(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayCollect));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(type, ".ctor", signature, [text]), true, out _));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(type, "Create", signature), true, out _));
			foreach (var wrongSignature in new[]
			{
				new FrameworkMethodSignatureId(0, 0, parameters.Length, signature.ReturnType, parameters),
				new FrameworkMethodSignatureId(0x20, 1, parameters.Length, signature.ReturnType, parameters),
				new FrameworkMethodSignatureId(0x20, 0, parameters.Length, text, parameters),
				new FrameworkMethodSignatureId(0x20, 0, parameters.Length + 1, signature.ReturnType, parameters),
				new FrameworkMethodSignatureId(0x20, 0, parameters.Length, signature.ReturnType, [FrameworkTypeId.SzArray(integer), .. parameters.Skip(1)]),
				new FrameworkMethodSignatureId(0x20, 0, 2, signature.ReturnType, [FrameworkTypeId.ByReference(FrameworkTypeId.GenericTypeParameter(0)), integer])
			})
				Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(type, ".ctor", wrongSignature), true, out _));
			foreach (var wrongType in new[]
			{
				FrameworkTypeId.GenericInstantiation(type.ElementType!, [FrameworkTypeId.Primitive("System.Int64")]),
				FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("User.Assembly", "System.ReadOnlySpan`1"), [text])
			})
				Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(wrongType, ".ctor", signature), true, out _));
		}
	}

	[Fact]
	public void ExperimentalWritableCharacterSpanDoesNotAdmitBorrowedCallerOwners()
	{
		using var pack = CoreLibPack.Create();
		// Host reflection retains the generated assembly, so keep it in test
		// output alongside the execution probes rather than the disposable pack.
		var directory = Path.Combine(AppContext.BaseDirectory, "CoreLibCharacterProbes", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		var path = CoreLibCharacterFixtureBuilder.Create(directory);
		var exception = Assert.Throws<M68kCompilationException>(() => M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = path,
			ManagedAssemblyPaths = [typeof(M68kRuntime).Assembly.Location, typeof(CopperSharp.Runtime.ShadowArray).Assembly.Location],
			EntryPoint = "CoreLibCharacterProbe::BorrowedWritableSpanEntry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Assembly,
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true }
		}));
		Assert.Equal(M68kDiagnosticIds.UnsupportedInstruction, exception.DiagnosticId);
		Assert.Contains("CallerBorrowed", exception.Message);
	}

	[Fact]
	public void ExperimentalIntegerMatchListHasMatchingCoreLibAndShadowReferenceLayout()
	{
		using var pack = CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		{
			EnableUnlistedManagedBodies = true
		});
		var path = CoreLibCharacterFixtureBuilder.Create(pack.Directory);
		using var module = new CompilationModule(path,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowArray).Assembly.Location], frameworkImplementationPack: catalog);
		var entry = module.ResolveEntryPoint("CoreLibCharacterProbe::ValueListEntry");
		var official = entry.Locals.Single(CompilationModule.IsIntegerValueListBuilder);
		var shadow = official with { DisplayName = "CopperSharp.Runtime.ShadowValueListBuilder`1<int>" };
		using var shadowModule = new CompilationModule(typeof(CopperSharp.Runtime.ShadowArray).Assembly.Location, frameworkImplementationPack: catalog);
		Assert.True(module.TryGetStructLayout(official, entry.ModuleName, out var officialLayout));
		Assert.True(shadowModule.TryGetStructLayout(shadow, shadowModule.AssemblyName, out var shadowLayout));
		foreach (var layout in new[] { officialLayout, shadowLayout })
		{
			Assert.Equal(20, layout.Size);
			Assert.Equal(0xCu, layout.ReferenceBitmap);
			Assert.Equal(new[] { 0, 12, 16 }, layout.FieldOffsets.Values.Order());
		}
		Assert.False(CompilationModule.IsIntegerValueListBuilder(official with
		{
			GenericArguments = [new CilType(CilTypeKind.Character, 2, "char")]
		}));
	}

	[Theory]
	[InlineData("System.Runtime")]
	[InlineData("System.Private.CoreLib")]
	public void ExperimentalStringEmptyFieldRequiresExactIdentityAndOptIn(string assembly)
	{
		Assert.NotNull(FrameworkImplementationProfile.TryCreateTargetRuntimeFieldOverride(
			assembly, "System.String", "Empty", "string", true));
		Assert.Null(FrameworkImplementationProfile.TryCreateTargetRuntimeFieldOverride(
			assembly, "System.String", "Empty", "string", false));
		Assert.Null(FrameworkImplementationProfile.TryCreateTargetRuntimeFieldOverride(
			"User.Assembly", "System.String", "Empty", "string", true));
		Assert.Null(FrameworkImplementationProfile.TryCreateTargetRuntimeFieldOverride(
			assembly, "User.String", "Empty", "string", true));
		Assert.Null(FrameworkImplementationProfile.TryCreateTargetRuntimeFieldOverride(
			assembly, "System.String", "Empty", "object", true));
	}

	[Theory]
	[InlineData("EmptyAddressEntry")]
	[InlineData("EmptyStoreEntry")]
	public void ExperimentalStringEmptyFieldRejectsAddressTakingAndWrites(string selector)
	{
		using var pack = CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		{
			EnableUnlistedManagedBodies = true
		});
		var path = CoreLibCharacterFixtureBuilder.Create(pack.Directory);
		using var module = new CompilationModule(path, frameworkImplementationPack: catalog);
		var entry = module.ResolveEntryPoint($"CoreLibCharacterProbe::{selector}");
		var access = entry.Instructions.Single(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Ldsflda ||
			instruction.OpCode == System.Reflection.Emit.OpCodes.Stsfld);
		var exception = Assert.Throws<M68kCompilationException>(() =>
			module.ResolveFieldToken((int)access.Operand!, entry, access.Offset));
		Assert.Equal(M68kDiagnosticIds.UnsupportedInstruction, exception.DiagnosticId);
		Assert.Contains("read-only static field", exception.Message);
	}

	[Fact]
	public void ExperimentalCoreLibCharAndValueTypeDefinitionsUseTargetRepresentation()
	{
		using var pack = CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		{
			EnableUnlistedManagedBodies = true
		});
		using var module = new CompilationModule(FixtureAssembly, frameworkImplementationPack: catalog);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibStringBuilderConstructorEntry");
		var construct = entry.Instructions.First(static instruction =>
			instruction.OpCode == System.Reflection.Emit.OpCodes.Newobj);
		var constructor = module.ResolveMethodToken((int)construct.Operand!, entry, construct.Offset).Definition!;
		var allocation = constructor.Instructions.First(static instruction =>
			instruction.OpCode == System.Reflection.Emit.OpCodes.Newarr);
		var character = module.ResolveTypeToken((int)allocation.Operand!, constructor, allocation.Offset);
		Assert.Equal(CilTypeKind.Character, character.Kind);
		Assert.Equal(2, character.Size);
		var typeHandle = module.ResolveRuntimeTypeIdentity(
			new CilType(CilTypeKind.ValueType, 0, "System.Runtime.CompilerServices.TypeHandle"), constructor.ModuleName);
		Assert.Equal(4, module.GetRuntimeTypeLayout(typeHandle).Size);
	}

	[Theory]
	[InlineData("System.Runtime", "System.Exception", "ShadowException")]
	[InlineData("System.Private.CoreLib", "System.Exception", "ShadowException")]
	[InlineData("System.Runtime", "System.Runtime.InteropServices.ExternalException", "ShadowExternalException")]
	[InlineData("System.Private.CoreLib", "System.Runtime.InteropServices.ExternalException", "ShadowExternalException")]
	public void ExperimentalProfileCutsOffExceptionToStringOnlyWhenUnlistedBodiesAreEnabled(
		string assemblyName, string typeName, string shadowType)
	{
		var member = new FrameworkMemberId(
			FrameworkTypeId.Named(assemblyName, typeName),
			"ToString",
			new FrameworkMethodSignatureId(
				0x20,
				0,
				0,
				FrameworkTypeId.Primitive("System.String"),
				[]));

		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
			member,
			enableUnlistedManagedBodies: false,
			out _));
		Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
			member,
			enableUnlistedManagedBodies: true,
			out var binding));
		Assert.Equal(FrameworkBindingKind.ShadowMethod, binding.Kind);
		Assert.Equal(
			$"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.{shadowType}::ToString",
			binding.Target);
		Assert.True(binding.PreservesVirtualDispatch);
		Assert.True(FrameworkImplementationProfile.IsTargetRuntimeOverride(binding));
	}

	[Theory]
	[InlineData("User.Assembly", "System.Runtime.InteropServices.ExternalException")]
	[InlineData("System.Runtime", "System.Runtime.InteropServices.COMException")]
	public void ExperimentalExternalExceptionOverrideDoesNotAdmitUnrelatedIdentities(
		string assemblyName, string typeName)
	{
		var member = new FrameworkMemberId(
			FrameworkTypeId.Named(assemblyName, typeName),
			"ToString",
			new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.String"), []));
		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
			member, enableUnlistedManagedBodies: true, out _));
	}

	[Fact]
	public void ExperimentalProfileCutsOffCultureInfoDateTimeFormatOnlyWhenUnlistedBodiesAreEnabled()
	{
		var member = new FrameworkMemberId(
			FrameworkTypeId.Named("System.Runtime", "System.Globalization.CultureInfo"),
			"get_DateTimeFormat",
			new FrameworkMethodSignatureId(
				0x20,
				0,
				0,
				FrameworkTypeId.Named(
					"System.Runtime",
					"System.Globalization.DateTimeFormatInfo"),
				[]));

		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
			member,
			enableUnlistedManagedBodies: false,
			out _));
		Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
			member,
			enableUnlistedManagedBodies: true,
			out var binding));
		Assert.Equal(FrameworkBindingKind.ShadowMethod, binding.Kind);
		Assert.Equal(
			"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowCultureInfo::GetDateTimeFormat",
			binding.Target);
		Assert.True(binding.PreservesVirtualDispatch);
		Assert.True(FrameworkImplementationProfile.IsTargetRuntimeOverride(binding));
	}

	[Fact]
	public void ExperimentalProfileUsesSystemResourceKeysOnlyWhenUnlistedBodiesAreEnabled()
	{
		var member = new FrameworkMemberId(
			FrameworkTypeId.Named("System.Runtime", "System.SR"),
			"GetResourceString",
			new FrameworkMethodSignatureId(
				0,
				0,
				1,
				FrameworkTypeId.Primitive("System.String"),
				[FrameworkTypeId.Primitive("System.String")]));

		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
			member,
			enableUnlistedManagedBodies: false,
			out _));
		Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
			member,
			enableUnlistedManagedBodies: true,
			out var binding));
		Assert.Equal(FrameworkBindingKind.ShadowMethod, binding.Kind);
		Assert.Equal(
			"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowSystemResources::GetResourceString",
			binding.Target);
		Assert.False(binding.PreservesVirtualDispatch);
		Assert.Equal(
			FrameworkTypeInitializerPolicy.TargetOwned,
			binding.TypeInitializerPolicy);
		Assert.True(FrameworkImplementationProfile.IsTargetRuntimeOverride(binding));
		Assert.False(FrameworkImplementationProfile.IsTargetRuntimeOverride(
			binding with
			{
				TypeInitializerPolicy = FrameworkTypeInitializerPolicy.Implementation
			}));
	}

	[Fact]
	public void ExperimentalProfileOwnsSystemResourceTypeInitialization()
	{
		var getter = new FrameworkMemberId(
			FrameworkTypeId.Named("System.Runtime", "System.SR"),
			"get_Arg_IndexOutOfRangeException",
			new FrameworkMethodSignatureId(
				0,
				0,
				0,
				FrameworkTypeId.Primitive("System.String"),
				[]));

		Assert.False(FrameworkImplementationProfile.TryCreatePinnedBinding(
			getter,
			fallback: null,
			enableUnlistedManagedBodies: false,
			out _));
		Assert.True(FrameworkImplementationProfile.TryCreatePinnedBinding(
			getter,
			fallback: null,
			enableUnlistedManagedBodies: true,
			out var binding));
		Assert.Equal(FrameworkBindingKind.PinnedManagedBody, binding.Kind);
		Assert.Equal(
			FrameworkTypeInitializerPolicy.TargetOwned,
			binding.TypeInitializerPolicy);
		Assert.True(FrameworkImplementationProfile.IsPinnedBinding(binding));
	}

	[Fact]
	public void CoreLibEnumWithSameModuleSystemEnumBaseUsesUnderlyingScalarType()
	{
		using var pack = CoreLibPack.Create();
		using var stream = File.OpenRead(pack.AssemblyPath);
		using var peReader = new PEReader(stream, PEStreamOptions.PrefetchEntireImage);
		var reader = peReader.GetMetadataReader();
		var provider = new CilSignatureTypeProvider();

		Assert.True(provider.TryGetDefinedEnumType(
			reader,
			"System.ExceptionArgument",
			out var enumType));
		Assert.True(provider.TryGetDefinedEnumType(
			reader,
			"System.ExceptionArgument",
			out var cachedEnumType));
		Assert.Same(enumType, cachedEnumType);
		Assert.True(enumType.IsEnum);
		Assert.Equal(CilTypeKind.SignedInteger, enumType.Kind);
		Assert.Equal(4, enumType.Size);
	}

	[Fact]
	public void ExperimentalCoreLibVirtualDiscoveryUsesOnlyReachableAllocatedLayouts()
	{
		using var pack = CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(
			new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
			{
				EnableUnlistedManagedBodies = true
			});
		using var module = new CompilationModule(FixtureAssembly, frameworkImplementationPack: catalog);
		var declaration = module.ResolveManagedMethod("System.Private.CoreLib", "System.Object::ToString");
		Assert.Empty(module.GetVirtualImplementations(declaration));
		var entry = module.ResolveEntryPoint(
			"CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibStringBuilderAppendIntEntry");
		var allocation = entry.Instructions.First(static instruction =>
			instruction.OpCode == System.Reflection.Emit.OpCodes.Newobj);
		var constructor = module.ResolveMethodToken((int)allocation.Operand!, entry, allocation.Offset).Definition!;
		module.RegisterReachableDispatchLayout(module.GetTypeLayout(constructor));
		var implementation = Assert.Single(module.GetVirtualImplementations(declaration));
		Assert.Equal("System.Text.StringBuilder::ToString", implementation.DisplayName);
	}

	[Fact]
	public void ValidCoreLibPackIsReportedAndSelectsPinnedStopwatchBodies()
	{
		using var pack = CoreLibPack.Create();
		var analysis = AmigaM68kCompiler.AnalyzeFramework(Request(pack.ManifestPath));

		var provenance = Assert.IsType<M68kFrameworkImplementationPackProvenance>(
			analysis.ImplementationPack);
		Assert.Equal("corelib-common-il-v1", provenance.ImplementationProfile);
		var assembly = Assert.Single(provenance.Assemblies);
		Assert.Equal("System.Private.CoreLib", assembly.Name);
		Assert.Equal(pack.Sha256, assembly.Sha256);
		var stopwatch = analysis.Members
			.Where(static member =>
				member.Member.TypeName == "System.Diagnostics.Stopwatch" &&
				member.Member.Name is ".ctor" or "Start" or "Stop" or "Reset" or
					"Restart" or "StartNew" or "get_IsRunning" or "get_ElapsedTicks")
			.ToArray();
		Assert.NotEmpty(stopwatch);
		Assert.All(stopwatch, member =>
		{
			Assert.Equal("System.Runtime", member.Member.AssemblyName);
			Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status);
			Assert.StartsWith("pinned:[System.Private.CoreLib]", member.Binding, StringComparison.Ordinal);
		});
	}

	[Fact]
	public void PackEnabledCompilationLinksOnlyReachableCoreLibStopwatchBodies()
	{
		using var pack = CoreLibPack.Create();
		var result = AmigaM68kCompiler.Compile(Request(pack.ManifestPath));

		Assert.Contains("IMPLEMENTATION PACK", result.Map, StringComparison.Ordinal);
		Assert.Contains($"sha256={pack.Sha256}", result.Map, StringComparison.Ordinal);
		Assert.Contains(
			result.Symbols,
			static symbol => symbol.Name == "System.Diagnostics.Stopwatch::.ctor");
		Assert.Contains(
			result.Symbols,
			static symbol => symbol.Name == "System.Diagnostics.Stopwatch::Reset");
		Assert.DoesNotContain(
			result.Symbols,
			static symbol => symbol.Name.Contains("ShadowStopwatch", StringComparison.Ordinal));
		Assert.DoesNotContain(
			result.Symbols,
			static symbol => symbol.Name == "System.Diagnostics.Stopwatch::.cctor");
		Assert.DoesNotContain(
			result.Symbols,
			static symbol => symbol.Name is
				"System.Diagnostics.Stopwatch::Start" or
				"System.Diagnostics.Stopwatch::StartNew" or
				"System.Diagnostics.Stopwatch::Stop" or
				"System.Diagnostics.Stopwatch::Restart" or
				"System.Diagnostics.Stopwatch::get_ElapsedTicks");
		Assert.DoesNotContain(
			result.Symbols,
			static symbol => symbol.Name.Contains("ClockPal", StringComparison.Ordinal));
	}

	[Fact]
	public void PinnedDependenciesRetainPublicIdentityAndPalPrecedence()
	{
		using var pack = CoreLibPack.Create();
		var result = AmigaM68kCompiler.Compile(Request(
			pack.ManifestPath,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableStopwatchInstanceEntry"));

		var stopwatch = result.FrameworkAnalysis.Members
			.Where(static member => member.Member.TypeName == "System.Diagnostics.Stopwatch")
			.ToArray();
		Assert.NotEmpty(stopwatch);
		Assert.All(stopwatch, static member =>
			Assert.Equal("System.Runtime", member.Member.AssemblyName));
		Assert.Contains(result.Symbols, static symbol =>
			symbol.Name.EndsWith("ClockPal::GetTimestamp", StringComparison.Ordinal));
		Assert.DoesNotContain(result.Symbols, static symbol =>
			symbol.Name == "System.Diagnostics.Stopwatch::GetTimestamp");
		Assert.DoesNotContain(result.Symbols, static symbol =>
			symbol.Name == "System.Object::.ctor");
	}

	[Fact]
	public void PublicGetTimestampStillUsesTheAmigaPalWithAPack()
	{
		using var pack = CoreLibPack.Create();
		var analysis = AmigaM68kCompiler.AnalyzeFramework(Request(
			pack.ManifestPath,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableStopwatchTimestampEntry"));

		var timestamp = Assert.Single(analysis.Members, static member =>
			member.Member.TypeName == "System.Diagnostics.Stopwatch" &&
			member.Member.Name == "GetTimestamp");
		Assert.Equal("System.Runtime", timestamp.Member.AssemblyName);
		Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, timestamp.Status);
		Assert.Equal("platform:amiga-stopwatch-get-timestamp", timestamp.Binding);
	}

	[Fact]
	public void StopwatchElapsedValuesUseTargetOwnedScalingWhenPackIsConfigured()
	{
		using var pack = CoreLibPack.Create();
		var request = Request(
			pack.ManifestPath,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableStopwatchElapsedValuesEntry");
		var analysis = AmigaM68kCompiler.AnalyzeFramework(request);

		var elapsed = Assert.Single(analysis.Members, static member =>
			member.Member.TypeName == "System.Diagnostics.Stopwatch" &&
			member.Member.Name == "get_ElapsedMilliseconds");
		Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, elapsed.Status);
		Assert.Equal("platform:amiga-stopwatch-elapsed-milliseconds", elapsed.Binding);
		var result = AmigaM68kCompiler.Compile(request);
		Assert.DoesNotContain(
			result.Symbols,
			static symbol => symbol.Name == "System.Diagnostics.Stopwatch::.cctor");
	}

	[Fact]
	public void ValidCoreLibPackSelectsPinnedTimeSpanLeafBodies()
	{
		using var pack = CoreLibPack.Create();
		var request = Request(
			pack.ManifestPath,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortablePinnedTimeSpanEntry");
		var analysis = AmigaM68kCompiler.AnalyzeFramework(request);

		var members = analysis.Members
			.Where(static member => member.Member.TypeName == "System.TimeSpan")
			.ToArray();
		Assert.Equal(14, members.Length);
		Assert.All(
			members.Where(static member => member.Member.Name is
				".ctor" or "FromTicks" or "get_Days" or "get_Hours" or
				"get_Minutes" or "get_Seconds" or "get_Milliseconds"),
			static member => Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, member.Status));
		Assert.All(members.Where(static member => member.Member.Name is
			not ".ctor" and not "FromTicks" and not "get_Days" and not "get_Hours" and
			not "get_Minutes" and not "get_Seconds" and not "get_Milliseconds"), static member =>
		{
			Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status);
			Assert.StartsWith(
				"pinned:[System.Private.CoreLib]System.TimeSpan::",
				member.Binding,
				StringComparison.Ordinal);
		});

		var result = AmigaM68kCompiler.Compile(request);
		Assert.Contains(result.Symbols, static symbol =>
			symbol.Name == "System.TimeSpan::get_Ticks");
		Assert.Contains(result.Symbols, static symbol =>
			symbol.Name.Contains("ShadowTimeSpan::Initialize", StringComparison.Ordinal));
		Assert.DoesNotContain(result.Symbols, static symbol =>
			symbol.Name.Contains("ShadowTimeSpan::Equal", StringComparison.Ordinal) ||
			symbol.Name.Contains("ShadowTimeSpan::LessThan", StringComparison.Ordinal) ||
			symbol.Name.Contains("ShadowTimeSpan::GreaterThan", StringComparison.Ordinal));
		Assert.DoesNotContain(result.Symbols, static symbol =>
			symbol.Name == "System.TimeSpan::.cctor");
		Assert.Equal(42, CompilerFixtures.PortablePinnedTimeSpanEntry());
	}

	[Fact]
	public void TimeSpanTotalGettersUseExactPalOverrides()
	{
		using var pack = CoreLibPack.Create();
		var request = Request(
			pack.ManifestPath,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableTimeSpanTotalsEntry",
			M68kCpuTarget.M68040,
			M68kFloatingPointMode.M68040);
		var analysis = AmigaM68kCompiler.AnalyzeFramework(request);

		var members = analysis.Members.Where(static candidate =>
			candidate.Member.TypeName == "System.TimeSpan").ToArray();
		Assert.Contains(members, static member => member.Member.Name == "FromTicks");
		Assert.Equal(5, members.Count(static member =>
			member.Member.Name.StartsWith("get_Total", StringComparison.Ordinal)));
		Assert.Equal(
			M68kFrameworkCompatibilityStatus.Platform,
			Assert.Single(members, static member => member.Member.Name == "FromTicks").Status);
		Assert.All(members.Where(static member =>
			member.Member.Name != "FromTicks"), static member =>
			Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, member.Status));

		var result = AmigaM68kCompiler.Compile(request);
		Assert.Contains(result.Symbols, static symbol =>
			symbol.Name.Contains("ShadowTimeSpan::GetTotalDays", StringComparison.Ordinal) ||
			symbol.Name.Contains("ShadowTimeSpan::GetTotalHours", StringComparison.Ordinal) ||
			symbol.Name.Contains("ShadowTimeSpan::GetTotalMinutes", StringComparison.Ordinal) ||
			symbol.Name.Contains("ShadowTimeSpan::GetTotalSeconds", StringComparison.Ordinal));
		Assert.Equal(42, CompilerFixtures.PortableTimeSpanTotalsEntry());
	}

	[Fact]
	public void UnchangedPackProducesByteIdenticalOutputAndProvenance()
	{
		using var pack = CoreLibPack.Create();
		var first = AmigaM68kCompiler.Compile(Request(pack.ManifestPath));
		var second = AmigaM68kCompiler.Compile(Request(pack.ManifestPath));

		Assert.Equal(first.Image, second.Image);
		Assert.Equal(first.Map, second.Map);
		var firstPack = Assert.IsType<M68kFrameworkImplementationPackProvenance>(
			first.FrameworkAnalysis.ImplementationPack);
		var secondPack = Assert.IsType<M68kFrameworkImplementationPackProvenance>(
			second.FrameworkAnalysis.ImplementationPack);
		Assert.Equal(firstPack with { Assemblies = [] }, secondPack with { Assemblies = [] });
		Assert.Equal(firstPack.Assemblies.ToArray(), secondPack.Assemblies.ToArray());
	}

	[Fact]
	public void StaticFieldOnlyUseDoesNotLinkCoreLibInitializationOrClockPal()
	{
		using var pack = CoreLibPack.Create();
		var result = AmigaM68kCompiler.Compile(Request(
			pack.ManifestPath,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableStopwatchHighResolutionEntry"));

		Assert.DoesNotContain(result.Symbols, static symbol =>
			symbol.Name.Contains("ClockPal", StringComparison.Ordinal));
		Assert.DoesNotContain(result.Symbols, static symbol =>
			symbol.Name.StartsWith("System.Diagnostics.Stopwatch::", StringComparison.Ordinal));
		Assert.DoesNotContain(result.Symbols, static symbol =>
			symbol.Name.Contains("ShadowStopwatch", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("schemaVersion", "2")]
	[InlineData("targetFramework", "net9.0")]
	[InlineData("referencePackVersion", "10.0.8")]
	[InlineData("implementationProfile", "automatic")]
	public void ManifestContractMismatchFailsBeforeAnalysis(string property, string replacement)
	{
		using var pack = CoreLibPack.Create();
		pack.Replace(property, replacement);

		var exception = Assert.Throws<M68kCompilationException>(() =>
			AmigaM68kCompiler.AnalyzeFramework(Request(pack.ManifestPath)));
		Assert.Equal(M68kDiagnosticIds.InvalidInput, exception.DiagnosticId);
		Assert.Contains(property, exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ManifestHashMismatchFailsClosed()
	{
		using var pack = CoreLibPack.Create();
		pack.ReplaceAssembly("sha256", new string('0', 64));

		var exception = Assert.Throws<M68kCompilationException>(() =>
			AmigaM68kCompiler.AnalyzeFramework(Request(pack.ManifestPath)));
		Assert.Equal(M68kDiagnosticIds.InvalidInput, exception.DiagnosticId);
		Assert.Contains("sha256", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void MalformedManifestFailsClosed()
	{
		using var pack = CoreLibPack.Create();
		File.WriteAllText(pack.ManifestPath, "{ definitely-not-json");

		AssertInvalidPack(pack, "manifest");
	}

	[Fact]
	public void MissingImplementationAssemblyFailsClosed()
	{
		using var pack = CoreLibPack.Create();
		File.Delete(pack.AssemblyPath);

		AssertInvalidPack(pack, "does not exist");
	}

	[Theory]
	[InlineData("name", "Other.CoreLib")]
	[InlineData("version", "0.0.0.0")]
	[InlineData("publicKeyToken", "0000000000000000")]
	[InlineData("mvid", "00000000-0000-0000-0000-000000000001")]
	public void AssemblyMetadataMismatchFailsClosed(string property, string replacement)
	{
		using var pack = CoreLibPack.Create();
		pack.ReplaceAssembly(property, replacement);

		AssertInvalidPack(pack, property);
	}

	[Fact]
	public void DuplicateCoreLibIdentityFailsClosed()
	{
		using var pack = CoreLibPack.Create();
		var document = JsonNode.Parse(File.ReadAllText(pack.ManifestPath))!.AsObject();
		var assemblies = document["assemblies"]!.AsArray();
		assemblies.Add(assemblies[0]!.DeepClone());
		File.WriteAllText(pack.ManifestPath, document.ToJsonString());

		AssertInvalidPack(pack, "exactly one assembly");
	}

	[Theory]
	[InlineData("../System.Private.CoreLib.dll")]
	[InlineData("sub/../../System.Private.CoreLib.dll")]
	public void AssemblyPathEscapeFailsClosed(string path)
	{
		using var pack = CoreLibPack.Create();
		pack.ReplaceAssembly("file", path);

		AssertInvalidPack(pack, "escapes its directory");
	}

	[Fact]
	public void AbsoluteAssemblyPathFailsClosed()
	{
		using var pack = CoreLibPack.Create();
		pack.ReplaceAssembly("file", pack.AssemblyPath);

		AssertInvalidPack(pack, "must be relative");
	}

	[Fact]
	public void NoPackRetainsStopwatchShadowFallback()
	{
		var result = AmigaM68kCompiler.Compile(Request(manifestPath: null));

		Assert.Null(result.FrameworkAnalysis.ImplementationPack);
		Assert.Contains(
			result.Symbols,
			static symbol => symbol.Name.Contains("ShadowStopwatch", StringComparison.Ordinal));
		Assert.DoesNotContain("IMPLEMENTATION PACK", result.Map, StringComparison.Ordinal);
	}

	private static M68kCompilationRequest Request(
		string? manifestPath,
		string entry = "CopperSharp.Compiler.Tests.CompilerFixtures::PortableStopwatchResetOnlyEntry",
		M68kCpuTarget cpu = M68kCpuTarget.M68000,
		M68kFloatingPointMode floatingPoint = M68kFloatingPointMode.Disabled) => new()
	{
		AssemblyPath = FixtureAssembly,
		EntryPoint = entry,
		Cpu = cpu,
		FloatingPoint = floatingPoint,
		OutputFormat = M68kOutputFormat.Hunk,
		RuntimeProfile = M68kRuntimeProfile.Application,
		Imports = new Dictionary<string, uint>
		{
			[M68kRuntimeImports.Allocate] = 0x2800
		},
		FrameworkImplementationPack = manifestPath is null
			? null
			: new M68kFrameworkImplementationPackOptions(manifestPath)
	};

	private static void AssertInvalidPack(CoreLibPack pack, string expectedText)
	{
		var exception = Assert.Throws<M68kCompilationException>(() =>
			AmigaM68kCompiler.AnalyzeFramework(Request(pack.ManifestPath)));
		Assert.Equal(M68kDiagnosticIds.InvalidInput, exception.DiagnosticId);
		Assert.Contains(expectedText, exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	internal sealed class CoreLibPack : IDisposable
	{
		private CoreLibPack(
			string directory,
			string manifestPath,
			string assemblyPath,
			string sha256)
		{
			Directory = directory;
			ManifestPath = manifestPath;
			AssemblyPath = assemblyPath;
			Sha256 = sha256;
		}

		public string Directory { get; }
		public string ManifestPath { get; }
		public string AssemblyPath { get; }
		public string Sha256 { get; }

		public static CoreLibPack Create()
			=> CreateFromAssembly(typeof(object).Assembly.Location, "Microsoft.NETCore.App.Runtime.test", "test-host");

		public const string PinnedCoreLibSha256 = "dc1945de746f94987ec705a1f27d512abf72a41a1da37e414d1951e4e823037a";

		public static CoreLibPack CreatePinned()
			=> CreateFromAssembly(Path.Combine(AppContext.BaseDirectory, "PinnedCoreLib", "10.0.9", "System.Private.CoreLib.dll"),
				"Microsoft.NETCore.App.Runtime.win-x64", "win-x64", PinnedCoreLibSha256);

		private static CoreLibPack CreateFromAssembly(string source, string packId, string runtimeIdentifier, string? expectedSha256 = null)
		{
			var directory = Path.Combine(
				Path.GetTempPath(),
				"CopperSharpCoreLibPackTests",
				Guid.NewGuid().ToString("N"));
			System.IO.Directory.CreateDirectory(directory);
			var destination = Path.Combine(directory, "System.Private.CoreLib.dll");
			File.Copy(source, destination);
			using var stream = File.OpenRead(destination);
			var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
			if (expectedSha256 is not null) Assert.Equal(expectedSha256, sha256);
			stream.Position = 0;
			using var peReader = new PEReader(stream, PEStreamOptions.PrefetchEntireImage);
			var reader = peReader.GetMetadataReader();
			var definition = reader.GetAssemblyDefinition();
			var module = reader.GetModuleDefinition();
			var token = Convert.ToHexString(
				AssemblyName.GetAssemblyName(destination).GetPublicKeyToken() ?? [])
				.ToLowerInvariant();
			var manifest = new
			{
				schemaVersion = 1,
				packId,
				packVersion = "10.0.9",
				runtimeIdentifier,
				targetFramework = "net10.0",
				referencePack = "Microsoft.NETCore.App.Ref",
				referencePackVersion = "10.0.9",
				implementationProfile = "corelib-common-il-v1",
				assemblies = new[]
				{
					new
					{
						name = reader.GetString(definition.Name),
						file = "System.Private.CoreLib.dll",
						version = definition.Version.ToString(),
						publicKeyToken = token,
						mvid = reader.GetGuid(module.Mvid).ToString("D"),
						sha256
					}
				}
			};
			var manifestPath = Path.Combine(directory, "corelib-pack.json");
			File.WriteAllText(
				manifestPath,
				JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
			return new CoreLibPack(directory, manifestPath, destination, sha256);
		}

		public void Replace(string property, string replacement)
		{
			var document = JsonNode.Parse(File.ReadAllText(ManifestPath))!.AsObject();
			document[property] = property == "schemaVersion"
				? JsonValue.Create(int.Parse(replacement))
				: JsonValue.Create(replacement);
			File.WriteAllText(ManifestPath, document.ToJsonString());
		}

		public void ReplaceAssembly(string property, string replacement)
		{
			var document = JsonNode.Parse(File.ReadAllText(ManifestPath))!.AsObject();
			document["assemblies"]!.AsArray()[0]!.AsObject()[property] = replacement;
			File.WriteAllText(ManifestPath, document.ToJsonString());
		}

		public void Dispose()
		{
			if (System.IO.Directory.Exists(Directory))
			{
				System.IO.Directory.Delete(Directory, recursive: true);
			}
		}
	}
}
