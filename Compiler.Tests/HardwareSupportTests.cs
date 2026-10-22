/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class HardwareSupportTests
{
	public static IEnumerable<object[]> Gates() => HardwareSupportFixtureBuilder.GateTypes().Select(type => new object[] { type.FullName! });

	[Theory]
	[MemberData(nameof(Gates))]
	public void ForeignArchitectureGateRequiresExactIdentityAndStaticBooleanSignature(string name)
	{
		foreach (var assembly in new[] { "System.Runtime", "System.Private.CoreLib", "System.Runtime.Intrinsics" })
		{
			var parts = name.Split('+');
			var type = FrameworkTypeId.Named(assembly, parts[0]);
			if (parts.Length == 2) type = FrameworkTypeId.Named(assembly, parts[1], type);
			var signature = new FrameworkMethodSignatureId(0, 0, 0, FrameworkTypeId.Primitive("System.Boolean"), []);
			var member = new FrameworkMemberId(type, "get_IsSupported", signature, []);
			Assert.True(FrameworkImplementationProfile.IsUnsupportedHardwareSupportGetter(member));
			foreach (var changed in new[] {
				new FrameworkMemberId(FrameworkTypeId.Named("Application", name), member.Name, signature, []),
				new FrameworkMemberId(type, "Other", signature, []),
				new FrameworkMemberId(type, member.Name, new FrameworkMethodSignatureId(0x20, 0, 0, signature.ReturnType, []), []),
				new FrameworkMemberId(type, member.Name, new FrameworkMethodSignatureId(0, 1, 0, signature.ReturnType, []), []),
				new FrameworkMemberId(type, member.Name, new FrameworkMethodSignatureId(0, 0, 1, signature.ReturnType, [FrameworkTypeId.Primitive("System.Int32")]), []),
				new FrameworkMemberId(type, member.Name, new FrameworkMethodSignatureId(0, 0, 0, FrameworkTypeId.Primitive("System.Int32"), []), []),
				new FrameworkMemberId(type, member.Name, signature, [FrameworkTypeId.Primitive("System.Int32")])
			}) Assert.False(FrameworkImplementationProfile.IsUnsupportedHardwareSupportGetter(changed));
			if (parts.Length == 2)
			{
				Assert.False(FrameworkImplementationProfile.IsUnsupportedHardwareSupportGetter(new FrameworkMemberId(FrameworkTypeId.Named(assembly, name), member.Name, signature, [])));
				Assert.False(FrameworkImplementationProfile.IsUnsupportedHardwareSupportGetter(new FrameworkMemberId(FrameworkTypeId.Named(assembly, parts[1], FrameworkTypeId.Named("Application", parts[0])), member.Name, signature, [])));
			}
		}
	}

	[Fact]
	public void CoreLibSoftwareTableUsesTheVerifiedInitializedBytePointerSpanPattern()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack: catalog);
		var method = module.ResolveManagedMethod("System.Private.CoreLib", "System.Numerics.BitOperations::get_Log2DeBruijn");
		var field = Assert.Single(method.Instructions, instruction => instruction.OpCode == OpCodes.Ldsflda);
		Assert.True(module.HasInitializedFieldRva((int)field.Operand!, method, field.Offset));
		Assert.Equal(32, module.ReadInitializedFieldRva((int)field.Operand!, method, field.Offset).Length);
		var constructor = Assert.Single(method.Instructions, instruction => instruction.OpCode == OpCodes.Newobj);
		var member = FrameworkImplementationProfile.Canonicalize(module.DescribeFrameworkMethodToken((int)constructor.Operand!, method, constructor.Offset));
		Assert.Equal(FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Byte")]), member.DeclaringType);
		Assert.Equal(".ctor", member.Name);
	}

	[Fact]
	public void GatesFoldBeforeReachabilityWithoutChangingProtectedOffsetsOrApplicationNames()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var assembly = HardwareSupportFixtureBuilder.Create(pack.Directory);
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(assembly, frameworkImplementationPack: catalog);
		var gates = module.ResolveEntryPoint("HardwareSupportProbe::Gates");
		Assert.DoesNotContain(gates.Instructions, instruction => instruction.OpCode == OpCodes.Call);
		var application = module.ResolveEntryPoint("HardwareSupportProbe::ApplicationGate");
		Assert.Single(application.Instructions, instruction => instruction.OpCode == OpCodes.Call);
		var protectedGate = module.ResolveEntryPoint("HardwareSupportProbe::ProtectedGate");
		using var disabled = new CompilationModule(assembly);
		var original = disabled.ResolveEntryPoint("HardwareSupportProbe::ProtectedGate");
		Assert.Equal(original.ExceptionRegions, protectedGate.ExceptionRegions);
		Assert.Equal(original.Instructions.Select(instruction => (instruction.Offset, instruction.NextOffset)), protectedGate.Instructions.Select(instruction => (instruction.Offset, instruction.NextOffset)));
		Assert.Single(protectedGate.Instructions, instruction => instruction.OpCode == OpCodes.Ldc_I4_0);
		Assert.DoesNotContain(protectedGate.Instructions, instruction => instruction.OpCode == OpCodes.Call);
		Assert.Equal(6, disabled.ResolveEntryPoint("HardwareSupportProbe::Gates").Instructions.Count(instruction => instruction.OpCode == OpCodes.Call));
		var plainCatalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath));
		using var plain = new CompilationModule(assembly, frameworkImplementationPack: plainCatalog);
		Assert.Equal(6, plain.ResolveEntryPoint("HardwareSupportProbe::Gates").Instructions.Count(instruction => instruction.OpCode == OpCodes.Call));
	}
}
