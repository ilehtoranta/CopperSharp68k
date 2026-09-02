/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

/// <summary>
/// Describes compiler-owned struct snapshots for arithmetic value numbering.
/// This neither changes the shared memory annotations nor treats arbitrary
/// pointers as private: every inferred byte range must fit a known frame home.
/// </summary>
internal sealed class M68kArithmeticFrameMemory
{
	private readonly record struct FrameAddress(M68kMemoryObject Home, int Offset = 0);
	private readonly M68kMachineFunction _function;
	private readonly CompilationModule _module;
	private readonly IReadOnlyDictionary<int, M68kMachineInstruction> _definitions;

	public M68kArithmeticFrameMemory(M68kMachineFunction function, CompilationModule module,
		IReadOnlyDictionary<int, M68kMachineInstruction> definitions)
	{
		_function = function;
		_module = module;
		_definitions = definitions;
	}

	public ImmutableArray<M68kExactMemoryAccess> AccessesFor(M68kMachineInstruction instruction)
	{
		var existing = instruction.ExactMemoryAccesses.IsDefault ? [] : instruction.ExactMemoryAccesses;
		if (instruction.Operation == M68kMachineOperation.AggregateFieldLoad &&
			instruction.Uses is [var source] && instruction.ArgumentIndex is { } destination &&
			TryField(instruction, out var displacement, out var size) &&
			Slice(AddressOf(source), displacement, size) is { } read &&
			Slice(LocalHome(destination), 0, size) is { } write)
			return [new(read, M68kExactMemoryAccessKind.Read), new(write, M68kExactMemoryAccessKind.Write)];

		if (instruction.Operation == M68kMachineOperation.Load &&
			instruction.SourceInstruction?.OpCode == OpCodes.Ldfld &&
			instruction.Uses is [var owner] && instruction.Definitions is [var loaded] &&
			_function.Values[loaded].Width == M68kMachineValueWidth.Long &&
			TryField(instruction, out displacement, out size) && size == 4 &&
			Slice(AddressOf(owner), displacement, size) is { } field)
			return [new(field, M68kExactMemoryAccessKind.Read, loaded)];

		if (instruction.Operation == M68kMachineOperation.Store &&
			instruction.SourceInstruction?.OpCode == OpCodes.Stfld &&
			instruction.Uses is [var storeOwner, var fieldValue] &&
			TryField(instruction, out displacement, out size) &&
			Slice(AddressOf(storeOwner), displacement, size) is { } writtenField)
		{
			// Writing a field of a private result struct must invalidate that
			// range, not every unrelated local snapshot. In particular, an out
			// value from an earlier call can be read for both quotient and
			// remainder around a store into a separate DateStamp-style struct.
			var value = _function.Values[fieldValue];
			return [new(writtenField, M68kExactMemoryAccessKind.Write,
				size == 4 && value.Width == M68kMachineValueWidth.Long &&
				value.Kind != CilStackValueKind.AggregateAddress ? fieldValue : null)];
		}

		if (instruction.Operation is M68kMachineOperation.LocalStore or M68kMachineOperation.ArgumentStore &&
			instruction.Uses is [var stored] &&
			_function.Values[stored].Kind == CilStackValueKind.AggregateAddress &&
			existing is [var store] && store.Kind == M68kExactMemoryAccessKind.Write &&
			Slice(AddressOf(stored), 0, store.Object.Size) is { } copied)
			return [new(copied, M68kExactMemoryAccessKind.Read), store with { ValueId = null }];

		return existing;
	}

	private FrameAddress? AddressOf(int value) => AddressOf(value, new HashSet<int>());

	private FrameAddress? AddressOf(int value, HashSet<int> visited)
	{
		if (!visited.Add(value) || !_definitions.TryGetValue(value, out var instruction) ||
			_function.Values[value].Width != M68kMachineValueWidth.Long)
			return null;
		if (instruction is { Operation: M68kMachineOperation.Copy, Uses: [var copied] })
			return AddressOf(copied, visited);
		if (instruction.ArgumentIndex is { } home)
		{
			if (instruction.Operation is M68kMachineOperation.LocalAddress or
				M68kMachineOperation.AggregateFieldLoad or M68kMachineOperation.AggregateIndirectLoad)
				return LocalHome(home);
			if (instruction.Operation == M68kMachineOperation.ArgumentAddress &&
				_function.ArgumentHomes.TryGetValue(home, out var argument))
				return new FrameAddress(M68kMemoryModel.FrameObject(M68kMemoryObjectKind.ArgumentHome, home, argument));
		}
		if (instruction.Operation == M68kMachineOperation.Load &&
			instruction.SourceInstruction?.OpCode == OpCodes.Ldflda &&
			instruction.Uses is [var owner] && AddressOf(owner, visited) is { } address &&
			TryField(instruction, out var displacement, out var size) &&
			Slice(address, displacement, size) is not null)
			return address with { Offset = address.Offset + displacement };
		return null;
	}

	private FrameAddress? LocalHome(int index) =>
		_function.LocalHomes.TryGetValue(index, out var home)
			? new FrameAddress(M68kMemoryModel.FrameObject(M68kMemoryObjectKind.FrameSlot, index, home))
			: null;

	private static M68kMemoryObject? Slice(FrameAddress? address, int displacement, int size)
	{
		if (address is not { } frame || size <= 0) return null;
		var offset = (long)frame.Offset + displacement;
		return offset >= 0 && offset + size <= frame.Home.Size
			? frame.Home with { Offset = (int)offset, Size = size }
			: null;
	}

	private bool TryField(M68kMachineInstruction instruction, out int displacement, out int size)
	{
		displacement = size = 0;
		var method = instruction.Origin?.SourceMethod ?? _function.SourceMethod;
		var source = instruction.SourceInstruction;
		if (method is null || source?.Operand is not int token ||
			(source.OpCode != OpCodes.Ldfld && source.OpCode != OpCodes.Ldflda &&
			 source.OpCode != OpCodes.Stfld))
			return false;
		var field = _module.ResolveFieldToken(token, method, source.Offset);
		if (field.IsStatic) return false;
		if (field.ExternalOffset is { } external) displacement = external;
		else
		{
			if (!_module.GetTypeLayout(field).FieldOffsets.TryGetValue(field.Handle, out displacement)) return false;
			// Match the compiler's public value transport, which omits CoreLib's
			// object header for the pinned TimeSpan implementation.
			if (field.ModuleName == "System.Private.CoreLib" &&
				field.DisplayName.EndsWith("System.TimeSpan::_ticks", StringComparison.Ordinal))
				displacement -= 8;
		}
		if (field.Type.IsSupportedScalar) size = field.Type.Size;
		else if (_module.TryGetReferenceFreeStructLayout(field.Type, field.ModuleName, out var layout)) size = layout.Size;
		var offset = (long)displacement + instruction.MemoryOffset;
		if (offset is < int.MinValue or > int.MaxValue) return false;
		displacement = (int)offset;
		return size > 0;
	}
}
