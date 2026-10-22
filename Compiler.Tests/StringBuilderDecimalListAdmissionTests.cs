/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Reflection.Emit;
using System.Reflection.Metadata.Ecma335;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderDecimalListAdmissionTests
{
	[Theory]
	[InlineData("String", "CopperSharp.Runtime.Managed")]
	[InlineData("Int32", "CopperSharp.Runtime.Managed")]
	[InlineData("Object", "CopperSharp.Runtime.Managed")]
	[InlineData("Decimal", "System.Private.CoreLib")]
	[InlineData("Single", "CopperSharp.Runtime.Managed")]
	[InlineData("Double", "CopperSharp.Runtime.Managed")]
	public void JoinListTypeTestsMatchTheAllocatedDescriptor(string element, string expectedModule)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		using var application = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(ShadowArray).Assembly.Location], frameworkImplementationPack: catalog);
		var entry = application.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::Stable" + element + "ListIdentity");
		var allocation = entry.Instructions.Single(instruction => instruction.OpCode == OpCodes.Newobj);
		var allocated = application.GetTypeLayout(application.ResolveMethodToken((int)allocation.Operand!, entry, allocation.Offset).Definition!);
		Assert.Equal(expectedModule, allocated.ModuleName);
		using var runtime = new CompilationModule(typeof(ShadowArray).Assembly.Location, frameworkImplementationPack: catalog);
		var factory = runtime.ResolveEntryPoint("CopperSharp.Runtime.Shadow" + element + "JoinEnumeration::GetEnumerator");
		var casts = factory.Instructions.Where(instruction => instruction.OpCode == OpCodes.Isinst || instruction.OpCode == OpCodes.Castclass)
			.Where(instruction => runtime.ResolveTypeToken((int)instruction.Operand!, factory, instruction.Offset).DisplayName.Contains("List`1<", StringComparison.Ordinal)).ToArray();
		Assert.Equal(2, casts.Length);
		foreach (var cast in casts)
		{
			var tested = runtime.GetRuntimeTypeLayout(runtime.ResolveRuntimeTypeToken((int)cast.Operand!, factory, cast.Offset));
			Assert.Equal(allocated.Identity, tested.Identity);
			Assert.Equal(allocated.Size, tested.Size);
			Assert.Equal(allocated.ReferenceBitmap, tested.ReferenceBitmap);
		}
	}

	[Fact]
	public void ReleasedListDefinitionsRequireClosedDecimalOwnership()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack: catalog);
			var list = module.Reader.TypeDefinitions.Single(handle => module.Reader.GetString(module.Reader.GetTypeDefinition(handle).Namespace) == "System.Collections.Generic" &&
				module.Reader.GetString(module.Reader.GetTypeDefinition(handle).Name) == "List`1");
			var enumerator = module.Reader.GetTypeDefinition(list).GetNestedTypes().Single(handle => module.Reader.GetString(module.Reader.GetTypeDefinition(handle).Name) == "Enumerator");
			var actual = new[] { list, enumerator }.SelectMany(handle => module.Reader.GetTypeDefinition(handle).GetMethods()).Select(module.GetMethod)
				.Select(method => module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(method.Handle), method, -1))
				.Select(member => FrameworkImplementationProfile.Canonicalize(new FrameworkMemberId(
					FrameworkTypeId.GenericInstantiation(member.DeclaringType, [FrameworkTypeId.Named("System.Private.CoreLib", "System.Decimal")]), member.Name, member.Signature, member.MethodTypeArguments))).ToArray();
			var caller = new FrameworkMemberId(FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Private.CoreLib", "System.Collections.Generic.List`1"),
				[FrameworkTypeId.Named("System.Private.CoreLib", "System.Decimal")]), "Add", StringBuilderDecimalListSurface.PublicMembers.Single(member => member.Name == "Add").Signature);
			var exceptions = module.Reader.TypeDefinitions.Select(module.Reader.GetTypeDefinition).Single(type => module.Reader.GetString(type.Namespace) == "System" && module.Reader.GetString(type.Name) == "ThrowHelper")
				.GetMethods().Select(module.GetMethod).Select(method => FrameworkImplementationProfile.Canonicalize(module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(method.Handle), method, -1))).ToArray();
			foreach (var helper in StringBuilderDecimalListSurface.ExceptionHelpers) Assert.Contains(helper, exceptions);
			foreach (var member in StringBuilderDecimalListSurface.PublicMembers.Concat(StringBuilderDecimalListSurface.Helpers))
			{
				Assert.Contains(member, actual);
				Assert.Equal(admitted, Admit(member, caller, out var binding));
				Assert.Equal(admitted && StringBuilderDecimalListSurface.IsPublic(member), Admit(member, null, out _));
				Assert.False(Admit(new(member.DeclaringType, "Unlisted", member.Signature), caller, out _));
				Assert.False(Admit(new(member.DeclaringType, member.Name, member.Signature, [FrameworkTypeId.Primitive("System.Int32")]), caller, out _));
				Assert.False(Admit(new(FrameworkTypeId.GenericInstantiation(member.DeclaringType.ElementType!, [FrameworkTypeId.Primitive("System.Single")]), member.Name, member.Signature), caller, out _));
				if (!StringBuilderDecimalListSurface.IsPublic(member))
					Assert.False(Admit(member, new(caller.DeclaringType, "Unlisted", caller.Signature), out _));
				if (admitted) Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayCollect | FrameworkEffects.MayThrow));
			}
			bool Admit(FrameworkMemberId member, FrameworkMemberId? source, out FrameworkBinding binding) =>
				FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, source, out binding);
		}
	}

	[Fact]
	public void CopyAndClearRetainAggregateWordsAndValidateRanges()
	{
		var source = new[] { new decimal(1, 2, 3, true, 28), new decimal(0, 0, 0, true, 3) };
		var destination = new decimal[3];
		ShadowArray.CopyDecimals(source, destination, 2);
		Assert.Equal(decimal.GetBits(source[0]), decimal.GetBits(destination[0]));
		Assert.Equal(decimal.GetBits(source[1]), decimal.GetBits(destination[1]));
		ShadowArray.ClearDecimals(destination, 1, 1);
		Assert.Equal(decimal.GetBits(source[0]), decimal.GetBits(destination[0]));
		Assert.Equal(new int[4], decimal.GetBits(destination[1]));
		Assert.Throws<ArgumentNullException>(() => ShadowArray.CopyDecimals(null!, destination, 0));
		Assert.Throws<NotSupportedException>(() => ShadowArray.CopyDecimals(new int[2], destination, 2));
		Assert.Throws<ArgumentException>(() => ShadowArray.CopyDecimals(source, destination, 3));
		Assert.Throws<ArgumentOutOfRangeException>(() => ShadowArray.CopyDecimals(source, destination, -1));
		Assert.Throws<IndexOutOfRangeException>(() => ShadowArray.ClearDecimals(destination, 2, 2));
		Assert.Throws<IndexOutOfRangeException>(() => ShadowArray.ClearDecimals(destination, -1, 1));
	}

	[Fact]
	public void EnumeratorTransportRequiresReleasedReferenceFreeDecimalLayout()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack:
				FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
			var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CreateOwnedCoreLibDecimalListIterator");
			Assert.Equal(admitted, module.TryGetReferenceFreeStructLayout(entry.Signature.ReturnType, "System.Private.CoreLib", out var layout));
			if (admitted) { Assert.Equal(28, layout.Size); Assert.Equal(1u, layout.ReferenceBitmap); }
		}
	}

	[Fact]
	public void DecimalPrefixCopyRequiresTheActualOwnedCapacityCaller()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, managedAssemblyPaths: [typeof(ShadowArray).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::StringBuilderStableDecimalListGrowthEntry");
		var add = entry.Instructions.Where(instruction => instruction.OpCode == OpCodes.Callvirt)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, entry, instruction.Offset).Definition).First(method => method?.Name == "Add")!;
		var pending = new Queue<CilMethod>(); pending.Enqueue(add);
		var visited = new HashSet<string>();
		CilMethod? capacity = null;
		while (pending.TryDequeue(out var caller))
		{
			if (!visited.Add(caller.DisplayName)) continue;
			if (caller.Name == "set_Capacity") { capacity = caller; break; }
			foreach (var call in caller.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt))
			{
				var declaration = module.DescribeFrameworkMethodToken((int)call.Operand!, caller, call.Offset);
				if (declaration.Name is "AddWithResize" or "Grow" or "set_Capacity") pending.Enqueue(module.ResolveMethodToken((int)call.Operand!, caller, call.Offset).Definition!);
			}
		}
		Assert.NotNull(capacity);
		var copy = capacity.Instructions.Single(instruction => instruction.OpCode == OpCodes.Call && module.DescribeFrameworkMethodToken((int)instruction.Operand!, capacity, instruction.Offset).Name == "Copy");
		var member = module.DescribeFrameworkMethodToken((int)copy.Operand!, capacity, copy.Offset);
		Assert.True(module.TryCreateExperimentalListPrefixCopyBinding(member, capacity, out var binding));
		Assert.Equal("CopyDecimals", binding.ShadowMethod!.MethodName);
		Assert.False(module.TryCreateExperimentalListPrefixCopyBinding(member, null, out _));
		Assert.False(module.TryCreateExperimentalListPrefixCopyBinding(member, capacity with { ModuleName = "Application" }, out _));
		Assert.False(module.TryCreateExperimentalListPrefixCopyBinding(member, capacity with { ConstructedDeclaringType = null }, out _));
		Assert.False(module.TryCreateExperimentalListPrefixCopyBinding(new(member.DeclaringType, "Unlisted", member.Signature), capacity, out _));
		pack.Replace("packVersion", "10.0.10");
		using var adjacent = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack:
			FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
		Assert.False(adjacent.TryCreateExperimentalListPrefixCopyBinding(member, capacity, out _));
	}

	[Fact]
	public void NongenericListEnumerationRetainsItsTypedInterfaceDispatch()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, managedAssemblyPaths: [typeof(ShadowArray).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::StringBuilderStableDecimalListGrowthEntry");
		var constructor = entry.Instructions.First(instruction => instruction.OpCode == OpCodes.Newobj);
		var layout = module.GetTypeLayout(module.ResolveMethodToken((int)constructor.Operand!, entry, constructor.Offset).Definition!);
		var contract = module.GetRuntimeInterfaceDefinition(module.ResolveRuntimeTypeIdentity(new CilType(CilTypeKind.ManagedReference, 4, "System.Collections.IEnumerable"), "System.Private.CoreLib"));
		var caller = module.TryGetInterfaceImplementation(layout, contract)!.Methods.Single(method => method.Name == "System.Collections.IEnumerable.GetEnumerator");
		var call = caller.Instructions.Single(instruction => instruction.OpCode == OpCodes.Callvirt);
		var reference = module.ResolveMethodToken((int)call.Operand!, caller, call.Offset);
		Assert.True(reference.Definition!.DeclaringTypeIsInterface);
		Assert.Equal("System.Collections.Generic.IEnumerable`1<System.Decimal>", reference.Definition.ConstructedDeclaringType!.DisplayName);
		Assert.Equal("GetEnumerator", reference.Definition.Name);
	}
}
