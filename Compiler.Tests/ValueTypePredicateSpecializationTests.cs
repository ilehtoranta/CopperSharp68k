/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Reflection.Emit;
using System.Reflection.Metadata;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class ValueTypePredicateSpecializationTests
{
	[Fact]
	public void ValueTypePredicatesRetainBothOutcomesAndUnsupportedControlFlow()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true }));
		using var stable = new CompilationModule(typeof(CompilerFixtures).Assembly.Location);
		var caller = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibValueTypePredicateEntry");
		var calls = caller.Instructions.Where(i => i.OpCode == OpCodes.Call).ToArray();
		Assert.Equal(9, calls.Length);
		for (var index = 0; index < calls.Length; index++)
		{
			var call = calls[index];
			var constructed = module.ResolveMethodToken((int)call.Operand!, caller, call.Offset).Definition!;
			var raw = module.GetMethod(constructed.Handle);
			Assert.Same(raw, CilTypePredicateSpecializer.Specialize(raw, module));
			var method = raw with { MethodTypeArguments = constructed.MethodTypeArguments };
			var specialized = CilTypePredicateSpecializer.Specialize(method, module);
			Assert.DoesNotContain(specialized.Instructions, i => i.OpCode == OpCodes.Ldtoken || i.OpCode == OpCodes.Callvirt);
			Assert.Equal(index < 5 ? 17 : 29, Convert.ToInt32(Assert.Single(specialized.Instructions, i => i.OpCode == OpCodes.Ldc_I4_S).Operand));
			Assert.Same(method, CilTypePredicateSpecializer.Specialize(method, stable));
			var protectedMethod = method with { ExceptionRegions = [new CilExceptionRegion(ExceptionRegionKind.Finally, 0, 1, 1, 1, default, 0)] };
			Assert.Same(protectedMethod, CilTypePredicateSpecializer.Specialize(protectedMethod, module));
			foreach (var interior in method.Instructions.Skip(1).Take(3))
			{
				var entered = method with { Instructions = [new CilInstruction(-1, OpCodes.Br, interior.Offset, 0), .. method.Instructions] };
				Assert.Same(entered, CilTypePredicateSpecializer.Specialize(entered, module));
			}
		}
	}
}
