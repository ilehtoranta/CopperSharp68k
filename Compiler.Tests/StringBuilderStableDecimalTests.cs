/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Reflection.Metadata.Ecma335;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderStableDecimalTests
{
	[Fact]
	public void ReleasedFacadeDecimalTokenEmitsAllocationInsteadOfAReferenceCopy()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
		var caller = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibStringBuilderDecimalSmokeEntry");
		var box = Assert.Single(caller.Instructions.Where(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Box));
		var type = module.ResolveTypeToken((int)box.Operand!, caller, box.Offset);
		Assert.Equal(CilTypeKind.ValueType, type.Kind);
		Assert.Equal("System.Decimal", type.DisplayName);
		Assert.True(module.IsSupportedStructType(type));
		var function = CopperSharp.Compiler.Backend.CilMachineIrBuilder.Build(caller, module);
		Assert.Contains(function.Blocks.SelectMany(block => block.Instructions), instruction => instruction.Operation == CopperSharp.Compiler.Backend.M68kMachineOperation.Box && instruction.IlOffset == box.Offset);
	}

	[Fact]
	public void DecimalAdmissionAndStorageRequireTheReleasedInputAndExactMembers()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack: catalog);
			var type = new CilType(CilTypeKind.ValueType, 0, "System.Decimal");
			Assert.Equal(admitted, module.IsSupportedStructType(type));
			Assert.Equal(admitted, module.TryGetReferenceFreeStructLayout(type, "System.Private.CoreLib", out var layout));
			Assert.Equal(admitted, module.RegisterBoxedDispatchLayout(type, "System.Private.CoreLib") is not null);
			if (layout is not null) { Assert.Equal(16, layout.Size); Assert.Equal(0u, layout.ReferenceBitmap); Assert.Equal(new[] { 0, 4, 8 }, layout.FieldOffsets.Values.Order()); }
			var caller = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.Decimal"), "ToString",
				new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.String"), []));
			foreach (var member in StringBuilderDecimalSurface.PublicMembers.Concat(StringBuilderDecimalSurface.PrivateMembers))
			{
				Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, caller, out _));
				Assert.Equal(admitted && StringBuilderDecimalSurface.IsPublic(member), FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(new FrameworkMemberId(member.DeclaringType, "Unlisted", member.Signature), null, catalog, caller, out _));
			}
		}
	}

	[Fact]
	public void DecimalScaleValidationUsesTheExactReleasedComparison()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(pack.AssemblyPath,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
		var caller = module.ResolveEntryPoint("System.ArgumentOutOfRangeException::ThrowIfGreaterThan");
		var definition = module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(caller.Handle), caller, -1);
		Assert.True(StringBuilderDecimalSurface.IsValidationCaller(definition), definition.DisplayName);
		var call = Assert.Single(caller.Instructions.Where(instruction => instruction.ConstrainedTypeToken is not null));
		var member = module.DescribeFrameworkMethodToken((int)call.Operand!, caller, call.Offset);
		Assert.True(StringBuilderDecimalSurface.IsComparisonDependency(member), System.Text.Json.JsonSerializer.Serialize(member));
		Assert.True(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, module.FrameworkImplementationPack!, definition, out _));
		var decimalType = module.Reader.TypeDefinitions.Single(handle => module.Reader.GetString(module.Reader.GetTypeDefinition(handle).Name) == "Decimal");
		var constructor = module.Reader.GetTypeDefinition(decimalType).GetMethods().Select(module.GetMethod).Single(method => method.Name == ".ctor" && method.Signature.ParameterTypes.Length == 5);
		var validation = Assert.Single(constructor.Instructions.Where(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Call));
		var closed = module.ResolveMethodToken((int)validation.Operand!, constructor, validation.Offset).Definition!;
		var comparison = Assert.Single(closed.Instructions.Where(instruction => instruction.ConstrainedTypeToken is not null));
		var comparisonMember = module.DescribeFrameworkMethodToken((int)comparison.Operand!, closed, comparison.Offset);
		Assert.True(module.TryCreatePinnedDecimalScaleComparisonBinding(comparisonMember, closed, comparison.Offset, out _, out _),
			string.Join(",", closed.MethodTypeArguments.Select(type => $"{type.Kind}/{type.Size}/{type.DisplayName}")) + "; " + comparisonMember.DisplayName + "; " +
			module.ResolveTypeToken(comparison.ConstrainedTypeToken!.Value, closed, comparison.Offset) + "; " + module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(closed.Handle), closed, -1).DisplayName);
		Assert.False(module.TryCreatePinnedDecimalScaleComparisonBinding(comparisonMember, null, comparison.Offset, out _, out _));
		Assert.False(module.TryCreatePinnedDecimalScaleComparisonBinding(comparisonMember, closed, -1, out _, out _));
		Assert.False(module.TryCreatePinnedDecimalScaleComparisonBinding(new FrameworkMemberId(comparisonMember.DeclaringType, "Unlisted", comparisonMember.Signature), closed, comparison.Offset, out _, out _));
		Assert.False(module.TryCreatePinnedDecimalScaleComparisonBinding(comparisonMember,
			closed with { MethodTypeArguments = closed.MethodTypeArguments.SetItem(0, new CilType(CilTypeKind.UnsignedInteger, 4, "uint")) }, comparison.Offset, out _, out _));
		pack.Replace("packVersion", "10.0.10");
		using var adjacent = new CompilationModule(pack.AssemblyPath,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
		Assert.False(adjacent.TryCreatePinnedDecimalScaleComparisonBinding(comparisonMember, closed, comparison.Offset, out _, out _));
	}
	[Fact]
	public void ReleasedDecimalFormattingClosesTheStableGraph()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(pack.AssemblyPath,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
		var type = module.Reader.TypeDefinitions.Select(module.Reader.GetTypeDefinition).Single(type => module.Reader.GetString(type.Name) == "Decimal");
		File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "stringbuilder-decimal-definitions.txt"), type.GetMethods().Select(module.GetMethod)
			.Select(method => module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(method.Handle), method, -1).DisplayName));
		File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "stringbuilder-decimal-supporting-definitions.txt"), module.Reader.TypeDefinitions
			.Where(handle => {
				var definition = module.Reader.GetTypeDefinition(handle);
				var name = module.Reader.GetString(definition.Name);
				var ns = module.Reader.GetString(definition.Namespace);
				return ns == "System.Globalization" && name == "NumberFormatInfo" ||
					ns == "System.Collections.Generic" && name == "List`1" ||
					name == "Enumerator" && !definition.GetDeclaringType().IsNil &&
					module.Reader.GetString(module.Reader.GetTypeDefinition(definition.GetDeclaringType()).Name) == "List`1";
			})
			.SelectMany(handle => module.Reader.GetTypeDefinition(handle).GetMethods()).Select(module.GetMethod)
			.Select(method => module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(method.Handle), method, -1).DisplayName));
		foreach (var entry in new[] { "CoreLibStringBuilderDecimalSmokeEntry", "CoreLibStringBuilderDecimalMatrixEntry" })
		{
			Assert.Equal(42, (int)typeof(CompilerFixtures).GetMethod(entry)!.Invoke(null, null)!);
			var result = M68kCompiler.AnalyzeFramework(new M68kCompilationRequest {
				AssemblyPath = typeof(CompilerFixtures).Assembly.Location, EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::" + entry,
				IncludedExportNames = [], ExceptionMode = M68kExceptionMode.Full, MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
			});
			File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "stringbuilder-stable-decimal-" + entry + ".json"), System.Text.Json.JsonSerializer.Serialize(result));
			Assert.True(result.IsCompatible, string.Join("\n", result.Members.Where(member => member.Status == M68kFrameworkCompatibilityStatus.Unsupported)
				.Select(member => member.Member.DisplayName + ": " + member.Reason)));
		}
	}
}

public sealed class StringBuilderStableDecimalContractGraphTests
{
	internal static void VerifyDecimalJoinLifetimeHostReference()
	{
		// AOT verifies the List/ShadowList field view. On CLR, construct the
		// adapter's backing list explicitly; the native fixture keeps its casts.
		var factory = typeof(CompilerFixtures).GetMethod("CreateRetainedDecimalJoinSource", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
		foreach (var useList in new[] { false, true })
		{
			var source = (IEnumerable<decimal>)factory.Invoke(null, [useList])!;
			var values = source.ToArray();
			Assert.Equal(new[] { "-123.4500", "0.0000000000000000000000000001" },
				values.Select(value => value.ToString(System.Globalization.NumberFormatInfo.InvariantInfo)));
			var backing = new CopperSharp.Runtime.ShadowList<decimal>(4);
			foreach (var value in values) backing.Add(value);
			var iterator = useList ? new CopperSharp.Runtime.ShadowDecimalJoinEnumerator(backing)
				: new CopperSharp.Runtime.ShadowDecimalJoinEnumerator(values);
			source = null!; values = null!; backing = null!;
			GC.Collect();
			var pressure = new int[64]; pressure[0] = 123;
			foreach (var expected in new[] { "-123.4500", "0.0000000000000000000000000001" })
			{
				Assert.True(iterator.MoveNext()); GC.Collect();
				Assert.Equal(expected, iterator.Current.ToString(System.Globalization.NumberFormatInfo.InvariantInfo));
			}
			Assert.False(iterator.MoveNext()); Assert.Equal(0m, iterator.Current); Assert.False(iterator.MoveNext());
			iterator.Dispose(); GC.Collect();
			Assert.False(iterator.MoveNext()); Assert.Equal(0m, iterator.Current); Assert.Equal(123, pressure[0]);
		}
		for (var mutation = 0; mutation < 4; mutation++)
		{
			var values = new[] { new decimal(1234500, 0, 0, true, 4), new decimal(1, 0, 0, false, 28) };
			var backing = new CopperSharp.Runtime.ShadowList<decimal>(4);
			foreach (var value in values) backing.Add(value);
			var reference = new List<decimal>(values);
			var iterator = new CopperSharp.Runtime.ShadowDecimalJoinEnumerator(backing);
			var referenceIterator = reference.GetEnumerator();
			Assert.True(iterator.MoveNext()); Assert.True(referenceIterator.MoveNext());
			if (mutation == 3)
			{
				Assert.True(iterator.MoveNext()); Assert.False(iterator.MoveNext());
				Assert.True(referenceIterator.MoveNext()); Assert.False(referenceIterator.MoveNext());
			}
			if (mutation == 1) { backing[0] = 7m; reference[0] = 7m; }
			else if (mutation == 2) { backing.Clear(); reference.Clear(); }
			else { backing.Add(9m); reference.Add(9m); }
			Assert.Throws<InvalidOperationException>(() => iterator.MoveNext());
			Assert.Throws<InvalidOperationException>(() => referenceIterator.MoveNext());
			iterator.Dispose(); referenceIterator.Dispose();
			Assert.False(iterator.MoveNext()); Assert.Equal(0m, iterator.Current);
		}
		var builder = new System.Text.StringBuilder(1).Append("seed");
		foreach (var character in new[] { false, true })
		{
			var error = Assert.Throws<ArgumentNullException>(() => character ? builder.AppendJoin<decimal>('|', (IEnumerable<decimal>)null!) : builder.AppendJoin<decimal>("|", (IEnumerable<decimal>)null!));
			Assert.Equal("values", error.ParamName); Assert.Equal("seed", builder.ToString());
			Assert.Throws<InvalidOperationException>(() => character ? builder.AppendJoin<decimal>('|', new CompilerFixtures.ThrowingDecimalJoinEnumerable()) : builder.AppendJoin<decimal>("|", new CompilerFixtures.ThrowingDecimalJoinEnumerable()));
			Assert.Equal("seed", builder.ToString());
		}
		builder.AppendJoin<decimal>("|", Array.Empty<decimal>()).AppendJoin<decimal>('|', new List<decimal>());
		Assert.Equal("seed", builder.ToString());
	}

	[Theory]
	[InlineData("CoreLibStringBuilderBoxedDecimalEntry")]
	[InlineData("CoreLibStringBuilderDecimalEnumerableJoinEntry")]
	[InlineData("CoreLibStringBuilderDecimalJoinLifetimeEntry")]
	[InlineData("CoreLibDecimalValueProviderContractsEntry")]
	[InlineData("CoreLibStringBuilderDecimalCapacityEntry")]
	[InlineData("CoreLibDecimalPublicConstantsEntry")]
	[InlineData("CoreLibDecimalCustomCallbackContractsEntry")]
	[InlineData("CoreLibDecimalCustomIteratorOwnershipEntry")]
	[InlineData("CoreLibDecimalListIteratorOwnershipEntry")]
	[InlineData("StringBuilderStableDecimalListGrowthEntry")]
	public void DecimalContractsRequireAClosedReleasedGraph(string entry)
	{
		if (entry == "CoreLibStringBuilderDecimalJoinLifetimeEntry")
			VerifyDecimalJoinLifetimeHostReference();
		else Assert.Equal(42, (int)typeof(CompilerFixtures).GetMethod(entry)!.Invoke(null, null)!);
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var result = M68kCompiler.AnalyzeFramework(new M68kCompilationRequest {
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location, EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::" + entry,
			ManagedAssemblyPaths = [typeof(CopperSharp.Runtime.AmigaPal.EnvironmentPal).Assembly.Location],
			IncludedExportNames = [], ExceptionMode = M68kExceptionMode.Full, MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		});
		File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "stringbuilder-stable-decimal-contract-" + entry + ".json"), System.Text.Json.JsonSerializer.Serialize(result));
		Assert.True(result.IsCompatible, string.Join("\n", result.Members.Where(member => member.Status == M68kFrameworkCompatibilityStatus.Unsupported)
			.Select(member => member.Member.DisplayName + ": " + member.Reason)));
	}
}
