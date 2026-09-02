/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class ArithmeticFrameDivisionTests
{
	[Theory]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.CopiedStructPair))]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.OutputHomeStructPair))]
	public void DivisionPairThroughPrivateFrameSnapshotsSharesItsArithmeticInstruction(string methodName)
	{
		using var module = new CompilationModule(typeof(IntegerArithmeticSelectionFixture).Assembly.Location);
		var entry = module.ResolveEntryPoint(typeof(IntegerArithmeticSelectionFixture).FullName + "::" + methodName);
		var methods = new Dictionary<CilMethodIdentity, CilMethod>();
		Collect(entry);
		var functions = methods.Values.ToDictionary(static method => method.Identity,
			method => CilMachineIrBuilder.Build(method, module));
		M68kMachineModuleOptimizer.Run(methods.Values.ToArray(), functions, module,
			M68kCpuTarget.M68000, new HashSet<CilMethodIdentity> { entry.Identity });
		var function = functions[entry.Identity];
		M68kCallAbiLowering.FinalizeLogicalCalls(function);
		M68kMachineArithmeticOptimizer.Run(function, M68kCpuTarget.M68000, module);
		var arithmetic = function.Blocks.SelectMany(static block => block.Instructions)
			.Where(static instruction => instruction.Operation is
				M68kMachineOperation.Divide or M68kMachineOperation.Remainder).ToArray();
		Assert.True(arithmetic.Length == 1, Describe(function));
		Assert.Equal(2, arithmetic[0].Definitions.Length);

		void Collect(CilMethod method)
		{
			if (!methods.TryAdd(method.Identity, method)) return;
			foreach (var source in method.Instructions.Where(static instruction =>
				instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Newobj))
			{
				if (module.ResolveMethodToken((int)source.Operand!, method, source.Offset).Definition is { } target &&
					target.DisplayName.Contains(nameof(IntegerArithmeticSelectionFixture), StringComparison.Ordinal))
					Collect(target);
			}
		}
	}

	private static string Describe(M68kMachineFunction function) => string.Join("\n",
		function.Blocks.Select(block => "BLOCK " + block.Id + "\n" + string.Join("\n",
			block.Instructions.Select(instruction =>
				$"{instruction.Id} {instruction.Operation} [{string.Join(',', instruction.Uses)}] -> " +
				$"[{string.Join(',', instruction.Definitions)}] ({instruction.MemoryEffect}) {instruction.SourceInstruction?.OpCode}\n" +
				string.Join("\n", instruction.ExactMemoryAccesses.Select(access => $"  {access}"))))));
}
