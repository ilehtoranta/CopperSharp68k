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
		var instructions = function.Blocks.SelectMany(b => b.Instructions).ToArray();
		var argument = Assert.Single(instructions,
			i => i.Operation == M68kMachineOperation.Argument && i.ArgumentIndex == 4);
		var store = Assert.Single(method.Instructions,
			i => i.OpCode == OpCodes.Stfld && i.Offset == load.NextOffset);
		var loweredStore = Assert.Single(instructions,
			i => i.IlOffset == store.Offset && i.Operation == M68kMachineOperation.Store);

		// Transparent field extraction is a copy, but Build propagates that copy
		// away. Verify the stored pointer is the original argument payload rather
		// than requiring an intermediate instruction that no longer survives.
		Assert.Equal(2, loweredStore.Uses.Length);
		Assert.Equal(Assert.Single(argument.Definitions), loweredStore.Uses[1]);
		Assert.DoesNotContain(instructions,
			i => i.IlOffset == load.Offset &&
				(i.MemoryEffect & M68kMachineMemoryEffect.Read) != 0);
	}
}
