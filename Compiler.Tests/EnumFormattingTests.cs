/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class EnumFormattingTests
{
	[Fact]
	public void BoxedEnumLayoutsRetainFormattingInterfaces()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowEnumFormatting).Assembly.Location], frameworkImplementationPack: catalog);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibStringBuilderEnumContractsEntry");
		var box = entry.Instructions.First(instruction => instruction.OpCode == OpCodes.Box);
		var type = module.ResolveTypeToken((int)box.Operand!, entry, box.Offset);
		var layout = module.RegisterBoxedDispatchLayout(type, entry.ModuleName)!;
		Assert.True(module.GetRuntimeTypeSignature(layout).IsEnum);
		var interfaces = 0;
		foreach (var instruction in entry.Instructions.Where(instruction => instruction.OpCode == OpCodes.Isinst))
		{
			var target = module.ResolveRuntimeTypeToken((int)instruction.Operand!, entry, instruction.Offset);
			if (target.Type.DisplayName is not ("System.IFormattable" or "System.ISpanFormattable")) continue;
			Assert.True(target.IsInterface);
			Assert.Equal("System.Private.CoreLib", target.ModuleName);
			interfaces++;
			var definition = module.GetRuntimeInterfaceDefinition(target);
			var implementation = module.TryGetInterfaceImplementation(layout, definition);
			Assert.NotNull(implementation);
			Assert.All(implementation.Methods, method => Assert.StartsWith("CopperSharp.Runtime.ShadowBoxedEnum::",
				module.ApplyTargetRuntimeOverride(method).DisplayName));
		}
		Assert.Equal(2, interfaces);
	}

	[Fact]
	public void EnumInterfaceFoldingRequiresTheLoadedArgumentAndUnenteredSequence()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack: catalog);
		using var stable = new CompilationModule(typeof(CompilerFixtures).Assembly.Location);
		var caller = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::EnumInterfaceBranchEntry");
		var call = caller.Instructions.Single(instruction => instruction.OpCode == OpCodes.Call);
		var constructed = module.ResolveMethodToken((int)call.Operand!, caller, call.Offset).Definition!;
		var raw = module.GetMethod(constructed.Handle) with { Signature = constructed.Signature, MethodTypeArguments = constructed.MethodTypeArguments };
		var folded = CilTypePredicateSpecializer.Specialize(raw, module);
		Assert.DoesNotContain(folded.Instructions, instruction => instruction.OpCode == OpCodes.Box || instruction.OpCode == OpCodes.Isinst);
		Assert.Single(folded.Instructions, instruction => instruction.OpCode == OpCodes.Ret);
		Assert.Same(raw, CilTypePredicateSpecializer.Specialize(raw, stable));
		var sequence = raw.Instructions.Take(4).ToArray();
		Assert.Equal(OpCodes.Ldarg_0, sequence[0].OpCode);
		Assert.Equal(OpCodes.Box, sequence[1].OpCode);
		Assert.Equal(OpCodes.Isinst, sequence[2].OpCode);
		foreach (var interior in sequence.Skip(1))
		{
			var branch = raw with { Instructions = [new CilInstruction(-1, OpCodes.Br, interior.Offset, 0), .. raw.Instructions] };
			Assert.Same(branch, CilTypePredicateSpecializer.Specialize(branch, module));
			var switched = raw with { Instructions = [new CilInstruction(-1, OpCodes.Switch, new[] { interior.Offset }, 0), .. raw.Instructions] };
			Assert.Same(switched, CilTypePredicateSpecializer.Specialize(switched, module));
		}
		var wrongArgument = raw with { Instructions = raw.Instructions.Select(instruction => instruction.Offset == sequence[0].Offset
			? instruction with { OpCode = OpCodes.Ldarg_1 } : instruction).ToArray() };
		Assert.Same(wrongArgument, CilTypePredicateSpecializer.Specialize(wrongArgument, module));
		var wrongBox = raw with { Signature = module.GetMethod(constructed.Handle).Signature };
		Assert.Same(wrongBox, CilTypePredicateSpecializer.Specialize(wrongBox, module));
	}

	[Theory]
	[InlineData("TryFormat")]
	[InlineData("TryFormatUnconstrained")]
	public void EnumFormattingBindingsRequireExactConstructedSignaturesAndOptIn(string name)
	{
		var boolean = FrameworkTypeId.Primitive("System.Boolean");
		var character = FrameworkTypeId.Primitive("System.Char");
		var parameter = FrameworkTypeId.GenericMethodParameter(0);
		var arguments = new[] { parameter, FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Span`1"), [character]),
			FrameworkTypeId.ByReference(FrameworkTypeId.Primitive("System.Int32")),
			FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [character]) };
		foreach (var assembly in new[] { "System.Runtime", "System.Private.CoreLib" })
		{
			var owner = FrameworkTypeId.Named(assembly, "System.Enum");
			var signature = new FrameworkMethodSignatureId(0x10, 1, 4, boolean, arguments);
			var member = new FrameworkMemberId(owner, name, signature, [FrameworkTypeId.Named("System.Runtime", "System.DayOfWeek")]);
			Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out _));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(owner, name, signature), true, out _));
			foreach (var wrong in new[] {
				new FrameworkMethodSignatureId(0x30, 1, 4, boolean, arguments),
				new FrameworkMethodSignatureId(0x10, 2, 4, boolean, arguments),
				new FrameworkMethodSignatureId(0x10, 1, 4, FrameworkTypeId.Primitive("System.Int32"), arguments),
				new FrameworkMethodSignatureId(0x10, 1, 5, boolean, [.. arguments, character]),
				new FrameworkMethodSignatureId(0x10, 1, 4, boolean, [parameter, arguments[3], arguments[2], arguments[3]]) })
				Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(owner, name, wrong, member.MethodTypeArguments), true, out _));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(
				FrameworkTypeId.Named("Application", "System.Enum"), name, signature, member.MethodTypeArguments), true, out _));
		}
	}

	[Fact]
	public void EnumTablesRetainUnderlyingWidthsUnsignedOrderAndNames()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowEnumFormatting).Assembly.Location], frameworkImplementationPack: catalog);
		Assert.Equal(28, module.GetEnumMetadataLayout().Size);
		foreach (var name in new[] { "HandlerSignedByte", "HandlerUnsignedByte", "HandlerSignedShort", "HandlerUnsignedShort",
			"HandlerSignedInt", "HandlerUnsignedInt", "HandlerSignedLong", "HandlerUnsignedLong" })
		{
			var method = module.ResolveEntryPoint($"CopperSharp.Compiler.Tests.CompilerFixtures::Check{name}");
			var array = method.Instructions.First(instruction => instruction.OpCode == OpCodes.Newarr);
			var type = module.ResolveTypeToken((int)array.Operand!, method, array.Offset);
			var data = CilEnumData.Read(module, type, method.ModuleName);
			Assert.Equal("Zero", data.Names[0]); Assert.Equal(0UL, data.Values[0]);
			Assert.Equal(type.Kind == CilTypeKind.UnsignedInteger, data.Flags);
			Assert.Equal(type.Kind == CilTypeKind.SignedInteger ? "Minimum" : "Maximum", data.Names[^1]);
			Assert.True(data.Values.Zip(data.Values.Skip(1), (left, right) => left <= right).All(sorted => sorted));
			var expectedLast = type.Kind == CilTypeKind.SignedInteger ? 1UL << (type.Size * 8 - 1) :
				type.Size == 8 ? ulong.MaxValue : (1UL << (type.Size * 8)) - 1;
			Assert.Equal(expectedLast, data.Values[^1]);
		}
	}
}
