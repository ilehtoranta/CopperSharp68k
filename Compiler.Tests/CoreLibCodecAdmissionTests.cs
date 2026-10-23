/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Metadata.Ecma335;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Runtime;
using CopperSharp.Runtime.AmigaPal;

namespace CopperSharp.Compiler.Tests;

public sealed class CoreLibCodecAdmissionTests
{
	[Fact]
	public void ReleasedMetadataContainsExactlyTheAdmittedCodecInventory()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = Load(pack);
		using var module = new CompilationModule(pack.AssemblyPath);
		var counts = new Dictionary<string, int>();
		foreach (var typeName in new[] { "System.Convert", "System.Buffers.Text.Base64", "System.Text.Unicode.Utf8" })
		{
			var definition = module.Reader.TypeDefinitions.Select(module.Reader.GetTypeDefinition).Single(t =>
				module.Reader.GetString(t.Namespace) + "." + module.Reader.GetString(t.Name) == typeName);
			var count = 0;
			foreach (var handle in definition.GetMethods())
			{
				var member = module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(handle), module.GetMethod(handle), 0);
				if (!CoreLibCodecSurface.TryCreateBinding(member, catalog, out var binding)) continue;
				count++;
				Assert.Equal(FrameworkBindingKind.ShadowMethod, binding.Kind);
				Assert.Equal(member, binding.Member);
				Reject(new FrameworkMemberId(FrameworkTypeId.Named("Application", typeName), member.Name, member.Signature));
				Reject(new FrameworkMemberId(member.DeclaringType, "Unlisted", member.Signature));
				var s = member.Signature;
				Reject(new FrameworkMemberId(member.DeclaringType, member.Name, new(0x20, 0, s.RequiredParameterCount, s.ReturnType, s.ParameterTypes)));
				Reject(new FrameworkMemberId(member.DeclaringType, member.Name, new(0, 1, s.RequiredParameterCount, s.ReturnType, s.ParameterTypes)));
				Reject(new FrameworkMemberId(member.DeclaringType, member.Name, new(0, 0, s.RequiredParameterCount + 1, s.ReturnType, s.ParameterTypes)));
				Reject(new FrameworkMemberId(member.DeclaringType, member.Name, new(0, 0, s.RequiredParameterCount, FrameworkTypeId.Primitive("System.Object"), s.ParameterTypes)));
				Reject(new FrameworkMemberId(member.DeclaringType, member.Name, member.Signature, [FrameworkTypeId.Primitive("System.Byte")]));
				Reject(new FrameworkMemberId(member.DeclaringType, member.Name, new(0, 0, s.RequiredParameterCount, s.ReturnType,
					[FrameworkTypeId.Primitive("System.Object"), .. s.ParameterTypes.Skip(1)])));
			}
			counts.Add(typeName, count);
		}
		Assert.Equal(28, counts["System.Convert"]);
		Assert.Equal(10, counts["System.Buffers.Text.Base64"]);
		Assert.Equal(3, counts["System.Text.Unicode.Utf8"]);
		Assert.False(catalog.EnableUnlistedManagedBodies);

		void Reject(FrameworkMemberId member) => Assert.False(CoreLibCodecSurface.TryCreateBinding(member, catalog, out _), member.DisplayName);
	}

	[Fact]
	public void StringConstructionDependencyRequiresItsExactOwnedHelper()
	{
		var chars = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Char")]);
		var signature = new FrameworkMethodSignatureId(0, 0, 1, FrameworkTypeId.Primitive("System.String"), [chars]);
		foreach (var name in new[] { "ShadowConvertHex", "ShadowConvertBase64" })
		{
			var type = FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime." + name);
			Assert.True(CoreLibCodecSurface.IsRuntimeStringFactory(new(type, "CreateString", signature)));
			Assert.False(CoreLibCodecSurface.IsRuntimeStringFactory(new(type, "Other", signature)));
			Assert.False(CoreLibCodecSurface.IsRuntimeStringFactory(new(type, "CreateString", signature, [chars])));
			Assert.False(CoreLibCodecSurface.IsRuntimeStringFactory(new(FrameworkTypeId.Named("Application", "CopperSharp.Runtime." + name), "CreateString", signature)));
			Assert.False(CoreLibCodecSurface.IsRuntimeStringFactory(new(type, "CreateString", new(0, 0, 2, signature.ReturnType, [chars]))));
		}
	}

	[Fact]
	public void OptionsExceptionConstructorRequiresTheExactOwnedValidator()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = Load(pack);
		var text = FrameworkTypeId.Primitive("System.String");
		var none = FrameworkTypeId.Primitive("System.Void");
		var member = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.ArgumentException"), ".ctor", new(0x20, 0, 2, none, [text, text]));
		var owner = FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowConvertBase64");
		var signature = new FrameworkMethodSignatureId(0, 0, 1, none, [FrameworkTypeId.Named("System.Runtime", "System.Base64FormattingOptions")]);
		var caller = new FrameworkMemberId(owner, "CheckOptions", signature);
		Assert.True(CoreLibCodecSurface.IsOptionsExceptionDependency(member, caller));
		Assert.True(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, caller, out var binding));
		Assert.Equal(FrameworkBindingKind.PinnedManagedBody, binding.Kind);
		Assert.False(CoreLibCodecSurface.IsOptionsExceptionDependency(member, null));
		Assert.False(CoreLibCodecSurface.IsOptionsExceptionDependency(member, new(owner, "Other", signature)));
		Assert.False(CoreLibCodecSurface.IsOptionsExceptionDependency(member, new(FrameworkTypeId.Named("Application", owner.MetadataName!), "CheckOptions", signature)));
		Assert.False(CoreLibCodecSurface.IsOptionsExceptionDependency(member, new(owner, "CheckOptions", signature, [text])));
		Assert.False(CoreLibCodecSurface.IsOptionsExceptionDependency(new(member.DeclaringType, ".ctor", new(0x20, 0, 1, none, [text])), caller));
	}

	[Fact]
	public void CodecValidationDependenciesRejectNeighbouringCallersAndConstructors()
	{
		var text = FrameworkTypeId.Primitive("System.String");
		var none = FrameworkTypeId.Primitive("System.Void");
		var integer = FrameworkTypeId.Primitive("System.Int32");
		var bytes = FrameworkTypeId.SzArray(FrameworkTypeId.Primitive("System.Byte"));
		var type = FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowConvertBase64");
		var nullCtor = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.ArgumentNullException"), ".ctor", new(0x20, 0, 1, none, [text]));
		var caller = new FrameworkMemberId(type, "FromBase64String", new(0, 0, 1, bytes, [text]));
		Assert.True(CoreLibCodecSurface.IsExceptionDependency(nullCtor, caller));
		Assert.False(CoreLibCodecSurface.IsExceptionDependency(nullCtor, new(type, "Unknown", caller.Signature)));
		Assert.False(CoreLibCodecSurface.IsExceptionDependency(nullCtor, new(type, caller.Name, new(0, 0, 1, text, [text]))));
		Assert.False(CoreLibCodecSurface.IsExceptionDependency(nullCtor, new(type, caller.Name, caller.Signature, [text])));
		Assert.False(CoreLibCodecSurface.IsExceptionDependency(nullCtor, new(FrameworkTypeId.Named("Application", type.MetadataName!), caller.Name, caller.Signature)));
		var range = new FrameworkMemberId(type, "CheckRange", new(0, 0, 3, none, [integer, integer, integer]));
		var rangeCtor = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.ArgumentOutOfRangeException"), ".ctor", nullCtor.Signature);
		Assert.True(CoreLibCodecSurface.IsExceptionDependency(rangeCtor, range));
		Assert.False(CoreLibCodecSurface.IsExceptionDependency(rangeCtor, new(type, "CheckRange", new(0, 0, 2, none, [integer, integer]))));
		Assert.False(CoreLibCodecSurface.IsExceptionDependency(new(rangeCtor.DeclaringType, ".ctor", new(0x20, 0, 0, none, [])), range));
	}

	[Fact]
	public void PrivateBase64HelpersAdmitOnlyTheirExactExceptionDependencies()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = Load(pack);
		var owner = FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowConvertBase64");
		var integer = FrameworkTypeId.Primitive("System.Int32");
		var boolean = FrameworkTypeId.Primitive("System.Boolean");
		var text = FrameworkTypeId.Primitive("System.String");
		var none = FrameworkTypeId.Primitive("System.Void");
		var readChars = FrameworkTypeId.GenericInstantiation(
			FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Char")]);
		var bytes = FrameworkTypeId.SzArray(FrameworkTypeId.Primitive("System.Byte"));
		var required = new FrameworkMemberId(owner, "Required", new(0, 0, 2, integer, [integer, boolean]));
		var fromChars = new FrameworkMemberId(owner, "FromChars", new(0, 0, 1, bytes, [readChars]));
		var oom = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.OutOfMemoryException"),
			".ctor", new(0x20, 0, 0, none, []));
		var format = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.FormatException"),
			".ctor", new(0x20, 0, 0, none, []));
		Assert.True(CoreLibCodecSurface.IsExceptionDependency(oom, required));
		Assert.True(CoreLibCodecSurface.IsExceptionDependency(format, fromChars));
		Assert.True(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(oom, null, catalog, required, out var oomBinding));
		Assert.True(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(format, null, catalog, fromChars, out var formatBinding));
		Assert.Equal(FrameworkBindingKind.PinnedManagedBody, oomBinding.Kind);
		Assert.Equal(FrameworkBindingKind.PinnedManagedBody, formatBinding.Kind);
		Assert.False(CoreLibCodecSurface.IsExceptionDependency(format, required));
		Assert.False(CoreLibCodecSurface.IsExceptionDependency(oom, fromChars));
		foreach (var pair in new[] { (Caller: required, Dependency: oom), (Caller: fromChars, Dependency: format) })
		{
			Assert.False(CoreLibCodecSurface.IsExceptionDependency(pair.Dependency,
				new FrameworkMemberId(owner, "Neighbour", pair.Caller.Signature)));
			Assert.False(CoreLibCodecSurface.IsExceptionDependency(pair.Dependency,
				new FrameworkMemberId(FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowBase64"),
					pair.Caller.Name, pair.Caller.Signature)));
			Assert.False(CoreLibCodecSurface.IsExceptionDependency(pair.Dependency,
				new FrameworkMemberId(FrameworkTypeId.Named("Application", owner.MetadataName!),
					pair.Caller.Name, pair.Caller.Signature)));
			Assert.False(CoreLibCodecSurface.IsExceptionDependency(pair.Dependency,
				new FrameworkMemberId(owner, pair.Caller.Name, pair.Caller.Signature, [integer])));
			Assert.False(CoreLibCodecSurface.IsExceptionDependency(pair.Dependency,
				new FrameworkMemberId(owner, pair.Caller.Name, new(0x20, 0,
					pair.Caller.Signature.RequiredParameterCount, pair.Caller.Signature.ReturnType,
					pair.Caller.Signature.ParameterTypes))));
			Assert.False(CoreLibCodecSurface.IsExceptionDependency(pair.Dependency,
				new FrameworkMemberId(owner, pair.Caller.Name, new(0, 0,
					pair.Caller.Signature.RequiredParameterCount, text,
					pair.Caller.Signature.ParameterTypes))));
			Assert.False(CoreLibCodecSurface.IsExceptionDependency(
				new FrameworkMemberId(pair.Dependency.DeclaringType, ".ctor", new(0x20, 0, 1, none, [text])), pair.Caller));
		}
		Assert.False(CoreLibCodecSurface.IsExceptionDependency(oom,
			new FrameworkMemberId(owner, "Required", new(0, 0, 2, integer, [boolean, integer]))));
		Assert.False(CoreLibCodecSurface.IsExceptionDependency(format,
			new FrameworkMemberId(owner, "FromChars", new(0, 0, 1, bytes, [text]))));
	}

	[Fact]
	public void HexValidationDependenciesRequireTheirOwnedSignatures()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = Load(pack);
		var text = FrameworkTypeId.Primitive("System.String");
		var none = FrameworkTypeId.Primitive("System.Void");
		var integer = FrameworkTypeId.Primitive("System.Int32");
		var boolean = FrameworkTypeId.Primitive("System.Boolean");
		var bytes = FrameworkTypeId.SzArray(FrameworkTypeId.Primitive("System.Byte"));
		var owner = FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowConvertHex");
		var signature = new FrameworkMethodSignatureId(0, 0, 4, text, [bytes, integer, integer, boolean]);
		var caller = new FrameworkMemberId(owner, "EncodeArraySlice", signature);
		foreach (var name in new[] { "System.ArgumentNullException", "System.ArgumentOutOfRangeException" })
		{
			var constructor = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", name), ".ctor", new(0x20, 0, 1, none, [text]));
			Assert.True(CoreLibCodecSurface.IsExceptionDependency(constructor, caller));
			Assert.True(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(constructor, null, catalog, caller, out var binding));
			Assert.Equal(FrameworkBindingKind.PinnedManagedBody, binding.Kind);
			Assert.False(CoreLibCodecSurface.IsExceptionDependency(constructor, new(owner, "Other", signature)));
			Assert.False(CoreLibCodecSurface.IsExceptionDependency(constructor, new(owner, caller.Name, signature, [integer])));
			Assert.False(CoreLibCodecSurface.IsExceptionDependency(constructor, new(FrameworkTypeId.Named("Application", owner.MetadataName!), caller.Name, signature)));
			Assert.False(CoreLibCodecSurface.IsExceptionDependency(constructor, new(owner, caller.Name, new(0, 0, 3, text, [bytes, integer, boolean]))));
			Assert.False(CoreLibCodecSurface.IsExceptionDependency(new(constructor.DeclaringType, ".ctor", new(0x20, 0, 0, none, [])), caller));
		}
	}

	[Fact]
	public void AdjacentPackCannotEnableTheCodecSurface()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		pack.Replace("packVersion", "10.0.10");
		var member = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Convert"), "ToHexString",
			new(0, 0, 1, FrameworkTypeId.Primitive("System.String"), [FrameworkTypeId.SzArray(FrameworkTypeId.Primitive("System.Byte"))]));
		Assert.False(CoreLibCodecSurface.TryCreateBinding(member, Load(pack), out _));
	}

	[Theory]
	[InlineData("EncodingObjectGraph", "System.Text.Encoding")]
	[InlineData("Base64Url", "System.Buffers.Text.Base64Url")]
	public void UnlistedCodecFamiliesRemainUnsupported(string entry, string owner)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var request = new M68kCompilationRequest
		{
			AssemblyPath = typeof(CodecBoundaryFixtures).Assembly.Location,
			EntryPoint = $"CopperSharp.Compiler.Tests.CodecBoundaryFixtures::{entry}", IncludedExportNames = [],
			ManagedAssemblyPaths = [typeof(ShadowGuid).Assembly.Location, typeof(EnvironmentPal).Assembly.Location],
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new() { StartAddress = 0x0010_0000, Size = 32768 },
			FrameworkImplementationPack = new(pack.ManifestPath)
		};
		var result = M68kCompiler.AnalyzeFramework(request);
		Assert.False(result.IsCompatible);
		Assert.Contains(result.Members, m => m.Member.TypeName == owner && m.Status == M68kFrameworkCompatibilityStatus.Unsupported);
		Assert.Throws<M68kCompilationException>(() => M68kCompiler.Compile(request));
	}

	private static FrameworkImplementationPackCatalog Load(FrameworkImplementationPackTests.CoreLibPack pack) =>
		FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
}

public static class CodecBoundaryFixtures
{
	public static byte[] EncodingObjectGraph() => System.Text.Encoding.UTF8.GetBytes("text");
	public static string Base64Url() => System.Buffers.Text.Base64Url.EncodeToString(new byte[1]);
}
