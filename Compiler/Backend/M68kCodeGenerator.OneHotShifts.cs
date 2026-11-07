/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
	private readonly HashSet<int> _allocatedOneHotShifts = new();
	private readonly Dictionary<int, int> _allocatedOneHotCountCopies = new();

	private void PrepareAllocatedOneHotShifts(M68kAllocatedFunction allocated)
	{
		_allocatedOneHotShifts.Clear();
		_allocatedOneHotCountCopies.Clear();
		var instructions = allocated.Function.Blocks.SelectMany(b => b.Instructions).ToArray();
		foreach (var shift in instructions)
		{
			if (shift.Operation != M68kMachineOperation.Shift || shift.Immediate is not null ||
				shift.SourceInstruction?.OpCode != OpCodes.Shl ||
				shift.Uses.Length != 2 || shift.Definitions.Length != 1 ||
				shift.ProducesConditionCodes || shift.ConsumesConditionCodes ||
				allocated.Function.Values[shift.Definitions[0]].Kind != CilStackValueKind.Int32 ||
				!IsAllocatedScalarIntegerConstant(allocated.Function, shift.Uses[0]) ||
				!TryGetAllocatedConstant(allocated.Function, shift.Uses[0], out var left) || left != 1 ||
				allocated.Allocation.Registers[shift.Uses[1]].Register != M68kRegister.D1 ||
				allocated.Allocation.Registers[shift.Definitions[0]].Register != M68kRegister.D0) continue;
			_allocatedOneHotShifts.Add(shift.Id);
			PrepareOneHotCountCopy(allocated, instructions, shift);
			if (!TryCollectExclusiveConstantChain(instructions, shift.Uses[0], shift, out var chain)) continue;
			foreach (var constant in chain)
			{
				_allocatedSuppressedInstructions.Add(constant.Id);
				_allocatedFoldedCopyConstants.Remove(constant.Id);
				_allocatedBitBranchDeadValues.UnionWith(constant.Definitions);
			}
		}
	}

	private void PrepareOneHotCountCopy(M68kAllocatedFunction allocated,
		IReadOnlyList<M68kMachineInstruction> instructions, M68kMachineInstruction shift)
	{
		// C# supplies an explicit count & 31 even though register BSET already
		// wraps modulo 32. Replace that AND in place with a copy, so the raw
		// count's allocated lifetime need not extend as far as the shift itself.
		var value = shift.Uses[1];
		var user = shift;
		var visited = new HashSet<int>();
		while (visited.Add(value))
		{
			if (allocated.Function.Blocks.Any(b => b.Phis.Any(p => p.Inputs.Values.Contains(value)))) return;
			var users = instructions.Where(i => i.Uses.Contains(value)).ToArray();
			var definition = instructions.SingleOrDefault(i => i.Definitions.Contains(value));
			if (users is not [var onlyUser] || onlyUser.Id != user.Id || definition is null ||
				definition.ProducesConditionCodes || definition.ConsumesConditionCodes ||
				(allocated.Function.Values[value].Kind != CilStackValueKind.Int32 &&
				 !IsNarrowIntegerKind(allocated.Function.Values[value].Kind))) return;
			if (definition is { Operation: M68kMachineOperation.Copy, Uses: [var source] })
			{
				value = source;
				user = definition;
				continue;
			}
			if (definition is not { Operation: M68kMachineOperation.And, Uses: [var left, var right] }) return;
			var maskOperand = IsAllocatedScalarIntegerConstant(allocated.Function, left) &&
				TryGetAllocatedConstant(allocated.Function, left, out var mask) && mask == 31 ? 0 :
				IsAllocatedScalarIntegerConstant(allocated.Function, right) &&
				TryGetAllocatedConstant(allocated.Function, right, out mask) && mask == 31 ? 1 : -1;
			if (maskOperand < 0) return;
			var count = definition.Uses[1 - maskOperand];
			if (allocated.Function.Values[count].Kind != CilStackValueKind.Int32 &&
				!IsNarrowIntegerKind(allocated.Function.Values[count].Kind)) return;
			_allocatedOneHotCountCopies.Add(definition.Id, count);
			if (TryCollectExclusiveConstantChain(instructions, definition.Uses[maskOperand], definition, out var chain) &&
				chain.All(c => !allocated.Function.Blocks.Any(b => b.Phis.Any(p => p.Inputs.Values.Any(c.Definitions.Contains)))))
				foreach (var constant in chain)
				{
					_allocatedSuppressedInstructions.Add(constant.Id);
					_allocatedFoldedCopyConstants.Remove(constant.Id);
					_allocatedBitBranchDeadValues.UnionWith(constant.Definitions);
				}
			return;
		}
	}
}
