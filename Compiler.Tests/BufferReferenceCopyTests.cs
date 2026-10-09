/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;

namespace CopperSharp.Compiler.Tests;

public sealed class BufferReferenceCopyTests
{
	[Fact]
	public void ParsedSegmentEnumeratorRetainsBothCurrentStringRoots()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = new CopperSharp.Compiler.Metadata.CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true }));
		var text = new CopperSharp.Compiler.Metadata.CilType(CopperSharp.Compiler.Metadata.CilTypeKind.ManagedReference, 4, "string");
		var integer = new CopperSharp.Compiler.Metadata.CilType(CopperSharp.Compiler.Metadata.CilTypeKind.SignedInteger, 4, "int");
		var segment = new CopperSharp.Compiler.Metadata.CilType(CopperSharp.Compiler.Metadata.CilTypeKind.ValueType, 0, "System.ValueTuple`4<string,int,int,string>", GenericArguments: [text, integer, integer, text]);
		var enumerator = new CopperSharp.Compiler.Metadata.CilType(CopperSharp.Compiler.Metadata.CilTypeKind.ValueType, 0, "System.Collections.Generic.List`1/Enumerator<System.ValueTuple`4<string,int,int,string>>", GenericArguments: [segment]);
		Assert.True(module.TryGetReferenceFreeStructLayout(enumerator, "System.Private.CoreLib", out var layout));
		Assert.Equal(28, layout.Size);
		Assert.Equal(0x49u, layout.ReferenceBitmap);
	}

	[Fact]
	public void ReferenceArrayDataProjectionRequiresExactClosedReferenceElement()
	{
		var owner = FrameworkTypeId.Named("System.Runtime", "System.Runtime.InteropServices.MemoryMarshal");
		var generic = FrameworkTypeId.GenericMethodParameter(0);
		var signature = new FrameworkMethodSignatureId(0x10, 1, 1, FrameworkTypeId.ByReference(generic), [FrameworkTypeId.SzArray(generic)]);
		foreach (var name in new[] { "System.String", "System.Object" })
		{
			var member = new FrameworkMemberId(owner, "GetArrayDataReference", signature, [FrameworkTypeId.Primitive(name)]);
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
			Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
			Assert.Equal("intrinsic:corelib-reference-array-data", binding.Target);
			Assert.Equal(FrameworkEffects.MayThrow, binding.EffectSummary.Effects);
		}
		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(owner, "GetArrayDataReference", signature, [FrameworkTypeId.Primitive("System.Byte")]), true, out _));
	}

	[Fact]
	public void MutableReferenceSpanConstructorsRequireExactClosedArraySignatures()
	{
		var generic = FrameworkTypeId.GenericTypeParameter(0);
		var integer = FrameworkTypeId.Primitive("System.Int32");
		var result = FrameworkTypeId.Primitive("System.Void");
		foreach (var assembly in new[] { "System.Runtime", "System.Private.CoreLib" })
		foreach (var name in new[] { "System.String", "System.Object" })
		{
			var owner = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(assembly, "System.Span`1"), [FrameworkTypeId.Primitive(name)]);
			foreach (var ranged in new[] { false, true })
			{
				FrameworkTypeId[] parameters = ranged ? [FrameworkTypeId.SzArray(generic), integer, integer] : [FrameworkTypeId.SzArray(generic)];
				var signature = new FrameworkMethodSignatureId(0x20, 0, parameters.Length, result, parameters);
				var member = new FrameworkMemberId(owner, ".ctor", signature);
				Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
				Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
				Assert.Equal((ranged ? "intrinsic:span-from-array-range-mutable:" : "intrinsic:span-from-array-ctor:") + (name == "System.String" ? "string" : "object"), binding.Target);
				Assert.Equal(FrameworkEffects.MayThrow, binding.EffectSummary.Effects);
				Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(owner, "Create", signature), true, out _));
				Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(owner, ".ctor", signature, [FrameworkTypeId.Primitive(name)]), true, out _));
				foreach (var wrong in new[] {
					new FrameworkMethodSignatureId(0, 0, parameters.Length, result, parameters),
					new FrameworkMethodSignatureId(0x20, 1, parameters.Length, result, parameters),
					new FrameworkMethodSignatureId(0x20, 0, parameters.Length + 1, result, parameters),
					new FrameworkMethodSignatureId(0x20, 0, parameters.Length, integer, parameters),
					new FrameworkMethodSignatureId(0x20, 0, 1, result, [FrameworkTypeId.ByReference(generic)]) })
					Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(owner, ".ctor", wrong), true, out _));
			}
		}
		var unrelated = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("Application", "System.Span`1"), [FrameworkTypeId.Primitive("System.Object")]);
		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(unrelated, ".ctor", new FrameworkMethodSignatureId(0x20, 0, 1, result, [FrameworkTypeId.SzArray(generic)])), true, out _));
	}

	[Fact]
	public void ReferenceByteCopyRequiresExactSignatureAndOptIn()
	{
		var owner = FrameworkTypeId.Named("System.Private.CoreLib", "System.Buffer");
		var reference = FrameworkTypeId.ByReference(FrameworkTypeId.Primitive("System.Byte"));
		var result = FrameworkTypeId.Primitive("System.Void");
		var signature = new FrameworkMethodSignatureId(0, 0, 3, result, [reference, reference, FrameworkTypeId.Primitive("System.UIntPtr")]);
		var member = new FrameworkMemberId(owner, "BulkMoveWithWriteBarrierInternal", signature);
		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
		Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
		Assert.Equal("intrinsic:corelib-memmove-bytes", binding.Target);
		Assert.Equal(FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory, binding.EffectSummary.Effects);
		var wrapper = new FrameworkMemberId(owner, "BulkMoveWithWriteBarrier", signature);
		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(wrapper, false, out _));
		Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(wrapper, true, out var wrapperBinding));
		Assert.Equal(binding.Target, wrapperBinding.Target);
		Assert.Equal(binding.EffectSummary.Effects, wrapperBinding.EffectSummary.Effects);
		Assert.Equal(binding.EffectSummary.RequiredFeatures.ToArray(), wrapperBinding.EffectSummary.RequiredFeatures.ToArray());
		var pointer = FrameworkTypeId.Pointer(FrameworkTypeId.Primitive("System.Byte"));
		var native = new FrameworkMemberId(owner, "MemmoveInternal", new FrameworkMethodSignatureId(0, 0, 3, result, [pointer, pointer, FrameworkTypeId.Primitive("System.UIntPtr")]));
		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(native, false, out _));
		Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(native, true, out var nativeBinding));
		Assert.Equal(binding.Target, nativeBinding.Target);
		Assert.Equal(FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory | FrameworkEffects.ReadsNativeMemory | FrameworkEffects.WritesNativeMemory, nativeBinding.EffectSummary.Effects);

		foreach (var wrong in new[] {
			new FrameworkMethodSignatureId(0x20, 0, 3, result, signature.ParameterTypes),
			new FrameworkMethodSignatureId(0, 1, 3, result, signature.ParameterTypes),
			new FrameworkMethodSignatureId(0, 0, 2, result, signature.ParameterTypes),
			new FrameworkMethodSignatureId(0, 0, 3, FrameworkTypeId.Primitive("System.Int32"), signature.ParameterTypes),
			new FrameworkMethodSignatureId(0, 0, 3, result, [reference, reference, FrameworkTypeId.Primitive("System.Int32")]),
			new FrameworkMethodSignatureId(0, 0, 3, result, [FrameworkTypeId.Pointer(FrameworkTypeId.Primitive("System.Byte")), reference, FrameworkTypeId.Primitive("System.UIntPtr")]) })
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(owner, member.Name, wrong), true, out _));
		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(FrameworkTypeId.Named("Application", "System.Buffer"), member.Name, signature), true, out _));
	}
}
