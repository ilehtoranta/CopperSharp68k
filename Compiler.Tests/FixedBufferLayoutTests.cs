/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class FixedBufferLayoutTests
{
	[Fact]
	public void UInt32AddressIntrinsicRequiresExactGenericSignatureAndOptIn()
	{
		var integer = FrameworkTypeId.Primitive("System.UInt32");
		var element = FrameworkTypeId.GenericMethodParameter(0);
		var pointer = FrameworkTypeId.Pointer(FrameworkTypeId.Primitive("System.Void"));
		var signature = new FrameworkMethodSignatureId(0x10, 1, 1, pointer, [FrameworkTypeId.ByReference(element)]);
		var member = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Runtime.CompilerServices.Unsafe"), "AsPointer", signature, [integer]);
		Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
		Assert.Equal("intrinsic:address-of-ref", binding.Target);
		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
		foreach (var changed in new[] {
			new FrameworkMemberId(FrameworkTypeId.Named("Application", "System.Runtime.CompilerServices.Unsafe"), member.Name, signature, [integer]),
			new FrameworkMemberId(member.DeclaringType, member.Name, signature, [FrameworkTypeId.Primitive("System.String")]),
			new FrameworkMemberId(member.DeclaringType, member.Name, new FrameworkMethodSignatureId(0x30, 1, 1, pointer, signature.ParameterTypes), [integer]),
			new FrameworkMemberId(member.DeclaringType, member.Name, new FrameworkMethodSignatureId(0x10, 1, 1, integer, signature.ParameterTypes), [integer]),
			new FrameworkMemberId(member.DeclaringType, member.Name, new FrameworkMethodSignatureId(0x10, 1, 1, pointer, [element]), [integer])
		}) Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(changed, true, out _));
	}

	[Theory]
	[InlineData("BigInteger::Pow10")]
	public void CoreLibBigIntegerNativePointerCastsRetainProvenanceAtSafepoints(string name)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowArray).Assembly.Location], frameworkImplementationPack: catalog, floatingPoint: M68kFloatingPointMode.SoftFloat);
		var method = module.ApplyTargetRuntimeOverride(module.ResolveManagedMethod("System.Private.CoreLib", name));
		var function = CilMachineIrBuilder.Build(method, module, M68kCpuTarget.M68020, true, CilOptimizer.Optimize(method, module));
		M68kByrefOwnerRooting.Insert(function, allowUntrackedManagedByrefs: false, allowCallerBorrowedByrefs: true, rejectManagedByrefReturn: false);
	}

	[Fact]
	public void CoreLibDragon4BigIntegerPreservesItsCompleteNestedFixedBuffer()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack: catalog);
		var type = new CilType(CilTypeKind.ValueType, 0, "System.Number/BigInteger");
		Assert.True(module.TryGetStructLayout(type, "System.Private.CoreLib", out var layout));
		Assert.Equal("System.Private.CoreLib", layout.ModuleName);
		Assert.Equal(468, layout.Size); // length and all 116 uint blocks.
		Assert.Equal(0u, layout.ReferenceBitmap);
		Assert.Equal(new[] { 0, 4 }, layout.FieldOffsets.Values.Order());
	}
}
