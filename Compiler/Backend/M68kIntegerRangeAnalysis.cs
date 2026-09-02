/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

/// <summary>
/// Conservative bounds for integer instruction selection. Facts describe values,
/// not the currently occupied register: narrow arithmetic still has its original
/// wrapping semantics, and a phi containing an unbounded backedge stays unknown.
/// </summary>
internal sealed class M68kIntegerRangeAnalysis
{
	internal readonly record struct Bounds(long Minimum, long Maximum)
	{
		public bool IsSignedWord => Minimum >= short.MinValue && Maximum <= short.MaxValue;
		public bool IsUnsignedWord => Minimum >= 0 && Maximum <= ushort.MaxValue;
	}

	private readonly M68kMachineFunction _function;
	private readonly Dictionary<int, M68kMachineInstruction> _definitions;
	private readonly Dictionary<int, M68kMachinePhi> _phis;
	private readonly Dictionary<int, Bounds?> _cache = new();
	private readonly HashSet<int> _visiting = new();

	public M68kIntegerRangeAnalysis(M68kMachineFunction function)
	{
		_function = function;
		_definitions = function.Blocks.SelectMany(static block => block.Instructions)
			.SelectMany(instruction => instruction.Definitions.Select(value => (value, instruction)))
			.ToDictionary(static pair => pair.value, static pair => pair.instruction);
		_phis = function.Blocks.SelectMany(static block => block.Phis)
			.ToDictionary(static phi => phi.Definition);
	}

	public bool TryGetConstant(int value, out int constant)
	{
		var bounds = Get(value);
		if (bounds is { } range && range.Minimum == range.Maximum &&
			range.Minimum is >= int.MinValue and <= int.MaxValue)
		{
			constant = (int)range.Minimum;
			return true;
		}
		constant = 0;
		return false;
	}

	public Bounds? Get(int value)
	{
		if (_cache.TryGetValue(value, out var cached)) return cached;
		if (!_visiting.Add(value)) return null;
		var result = Analyze(value);
		_visiting.Remove(value);
		_cache[value] = result;
		return result;
	}

	private Bounds? Analyze(int value)
	{
		var machineValue = _function.Values[value];
		if (machineValue.Width == M68kMachineValueWidth.LongPair ||
			machineValue.Kind is CilStackValueKind.Float32 or CilStackValueKind.Float64)
			return null;
		if (_definitions.TryGetValue(value, out var instruction))
		{
			if (instruction.Operation == M68kMachineOperation.Constant &&
				instruction.ConstantValue is { } constant &&
				constant.TryGetIntegral(out var integer) &&
				integer is >= int.MinValue and <= int.MaxValue)
			{
				integer = machineValue.Kind switch
				{
					CilStackValueKind.BooleanByte or CilStackValueKind.UnsignedByte => unchecked((byte)integer),
					CilStackValueKind.SignedByte => unchecked((sbyte)integer),
					CilStackValueKind.UnsignedWord => unchecked((ushort)integer),
					CilStackValueKind.SignedWord => unchecked((short)integer),
					_ => integer
				};
				return new Bounds(integer, integer);
			}

			// A narrow result denotes the result after its required normalization,
			// even when the emitter can defer that normalization to a later use.
			if (NarrowBounds(machineValue.Kind) is { } narrow) return narrow;
			if (instruction is { Operation: M68kMachineOperation.Copy, Uses: [var copied] })
				return Get(copied);
			if (instruction.Operation == M68kMachineOperation.Convert &&
				instruction.Uses is [var converted])
			{
				var op = instruction.SourceInstruction?.OpCode;
				if (op == OpCodes.Conv_I1) return new Bounds(sbyte.MinValue, sbyte.MaxValue);
				if (op == OpCodes.Conv_U1) return new Bounds(0, byte.MaxValue);
				if (op == OpCodes.Conv_I2) return new Bounds(short.MinValue, short.MaxValue);
				if (op == OpCodes.Conv_U2) return new Bounds(0, ushort.MaxValue);
				if (op == OpCodes.Conv_I4 || op == OpCodes.Conv_U4 ||
					op == OpCodes.Conv_I || op == OpCodes.Conv_U)
					return Get(converted);
			}
			if (instruction.Operation == M68kMachineOperation.Negate &&
				instruction.Uses is [var negated] && Get(negated) is { } argument &&
				argument.Minimum > int.MinValue && argument.Maximum <= int.MaxValue)
				return new Bounds(-argument.Maximum, -argument.Minimum);

			if (instruction.Operation == M68kMachineOperation.Shift &&
				instruction.Uses.Length != 0)
			{
				var count = instruction.Immediate;
				if (count is null && instruction.Uses.Length == 2 &&
					TryGetConstant(instruction.Uses[1], out var constantCount))
					count = constantCount;
				if (count is { } shift && (shift & 31) is > 0 and var masked &&
					instruction.SourceInstruction?.OpCode == OpCodes.Shr_Un)
					return new Bounds(0, uint.MaxValue >> masked);
			}
			if (instruction.Uses is [var left, var right])
			{
				if (instruction.Operation == M68kMachineOperation.And)
				{
					if (TryGetConstant(right, out var mask) && mask >= 0 ||
						TryGetConstant(left, out mask) && mask >= 0)
						return new Bounds(0, mask);
				}
				if (instruction.Operation is M68kMachineOperation.Divide or M68kMachineOperation.Remainder &&
					TryGetConstant(right, out var divisor) && divisor != 0)
				{
					var remainder = instruction.Operation == M68kMachineOperation.Remainder;
					if (instruction.Definitions.Length == 2 && instruction.Definitions[1] == value)
						remainder = !remainder;
					var unsigned = instruction.SourceInstruction?.OpCode == OpCodes.Div_Un ||
						instruction.SourceInstruction?.OpCode == OpCodes.Rem_Un;
					if (remainder)
					{
						var magnitude = unsigned ? (long)unchecked((uint)divisor) : Math.Abs((long)divisor);
						if (magnitude - 1 > int.MaxValue) return null;
						var minimum = unsigned || Get(left) is { Minimum: >= 0 } ? 0 : 1 - magnitude;
						return new Bounds(minimum, magnitude - 1);
					}
					if (!unsigned && Get(left) is { } dividend &&
						!(dividend.Minimum <= int.MinValue && divisor == -1))
					{
						var a = dividend.Minimum / divisor;
						var b = dividend.Maximum / divisor;
						return new Bounds(Math.Min(a, b), Math.Max(a, b));
					}
				}
				if (Get(left) is { } lhs && Get(right) is { } rhs)
				{
					long minimum;
					long maximum;
					if (instruction.Operation == M68kMachineOperation.Add)
					{
						minimum = lhs.Minimum + rhs.Minimum;
						maximum = lhs.Maximum + rhs.Maximum;
					}
					else if (instruction.Operation == M68kMachineOperation.Subtract)
					{
						minimum = lhs.Minimum - rhs.Maximum;
						maximum = lhs.Maximum - rhs.Minimum;
					}
					else return null;
					if (minimum >= int.MinValue && maximum <= int.MaxValue)
						return new Bounds(minimum, maximum);
				}
			}
		}
		if (NarrowBounds(machineValue.Kind) is { } typed) return typed;
		if (_phis.TryGetValue(value, out var phi))
		{
			var inputs = phi.Inputs.Values.Select(Get).ToArray();
			if (inputs.Length != 0 && inputs.All(static input => input.HasValue))
				return new Bounds(inputs.Min(static input => input!.Value.Minimum),
					inputs.Max(static input => input!.Value.Maximum));
		}
		return null;
	}

	private static Bounds? NarrowBounds(CilStackValueKind kind) => kind switch
	{
		CilStackValueKind.BooleanByte or CilStackValueKind.UnsignedByte => new Bounds(0, byte.MaxValue),
		CilStackValueKind.SignedByte => new Bounds(sbyte.MinValue, sbyte.MaxValue),
		CilStackValueKind.UnsignedWord => new Bounds(0, ushort.MaxValue),
		CilStackValueKind.SignedWord => new Bounds(short.MinValue, short.MaxValue),
		_ => null
	};
}
