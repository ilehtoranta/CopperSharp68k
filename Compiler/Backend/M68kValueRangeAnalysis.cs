/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal readonly record struct M68kValueRange(bool IsKnown, uint Minimum, uint Maximum)
{
	internal static M68kValueRange Unknown => default;

	internal static M68kValueRange Exact(uint value) => new(true, value, value);

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
		return maximum <= uint.MaxValue
			? new M68kValueRange(true, (uint)minimum, (uint)maximum)
			: Unknown;
	}

	internal static M68kValueRange Join(
		M68kValueRange left,
		M68kValueRange right,
		bool widen)
	{
		if (!left.IsKnown || !right.IsKnown)
		{
			return Unknown;
		}
		if (left == right)
		{
			return left;
		}
		if (widen)
		{
			return Unknown;
		}

		return new M68kValueRange(
			true,
			Math.Min(left.Minimum, right.Minimum),
			Math.Max(left.Maximum, right.Maximum));
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
		IReadOnlyList<M68kInstructionEffects> effects)
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
			var output = Transfer(input, instructions[index], effects[index]);
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
		M68kInstructionEffects effects)
	{
		var output = input.Clone();
		if (effects.IsBarrier)
		{
			// A call or undecoded instruction can replace register values even when
			// the conservative liveness model treats every register as an input.
			// Do not let constants or address aliases survive that boundary.
			Array.Fill(output.Data, M68kValueRange.Unknown);
			var addressRegisterCount = instruction.Kind == M68kInstructionKind.Call
				? 7
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

		var preciseStackWrite = TryGetPreciseStackWrite(input, instruction, out var writeOffset, out var writeValue);
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
			TransferMoveLong(input, output, instruction);
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

		if ((opcode & 0xF1F8) == 0xD080)
		{
			var source = opcode & 7;
			var destination = (opcode >> 9) & 7;
			output.Data[destination] = M68kValueRange.Add(
				input.Data[destination],
				input.Data[source]);
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
		M68kEmittedInstruction instruction)
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
				instruction);
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
		M68kEmittedInstruction instruction)
	{
		if (mode == 0)
		{
			return state.Data[register];
		}
		if (mode == 7 && register == 4)
		{
			return M68kValueRange.Exact(instruction.ExtensionLong);
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
			instruction);
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
