using System.Reflection.Emit;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class TransparentPointerFieldLoweringTests
{
	[Fact]
	public void ByValuePointerFieldCopyDoesNotLoadItsPointee()
	{
		using var module = new CompilationModule(typeof(TransparentOutStructFixtures).Assembly.Location);
		var caller = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.TransparentOutStructFixtures::PointerEntry");
		var method = caller.Instructions.Where(i => i.OpCode == OpCodes.Call)
			.Select(i => module.ResolveMethodToken((int)i.Operand!, caller, i.Offset).Definition)
			.Single(m => m?.DisplayName.Contains("::ProjectStack", StringComparison.Ordinal) == true)!;
		var load = Assert.Single(method.Instructions, i => i.OpCode == OpCodes.Ldfld);
		var field = module.ResolveFieldToken((int)load.Operand!, method, load.Offset);
		Assert.True(module.IsTransparentScalarField(field),
			$"Field={field.DisplayName}, Type={field.Type}, IL={string.Join("; ", method.Instructions.Select(i => $"{i.Offset:X4} {i.OpCode} {i.Operand}"))}");
		var function = CilMachineIrBuilder.Build(method, module);
		Assert.Contains(function.Blocks.SelectMany(b => b.Instructions),
			i => i.IlOffset == load.Offset && i.Operation == M68kMachineOperation.Copy);
	}
}
