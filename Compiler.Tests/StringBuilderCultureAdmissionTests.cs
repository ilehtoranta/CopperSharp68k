/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderCultureAdmissionTests
{
	[Fact]
	public void EmptySnapshotUsesTheTargetSingletonAndRejectsWrites()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		CilMethod body;
		System.Reflection.Emit.OpCode load = System.Reflection.Emit.OpCodes.Ldsfld;
		int token, offset;
		using (var module = Open())
		{
			var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::StringBuilderConstructedSnapshot");
			var call = entry.Instructions.Last(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Callvirt);
			body = module.ResolveMethodToken((int)call.Operand!, entry, call.Offset).Definition!;
			var fieldLoad = Assert.Single(body.Instructions.Where(instruction => instruction.OpCode == load &&
				module.ResolveFieldToken((int)instruction.Operand!, body, instruction.Offset).DisplayName == "CopperSharp.Runtime.ShadowStringData::Empty"));
			token = (int)fieldLoad.Operand!;
			offset = fieldLoad.Offset;
			var target = module.ResolveFieldToken(token, body, offset);
			Assert.Equal("CopperSharp.Runtime.Managed", target.ModuleName);
			var write = body with { Instructions = body.Instructions.Select(instruction => instruction.Offset == offset
				? instruction with { OpCode = System.Reflection.Emit.OpCodes.Stsfld } : instruction).ToArray() };
			var error = Assert.Throws<M68kCompilationException>(() => module.ResolveFieldToken(token, write, offset));
			Assert.Equal(M68kDiagnosticIds.UnsupportedInstruction, error.DiagnosticId);
		}
		pack.Replace("packVersion", "10.0.10");
		using var adjacent = Open();
		Assert.Equal("System.Private.CoreLib", adjacent.ResolveFieldToken(token, body, offset).ModuleName);
		CompilationModule Open() => new(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowStringData).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
	}

	[Fact]
	public void CultureGraphRetainsAllDependencyObservations()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var analysis = M68kCompiler.AnalyzeFramework(new M68kCompilationRequest {
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::StringBuilderStableCultureEntry", IncludedExportNames = [],
			ExceptionMode = M68kExceptionMode.Full, MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions { StartAddress = 0x0010_0000, Size = 0x8000 },
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		});
		File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "stringbuilder-stable-culture-analysis.json"),
			System.Text.Json.JsonSerializer.Serialize(analysis, new System.Text.Json.JsonSerializerOptions { WriteIndented = true,
				Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }));
		Assert.True(analysis.IsCompatible, string.Join("\n", analysis.Members.Where(member => member.Status == M68kFrameworkCompatibilityStatus.Unsupported)
			.Select(member => member.Member.DisplayName + ": " + member.Reason)));
	}

	[Fact]
	public void CulturePrivateClosureRejectsUnlistedCallersAndAdjacentInputs()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var culture = FrameworkTypeId.Named("System.Runtime", "System.Globalization.CultureInfo");
		var number = FrameworkTypeId.Named("System.Runtime", "System.Globalization.NumberFormatInfo");
		var text = FrameworkTypeId.Primitive("System.String");
		var none = FrameworkTypeId.Primitive("System.Void");
		var owner = FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowCultureInfo");
		var initialize = new FrameworkMemberId(owner, "InitializeName", new FrameworkMethodSignatureId(0x20, 0, 1, none, [text]));
		var getter = new FrameworkMemberId(owner, "GetNumberFormat", new FrameworkMethodSignatureId(0x20, 0, 0, number, []));
		var constructor = new FrameworkMemberId(number, ".ctor", new FrameworkMethodSignatureId(0x20, 0, 0, none, []));
		var getType = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Object"), "GetType",
			new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Named("System.Runtime", "System.Type"), []));
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(getType, catalog, initialize, out _));
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(getType, catalog, getter, out _));
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(getType, catalog, null, out _));
			Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(constructor, null, catalog, getter, out var allocation));
			if (admitted) Assert.True(allocation.EffectSummary.Effects.HasFlag(FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect));
			// The exact NumberFormatInfo constructor is now also a released public formatting primitive.
			Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(constructor, null, catalog, initialize, out _));
			foreach (var member in StringBuilderExceptionSurface.Members)
			{
				var expected = admitted && (StringBuilderExceptionSurface.IsPublicCallbackConstructor(member) ||
					member.Name == ".ctor" && member.DeclaringType.FullMetadataName is
					"System.ArgumentNullException" or "System.NotSupportedException" or "System.InvalidOperationException");
				Assert.Equal(expected, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, initialize, out _));
				Assert.Equal(admitted && StringBuilderExceptionSurface.IsPublicCallbackConstructor(member), FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog,
					new FrameworkMemberId(owner, "Unlisted", initialize.Signature), out _));
			}
			Assert.False(FrameworkImplementationProfile.IsCultureStorageCaller(new FrameworkMemberId(
				FrameworkTypeId.Named("Application", owner.FullMetadataName!), initialize.Name, initialize.Signature)));
			Assert.False(FrameworkImplementationProfile.IsCultureStorageCaller(new FrameworkMemberId(owner, initialize.Name, getter.Signature)));
		}
	}

	[Fact]
	public void CultureBindingsRequireReleasedInputAndExactTargetSignatures()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var type = FrameworkTypeId.Named("System.Runtime", "System.Globalization.CultureInfo");
		var none = FrameworkTypeId.Primitive("System.Void");
		var members = new[] {
			Member("get_CurrentCulture", 0, type, []), Member("set_CurrentCulture", 0, none, [type]),
			Member("get_DefaultThreadCurrentCulture", 0, type, []), Member("set_DefaultThreadCurrentCulture", 0, none, [type]),
			Member("get_InvariantCulture", 0, type, []), Member(".ctor", 0x20, none, [FrameworkTypeId.Primitive("System.String")]),
			Member(".ctor", 0x20, none, [FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.Primitive("System.Boolean")]),
			Member("get_NumberFormat", 0x20, FrameworkTypeId.Named("System.Runtime", "System.Globalization.NumberFormatInfo"), []),
			Member("set_NumberFormat", 0x20, none, [FrameworkTypeId.Named("System.Runtime", "System.Globalization.NumberFormatInfo")])
		};
		Check(true);
		using (var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack: Load()))
		{
			var field = module.GetExperimentalReadOnlyField("System.Globalization.CultureInfo");
			Assert.Equal("bool", field.Type.DisplayName);
			Assert.Equal("System.Private.CoreLib", field.ModuleName);
			Assert.Throws<M68kCompilationException>(() => module.GetExperimentalReadOnlyField("Application.CultureInfo"));
		}
		pack.Replace("packVersion", "10.0.10");
		Check(false);

		void Check(bool admitted)
		{
			foreach (var member in members)
			{
				Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, Load(), null, out var binding));
				if (admitted) Assert.Equal(FrameworkTypeInitializerPolicy.TargetOwned, binding.TypeInitializerPolicy);
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(
					new FrameworkMemberId(FrameworkTypeId.Named("Application", type.FullMetadataName!), member.Name, member.Signature), Load(), null, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(
					new FrameworkMemberId(type, "Unlisted", member.Signature), Load(), null, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(
					new FrameworkMemberId(type, member.Name, member.Signature, [FrameworkTypeId.Primitive("System.Int32")]), Load(), null, out _));
			}
		}
		FrameworkMemberId Member(string name, byte header, FrameworkTypeId result, FrameworkTypeId[] parameters) =>
			new(type, name, new FrameworkMethodSignatureId(header, 0, parameters.Length, result, parameters));
		FrameworkImplementationPackCatalog Load() => FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
	}
}
