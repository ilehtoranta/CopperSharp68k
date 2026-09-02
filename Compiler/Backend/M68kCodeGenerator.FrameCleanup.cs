/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
	private M68kAllocatedFunction RefineAllocatedPreservation(M68kAllocatedFunction allocated)
	{
		if (allocated.Frame.CalleeSavedRegisters.Count == 0) return allocated;
		var instructions = allocated.Function.Blocks.SelectMany(static block => block.Instructions).ToArray();
		var nonEmittedConstants = instructions.Where(instruction =>
				_allocatedSuppressedInstructions.Contains(instruction.Id) &&
				instruction.Operation is M68kMachineOperation.Constant or M68kMachineOperation.Copy &&
				instruction.Definitions is [var value] &&
				IsAllocatedScalarIntegerConstant(allocated.Function, value))
			.Select(static instruction => instruction.Definitions[0]).ToHashSet();
		var selectedClobbers = instructions.Where(instruction =>
				_allocatedConstantMultiplies.ContainsKey(instruction.Id))
			.ToDictionary(static instruction => instruction.Id, instruction =>
			{
				// The selected shift/add multiply reads its source and writes D2
				// and D0 (only D0 for 0/1). D3 belongs to the unused general helper.
				var clobbers = instruction.Clobbers.Remove(M68kRegister.D3);
				return _allocatedConstantMultiplies[instruction.Id].Factor <= 1
					? clobbers.Remove(M68kRegister.D2) : clobbers;
			});
		var registers = M68kAllocatedPreservationAnalysis.RequiredRegisters(
			allocated.Function, allocated.Allocation, allocated.Frame.CalleeSavedRegisters,
			nonEmittedConstants, selectedClobbers,
			_allocatedFunctionAddressSwitches.Values.SelectMany(static plan =>
				new[] { plan.Selector, plan.ValueRegister }));
		return registers.Count == allocated.Frame.CalleeSavedRegisters.Count ? allocated : allocated with
		{
			Frame = allocated.Frame with { CalleeSavedRegisters = registers },
			Statistics = allocated.Statistics with { CalleeSavedRegisters = registers.Count }
		};
	}

	private static bool IsAllocatedScalarIntegerConstant(M68kMachineFunction function, int value)
	{
		var visited = new HashSet<int>();
		while (visited.Add(value))
		{
			var machineValue = function.Values[value];
			if (machineValue.Kind is < CilStackValueKind.BooleanByte or > CilStackValueKind.Int32 ||
				machineValue.Width is not (M68kMachineValueWidth.Byte or M68kMachineValueWidth.Word or M68kMachineValueWidth.Long))
			{
				return false;
			}
			var definition = function.Blocks.SelectMany(static block => block.Instructions)
				.SingleOrDefault(instruction => instruction.Definitions.Contains(value));
			if (definition is { Operation: M68kMachineOperation.Constant })
			{
				// Inspect the actual constant kind as well as the transport width:
				// a copied lane or bitcast must not reinterpret ldc.i8/ldc.r4 here.
				var constant = definition.ConstantValue;
				if (constant is null && definition.SourceInstruction is { } source &&
					M68kMachineConstant.TryFromCil(source, boolean: false, out var decoded))
				{
					constant = decoded;
				}
				return constant?.Kind is M68kMachineConstantKind.Int32 or M68kMachineConstantKind.Boolean;
			}
			if (definition is not { Operation: M68kMachineOperation.Copy, Uses: [var copied] }) return false;
			value = copied;
		}
		return false;
	}

	private int AllocatedEntryLocalWriteSize(
		CilMethod method,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		if (instruction.Operation == M68kMachineOperation.LocalStore &&
			instruction.ArgumentIndex is { } local && instruction.Uses is [var value])
		{
			var stored = allocated.Function.Values[value];
			if (local < method.Locals.Length)
			{
				if (stored.Kind == CilStackValueKind.AggregateAddress &&
					_module.TryGetReferenceFreeStructLayout(
						method.Locals[local], method.ModuleName, out var layout) &&
					layout.Size > 4)
				{
					return instruction.MemoryOffset == 0 ? layout.Size : 0;
				}
				return (int)AllocatedFrameStorageWidth(method.Locals[local], stored.Width);
			}
			return (int)stored.Width;
		}
		if (instruction.Operation == M68kMachineOperation.AggregateFieldLoad)
		{
			var field = ResolveAllocatedField(method, instruction);
			return instruction.MemoryOffset == 0 && _module.TryGetReferenceFreeStructLayout(
				field.Type, field.ModuleName, out var layout) ? layout.Size : 0;
		}
		if (instruction.Operation is M68kMachineOperation.AggregateIndirectInitialize or
			M68kMachineOperation.AggregateIndirectLoad or M68kMachineOperation.AggregateIndirectCopy or
			M68kMachineOperation.AggregateArrayLoad && instruction.MemoryOffset == 0 &&
			instruction.SourceInstruction?.Operand is int token)
		{
			var type = _module.ResolveTypeToken(token, method, instruction.IlOffset);
			var found = instruction.Operation == M68kMachineOperation.AggregateIndirectInitialize
				? _module.TryGetIndirectInitializeLayout(type, method.ModuleName, out var layout)
				: _module.TryGetReferenceFreeStructLayout(type, method.ModuleName, out layout);
			return found ? layout.Size : 0;
		}
		return 0;
	}
}
