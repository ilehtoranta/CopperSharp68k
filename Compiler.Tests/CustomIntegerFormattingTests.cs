/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Reflection.Emit;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class CustomIntegerFormattingTests
{
	[Fact]
	public void NumericBufferViewsRequireVerifiedLayoutsAndRetainTheirSpanOwners()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = Open(pack, true);
		using var ordinary = Open(pack, false);
		var character = new CilType(CilTypeKind.Character, 2, "char");
		foreach (var (name, assembly, size, bitmap) in new[] {
			("System.Number/NumberBuffer", "System.Private.CoreLib", 32, 128u),
			("CopperSharp.Runtime.ShadowNumberBuffer", "CopperSharp.Runtime.Managed", 32, 128u),
			("System.Collections.Generic.ValueListBuilder`1<char>", "System.Private.CoreLib", 20, 12u),
			("CopperSharp.Runtime.ShadowValueListBuilder`1<char>", "CopperSharp.Runtime.Managed", 20, 12u) })
		{
			var type = new CilType(CilTypeKind.ValueType, 0, name, GenericArguments: name.Contains('`') ? [character] : []);
			Assert.True(module.TryGetReferenceFreeStructLayout(type, assembly, out var layout));
			Assert.True(module.IsSupportedStructType(type));
			Assert.Equal(assembly, layout.ModuleName);
			Assert.Equal(size, layout.Size);
			Assert.Equal(bitmap, layout.ReferenceBitmap);
			Assert.False(ordinary.TryGetReferenceFreeStructLayout(type, assembly, out _));
			Assert.False(module.TryGetReferenceFreeStructLayout(type with { DisplayName = "Different.NumericBuffer" }, assembly, out _));
		}
	}

	[Fact]
	public void GenericPrimitiveSizesUseTargetStorageAndFoldBothBranchOutcomes()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = Open(pack, true);
		using var ordinary = Open(pack, false);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibPrimitiveGenericSizesEntry");
		foreach (var call in entry.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call))
		{
			var method = module.ResolveMethodToken((int)call.Operand!, entry, call.Offset).Definition!;
			var type = Assert.Single(method.MethodTypeArguments);
			if (method.Name == "PrimitiveGenericSize")
			{
				var function = CilMachineIrBuilder.Build(method, module);
				var size = Assert.Single(function.Blocks.SelectMany(block => block.Instructions), instruction => instruction.SourceInstruction?.OpCode == OpCodes.Sizeof);
				Assert.Equal(M68kMachineConstant.Int32(type.Size), size.ConstantValue);
				continue;
			}
			var raw = module.GetMethod(method.Handle) with { MethodTypeArguments = method.MethodTypeArguments };
			var folded = CilTypePredicateSpecializer.Specialize(raw, module);
			Assert.DoesNotContain(folded.Instructions, instruction => instruction.OpCode == OpCodes.Sizeof);
			Assert.Same(raw, CilTypePredicateSpecializer.Specialize(raw, ordinary));
			foreach (var interior in raw.Instructions.SkipWhile(instruction => instruction.OpCode != OpCodes.Sizeof).Skip(1).Take(2))
			{
				var entered = raw with { Instructions = [new CilInstruction(-1, OpCodes.Br, interior.Offset, 0), .. raw.Instructions] };
				Assert.Same(entered, CilTypePredicateSpecializer.Specialize(entered, module));
			}
		}
	}

	[Fact]
	public void BytePointerConversionAndSpanProjectionRequireExactMetadata()
	{
		var owner = FrameworkTypeId.Named("System.Runtime", "System.Runtime.CompilerServices.Unsafe");
		var element = FrameworkTypeId.GenericMethodParameter(0);
		var signature = new FrameworkMethodSignatureId(0x10, 1, 1,
			FrameworkTypeId.Pointer(FrameworkTypeId.Primitive("System.Void")), [FrameworkTypeId.ByReference(element)]);
		var member = new FrameworkMemberId(owner, "AsPointer", signature, [FrameworkTypeId.Primitive("System.Byte")]);
		Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
		Assert.Equal("intrinsic:address-of-ref", binding.Target);
		Assert.Equal(FrameworkEffects.None, binding.EffectSummary.Effects);
		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
		foreach (var wrong in new[] {
			new FrameworkMemberId(FrameworkTypeId.Named("Application", "System.Runtime.CompilerServices.Unsafe"), "AsPointer", signature, member.MethodTypeArguments),
			new FrameworkMemberId(owner, "AsPointer", signature, [FrameworkTypeId.Primitive("System.Char")]),
			new FrameworkMemberId(owner, "AsPointer", new FrameworkMethodSignatureId(0x10, 1, 1, FrameworkTypeId.Primitive("System.UIntPtr"), signature.ParameterTypes), member.MethodTypeArguments) })
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(wrong, true, out _));
		foreach (var assembly in new[] { "System.Memory", "System.Runtime", "System.Private.CoreLib" })
		foreach (var spanName in new[] { "System.Span`1", "System.ReadOnlySpan`1" })
		{
			var span = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", spanName), [element]);
			var projectionSignature = new FrameworkMethodSignatureId(0x10, 1, 1, FrameworkTypeId.ByReference(element), [span]);
			var projectionOwner = FrameworkTypeId.Named(assembly, "System.Runtime.InteropServices.MemoryMarshal");
			var projection = new FrameworkMemberId(projectionOwner, "GetReference", projectionSignature, member.MethodTypeArguments);
			Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(projection, true, out var projected));
			Assert.Equal("intrinsic:corelib-byte-span-data", projected.Target);
			Assert.Equal(FrameworkEffects.ReadsManagedMemory, projected.EffectSummary.Effects);
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(projection, false, out _));
			foreach (var wrong in new[] {
				new FrameworkMemberId(FrameworkTypeId.Named("Application", "System.Runtime.InteropServices.MemoryMarshal"), "GetReference", projectionSignature, member.MethodTypeArguments),
				new FrameworkMemberId(projectionOwner, "GetReference", projectionSignature, [FrameworkTypeId.Primitive("System.Int64")]),
				new FrameworkMemberId(projectionOwner, "GetReference", new FrameworkMethodSignatureId(0x30, 1, 1, projectionSignature.ReturnType, projectionSignature.ParameterTypes), member.MethodTypeArguments),
				new FrameworkMemberId(projectionOwner, "GetReference", new FrameworkMethodSignatureId(0x10, 1, 1, FrameworkTypeId.Pointer(element), projectionSignature.ParameterTypes), member.MethodTypeArguments) })
				Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(wrong, true, out _));
		}

	}

	[Fact]
	public void RendererRequiresOptInAndMatchesTheClosedCharacterBufferSignature()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var enabled = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(typeof(CopperSharp.Runtime.ShadowCustomNumberFormatting).Assembly.Location, frameworkImplementationPack: enabled);
		using var ordinary = new CompilationModule(typeof(CopperSharp.Runtime.ShadowCustomNumberFormatting).Assembly.Location);
		var caller = module.ResolveEntryPoint("CopperSharp.Runtime.ShadowCustomNumberFormatting::FormatMagnitude");
		var call = caller.Instructions.Single(instruction => instruction.OpCode == OpCodes.Call && module.DescribeMethodTokenName((int)instruction.Operand!, caller, instruction.Offset) == "Render");
		var target = module.ResolveMethodToken((int)call.Operand!, caller, call.Offset);
		Assert.Equal("System.Number::NumberToStringFormat<char>", target.Definition!.DisplayName);
		Assert.Equal("System.Private.CoreLib", target.Definition.ModuleName);
		Assert.Equal("char", Assert.Single(target.Definition.MethodTypeArguments).DisplayName);
		Assert.Equal(FrameworkBindingKind.ShadowMethod, target.FrameworkBinding!.Kind);
		Assert.Equal(4, target.Signature.ParameterTypes.Length);
		Assert.True(target.FrameworkBinding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayCollect));
		var raw = ordinary.ResolveEntryPoint("CopperSharp.Runtime.ShadowCustomNumberFormatting::FormatMagnitude");
		var original = ordinary.ResolveMethodToken((int)call.Operand!, raw, call.Offset);
		Assert.Equal("CopperSharp.Runtime.ShadowCustomNumberFormatting::Render", original.Definition!.DisplayName);
	}

	[Fact]
	public void CustomAdaptersRejectStandardFormatsAndUnresolvedSettings()
	{
		var info = new System.Globalization.NumberFormatInfo();
		foreach (var format in new string?[] { null, "", "D", "X4", "G", "\0ignored" })
			Assert.Throws<ArgumentException>(() => CopperSharp.Runtime.ShadowCustomNumberFormatting.FormatInt64(42, format, info));
		Assert.Throws<ArgumentNullException>(() => CopperSharp.Runtime.ShadowCustomNumberFormatting.FormatInt64(42, "000.0", null!));
		Assert.Throws<FormatException>(() => CopperSharp.Runtime.ShadowCustomNumberFormatting.FormatInt64(42, "Q1000000000.", info));
	}

	[Fact]
	public void PublicMatrixRetainsAllEightPrimitiveReceiverTypes()
	{
		Type[] types = [typeof(int), typeof(sbyte), typeof(short), typeof(uint), typeof(ulong), typeof(int),
			typeof(long), typeof(byte), typeof(ushort), typeof(int), typeof(long), typeof(ulong), typeof(uint),
			typeof(sbyte), typeof(short), typeof(byte), typeof(ushort), typeof(long), typeof(uint), typeof(ulong)];
		for (int scenario = 0; scenario < types.Length; scenario++)
			Assert.Equal(types[scenario], CompilerFixtures.CustomIntegerValue(scenario).GetType());
	}

	private static CompilationModule Open(FrameworkImplementationPackTests.CoreLibPack pack, bool enabled) =>
		new(typeof(CompilerFixtures).Assembly.Location, managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowCustomNumberFormatting).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = enabled }));
}
