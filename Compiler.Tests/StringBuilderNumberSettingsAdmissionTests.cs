/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Reflection.Metadata.Ecma335;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderNumberSettingsAdmissionTests
{
	[Fact]
	public void RangeErrorsRequireTheOwnedSetterAndExactInt32Specialization()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack: catalog);
		var setter = module.ResolveEntryPoint("System.Globalization.NumberFormatInfo::set_NumberDecimalDigits");
		var call = setter.Instructions.Single(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Call &&
			module.DescribeFrameworkMethodToken((int)instruction.Operand!, setter, instruction.Offset).Name == "ThrowArgumentOutOfRange_Range");
		var member = module.DescribeFrameworkMethodToken((int)call.Operand!, setter, call.Offset);
		var caller = module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(setter.Handle), setter, -1);
		Assert.True(StringBuilderNumberSettingsSurface.IsRangeThrow(member));
		Assert.True(Admit(member, caller, catalog, out var binding));
		Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect));
		Assert.False(Admit(member, null, catalog, out _));
		Assert.False(Admit(member, new FrameworkMemberId(caller.DeclaringType, "Unlisted", caller.Signature), catalog, out _));
		Assert.False(Admit(new FrameworkMemberId(member.DeclaringType, member.Name, member.Signature, [FrameworkTypeId.Primitive("System.UInt32")]), caller, catalog, out _));
		Assert.False(Admit(new FrameworkMemberId(member.DeclaringType, "Unlisted", member.Signature, member.MethodTypeArguments), caller, catalog, out _));
		pack.Replace("packVersion", "10.0.10");
		Assert.False(Admit(member, caller, FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!, out _));
		static bool Admit(FrameworkMemberId member, FrameworkMemberId? caller, FrameworkImplementationPackCatalog catalog, out FrameworkBinding binding) =>
			FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, caller, out binding);
	}

	[Fact]
	public void DecimalUnboxingResolvesTheFacadeAggregateIdentityInPinnedCoreLib()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowStringData).Assembly.Location], frameworkImplementationPack:
			FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
		var owner = module.Reader.TypeDefinitions.Select(module.Reader.GetTypeDefinition).Single(type => module.Reader.GetString(type.Name) == "DecimalContractProvider");
		var caller = owner.GetMethods().Select(module.GetMethod).Single(method => method.Name == "Format");
		var unbox = Assert.Single(caller.Instructions.Where(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Unbox_Any));
		var token = (int)unbox.Operand!;
		var type = module.ResolveTypeToken(token, caller, unbox.Offset);
		var facade = module.ResolveTypeTokenModuleName(token, caller, unbox.Offset);
		Assert.Equal("System.Runtime", facade);
		var identity = module.ResolveRuntimeTypeIdentity(type, facade);
		Assert.Equal("System.Private.CoreLib", identity.ModuleName);
		Assert.Equal("System.Decimal", identity.Type.DisplayName);
		var layout = module.GetRuntimeTypeLayout(identity);
		Assert.Equal(16, layout.Size);
		Assert.Equal(0u, layout.ReferenceBitmap);
		var typeTest = Assert.Single(caller.Instructions.Where(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Isinst));
		Assert.Equal("System.Private.CoreLib", module.ResolveRuntimeTypeToken((int)typeTest.Operand!, caller, typeTest.Offset).ModuleName);
		_ = CopperSharp.Compiler.Backend.CilMachineIrBuilder.Build(caller, module);
	}

	[Fact]
	public void DirectDecimalSpanFormattingRequiresTheExactReleasedMethod()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack:
			FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
		var owner = module.Reader.TypeDefinitions.Select(module.Reader.GetTypeDefinition).Single(type => module.Reader.GetString(type.Name) == "Decimal");
		var methods = owner.GetMethods().Select(module.GetMethod).ToArray();
		var method = methods.Single(candidate => candidate.Name == "TryFormat" && candidate.Signature.ParameterTypes[0].DisplayName == "System.Span`1<char>");
		Assert.True(module.IsPinnedDecimalTryFormatMethod(method));
		Assert.False(module.IsPinnedDecimalTryFormatMethod(method with { ModuleName = "Application" }));
		Assert.False(module.IsPinnedDecimalTryFormatMethod(method with { Name = "Unlisted" }));
		Assert.False(module.IsPinnedDecimalTryFormatMethod(method with { MethodTypeArguments = [new CilType(CilTypeKind.SignedInteger, 4, "int")] }));
		var unrelated = methods.Single(candidate => candidate.Name == "ToString" && candidate.Signature.ParameterTypes.Length == 0);
		Assert.False(module.IsPinnedDecimalTryFormatMethod(unrelated with { Name = "TryFormat" }));
		Assert.False(module.IsPinnedDecimalTryFormatMethod(methods.Single(candidate => candidate.Name == "TryFormat" && candidate.Signature.ParameterTypes[0].DisplayName == "System.Span`1<byte>")));
		pack.Replace("packVersion", "10.0.10");
		using var adjacent = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack:
			FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
		Assert.False(adjacent.IsPinnedDecimalTryFormatMethod(method));
	}

	[Theory]
	[InlineData("StringBuilderStableNumberSettingsValidationEntry")]
	[InlineData("StringBuilderStableNumberSettingsFallbackEntry")]
	public void NumberSettingsMatchHostAndCloseTheReleasedGraph(string entry)
	{
		Assert.Equal(42, (int)typeof(CompilerFixtures).GetMethod(entry)!.Invoke(null, null)!);
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var result = M68kCompiler.AnalyzeFramework(new M68kCompilationRequest {
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location, EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::" + entry,
			IncludedExportNames = [], ExceptionMode = M68kExceptionMode.Full, MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		});
		File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "stringbuilder-stable-number-settings-" + entry + ".json"), System.Text.Json.JsonSerializer.Serialize(result));
		Assert.True(result.IsCompatible, string.Join("\n", result.Members.Where(member => member.Status == M68kFrameworkCompatibilityStatus.Unsupported).Select(member => member.Member.DisplayName + ": " + member.Reason)));
	}

	[Fact]
	public void GroupCloningRequiresActualReleasedAccessorsAndInt32Arrays()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack: catalog);
		foreach (var name in new[] { "NumberGroupSizes", "CurrencyGroupSizes", "PercentGroupSizes" })
		foreach (var prefix in new[] { "get_", "set_" })
		{
			var caller = module.ResolveEntryPoint("System.Globalization.NumberFormatInfo::" + prefix + name);
			var call = caller.Instructions.Single(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Callvirt);
			var member = module.DescribeFrameworkMethodToken((int)call.Operand!, caller, call.Offset);
			Assert.True(module.TryCreateExperimentalNumberGroupCloneBinding(member, caller, out var binding));
			Assert.Equal("CloneInt32", binding.ShadowMethod!.MethodName);
			Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect));
			Assert.False(module.TryCreateExperimentalNumberGroupCloneBinding(member, null, out _));
			Assert.False(module.TryCreateExperimentalNumberGroupCloneBinding(member, caller with { ModuleName = "Application" }, out _));
			Assert.False(module.TryCreateExperimentalNumberGroupCloneBinding(new FrameworkMemberId(member.DeclaringType, "Unlisted", member.Signature), caller, out _));
			Assert.False(module.TryCreateExperimentalNumberGroupCloneBinding(new FrameworkMemberId(member.DeclaringType, member.Name, member.Signature, [FrameworkTypeId.Primitive("System.Int32")]), caller, out _));
			pack.Replace("packVersion", "10.0.10");
			using var adjacent = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack:
				FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
			Assert.False(adjacent.TryCreateExperimentalNumberGroupCloneBinding(member, caller, out _));
			pack.Replace("packVersion", "10.0.9");
		}
	}

	[Fact]
	public void NumberSettingsRequireExactReleasedDefinitionsAndOwnedHelpers()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack: catalog);
			var definition = module.Reader.TypeDefinitions.Select(module.Reader.GetTypeDefinition).Single(type =>
				module.Reader.GetString(type.Namespace) == "System.Globalization" && module.Reader.GetString(type.Name) == "NumberFormatInfo");
			var actual = definition.GetMethods().Select(module.GetMethod).Select(method => FrameworkImplementationProfile.Canonicalize(
				module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(method.Handle), method, -1))).ToArray();
			var setter = StringBuilderNumberSettingsSurface.PublicMembers.First();
			var caller = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", setter.DeclaringType.FullMetadataName!), setter.Name, setter.Signature);
			foreach (var member in StringBuilderNumberSettingsSurface.PublicMembers.Concat(StringBuilderNumberSettingsSurface.Helpers))
			{
				Assert.Contains(member, actual);
				Assert.Equal(admitted, Admit(member, caller, out var binding));
				Assert.Equal(admitted && StringBuilderNumberSettingsSurface.IsPublic(member), Admit(member, null, out _));
				Assert.False(Admit(new FrameworkMemberId(member.DeclaringType, "Unlisted", member.Signature), caller, out _));
				Assert.False(Admit(new FrameworkMemberId(member.DeclaringType, member.Name, member.Signature, [FrameworkTypeId.Primitive("System.Int32")]), caller, out _));
				Assert.False(Admit(new FrameworkMemberId(FrameworkTypeId.Named("Application", member.DeclaringType.FullMetadataName!), member.Name, member.Signature), caller, out _));
				if (StringBuilderNumberSettingsSurface.IsHelper(member))
				{
					Assert.False(Admit(member, new FrameworkMemberId(caller.DeclaringType, "Unlisted", caller.Signature), out _));
					Assert.False(Admit(member, new FrameworkMemberId(FrameworkTypeId.Named("Application", caller.DeclaringType.FullMetadataName!), caller.Name, caller.Signature), out _));
				}
				if (admitted) Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect | FrameworkEffects.WritesManagedMemory));
			}
			bool Admit(FrameworkMemberId member, FrameworkMemberId? source, out FrameworkBinding binding) =>
				FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, source, out binding);
		}
	}
}
