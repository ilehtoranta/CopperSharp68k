/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.CompilerServices;
using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

/// <summary>
/// Compact fallback bodies for virtual <see cref="object"/> contracts.
/// </summary>
public class ShadowObject
{
	/// <summary>Implements the static <see cref="object.Equals(object?, object?)"/> contract.</summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool EqualsObjects(object? first, object? second)
	{
		if (ReferenceEquals(first, second))
		{
			return true;
		}
		if (first is null)
		{
			return false;
		}
		if (first is Delegate firstDelegate)
		{
			return firstDelegate.Equals(second);
		}

		return first.Equals(second);
	}

	// The default Object.Equals contract is reference identity. The target
	// receiver need not actually be a ShadowObject: this body is the canonical
	// fallback for the public System.Object slot and accesses no instance fields.
	[MethodImpl(MethodImplOptions.NoInlining)]
	public override bool Equals(object? other) => this == other;

	// A constant hash is contract-correct: unequal objects may collide. User
	// overrides replace this slot through ordinary virtual dispatch.
	[MethodImpl(MethodImplOptions.NoInlining)]
	public override int GetHashCode() => 0;
}

/// <summary>Compact target implementations for supported <see cref="Math"/> members.</summary>
public static class ShadowMath
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int Abs(int value)
	{
		if (value == int.MinValue)
		{
			M68kRuntime.ThrowOverflowException();
		}

		return Identity(value < 0 ? -value : value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static T Identity<T>(T value) => value;
}

/// <summary>
/// Big-endian target implementations for supported <see cref="BitConverter"/> members.
/// </summary>
public static class ShadowBitConverter
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static byte[] GetBytes(int value)
	{
		var bytes = new byte[4];
		bytes[0] = (byte)(value >> 24);
		bytes[1] = (byte)(value >> 16);
		bytes[2] = (byte)(value >> 8);
		bytes[3] = (byte)value;
		return bytes;
	}
}

/// <summary>Target implementation for parameterless <see cref="int.ToString()"/>.</summary>
public readonly struct ShadowInt32
{
	// The public Int32 receiver and this shadow receiver are both one four-byte
	// value. Framework binding keeps the public identity while executing this body.
#pragma warning disable CS0649 // Assigned through the representation-compatible public receiver.
	private readonly int _value;
#pragma warning restore CS0649

	[MethodImpl(MethodImplOptions.NoInlining)]
	public override string ToString() =>
		ShadowIntegerFormatter.FormatInt32(_value);
}

/// <summary>Target implementation for parameterless <see cref="uint.ToString()"/>.</summary>
public readonly struct ShadowUInt32
{
#pragma warning disable CS0649 // Assigned through the representation-compatible public receiver.
	private readonly uint _value;
#pragma warning restore CS0649

	[MethodImpl(MethodImplOptions.NoInlining)]
	public override string ToString() =>
		ShadowIntegerFormatter.FormatUInt32(_value);
}

/// <summary>Allocation-exact invariant decimal formatting shared by integer shadows.</summary>
public static class ShadowIntegerFormatter
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string FormatInt32(int value)
	{
		if (value >= 0)
		{
			return FormatUInt32((uint)value);
		}

		// This form also handles Int32.MinValue without signed overflow.
		var magnitude = (uint)(-(value + 1)) + 1u;
		var digits = CountDecimalDigits(magnitude);
		var result = M68kRuntime.AllocateString(digits + 1);
		M68kRuntime.SetStringChar(result, 0, '-');
		WriteDecimalValue(result, 1, digits, magnitude);
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string FormatUInt32(uint value)
	{
		var digits = CountDecimalDigits(value);
		var result = M68kRuntime.AllocateString(digits);
		WriteDecimalValue(result, 0, digits, value);
		return result;
	}

	private static void WriteDecimalValue(
		string result,
		int start,
		int digits,
		uint value)
	{
		if (value >= 100_000_000)
		{
			var high = 0u;
			var remainder = value;
			while (remainder >= 100_000_000)
			{
				remainder -= 100_000_000;
				high++;
			}
			WriteDecimalChunk(result, start, digits - 8, high);
			var middle = remainder / 10_000;
			WriteDecimalChunk(result, start + digits - 8, 4, middle);
			WriteDecimalChunk(
				result,
				start + digits - 4,
				4,
				remainder - middle * 10_000);
		}
		else if (value >= 10_000)
		{
			var high = value / 10_000;
			WriteDecimalChunk(result, start, digits - 4, high);
			WriteDecimalChunk(
				result,
				start + digits - 4,
				4,
				value - high * 10_000);
		}
		else
		{
			WriteDecimalChunk(result, start, digits, value);
		}
	}

	private static int CountDecimalDigits(uint value)
	{
		if (value >= 1_000_000_000) return 10;
		if (value >= 100_000_000) return 9;
		if (value >= 10_000_000) return 8;
		if (value >= 1_000_000) return 7;
		if (value >= 100_000) return 6;
		if (value >= 10_000) return 5;
		if (value >= 1_000) return 4;
		if (value >= 100) return 3;
		if (value >= 10) return 2;
		return 1;
	}

	private static void WriteDecimalChunk(
		string result,
		int start,
		int digits,
		uint value)
	{
		var index = start;
		var remainder = value;
		if (digits >= 4)
		{
			remainder = WriteDecimalPlace(result, index, remainder, 1_000);
			index++;
		}
		if (digits >= 3)
		{
			remainder = WriteDecimalPlace(result, index, remainder, 100);
			index++;
		}
		if (digits >= 2)
		{
			remainder = WriteDecimalPlace(result, index, remainder, 10);
			index++;
		}
		M68kRuntime.SetStringChar(result, index, (char)('0' + remainder));
	}

	private static uint WriteDecimalPlace(
		string result,
		int index,
		uint value,
		uint place)
	{
		var digit = 0u;
		var remainder = value;
		while (remainder >= place)
		{
			remainder -= place;
			digit++;
		}
		M68kRuntime.SetStringChar(result, index, (char)('0' + digit));
		return remainder;
	}
}
