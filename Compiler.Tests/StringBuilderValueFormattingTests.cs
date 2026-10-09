/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderValueFormattingTests
{
	[Fact]
	public void DefaultClassJoinDispatchPreservesTheOriginalObjectSlot()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = Open(pack, true);
		using var stable = Open(pack, false);
		var caller = module.ResolveManagedMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowObjectJoinText::ToString");
		var call = Assert.Single(caller.Instructions.Where(instruction => instruction.OpCode == OpCodes.Callvirt));
		var declaration = module.ResolveMethodToken((int)call.Operand!, caller, call.Offset).Definition!;
		Assert.True(module.IsObjectJoinDispatch(declaration));
		Assert.False(stable.IsObjectJoinDispatch(declaration));
		var stableCaller = stable.ResolveManagedMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowObjectJoinText::ToString");
		var stableCall = Assert.Single(stableCaller.Instructions.Where(instruction => instruction.OpCode == OpCodes.Callvirt));
		var stableDeclaration = stable.ResolveMethodToken((int)stableCall.Operand!, stableCaller, stableCall.Offset).Definition!;
		Assert.True(stable.IsObjectJoinDispatch(stableDeclaration));
		Assert.Empty(module.GetObjectJoinDispatchEntries(declaration));
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibStringBuilderDefaultClassNamesEntry");
		foreach (var allocation in entry.Instructions.Where(instruction => instruction.OpCode == OpCodes.Newobj))
		{
			var constructor = module.ResolveMethodToken((int)allocation.Operand!, entry, allocation.Offset).Definition;
			if (constructor is not null && constructor.ModuleName == module.AssemblyName)
				module.RegisterReachableDispatchLayout(module.GetTypeLayout(constructor));
		}
		var entries = module.GetObjectJoinDispatchEntries(declaration);
		Assert.Equal(8, entries.Count);
		Assert.Equal(7, entries.Count(item => item.Method.DisplayName == "System.Object::ToString" && item.Method.ModuleName == "System.Private.CoreLib"));
		Assert.Contains(entries, item => item.Layout.DisplayName == "HiddenInheritedClassOverride" && item.Method.DisplayName.EndsWith("DefaultClassOverride::ToString", StringComparison.Ordinal));
		Assert.DoesNotContain(entries, item => item.Method.DisplayName.Contains("HiddenDefaultClassName::ToString", StringComparison.Ordinal) || item.Method.DisplayName.Contains("UnsupportedObjectJoinValue::ToString", StringComparison.Ordinal));
		Assert.Empty(stable.GetObjectJoinDispatchEntries(declaration));
	}

	[Fact]
	public void ApplicationValueOverridesUseThePinnedObjectSlotAndRequireOptIn()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = Open(pack, true);
		using var stable = Open(pack, false);
		foreach (var name in new[] { "PlainFormattingValue", "FormalFormattingValue", "SpanFormattingValue", "WordSpanFormattingValue", "WordPlainFormattingValue", "WordFormalFormattingValue" })
		{
			var method = ClosedHandler(module, name);
			var call = method.Instructions.Single(instruction => instruction.ConstrainedTypeToken is not null &&
				module.ResolveMethodToken((int)instruction.Operand!, method, instruction.Offset).Definition?.DisplayName == "System.Object::ToString");
			var declaration = module.ResolveMethodToken((int)call.Operand!, method, call.Offset).Definition!;
			var implementation = module.ResolveConstrainedInterfaceImplementation(method, call.ConstrainedTypeToken!.Value, call.Offset, declaration);
			Assert.Equal(module.AssemblyName, implementation.ModuleName);
			Assert.EndsWith(name + "::ToString", implementation.DisplayName);
			var type = module.GetMethodDeclaringType(implementation);
			Assert.True(module.TryGetReferenceFreeStructLayout(type, implementation.ModuleName, out var layout));
			Assert.Equal(name.StartsWith("Word", StringComparison.Ordinal) ? 4 : 8, layout.Size);
			Assert.Equal(0u, layout.ReferenceBitmap);
			Assert.Equal(implementation.Identity, module.GetVirtualTable(layout).Slots[module.GetVirtualSlot(declaration)].Identity);
			Assert.Equal(implementation.Identity, module.TryGetVirtualImplementation(layout, declaration)!.Identity);
			Assert.Throws<M68kCompilationException>(() => stable.ResolveConstrainedInterfaceImplementation(method, call.ConstrainedTypeToken.Value, call.Offset, declaration));
			foreach (var wrong in new[] { declaration with { ModuleName = "Application" }, declaration with { DisplayName = "System.Object::GetHashCode" } })
				Assert.Throws<M68kCompilationException>(() => module.ResolveConstrainedInterfaceImplementation(method, call.ConstrainedTypeToken.Value, call.Offset, wrong));
			var withoutOverride = module.RegisterBoxedDispatchLayout(new CilType(CilTypeKind.ValueType, 4,
				"CopperSharp.Compiler.Tests.CompilerFixtures/BoxedWord"), module.AssemblyName)!;
			Assert.Equal("System.ValueType::ToString", module.TryGetVirtualImplementation(withoutOverride, declaration)!.DisplayName);
		}
	}

	[Fact]
	public void SingleWordArgumentBitsAndTheirFrameAddressesHaveDifferentLifetimes()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = Open(pack, true);
		var method = ClosedHandler(module, "WordSpanFormattingValue");
		var function = CilMachineIrBuilder.Build(method, module);
		var loads = function.Blocks.SelectMany(block => block.Instructions).Where(instruction =>
			instruction.Operation == M68kMachineOperation.ArgumentLoad && instruction.ArgumentIndex == 1).ToArray();
		Assert.NotEmpty(loads);
		Assert.All(loads, instruction => Assert.Equal(CilStackValueKind.Int32, function.Values[Assert.Single(instruction.Definitions)].Kind));
		var addresses = function.Blocks.SelectMany(block => block.Instructions).Where(instruction =>
			instruction.Operation == M68kMachineOperation.ArgumentAddress && instruction.ArgumentIndex == 1).ToArray();
		Assert.NotEmpty(addresses);
		var provenance = M68kByrefProvenanceAnalyzer.Analyze(function, true, out _);
		Assert.All(addresses, instruction => Assert.Equal(M68kByrefProvenanceKind.Frame, provenance[Assert.Single(instruction.Definitions)].Kind));
		M68kByrefOwnerRooting.Insert(function, allowCallerBorrowedByrefs: true);
	}

	[Fact]
	public void ReferencedApplicationValuesKeepPayloadRootsAndUseAggregateTransport()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = Open(pack, true);
		using var stable = Open(pack, false);
		foreach (var name in new[] { "NestedPlainReferenceValue", "NestedFormalReferenceValue", "NestedSpanReferenceValue",
			"WordSpanReferenceValue", "WordPlainReferenceValue", "WordFormalReferenceValue" })
		{
			var method = ClosedHandler(module, name, "AppendReferenceFormattingValue");
			var call = method.Instructions.Single(instruction => instruction.ConstrainedTypeToken is not null &&
				module.ResolveMethodToken((int)instruction.Operand!, method, instruction.Offset).Definition?.DisplayName == "System.Object::ToString");
			var declaration = module.ResolveMethodToken((int)call.Operand!, method, call.Offset).Definition!;
			var implementation = module.ResolveConstrainedInterfaceImplementation(method, call.ConstrainedTypeToken!.Value, call.Offset, declaration);
			var type = module.GetMethodDeclaringType(implementation);
			Assert.True(module.TryGetReferenceFreeStructLayout(type, implementation.ModuleName, out var layout));
			var word = name.StartsWith("Word", StringComparison.Ordinal);
			Assert.Equal(word ? 4 : 12, layout.Size);
			Assert.Equal(word ? 1u : 6u, layout.ReferenceBitmap);
			Assert.True(layout.UsesAggregateTransport);
			Assert.Equal(implementation.Identity, module.TryGetVirtualImplementation(layout, declaration)!.Identity);
			Assert.Throws<M68kCompilationException>(() => stable.ResolveConstrainedInterfaceImplementation(method, call.ConstrainedTypeToken.Value, call.Offset, declaration));
			var function = CilMachineIrBuilder.Build(method, module);
			Assert.Equal(word ? new[] { 0 } : new[] { 4, 8 }, function.ArgumentHomes[1].GcReferenceOffsets);
			Assert.All(function.Blocks.SelectMany(block => block.Instructions).Where(instruction =>
				instruction.Operation == M68kMachineOperation.ArgumentAddress && instruction.ArgumentIndex == 1 &&
				instruction.SourceInstruction?.OpCode != OpCodes.Ldarga && instruction.SourceInstruction?.OpCode != OpCodes.Ldarga_S),
				instruction => Assert.Equal(CilStackValueKind.AggregateAddress, function.Values[Assert.Single(instruction.Definitions)].Kind));
			M68kByrefOwnerRooting.Insert(function, allowCallerBorrowedByrefs: true);
		}
	}

	[Fact]
	public void ManagedValueAdmissionRequiresACompleteBitmapAndAnOrdinaryValueLifetime()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = Open(pack, true);
		CilType Type(string name) => new(CilTypeKind.ValueType, 4,
			"CopperSharp.Compiler.Tests.StringBuilderValueFormattingTests/" + name);
		Assert.True(module.TryGetReferenceFreeStructLayout(Type(nameof(LastBitmapWordValue)), module.AssemblyName, out var boundary));
		Assert.Equal(128, boundary.Size); Assert.Equal(0x80000000u, boundary.ReferenceBitmap);
		Assert.False(module.TryGetReferenceFreeStructLayout(Type(nameof(OversizedReferenceValue)), module.AssemblyName, out _));
		Assert.False(module.TryGetReferenceFreeStructLayout(Type(nameof(RefLikeReferenceValue)), module.AssemblyName, out _));
		var overflow = Assert.Throws<M68kCompilationException>(() =>
			module.TryGetReferenceFreeStructLayout(Type(nameof(OverflowReferenceValue)), module.AssemblyName, out _));
		Assert.Equal(M68kDiagnosticIds.UnsupportedSignature, overflow.DiagnosticId);
		Assert.Contains("descriptor bitmap", overflow.Message);
		var constructed = Type("OverflowGenericReferenceValue`1<string>") with { GenericArguments = [new(CilTypeKind.ManagedReference, 4, "string")] };
		var genericOverflow = Assert.Throws<M68kCompilationException>(() =>
			module.TryGetReferenceFreeStructLayout(constructed, module.AssemblyName, out _));
		Assert.Equal(M68kDiagnosticIds.UnsupportedSignature, genericOverflow.DiagnosticId);
		Assert.Contains("descriptor bitmap", genericOverflow.Message);
	}

	[Fact]
	public void DefaultValueNamesUseTheInheritedCoreLibSlot()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = Open(pack, true);
		var type = new CilType(CilTypeKind.ValueType, 4, "CopperSharp.Compiler.Tests.CompilerFixtures/DefaultEmptyNameValue");
		Assert.True(module.TryGetReferenceFreeStructLayout(type, module.AssemblyName, out var layout));
		var declaration = module.ResolveManagedMethod("System.Private.CoreLib", "System.Object::ToString");
		var table = module.GetVirtualTable(layout);
		Assert.Equal(4, layout.Size);
		Assert.True(module.IsSupportedStructType(type));
		Assert.Equal("System.ValueType::ToString", table.Slots[module.GetVirtualSlot(declaration)].DisplayName);
		using var stable = Open(pack, false);
		Assert.False(stable.IsSupportedStructType(type));
		var caller = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibStringBuilderDefaultTypedValueEntry");
		var call = caller.Instructions.Single(instruction => instruction.ConstrainedTypeToken is not null);
		Assert.Equal("System.ValueType::ToString", module.ResolveConstrainedInterfaceImplementation(caller,
			call.ConstrainedTypeToken!.Value, call.Offset, declaration).DisplayName);
		Assert.Throws<M68kCompilationException>(() => stable.ResolveConstrainedInterfaceImplementation(caller,
			call.ConstrainedTypeToken.Value, call.Offset, declaration));
		var function = CilMachineIrBuilder.Build(caller, module);
		var boxes = function.Blocks.SelectMany(block => block.Instructions).Where(instruction => instruction.Operation == M68kMachineOperation.Box).ToArray();
		Assert.NotEmpty(boxes);
		Assert.All(boxes, box => { Assert.True(box.IsSafepoint); Assert.True(box.MayThrow); });
		var analysis = FrameworkReachabilityAnalyzer.Analyze(module, caller, [], null, M68kFloatingPointMode.Disabled);
		Assert.Contains(analysis.ManagedAllocationSites, site => site.Kind == "box" && site.Caller == caller.DisplayName && site.IlOffset == call.Offset);
		var refLikeCaller = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::DefaultRefLikeTypeToken");
		var token = (int)refLikeCaller.Instructions.Single(instruction => instruction.OpCode == OpCodes.Ldtoken).Operand!;
		Assert.Throws<M68kCompilationException>(() => module.ResolveConstrainedInterfaceImplementation(refLikeCaller, token, 0, declaration));
	}

	[Fact]
	public void RuntimeTypeNameOverrideRequiresTheExactOptInDeclaration()
	{
		var owner = FrameworkTypeId.Named("System.Private.CoreLib", "System.RuntimeType");
		var signature = new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.String"), []);
		var member = new FrameworkMemberId(owner, "ToString", signature);
		Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out _));
		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
		foreach (var wrong in new[] {
			new FrameworkMemberId(FrameworkTypeId.Named("Application", "System.RuntimeType"), "ToString", signature),
			new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.Type"), "ToString", signature),
			new FrameworkMemberId(owner, "ToString", new FrameworkMethodSignatureId(0, 0, 0, FrameworkTypeId.Primitive("System.String"), [])),
			new FrameworkMemberId(owner, "ToString", new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.Int32"), [])) })
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(wrong, true, out _));
	}

	[Fact]
	public void RuntimeTypeNamesKeepMetadataIdentityAndDeferUnusedBodies()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = Open(pack, true);
		Assert.Equal(20, module.GetRuntimeTypeNameLayout().Size);
		var layout = module.RegisterRuntimeTypeDispatchLayout()!;
		var table = module.GetVirtualTable(layout);
		var unsupported = table.Slots.Single(method => method.Name == "InvokeMember");
		Assert.True(unsupported.HasDeferredBody);
		Assert.True(module.ApplyTargetRuntimeOverride(unsupported, materializeBody: false).HasDeferredBody);
		var failure = Assert.Throws<M68kCompilationException>(() => module.ApplyTargetRuntimeOverride(unsupported));
		Assert.Contains("filter", failure.Message, StringComparison.OrdinalIgnoreCase);
		var declaration = module.ResolveManagedMethod("System.Private.CoreLib", "System.Object::ToString");
		Assert.Equal("CopperSharp.Runtime.ShadowRuntimeType::ToString",
			module.ApplyTargetRuntimeOverride(table.Slots[module.GetVirtualSlot(declaration)]).DisplayName);
		var caller = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibDefaultRuntimeTypeNamesEntry");
		var names = caller.Instructions.Where(instruction => instruction.OpCode == OpCodes.Ldtoken)
			.Select(instruction => module.FormatRuntimeTypeName(module.ResolveRuntimeTypeToken((int)instruction.Operand!, caller, instruction.Offset))).ToArray();
		Assert.Equal(new[] { "CopperSharp.Compiler.Tests.CompilerFixtures+DefaultGenericNameValue`1[System.Int32[][]]",
			"System.Int32", "System.String[]", "CopperSharp.Compiler.Tests.CompilerFixtures+DefaultGenericNameValue`1[T]" }, names);
		var unknown = module.ResolveRuntimeTypeIdentity(new CilType(CilTypeKind.ValueType, 4, "Missing.ApplicationValue"), module.AssemblyName);
		Assert.Equal(M68kDiagnosticIds.UnsupportedSignature,
			Assert.Throws<M68kCompilationException>(() => module.FormatRuntimeTypeName(unknown)).DiagnosticId);
	}

	private readonly struct PaddingValue(long value)
	{
		public readonly long A = value, B = value, C = value, D = value, E = value, F = value, G = value, H = value;
	}
	private readonly struct LastBitmapWordValue(string text)
	{
		public readonly PaddingValue First = new(0);
		public readonly long A = 0, B = 0, C = 0, D = 0, E = 0, F = 0, G = 0;
		public readonly int Tag = 0;
		public readonly string Text = text;
	}
	private readonly struct OversizedReferenceValue(string text)
	{
		public readonly string Text = text;
		public readonly PaddingValue First = new(0), Second = new(0);
	}
	private readonly struct OverflowReferenceValue(string text)
	{
		public readonly PaddingValue First = new(0), Second = new(0);
		public readonly string Text = text;
	}
	private readonly struct OverflowGenericReferenceValue<T>(T text)
	{
		public readonly PaddingValue First = new(0), Second = new(0);
		public readonly T Text = text;
	}
	private readonly ref struct RefLikeReferenceValue(string text)
	{
		public readonly string Text = text;
	}

	private static CilMethod ClosedHandler(CompilationModule module, string name, string callerName = "AppendFormattingValue")
	{
		var caller = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::" + callerName);
		var aligned = caller.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, caller, instruction.Offset).Definition)
			.Single(method => method?.Name == "AppendFormatted" && method.MethodTypeArguments is [var type] && type.DisplayName.EndsWith("/" + name, StringComparison.Ordinal))!;
		return aligned.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, aligned, instruction.Offset).Definition)
			.DistinctBy(method => method?.Identity)
			.Single(method => method?.Name == "AppendFormatted" && method.Signature.ParameterTypes.Length == 2 &&
				method.MethodTypeArguments is [var type] && type.DisplayName.EndsWith("/" + name, StringComparison.Ordinal))!;
	}
	private static CompilationModule Open(FrameworkImplementationPackTests.CoreLibPack pack, bool enabled) =>
		new(typeof(CompilerFixtures).Assembly.Location, managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowObject).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = enabled }));
}
