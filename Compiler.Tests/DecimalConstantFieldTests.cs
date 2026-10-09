/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Reflection.Emit;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class DecimalConstantFieldTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void DecimalConstantsRequireAdmittedInputsAndReadOnlyLoads(bool released)
	{
		using var pack = released ? FrameworkImplementationPackTests.CoreLibPack.CreatePinned() : FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = Open(pack, true);
		using var stable = Open(pack, false);
		var caller = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibDecimalPublicConstantsEntry");
		var loads = caller.Instructions.Where(i => i.OpCode == OpCodes.Ldsfld).ToArray();
		var names = new HashSet<string>();
		foreach (var instruction in loads)
		{
			var field = module.ResolveFieldToken((int)instruction.Operand!, caller, instruction.Offset);
			Assert.Equal("CopperSharp.Runtime.Managed", field.ModuleName);
			Assert.StartsWith("CopperSharp.Runtime.ShadowDecimalConstants::", field.DisplayName);
			Assert.True(field.IsStatic);
			Assert.Equal("System.Decimal", field.Type.DisplayName);
			names.Add(field.DisplayName.Split("::")[1]);
			if (released) Assert.Equal(field.DisplayName, stable.ResolveFieldToken((int)instruction.Operand!, caller, instruction.Offset).DisplayName);
			else Assert.Throws<M68kCompilationException>(() => stable.ResolveFieldToken((int)instruction.Operand!, caller, instruction.Offset));
			foreach (var opcode in new[] { OpCodes.Stsfld, OpCodes.Ldsflda })
			{
				var changed = caller with { Instructions = caller.Instructions.Select(i => i.Offset == instruction.Offset ? i with { OpCode = opcode } : i).ToArray() };
				var error = Assert.Throws<M68kCompilationException>(() => module.ResolveFieldToken((int)instruction.Operand!, changed, instruction.Offset));
				Assert.Contains("read-only static field", error.Message);
				if (released) Assert.Contains("read-only static field", Assert.Throws<M68kCompilationException>(() => stable.ResolveFieldToken((int)instruction.Operand!, changed, instruction.Offset)).Message);
			}
		}
		Assert.Equal(new[] { "MinusOne", "One", "Zero" }, names.Order().ToArray());
	}
	private static CompilationModule Open(FrameworkImplementationPackTests.CoreLibPack pack, bool enabled) =>
		new(typeof(CompilerFixtures).Assembly.Location, managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowDecimalConstants).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = enabled }));
}
