/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Reflection.Emit;
using System.Reflection.Metadata.Ecma335;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class FloatingPointFormattingTests
{
	[Fact]
	public void CustomFloatingCallbackContractsMatchCoreLib() => Assert.Equal(42, CompilerFixtures.CoreLibFloatingCustomCallbackContractsEntry());
	[Fact]
	public void ManagedCustomFloatingCallbackAdaptersPreserveCoreLibContracts() => Assert.Equal(42, CompilerFixtures.ManagedFloatingCustomCallbackContractsEntry());
	[Fact]
	public void DistinctFloatingIteratorsRetainSoleOwnersThroughEveryCallback() => Assert.Equal(42, CompilerFixtures.CoreLibFloatingDistinctIteratorOwnershipEntry());

	[Theory]
	[InlineData("Single")]
	[InlineData("Double")]
	public void CustomFloatingSourcesHaveCompleteCoreLibEnumerationMaps(string precision)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		{
			EnableUnlistedManagedBodies = true
		});
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowSingleJoinEnumeration).Assembly.Location], frameworkImplementationPack: catalog);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CreateOwnedCustom" + precision + "Adapter");
		var constructor = entry.Instructions.Single(instruction => instruction.OpCode == OpCodes.Newobj);
		var source = module.GetTypeLayout(module.ResolveMethodToken((int)constructor.Operand!, entry, constructor.Offset).Definition!);
		var call = entry.Instructions.Single(instruction => instruction.OpCode == OpCodes.Call);
		var factory = module.ResolveMethodToken((int)call.Operand!, entry, call.Offset).Definition!;
		var enumerable = factory.Signature.ParameterTypes[0];
		var enumeration = module.GetRuntimeInterfaceDefinition(module.ResolveRuntimeTypeIdentity(enumerable, factory.ModuleName));
		var implementation = module.TryGetInterfaceImplementation(source, enumeration);
		Assert.NotNull(implementation);
		Assert.Equal(2, implementation.Methods.Length); // Generic getter and inherited nongeneric getter.
		Assert.All(implementation.Methods, method => Assert.StartsWith("Custom" + precision + "JoinSource::", method.DisplayName));
		using var ordinary = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		Assert.Null(ordinary.TryGetInterfaceImplementation(source, enumeration));
		var enumerator = enumeration.Slots.Single(method => !method.Signature.ReturnType.GenericArguments.IsDefaultOrEmpty).Signature.ReturnType;
		foreach (var type in new[] { enumerator,
			new CilType(CilTypeKind.ManagedReference, 4, "System.Collections.IEnumerator"),
			new CilType(CilTypeKind.ManagedReference, 4, "System.IDisposable") })
		{
			var definition = module.GetRuntimeInterfaceDefinition(module.ResolveRuntimeTypeIdentity(type, "System.Private.CoreLib"));
			Assert.NotNull(module.TryGetInterfaceImplementation(source, definition));
		}
		var unrelated = enumerable with { DisplayName = "System.Collections.Generic.IEnumerable`1<int>",
			GenericArguments = [new CilType(CilTypeKind.SignedInteger, 4, "int")] };
		var unrelatedInterface = module.GetRuntimeInterfaceDefinition(module.ResolveRuntimeTypeIdentity(unrelated, "System.Private.CoreLib"));
		Assert.Null(module.TryGetInterfaceImplementation(source, unrelatedInterface));
	}

	[Fact]
	public void CustomFloatingAdaptersRetainOwnersAndClearAfterDispose() => Assert.Equal(42, CompilerFixtures.CoreLibFloatingCustomAdapterOwnershipEntry());

	[Fact]
	public void CustomFloatingEnumerableGoldensMatchCoreLib() => Assert.Equal(42, CompilerFixtures.CoreLibStringBuilderCustomFloatingEnumerableEntry());

	[Fact]
	public void FloatingHandlerConsumerGoldensMatchCoreLib() => Assert.Equal(42, CompilerFixtures.CoreLibFloatingHandlerConsumersOracleEntry());

	[Fact]
	public void FloatingAlignedMutationGoldensMatchCoreLib() => Assert.Equal(42, CompilerFixtures.CoreLibFloatingAlignedMutationOracleEntry());

	[Fact]
	public void CoreLibCrossChunkInsertPreallocationOffsetStateRetainsGapAndCanClear()
	{
		var builder = new System.Text.StringBuilder(4).Append("seed").Append("TAIL");
		var snapshot = builder.ToString();
		var offset = typeof(System.Text.StringBuilder).GetField("m_ChunkOffset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
		Assert.Equal(4, offset.GetValue(builder));
		// MakeRoom adds the six-character insertion width to successor offsets before constructing a new chunk.
		offset.SetValue(builder, 10);
		var expected = "seed" + new string('\0', 6) + "TAIL";
		var failed = builder.ToString();
		Assert.Equal(expected, failed); Assert.Equal(14, builder.Length);
		Assert.Equal("seedTAIL", snapshot);
		builder.Clear().Append("seedTAIL").Insert(2, (-123.5f).ToString(System.Globalization.CultureInfo.InvariantCulture));
		Assert.Equal("se-123.5edTAIL", builder.ToString());
		Assert.Equal(expected, failed); Assert.Equal("seedTAIL", snapshot);
	}

	[Fact]
	public void FloatingIndexedMutationGoldensMatchCoreLib() => Assert.Equal(42, CompilerFixtures.CoreLibFloatingIndexedMutationOracleEntry());

	[Fact]
	public void FloatingSymbolsReuseProviderStringsOnCoreLib()
	{
		foreach (var custom in new[] { false, true })
		{
			var info = custom ? new System.Globalization.NumberFormatInfo {
				NaNSymbol = "unknown\u03A9\0", PositiveInfinitySymbol = "forever\u03A9\0", NegativeInfinitySymbol = "below\u03A9\0"
			} : System.Globalization.NumberFormatInfo.InvariantInfo;
			for (var value = 0; value < 3; value++)
			{
				var expected = value == 0 ? info.NaNSymbol : value == 1 ? info.PositiveInfinitySymbol : info.NegativeInfinitySymbol;
				var single = value == 0 ? float.NaN : value == 1 ? float.PositiveInfinity : float.NegativeInfinity;
				var wide = value == 0 ? double.NaN : value == 1 ? double.PositiveInfinity : double.NegativeInfinity;
				Assert.Same(expected, single.ToString("G", info));
				Assert.Same(expected, wide.ToString("G", info));
				var buffer = new char[expected.Length];
				Assert.True(single.TryFormat(buffer, out var written, "G", info));
				Assert.Equal(expected.Length, written); Assert.Equal(expected, new string(buffer));
				Assert.True(wide.TryFormat(buffer, out written, "G", info));
				Assert.Equal(expected.Length, written); Assert.Equal(expected, new string(buffer));
			}
		}
	}

	[Fact]
	public void LargeFloatingAllocationProbeGoldensMatchCoreLib()
	{
		var info = System.Globalization.NumberFormatInfo.InvariantInfo;
		foreach (var style in new[] { 0, 1 })
		{
			var format = style == 0 ? "F512" : new string('0', 320) + ".0000";
			var expected = style == 0 ? "-123.5" + new string('0', 511) : "-" + new string('0', 317) + "123.5000";
			Assert.Equal(expected, (-123.5f).ToString(format, info));
			Assert.Equal(expected, (-123.5d).ToString(format, info));
			foreach (var wide in new[] { false, true })
			foreach (var shortBuffer in new[] { false, true })
			{
				var buffer = Enumerable.Repeat('#', expected.Length + (shortBuffer ? 1 : 2)).ToArray();
				var destination = buffer.AsSpan(1, buffer.Length - 2);
				var success = wide ? (-123.5d).TryFormat(destination, out var written, format, info) : (-123.5f).TryFormat(destination, out written, format, info);
				Assert.Equal(!shortBuffer, success);
				Assert.Equal(shortBuffer ? 0 : expected.Length, written);
				Assert.Equal(shortBuffer ? new string('#', buffer.Length) : "#" + expected + "#", new string(buffer));
			}
		}
	}

	[Fact]
	public void FloatingCapacityFailuresMatchCoreLib() => Assert.Equal(42, CompilerFixtures.CoreLibStringBuilderFloatingCapacityEntry());

	[Fact]
	public void FloatingListPrefixCopiesMatchCoreLib() => Assert.Equal(42, CompilerFixtures.CoreLibFloatingListPrefixCopyContractsEntry());

	[Theory]
	[InlineData("float", "CopySingles")]
	[InlineData("double", "CopyDoubles")]
	[InlineData("object", "CopyObjects")]
	public void ListPrefixCopyBindingRequiresExactOptInCallerAndArraySignature(string element, string target)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowArray).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true }));
		using var ordinary = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibFloatingListPrefixCopyContractsEntry");
		var callers = entry.Instructions.Where(instruction => instruction.OpCode == OpCodes.Callvirt)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, entry, instruction.Offset).Definition)
			.Where(method => method?.DisplayName == $"System.Collections.Generic.List`1<{element}>::set_Capacity" || method?.DisplayName == $"System.Collections.Generic.List`1<{element}>::ToArray")
			.OfType<CilMethod>().DistinctBy(method => method.Identity).ToArray();
		Assert.Equal(2, callers.Length);
		var array = FrameworkTypeId.Named("System.Runtime", "System.Array");
		var signature = new FrameworkMethodSignatureId(0, 0, 3, FrameworkTypeId.Primitive("System.Void"), [array, array, FrameworkTypeId.Primitive("System.Int32")]);
		var member = new FrameworkMemberId(array, "Copy", signature);
		foreach (var caller in callers)
		{
			Assert.True(module.TryCreateExperimentalListPrefixCopyBinding(member, caller, out var binding));
			Assert.Equal(target, binding.ShadowMethod!.MethodName);
			Assert.False(ordinary.TryCreateExperimentalListPrefixCopyBinding(member, caller, out _));
			Assert.False(module.TryCreateExperimentalListPrefixCopyBinding(member, entry, out _));
			Assert.False(module.TryCreateExperimentalListPrefixCopyBinding(member, caller with { ModuleName = "User.Assembly" }, out _));
			Assert.False(module.TryCreateExperimentalListPrefixCopyBinding(member, caller with { Name = "Other" }, out _));
			Assert.False(module.TryCreateExperimentalListPrefixCopyBinding(new FrameworkMemberId(array, "Copy", new FrameworkMethodSignatureId(0, 0, 2, FrameworkTypeId.Primitive("System.Void"), [array, array])), caller, out _));
			Assert.False(module.TryCreateExperimentalListPrefixCopyBinding(new FrameworkMemberId(FrameworkTypeId.Named("User.Assembly", "System.Array"), "Copy", signature), caller, out _));
		}
	}

	[Fact]
	public void FloatingArrayBitsAndBoundsMatchCoreLib() => Assert.Equal(42, CompilerFixtures.CoreLibFloatingPointArrayBitsEntry());

	[Theory]
	[InlineData("Single", 32)]
	[InlineData("Double", 36)]
	public void FloatingJoinEnumeratorKeepsReferenceFieldsSeparateFromCurrent(string precision, int size)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowSingleJoinEnumeration).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true }));
		var factory = module.ResolveManagedMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.Shadow" + precision + "JoinEnumeration::GetEnumerator");
		var layout = module.GetRuntimeTypeLayout(module.ResolveRuntimeTypeIdentity(factory.Signature.ReturnType, factory.ModuleName));
		Assert.Equal(size, layout.Size);
		Assert.Equal(precision == "Single" ? 0x23u : 0x43u, layout.ReferenceBitmap);
		Assert.Equal(new[] { 8, 12, 16, 20, 24, size - 4 }, layout.FieldOffsets.Values.Order());
	}

	[Fact]
	public void FloatingJoinEnumeratorsClearCurrentAndRejectListMutation()
	{
		var single = CopperSharp.Runtime.ShadowSingleJoinEnumeration.GetEnumerator(new float[] { -0f, float.Epsilon });
		Assert.True(single.MoveNext());
		Assert.Equal(0x80000000u, System.Runtime.CompilerServices.Unsafe.BitCast<float, uint>(single.Current));
		Assert.True(single.MoveNext());
		Assert.Equal(1u, System.Runtime.CompilerServices.Unsafe.BitCast<float, uint>(single.Current));
		Assert.False(single.MoveNext());
		Assert.Equal(0u, System.Runtime.CompilerServices.Unsafe.BitCast<float, uint>(single.Current));
		Assert.Throws<NotSupportedException>(() => single.Reset());
		var wide = CopperSharp.Runtime.ShadowDoubleJoinEnumeration.GetEnumerator(new double[] { -0d, double.Epsilon });
		Assert.True(wide.MoveNext());
		Assert.Equal(0x8000000000000000ul, System.Runtime.CompilerServices.Unsafe.BitCast<double, ulong>(wide.Current));
		wide.Dispose();
		Assert.False(wide.MoveNext());
		Assert.Equal(0ul, System.Runtime.CompilerServices.Unsafe.BitCast<double, ulong>(wide.Current));
		var singleList = new CopperSharp.Runtime.ShadowList<float>();
		singleList.Add(float.Epsilon);
		var singleEnumerator = new CopperSharp.Runtime.ShadowSingleJoinEnumerator(singleList);
		Assert.True(singleEnumerator.MoveNext());
		singleList.Add(0f);
		Assert.Throws<InvalidOperationException>(() => singleEnumerator.MoveNext());
		var doubleList = new CopperSharp.Runtime.ShadowList<double>();
		doubleList.Add(double.Epsilon);
		var doubleEnumerator = new CopperSharp.Runtime.ShadowDoubleJoinEnumerator(doubleList);
		Assert.True(doubleEnumerator.MoveNext());
		doubleList.Add(0d);
		Assert.Throws<InvalidOperationException>(() => doubleEnumerator.MoveNext());
	}

	[Fact]
	public void FloatingEnumerableFormattingMatchesCoreLib() => Assert.Equal(42, CompilerFixtures.CoreLibStringBuilderFloatingEnumerableEntry());

	[Fact]
	public void FloatingObjectFormattingMatchesCoreLib() => Assert.Equal(42, CompilerFixtures.CoreLibStringBuilderFloatingObjectsEntry());

	[Fact]
	public void BoxedFloatingJoinDispatchRequiresVerifiedOptInLayouts()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowObjectJoinText).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true }));
		using var ordinary = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		var caller = module.ResolveManagedMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowObjectJoinText::ToString");
		var call = Assert.Single(caller.Instructions.Where(instruction => instruction.OpCode == OpCodes.Callvirt));
		var declaration = module.ResolveMethodToken((int)call.Operand!, caller, call.Offset).Definition!;
		Assert.Empty(module.GetObjectJoinDispatchEntries(declaration));
		foreach (var (name, size, metadataName) in new[] { ("float", 4, "System.Single"), ("double", 8, "System.Double") })
		{
			var type = new CilType(CilTypeKind.FloatingPoint, size, name);
			var layout = module.RegisterBoxedDispatchLayout(type, "System.Private.CoreLib");
			Assert.NotNull(layout);
			Assert.True(module.TryGetExperimentalFloatingFormattingType(layout, out var payload));
			Assert.Equal(type, payload);
			Assert.False(ordinary.TryGetExperimentalFloatingFormattingType(layout, out _));
			Assert.False(module.TryGetExperimentalFloatingFormattingType(layout with { Size = size + 4 }, out _));
			Assert.False(module.TryGetExperimentalFloatingFormattingType(layout with { ReferenceBitmap = 1 }, out _));
			Assert.False(module.TryGetExperimentalFloatingFormattingType(layout with { FieldOffsets = new Dictionary<System.Reflection.Metadata.FieldDefinitionHandle, int>() }, out _));
			var entry = Assert.Single(module.GetObjectJoinDispatchEntries(declaration).Where(entry => entry.Layout.Identity == layout.Identity));
			Assert.Equal(metadataName + "::ToString", entry.Method.DisplayName);
			Assert.Equal("System.Private.CoreLib", entry.Method.ModuleName);
		}
	}

	[Fact]
	public void FloatingProviderGrowthTextMatchesCoreLib() => Assert.Equal("seedAnew123:50|2|2|valid\nseedAnew123:50|2|2|valid", CompilerFixtures.CoreLibFloatingProviderTwoArgumentGrowthEntry());

	[Fact]
	public void FloatingProviderContractsMatchCoreLib() => Assert.Equal(42, CompilerFixtures.CoreLibFloatingValueProviderContractsEntry());

	[Fact]
	public void ExplicitFloatingFormatsMatchCoreLibOracles()
	{
		for (var scenario = 0; scenario < 46; scenario++)
		foreach (var custom in new[] { false, true })
		{
			var info = CompilerFixtures.FloatingExplicitInfo(custom);
			var format = CompilerFixtures.FloatingExplicitFormat(scenario);
			Assert.Equal(CompilerFixtures.FloatingExplicitExpected(scenario, false, custom), CompilerFixtures.FloatingExplicitSingle(scenario).ToString(format, info));
			Assert.Equal(CompilerFixtures.FloatingExplicitExpected(scenario, true, custom), CompilerFixtures.FloatingExplicitDouble(scenario).ToString(format, info));
		}
		Assert.Equal(42, CompilerFixtures.CoreLibFloatingPointStandardFormatsEntry());
		Assert.Equal(42, CompilerFixtures.CoreLibFloatingPointCustomFormatsEntry());
		Assert.Equal(42, CompilerFixtures.CoreLibStringBuilderFloatingExplicitFormatsEntry());
	}

	[Fact]
	public void ApplicationFieldHandleNamesDoNotAcquireTheOpaqueCoreLibRepresentation()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var assembly = InitializedSpanFixtureBuilder.CreateApplicationFieldHandle(pack.Directory);
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(assembly, frameworkImplementationPack: catalog);
		var method = module.ResolveEntryPoint("System.RuntimeFieldHandle::Identity");
		Assert.False(module.IsTransparentScalarType(method.Signature.ReturnType));
	}

	[Fact]
	public void CoreLibCeilingOverrideRequiresExactDoubleSignatureAndOptIn()
	{
		var real = FrameworkTypeId.Primitive("System.Double");
		var signature = new FrameworkMethodSignatureId(0, 0, 1, real, [real]);
		var member = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Math"), "Ceiling", signature, []);
		Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
		Assert.Equal("CopperSharp.Runtime.ShadowMath", binding.ShadowMethod!.TypeName);
		Assert.Equal("Ceiling", binding.ShadowMethod.MethodName);
		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
		foreach (var changed in new[] {
			new FrameworkMemberId(FrameworkTypeId.Named("Application", "System.Math"), member.Name, signature, []),
			new FrameworkMemberId(member.DeclaringType, "Other", signature, []),
			new FrameworkMemberId(member.DeclaringType, member.Name, new FrameworkMethodSignatureId(0x20, 0, 1, real, [real]), []),
			new FrameworkMemberId(member.DeclaringType, member.Name, new FrameworkMethodSignatureId(0, 0, 1, real, [FrameworkTypeId.Primitive("System.Single")]), []),
			new FrameworkMemberId(member.DeclaringType, member.Name, new FrameworkMethodSignatureId(0, 0, 1, FrameworkTypeId.Primitive("System.Int32"), [real]), [])
		}) Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(changed, true, out _));
	}

	[Theory]
	[InlineData("FloatingPointOverwriteUInt64")]
	[InlineData("FloatingPointOverwriteDouble")]
	public void ArgumentStoreProbesKeepTheAddressTakenParameterAndBothAssignments(string name)
	{
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location);
		var method = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::" + name);
		Assert.Contains(method.Instructions, instruction => instruction.OpCode == OpCodes.Ldarga || instruction.OpCode == OpCodes.Ldarga_S);
		Assert.Equal(2, method.Instructions.Count(instruction => instruction.OpCode == OpCodes.Starg || instruction.OpCode == OpCodes.Starg_S));
	}

	[Theory]
	[InlineData(false, 4, false, "Initialized spans require a static RVA field")]
	[InlineData(true, 3, false, "Initialized field size is not a multiple")]
	[InlineData(true, 4, true, "Initialized field handles may only feed an immediate span factory")]
	public void InitializedSpanCompilerRejectsInvalidStorageAndEscapingHandles(bool rva, int size, bool storeHandle, string message)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		// Host reflection can retain generated assemblies for the test process.
		var directory = Path.Combine(Path.GetDirectoryName(typeof(CompilerFixtures).Assembly.Location)!, "InitializedSpanProbes", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		var assembly = InitializedSpanFixtureBuilder.Create(directory, rva, size, storeHandle);
		var error = Assert.Throws<M68kCompilationException>(() => M68kCompiler.Compile(new M68kCompilationRequest {
			AssemblyPath = assembly, EntryPoint = "InitializedSpanProbe::Entry", Cpu = M68kCpuTarget.M68020,
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true }
		}));
		Assert.Contains(message, error.Message);
	}

	[Theory]
	[InlineData("System.Byte")]
	[InlineData("System.SByte")]
	[InlineData("System.Char")]
	[InlineData("System.Int16")]
	[InlineData("System.UInt16")]
	[InlineData("System.Int32")]
	[InlineData("System.UInt32")]
	[InlineData("System.Int64")]
	[InlineData("System.UInt64")]
	[InlineData("System.Single")]
	[InlineData("System.Double")]
	public void InitializedSpansRequireExactPrimitiveFactoryAndOptIn(string primitive)
	{
		var generic = FrameworkTypeId.GenericMethodParameter(0);
		var result = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [generic]);
		var parameters = new[] { FrameworkTypeId.Named("System.Runtime", "System.RuntimeFieldHandle") };
		var signature = new FrameworkMethodSignatureId(0x10, 1, 1, result, parameters);
		var member = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Runtime.CompilerServices.RuntimeHelpers"),
			"CreateSpan", signature, [FrameworkTypeId.Primitive(primitive)]);
		Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
		Assert.Equal("intrinsic:initialized-data-span", binding.Target);
		Assert.Equal(FrameworkEffects.ReadsManagedMemory, binding.EffectSummary.Effects);
		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
		foreach (var changed in new[] {
			new FrameworkMemberId(FrameworkTypeId.Named("Application", "System.Runtime.CompilerServices.RuntimeHelpers"), member.Name, signature, member.MethodTypeArguments),
			new FrameworkMemberId(member.DeclaringType, "Other", signature, member.MethodTypeArguments),
			new FrameworkMemberId(member.DeclaringType, member.Name, signature, [FrameworkTypeId.Primitive("System.Object")]),
			new FrameworkMemberId(member.DeclaringType, member.Name, signature, [FrameworkTypeId.Primitive("System.Boolean")]),
			new FrameworkMemberId(member.DeclaringType, member.Name, signature, [FrameworkTypeId.Named("Application", primitive)]),
			new FrameworkMemberId(member.DeclaringType, member.Name, new FrameworkMethodSignatureId(0x30, 1, 1, result, parameters), member.MethodTypeArguments),
			new FrameworkMemberId(member.DeclaringType, member.Name, new FrameworkMethodSignatureId(0x10, 1, 1, result,
				[FrameworkTypeId.Named("System.Runtime", "System.RuntimeTypeHandle")]), member.MethodTypeArguments),
			new FrameworkMemberId(member.DeclaringType, member.Name, new FrameworkMethodSignatureId(0x10, 1, 1,
				FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Span`1"), [generic]), parameters), member.MethodTypeArguments)
		}) Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(changed, true, out _));
	}

	[Fact]
	public void UInt32MemmoveRejectsReferenceElementsAndWrongSignatures()
	{
		var genericRef = FrameworkTypeId.ByReference(FrameworkTypeId.GenericMethodParameter(0));
		var parameters = new[] { genericRef, genericRef, FrameworkTypeId.Primitive("System.UIntPtr") };
		var signature = new FrameworkMethodSignatureId(0x10, 1, 3, FrameworkTypeId.Primitive("System.Void"), parameters);
		var member = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Buffer"), "Memmove", signature,
			[FrameworkTypeId.Primitive("System.UInt32")]);
		Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
		Assert.Equal("intrinsic:corelib-memmove-uint32", binding.Target);
		Assert.Equal(FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory, binding.EffectSummary.Effects);
		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
		foreach (var changed in new[] {
			new FrameworkMemberId(member.DeclaringType, member.Name, signature, [FrameworkTypeId.Primitive("System.Object")]),
			new FrameworkMemberId(member.DeclaringType, member.Name, signature, [FrameworkTypeId.Primitive("System.UInt64")]),
			new FrameworkMemberId(FrameworkTypeId.Named("Application", "System.Buffer"), member.Name, signature, member.MethodTypeArguments),
			new FrameworkMemberId(member.DeclaringType, member.Name, new FrameworkMethodSignatureId(0x10, 1, 3, signature.ReturnType,
				[genericRef, genericRef, FrameworkTypeId.Primitive("System.Int32")]), member.MethodTypeArguments),
			new FrameworkMemberId(member.DeclaringType, member.Name, new FrameworkMethodSignatureId(0x30, 1, 3, signature.ReturnType, parameters), member.MethodTypeArguments)
		}) Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(changed, true, out _));
	}

	[Theory]
	[InlineData("FloatingPointSignedTable", "3412C7CF0080FF7F")]
	[InlineData("FloatingPointWordTable", "78563412EFCDAB8900000000FFFFFFFF")]
	[InlineData("FloatingPointWideTable", "EFCDAB89674523011032547698BADCFE0000000000000000FFFFFFFFFFFFFFFF")]
	[InlineData("FloatingPointCharacterTable", "4100A903000000D8")]
	public void InitializedSpanMetadataRetainsExactPeBytes(string factory, string expected)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack: catalog);
		var method = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::" + factory);
		var token = method.Instructions.Single(instruction => instruction.OpCode == OpCodes.Ldtoken);
		Assert.Equal(expected, Convert.ToHexString(module.ReadInitializedFieldRva((int)token.Operand!, method, token.Offset).AsSpan()));
		Assert.Throws<M68kCompilationException>(() => module.ReadInitializedFieldRva(MetadataTokens.GetToken(method.Handle), method, token.Offset));
	}

	[Fact]
	public void FormattingClosureUsesTargetCompatibleBodies()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var analysis = M68kCompiler.AnalyzeFramework(new M68kCompilationRequest {
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibStringBuilderFloatingPointSmokeEntry",
			ManagedAssemblyPaths = [typeof(CopperSharp.Runtime.AmigaPal.EnvironmentPal).Assembly.Location],
			FloatingPoint = M68kFloatingPointMode.SoftFloat, Cpu = M68kCpuTarget.M68020, ExceptionMode = M68kExceptionMode.Full,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true }
		});
		Assert.True(analysis.IsCompatible, string.Join("\n", analysis.Members
			.Where(member => member.Status == M68kFrameworkCompatibilityStatus.Unsupported)
			.Select(member => member.Member.DisplayName + ": " + string.Join(" -> ", member.CallSites[0].RootPath))));
	}

	[Theory]
	[InlineData("float", "System.Single")]
	[InlineData("double", "System.Double")]
	public void ConstrainedFallbackSelectsTheExactPrimitiveOverride(string primitive, string owner)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack: catalog);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibStringBuilderFloatingPointSmokeEntry");
		var append = entry.Instructions.Where(instruction => instruction.OpCode == OpCodes.Callvirt)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, entry, instruction.Offset).Definition)
			.First(method => method?.DisplayName == "System.Text.StringBuilder::Append" && method.Signature.ParameterTypes is [var value] && value.DisplayName == primitive)!;
		var core = append.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, append, instruction.Offset).Definition)
			.First(method => method?.DisplayName == $"System.Text.StringBuilder::AppendSpanFormattable<{primitive}>")!;
		var call = core.Instructions.Single(instruction => instruction.ConstrainedTypeToken is not null &&
			module.DescribeMethodToken((int)instruction.Operand!, core, instruction.Offset)?.Name == "ToString");
		var implementation = module.ResolveMethodToken((int)call.Operand!, core, call.Offset).Definition!;
		Assert.Equal(owner + "::ToString", implementation.DisplayName);
		Assert.Equal("System.Private.CoreLib", implementation.ModuleName);
		Assert.True(module.TryResolveConstrainedValueInterfaceImplementation(core, call.ConstrainedTypeToken!.Value, call.Offset, implementation, out var constrained));
		Assert.Equal(implementation.Identity, constrained.Identity);
		using var ordinary = new CompilationModule(typeof(CompilerFixtures).Assembly.Location);
		Assert.Throws<M68kCompilationException>(() => ordinary.ResolveConstrainedInterfaceImplementation(core,
			call.ConstrainedTypeToken.Value, call.Offset, implementation));
	}

	[Fact]
	public void CharacterSpanIdentityCastRequiresExactTypesSignatureAndOptIn()
	{
		var span = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Span`1"), [FrameworkTypeId.Primitive("System.Char")]);
		var signature = new FrameworkMethodSignatureId(0x10, 2, 1, FrameworkTypeId.GenericMethodParameter(1), [FrameworkTypeId.GenericMethodParameter(0)]);
		var member = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Runtime.CompilerServices.Unsafe"), "BitCast", signature, [span, span]);
		Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
		Assert.Equal("CopperSharp.Runtime.ShadowCharacterSpans", binding.ShadowMethod!.TypeName);
		Assert.Equal("Identity", binding.ShadowMethod.MethodName);
		Assert.Equal(FrameworkEffects.None, binding.EffectSummary.Effects);
		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
		var bytes = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Span`1"), [FrameworkTypeId.Primitive("System.Byte")]);
		var readOnly = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Char")]);
		foreach (var changed in new[] {
			new FrameworkMemberId(member.DeclaringType, member.Name, signature, [span, bytes]),
			new FrameworkMemberId(member.DeclaringType, member.Name, signature, [bytes, bytes]),
			new FrameworkMemberId(member.DeclaringType, member.Name, signature, [span, readOnly]),
			new FrameworkMemberId(FrameworkTypeId.Named("Application", "System.Runtime.CompilerServices.Unsafe"), member.Name, signature, [span, span]),
			new FrameworkMemberId(member.DeclaringType, member.Name, new FrameworkMethodSignatureId(0x30, 2, 1, signature.ReturnType, signature.ParameterTypes), [span, span]),
			new FrameworkMemberId(member.DeclaringType, member.Name, new FrameworkMethodSignatureId(0x10, 2, 1, FrameworkTypeId.Primitive("System.Int32"), signature.ParameterTypes), [span, span])
		}) Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(changed, true, out _));
	}

	[Theory]
	[InlineData("System.Single", "System.UInt32", "intrinsic:runtime-bitcast-32")]
	[InlineData("System.Single", "System.Int32", "intrinsic:runtime-bitcast-32")]
	[InlineData("System.UInt32", "System.Single", "intrinsic:runtime-bitcast-32")]
	[InlineData("System.Int32", "System.Single", "intrinsic:runtime-bitcast-32")]
	[InlineData("System.Double", "System.UInt64", "DoubleToUInt64")]
	[InlineData("System.Double", "System.Int64", "DoubleToInt64")]
	[InlineData("System.UInt64", "System.Double", "UInt64ToDouble")]
	[InlineData("System.Int64", "System.Double", "Int64ToDouble")]
	public void NumericBitProjectionsRequireExactPrimitivePairs(string source, string destination, string target)
	{
		var signature = new FrameworkMethodSignatureId(0x10, 2, 1, FrameworkTypeId.GenericMethodParameter(1), [FrameworkTypeId.GenericMethodParameter(0)]);
		var member = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Runtime.CompilerServices.Unsafe"), "BitCast", signature,
			[FrameworkTypeId.Primitive(source), FrameworkTypeId.Primitive(destination)]);
		Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
		Assert.Equal(target, binding.ShadowMethod?.MethodName ?? binding.Target);
		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
		foreach (var changed in new[] {
			new FrameworkMemberId(member.DeclaringType, "Other", signature, member.MethodTypeArguments),
			new FrameworkMemberId(FrameworkTypeId.Named("Application", "System.Runtime.CompilerServices.Unsafe"), member.Name, signature, member.MethodTypeArguments),
			new FrameworkMemberId(member.DeclaringType, member.Name, signature, [FrameworkTypeId.Primitive(source), FrameworkTypeId.Primitive("System.Byte")]),
			new FrameworkMemberId(member.DeclaringType, member.Name, new FrameworkMethodSignatureId(0x30, 2, 1, signature.ReturnType, signature.ParameterTypes), member.MethodTypeArguments)
		}) Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(changed, true, out _));
	}
}
