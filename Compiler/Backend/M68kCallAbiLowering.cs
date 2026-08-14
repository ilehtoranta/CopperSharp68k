/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

/// <summary>
/// Closes the logical-call phase. The current builder already materializes ABI
/// staging instructions; after whole-program rewrites finish, logical SSA ids
/// must not survive into spill rewriting or physical allocation.
/// </summary>
internal static class M68kCallAbiLowering
{
	public static void FinalizeLogicalCalls(M68kMachineFunction function)
	{
		M68kMachineIrVerifier.Verify(function);
		foreach (var block in function.Blocks)
		{
			for (var index = 0; index < block.Instructions.Count; index++)
			{
				if (block.Instructions[index].LogicalCall is not null)
				{
					block.Instructions[index] = block.Instructions[index] with
					{
						LogicalCall = null
					};
				}
			}
		}
		M68kMachineIrVerifier.Verify(function);
	}
}
