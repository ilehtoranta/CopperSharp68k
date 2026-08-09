/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal readonly record struct M68kValueRange(
	bool IsKnown,
	uint Minimum,
	uint Maximum,
	uint KnownZeroMask = 0)
{
	internal static M68kValueRange Unknown => default;

	internal static M68kValueRange Exact(uint value) => new(true, value, value, ~value);

	internal bool IsExact(out uint value)
	{
		value = Minimum;
		return IsKnown && Minimum == Maximum;
	}

	internal static M68kValueRange Add(M68kValueRange left, M68kValueRange right)
	{
		if (!left.IsKnown || !right.IsKnown)
		{
			return Unknown;
		}

		var minimum = (ulong)left.Minimum + right.Minimum;
		var maximum = (ulong)left.Maximum + right.Maximum;
		if (maximum > uint.MaxValue)
		{
			return Unknown;
		}

		return minimum == maximum
			? Exact((uint)minimum)
			: new M68kValueRange(true, (uint)minimum, (uint)maximum);
	}

	internal static M68kValueRange Join(
		M68kValueRange left,
		M68kValueRange right,
		bool widen)
	{
		var knownZeroMask = left.KnownZeroMask & right.KnownZeroMask;
		if (!left.IsKnown || !right.IsKnown)
		{
			return new M68kValueRange(false, 0, 0, knownZeroMask);
		}
		if (left == right)
		{
			return left;
		}
		if (widen)
		{
			if ((left.Minimum >> 16) == (left.Maximum >> 16) &&
				(right.Minimum >> 16) == (right.Maximum >> 16) &&
				(left.Minimum >> 16) == (right.Minimum >> 16))
			{
				var upperWord = left.Minimum & 0xFFFF0000;
				return new M68kValueRange(
					true,
					upperWord,
					upperWord | 0x0000FFFF,
					knownZeroMask | (~upperWord & 0xFFFF0000));
			}
			return new M68kValueRange(false, 0, 0, knownZeroMask);
		}

		return new M68kValueRange(
			true,
			Math.Min(left.Minimum, right.Minimum),
			Math.Max(left.Maximum, right.Maximum),
			knownZeroMask);
	}
}

internal enum M68kAddressAliasKind : byte
{
	Unknown,
	Stack
}

internal readonly record struct M68kAddressAlias(M68kAddressAliasKind Kind, int Offset)
{
	internal static M68kAddressAlias Unknown => default;

	internal static M68kAddressAlias Stack(int offset) =>
		new(M68kAddressAliasKind.Stack, offset);

	internal M68kAddressAlias Add(int displacement) =>
		Kind == M68kAddressAliasKind.Stack &&
		(long)Offset + displacement is >= int.MinValue and <= int.MaxValue
			? Stack(Offset + displacement)
			: Unknown;
}

internal sealed class M68kValueRangeAnalysis
{
	private readonly IReadOnlyDictionary<int, AbstractState> _before;

	private M68kValueRangeAnalysis(IReadOnlyDictionary<int, AbstractState> before)
	{
		_before = before;
	}

	internal static M68kValueRangeAnalysis Empty { get; } =
		new(new Dictionary<int, AbstractState>());

	internal M68kValueRange GetDataValueBefore(int offset, int register)
	{
		ValidateRegister(register);
		return _before.TryGetValue(offset, out var state)
			? state.Data[register]
			: M68kValueRange.Unknown;
	}

	internal M68kAddressAlias GetAddressAliasBefore(int offset, int register)
	{
		ValidateRegister(register);
		return _before.TryGetValue(offset, out var state)
			? state.Address[register]
			: M68kAddressAlias.Unknown;
	}

	internal static M68kValueRangeAnalysis Analyze(
		IReadOnlyList<M68kEmittedInstruction> instructions,
		IReadOnlyList<int>[] successors,
		IReadOnlyList<int>[] predecessors,
		IReadOnlyList<M68kInstructionEffects> effects,
		IReadOnlySet<int> addressFixupOffsets)
	{
		var before = new AbstractState?[instructions.Count];
		var pending = new Queue<int>();
		var queued = new bool[instructions.Count];
		for (var index = 0; index < instructions.Count; index++)
		{
			if (predecessors[index].Count != 0)
			{
				continue;
			}

			before[index] = AbstractState.CreateEntry();
			pending.Enqueue(index);
			queued[index] = true;
		}

		while (pending.Count != 0)
		{
			var index = pending.Dequeue();
			queued[index] = false;
			var input = before[index]!;
			var output = Transfer(
				input,
				instructions[index],
				effects[index],
				addressFixupOffsets);
			foreach (var successor in successors[index])
			{
				var changed = before[successor] is null
					? SetInitial(before, successor, output)
					: before[successor]!.JoinFrom(output, widen: successor <= index);
				if (changed && !queued[successor])
				{
					pending.Enqueue(successor);
					queued[successor] = true;
				}
			}
		}

		var result = new Dictionary<int, AbstractState>();
		for (var index = 0; index < instructions.Count; index++)
		{
			if (before[index] is not null)
			{
				result[instructions[index].Offset] = before[index]!;
			}
		}
		return new M68kValueRangeAnalysis(result);
	}

	private static bool SetInitial(AbstractState?[] states, int index, AbstractState value)
	{
		states[index] = value.Clone();
		return true;
	}

	private static AbstractState Transfer(
		AbstractState input,
		M68kEmittedInstruction instruction,
		M68kInstructionEffects effects,
		IReadOnlySet<int> addressFixupOffsets)
	{
		var output = input.Clone();
		if (effects.IsBarrier)
		{
			// Calls obey the compiler/Amiga ABI: D0-D1/A0-A1 are caller-saved,
			// while D2-D7/A2-A7 retain their values. An undecoded barrier has no
			// such contract and must invalidate every register.
			var dataRegisterCount = instruction.Kind == M68kInstructionKind.Call
				? 2
				: 8;
			for (var register = 0; register < dataRegisterCount; register++)
			{
				output.Data[register] = M68kValueRange.Unknown;
			}
			var addressRegisterCount = instruction.Kind == M68kInstructionKind.Call
				? 2
				: 8;
			for (var register = 0; register < addressRegisterCount; register++)
			{
				output.Address[register] = M68kAddressAlias.Unknown;
			}
		}
		else
		{
			InvalidateDefinedRegisters(output, effects);
		}

		var preciseStackWrite = TryGetPreciseStackWrite(
			input,
			instruction,
			addressFixupOffsets,
			out var writeOffset,
			out var writeValue);
		if ((effects.WritesMemory &
			(M68kMemorySet.Stack | M68kMemorySet.Indirect | M68kMemorySet.Unknown)) != 0)
		{
			if (preciseStackWrite)
			{
				output.StoreStackLong(writeOffset, writeValue);
			}
			else
			{
				output.StackValues.Clear();
			}
		}

		ApplyStackDelta(input, output, effects);

		var opcode = instruction.Opcode;
		if ((opcode & 0xF100) == 0x7000)
		{
			var register = (opcode >> 9) & 7;
			output.Data[register] = M68kValueRange.Exact(
				unchecked((uint)(int)(sbyte)(opcode & 0xFF)));
			return output;
		}

		if ((opcode & 0xF000) == 0x2000)
		{
			TransferMoveLong(input, output, instruction, addressFixupOffsets);
			return output;
		}

		if ((opcode & 0xF000) == 0x1000 &&
			((opcode >> 3) & 7) == 0 &&
			((opcode >> 6) & 7) == 0)
		{
			var destination = (opcode >> 9) & 7;
			var priorDestination = input.Data[destination];
			if (priorDestination.IsKnown &&
				(priorDestination.Minimum >> 8) == (priorDestination.Maximum >> 8))
			{
				var upper = priorDestination.Minimum & 0xFFFFFF00;
				output.Data[destination] = new M68kValueRange(
					true,
					upper,
					upper | 0xFF,
					~upper & 0xFFFFFF00);
			}
			return output;
		}

		if ((opcode & 0xFFC0) == 0x4280)
		{
			if (((opcode >> 3) & 7) == 0)
			{
				output.Data[opcode & 7] = M68kValueRange.Exact(0);
			}
			return output;
		}

		if ((opcode & 0xFFF8) == 0x0680)
		{
			var register = opcode & 7;
			output.Data[register] = M68kValueRange.Add(
				input.Data[register],
				M68kValueRange.Exact(instruction.ExtensionLong));
			return output;
		}

		if ((opcode & 0xFFF8) == 0x0280 && instruction.Length == 6)
		{
			var register = opcode & 7;
			var mask = instruction.ExtensionLong;
			var inputRange = input.Data[register];
			if (inputRange.IsExact(out var exact))
			{
				output.Data[register] = M68kValueRange.Exact(exact & mask);
			}
			else if ((mask & unchecked(mask + 1)) == 0)
			{
				output.Data[register] = inputRange.IsKnown && inputRange.Maximum <= mask
					? inputRange
					: new M68kValueRange(true, 0, mask, ~mask);
			}
			return output;
		}

		if ((opcode & 0xF1F8) == 0xD080)
		{
			var source = opcode & 7;
			var destination = (opcode >> 9) & 7;
			output.Data[destination] = M68kValueRange.Add(
				input.Data[destination],
				input.Data[source]);
			return output;
		}

		if ((opcode & 0xF1F8) == 0xD040)
		{
			var destination = (opcode >> 9) & 7;
			output.Data[destination] = PreserveUpperWord(input.Data[destination]);
			return output;
		}

		if ((opcode & 0xF000) == 0xE000 &&
			(opcode & 0x00C0) == 0x0040)
		{
			var destination = opcode & 7;
			output.Data[destination] = PreserveUpperWord(input.Data[destination]);
			return output;
		}

		if ((opcode & 0xF1F8) == 0x5080)
		{
			var register = opcode & 7;
			output.Data[register] = M68kValueRange.Add(
				input.Data[register],
				M68kValueRange.Exact((uint)QuickCount(opcode)));
			return output;
		}

		if ((opcode & 0xF1F8) == 0x5180)
		{
			var register = opcode & 7;
			if (input.Data[register].IsKnown &&
				input.Data[register].Minimum >= QuickCount(opcode))
			{
				var count = (uint)QuickCount(opcode);
				output.Data[register] = new M68kValueRange(
					true,
					input.Data[register].Minimum - count,
					input.Data[register].Maximum - count);
			}
			return output;
		}

		if ((opcode & 0xF1FF) == 0xD0FC)
		{
			var destination = (opcode >> 9) & 7;
			output.Address[destination] = input.Address[destination].Add(
				unchecked((short)instruction.ExtensionWord));
			return output;
		}

		if ((opcode & 0xF1FF) == 0xD1FC)
		{
			var destination = (opcode >> 9) & 7;
			output.Address[destination] = input.Address[destination].Add(
				unchecked((int)instruction.ExtensionLong));
			return output;
		}

		if ((opcode & 0xF1FF) == 0x41EF)
		{
			var destination = (opcode >> 9) & 7;
			var displacement = unchecked((short)instruction.ExtensionWord);
			output.Address[destination] = input.Address[7].Add(displacement);
			return output;
		}

		if ((opcode & 0xFFC0) == 0x41C0)
		{
			var destination = (opcode >> 9) & 7;
			output.Address[destination] = ResolveAddress(
				input,
				(opcode >> 3) & 7,
				opcode & 7,
				instruction.ExtensionWord,
				4);
		}

		return output;
	}

	private static M68kValueRange PreserveUpperWord(M68kValueRange input)
	{
		if (!input.IsKnown || (input.Minimum >> 16) != (input.Maximum >> 16))
		{
			return new M68kValueRange(
				false,
				0,
				0,
				input.KnownZeroMask & 0xFFFF0000);
		}

		var upperWord = input.Minimum & 0xFFFF0000;
		return new M68kValueRange(
			true,
			upperWord,
			upperWord | 0x0000FFFF,
			~upperWord & 0xFFFF0000);
	}

	private static void InvalidateDefinedRegisters(
		AbstractState output,
		M68kInstructionEffects effects)
	{
		for (var register = 0; register < 8; register++)
		{
			if ((effects.DefinesData & (1 << register)) != 0)
			{
				output.Data[register] = M68kValueRange.Unknown;
			}
			if (register != 7 && (effects.DefinesAddress & (1 << register)) != 0)
			{
				output.Address[register] = M68kAddressAlias.Unknown;
			}
		}
	}

	private static void ApplyStackDelta(
		AbstractState input,
		AbstractState output,
		M68kInstructionEffects effects)
	{
		if ((effects.DefinesAddress & 0x80) == 0)
		{
			return;
		}

		output.Address[7] = effects.StackDelta.HasValue
			? input.Address[7].Add(effects.StackDelta.Value)
			: M68kAddressAlias.Unknown;
	}

	private static void TransferMoveLong(
		AbstractState input,
		AbstractState output,
		M68kEmittedInstruction instruction,
		IReadOnlySet<int> addressFixupOffsets)
	{
		var opcode = instruction.Opcode;
		var sourceMode = (opcode >> 3) & 7;
		var sourceRegister = opcode & 7;
		var destinationMode = (opcode >> 6) & 7;
		var destinationRegister = (opcode >> 9) & 7;

		if (destinationMode == 0)
		{
			output.Data[destinationRegister] = ReadLongValue(
				input,
				sourceMode,
				sourceRegister,
				instruction,
				addressFixupOffsets);
			return;
		}

		if (destinationMode == 1)
		{
			output.Address[destinationRegister] = sourceMode == 1
				? input.Address[sourceRegister]
				: M68kAddressAlias.Unknown;
		}
	}

	private static M68kValueRange ReadLongValue(
		AbstractState state,
		int mode,
		int register,
		M68kEmittedInstruction instruction,
		IReadOnlySet<int> addressFixupOffsets)
	{
		if (mode == 0)
		{
			return state.Data[register];
		}
		if (mode == 7 && register == 4)
		{
			return addressFixupOffsets.Contains(instruction.Offset + 2)
				? M68kValueRange.Unknown
				: M68kValueRange.Exact(instruction.ExtensionLong);
		}

		var address = ResolveAddress(
			state,
			mode,
			register,
			instruction.ExtensionWord,
			4);
		return address.Kind == M68kAddressAliasKind.Stack &&
			state.StackValues.TryGetValue(address.Offset, out var value)
				? value
				: M68kValueRange.Unknown;
	}

	private static bool TryGetPreciseStackWrite(
		AbstractState state,
		M68kEmittedInstruction instruction,
		IReadOnlySet<int> addressFixupOffsets,
		out int offset,
		out M68kValueRange value)
	{
		offset = 0;
		value = M68kValueRange.Unknown;
		var opcode = instruction.Opcode;
		if ((opcode & 0xF000) != 0x2000)
		{
			return false;
		}

		var destinationMode = (opcode >> 6) & 7;
		if (destinationMode is 0 or 1)
		{
			return false;
		}

		var sourceMode = (opcode >> 3) & 7;
		var destinationRegister = (opcode >> 9) & 7;
		if (sourceMode is 3 or 4 && (opcode & 7) == destinationRegister)
		{
			return false;
		}
		if (destinationMode == 5 && sourceMode > 4)
		{
			return false;
		}
		var address = ResolveAddress(
			state,
			destinationMode,
			destinationRegister,
			destinationMode == 5 ? instruction.ExtensionWord : (ushort)0,
			4);
		if (address.Kind != M68kAddressAliasKind.Stack)
		{
			return false;
		}

		offset = address.Offset;
		value = ReadLongValue(
			state,
			sourceMode,
			opcode & 7,
			instruction,
			addressFixupOffsets);
		return true;
	}

	private static M68kAddressAlias ResolveAddress(
		AbstractState state,
		int mode,
		int register,
		ushort extensionWord,
		int size)
	{
		var alias = state.Address[register];
		return mode switch
		{
			2 or 3 => alias,
			4 => alias.Add(-size),
			5 => alias.Add(unchecked((short)extensionWord)),
			_ => M68kAddressAlias.Unknown
		};
	}

	private static int QuickCount(ushort opcode)
	{
		var count = (opcode >> 9) & 7;
		return count == 0 ? 8 : count;
	}

	private static void ValidateRegister(int register)
	{
		if ((uint)register >= 8)
		{
			throw new ArgumentOutOfRangeException(nameof(register));
		}
	}

	private sealed class AbstractState
	{
		internal M68kValueRange[] Data { get; }
		internal M68kAddressAlias[] Address { get; }
		internal Dictionary<int, M68kValueRange> StackValues { get; }

		private AbstractState(
			M68kValueRange[] data,
			M68kAddressAlias[] address,
			Dictionary<int, M68kValueRange> stackValues)
		{
			Data = data;
			Address = address;
			StackValues = stackValues;
		}

		internal static AbstractState CreateEntry()
		{
			var address = new M68kAddressAlias[8];
			address[7] = M68kAddressAlias.Stack(0);
			return new AbstractState(
				new M68kValueRange[8],
				address,
				new Dictionary<int, M68kValueRange>());
		}

		internal AbstractState Clone() => new(
			(M68kValueRange[])Data.Clone(),
			(M68kAddressAlias[])Address.Clone(),
			new Dictionary<int, M68kValueRange>(StackValues));

		internal bool JoinFrom(AbstractState incoming, bool widen)
		{
			var changed = false;
			for (var register = 0; register < 8; register++)
			{
				var joinedValue = M68kValueRange.Join(Data[register], incoming.Data[register], widen);
				if (joinedValue != Data[register])
				{
					Data[register] = joinedValue;
					changed = true;
				}

				var joinedAlias = Address[register] == incoming.Address[register]
					? Address[register]
					: M68kAddressAlias.Unknown;
				if (joinedAlias != Address[register])
				{
					Address[register] = joinedAlias;
					changed = true;
				}
			}

			foreach (var offset in StackValues.Keys.ToArray())
			{
				if (!incoming.StackValues.TryGetValue(offset, out var incomingValue))
				{
					StackValues.Remove(offset);
					changed = true;
					continue;
				}

				var joined = M68kValueRange.Join(StackValues[offset], incomingValue, widen);
				if (!joined.IsKnown)
				{
					StackValues.Remove(offset);
					changed = true;
				}
				else if (joined != StackValues[offset])
				{
					StackValues[offset] = joined;
					changed = true;
				}
			}
			return changed;
		}

		internal void StoreStackLong(int offset, M68kValueRange value)
		{
			foreach (var existing in StackValues.Keys
				.Where(existing => existing < offset + 4 && offset < existing + 4)
				.ToArray())
			{
				StackValues.Remove(existing);
			}
			if (value.IsKnown)
			{
				StackValues[offset] = value;
			}
		}
	}
}
