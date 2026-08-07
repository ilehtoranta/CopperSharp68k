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

	[MethodImpl(MethodImplOptions.NoInlining)]
	public string ToString(string? format) =>
		ShadowIntegerFormatter.FormatInt32(_value, format);
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

	[MethodImpl(MethodImplOptions.NoInlining)]
	public string ToString(string? format) =>
		ShadowIntegerFormatter.FormatUInt32(_value, format);
}

/// <summary>Allocation-exact invariant decimal formatting shared by integer shadows.</summary>
public static class ShadowIntegerFormatter
{
	private const int MaximumPrecision = 999_999_999;

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

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string FormatInt32(int value, string? format)
	{
		if (format is null || format.Length == 0)
		{
			return FormatInt32(value);
		}

		var specifier = format[0];
		var precision = ParsePrecision(format);
		if (specifier is 'G' or 'g')
		{
			if (format.Length == 1 || precision == 0)
			{
				return FormatInt32(value);
			}
			M68kRuntime.ThrowFormatException();
		}
		if (specifier is 'D' or 'd')
		{
			return FormatDecimalInt32(value, precision);
		}
		if (specifier is 'X' or 'x')
		{
			return FormatHexUInt32((uint)value, precision, specifier == 'X');
		}

		M68kRuntime.ThrowFormatException();
		return "";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string FormatUInt32(uint value, string? format)
	{
		if (format is null || format.Length == 0)
		{
			return FormatUInt32(value);
		}

		var specifier = format[0];
		var precision = ParsePrecision(format);
		if (specifier is 'G' or 'g')
		{
			if (format.Length == 1 || precision == 0)
			{
				return FormatUInt32(value);
			}
			M68kRuntime.ThrowFormatException();
		}
		if (specifier is 'D' or 'd')
		{
			return FormatDecimalUInt32(value, precision);
		}
		if (specifier is 'X' or 'x')
		{
			return FormatHexUInt32(value, precision, specifier == 'X');
		}

		M68kRuntime.ThrowFormatException();
		return "";
	}

	private static int ParsePrecision(string format)
	{
		var precision = 0;
		for (var index = 1; index < format.Length; index++)
		{
			var character = format[index];
			if (character < '0' || character > '9' ||
				precision > MaximumPrecision / 10)
			{
				M68kRuntime.ThrowFormatException();
			}
			precision = precision * 10 + character - '0';
			if (precision > MaximumPrecision)
			{
				M68kRuntime.ThrowFormatException();
			}
		}
		return precision;
	}

	private static string FormatDecimalInt32(int value, int precision)
	{
		if (value >= 0)
		{
			return FormatDecimalUInt32((uint)value, precision);
		}

		var magnitude = (uint)(-(value + 1)) + 1u;
		var digits = CountDecimalDigits(magnitude);
		var paddedDigits = precision > digits ? precision : digits;
		var result = M68kRuntime.AllocateString(paddedDigits + 1);
		M68kRuntime.SetStringChar(result, 0, '-');
		WriteZeroes(result, 1, paddedDigits - digits);
		WriteDecimalValue(result, 1 + paddedDigits - digits, digits, magnitude);
		return result;
	}

	private static string FormatDecimalUInt32(uint value, int precision)
	{
		var digits = CountDecimalDigits(value);
		var paddedDigits = precision > digits ? precision : digits;
		var result = M68kRuntime.AllocateString(paddedDigits);
		WriteZeroes(result, 0, paddedDigits - digits);
		WriteDecimalValue(result, paddedDigits - digits, digits, value);
		return result;
	}

	private static string FormatHexUInt32(uint value, int precision, bool upperCase)
	{
		var digits = CountHexDigits(value);
		var paddedDigits = precision > digits ? precision : digits;
		var result = M68kRuntime.AllocateString(paddedDigits);
		WriteZeroes(result, 0, paddedDigits - digits);
		var index = paddedDigits - 1;
		var remainder = value;
		do
		{
			var digit = remainder & 15u;
			var character = digit < 10u
				? (char)('0' + digit)
				: (char)((upperCase ? 'A' : 'a') + digit - 10u);
			M68kRuntime.SetStringChar(result, index, character);
			index--;
			remainder >>= 4;
		}
		while (remainder != 0);
		return result;
	}

	private static int CountHexDigits(uint value)
	{
		if (value >= 0x1000_0000) return 8;
		if (value >= 0x0100_0000) return 7;
		if (value >= 0x0010_0000) return 6;
		if (value >= 0x0001_0000) return 5;
		if (value >= 0x0000_1000) return 4;
		if (value >= 0x0000_0100) return 3;
		if (value >= 0x0000_0010) return 2;
		return 1;
	}

	private static void WriteZeroes(string result, int start, int count)
	{
		for (var index = 0; index < count; index++)
		{
			M68kRuntime.SetStringChar(result, start + index, '0');
		}
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

/// <summary>
/// Private target body for the pinned .NET 10 interpolated-string handler.
/// Field order mirrors the public handler so its framework identity and stack
/// layout stay public while target behavior remains compact and pay-for-play.
/// </summary>
public ref struct ShadowDefaultInterpolatedStringHandler
{
#pragma warning disable CS0414 // Layout-compatibility fields for the public .NET 10 handler.
	private object? _provider;
	private char[]? _buffer;
	private uint _spanData;
	private uint _spanLength;
	private uint _spanOwner;
	private int _position;
	private bool _hasCustomFormatter;
#pragma warning restore CS0414

	[MethodImpl(MethodImplOptions.NoInlining)]
	public ShadowDefaultInterpolatedStringHandler(int literalLength, int formattedCount)
	{
		_provider = null;
		_buffer = new char[literalLength + formattedCount * 11];
		_spanData = 0;
		_spanLength = 0;
		_spanOwner = 0;
		_position = 0;
		_hasCustomFormatter = false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void AppendLiteral(string value) => AppendString(value);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void AppendFormattedInt32(int value)
	{
		if (value >= 0)
		{
			AppendDecimalUInt32((uint)value, 0);
			return;
		}

		var magnitude = (uint)(-(value + 1)) + 1u;
		var digits = CountDecimalDigits(magnitude);
		EnsureCapacity(_position + digits + 1);
		_buffer![_position++] = '-';
		WriteDecimalBackwards(magnitude, digits);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void AppendFormattedUInt32(uint value, string? format)
	{
		if (format is null || format.Length == 0)
		{
			AppendDecimalUInt32(value, 0);
			return;
		}

		var specifier = format[0];
		var precision = ParsePrecision(format);
		if (specifier is 'G' or 'g')
		{
			if (format.Length == 1 || precision == 0)
			{
				AppendDecimalUInt32(value, 0);
				return;
			}
			M68kRuntime.ThrowFormatException();
			return;
		}
		if (specifier is 'D' or 'd')
		{
			AppendDecimalUInt32(value, precision);
			return;
		}
		if (specifier is 'X' or 'x')
		{
			AppendHexUInt32(value, precision, specifier == 'X');
			return;
		}

		M68kRuntime.ThrowFormatException();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public string ToStringAndClear()
	{
		var result = M68kRuntime.AllocateString(_position);
		var buffer = _buffer!;
		for (var index = 0; index < _position; index++)
		{
			M68kRuntime.SetStringChar(result, index, buffer[index]);
		}
		_buffer = null;
		_position = 0;
		return result;
	}

	private void AppendString(string value)
	{
		EnsureCapacity(_position + value.Length);
		var buffer = _buffer!;
		for (var index = 0; index < value.Length; index++)
		{
			buffer[_position + index] = value[index];
		}
		_position += value.Length;
	}

	private void AppendDecimalUInt32(uint value, int precision)
	{
		var digits = CountDecimalDigits(value);
		var paddedDigits = precision > digits ? precision : digits;
		EnsureCapacity(_position + paddedDigits);
		var zeroes = paddedDigits - digits;
		for (var index = 0; index < zeroes; index++)
		{
			_buffer![_position++] = '0';
		}
		WriteDecimalBackwards(value, digits);
	}

	private void WriteDecimalBackwards(uint value, int digits)
	{
		var end = _position + digits;
		var remainder = value;
		for (var index = end - 1; index >= _position; index--)
		{
			var quotient = remainder / 10u;
			_buffer![index] = (char)('0' + remainder - quotient * 10u);
			remainder = quotient;
		}
		_position = end;
	}

	private void AppendHexUInt32(uint value, int precision, bool upperCase)
	{
		var digits = CountHexDigits(value);
		var paddedDigits = precision > digits ? precision : digits;
		EnsureCapacity(_position + paddedDigits);
		var zeroes = paddedDigits - digits;
		for (var index = 0; index < zeroes; index++)
		{
			_buffer![_position++] = '0';
		}
		var end = _position + digits;
		var remainder = value;
		for (var index = end - 1; index >= _position; index--)
		{
			var digit = remainder & 15u;
			_buffer![index] = digit < 10u
				? (char)('0' + digit)
				: (char)((upperCase ? 'A' : 'a') + digit - 10u);
			remainder >>= 4;
		}
		_position = end;
	}

	private static int ParsePrecision(string format)
	{
		const int maximumPrecision = 999_999_999;
		var precision = 0;
		for (var index = 1; index < format.Length; index++)
		{
			var character = format[index];
			if (character < '0' || character > '9' ||
				precision > maximumPrecision / 10)
			{
				M68kRuntime.ThrowFormatException();
			}
			precision = precision * 10 + character - '0';
			if (precision > maximumPrecision)
			{
				M68kRuntime.ThrowFormatException();
			}
		}
		return precision;
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

	private static int CountHexDigits(uint value)
	{
		if (value >= 0x1000_0000) return 8;
		if (value >= 0x0100_0000) return 7;
		if (value >= 0x0010_0000) return 6;
		if (value >= 0x0001_0000) return 5;
		if (value >= 0x0000_1000) return 4;
		if (value >= 0x0000_0100) return 3;
		if (value >= 0x0000_0010) return 2;
		return 1;
	}

	private void EnsureCapacity(int required)
	{
		var buffer = _buffer!;
		if (required <= buffer.Length)
		{
			return;
		}

		var replacement = new char[required];
		for (var index = 0; index < _position; index++)
		{
			replacement[index] = buffer[index];
		}
		_buffer = replacement;
	}
}

/// <summary>
/// Compact growable backing implementation for the admitted
/// <see cref="System.Collections.Generic.List{T}"/> contract.
/// </summary>
public sealed class ShadowList<T>
{
	private T[]? _items;
	private int _size;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public ShadowList()
	{
		_items = null;
		_size = 0;
	}

	public int Count
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get => _size;
	}

	public T this[int index]
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			if ((uint)index >= (uint)_size)
			{
				M68kRuntime.ThrowArgumentOutOfRangeException();
			}
			return _items![index];
		}
		[MethodImpl(MethodImplOptions.NoInlining)]
		set
		{
			if ((uint)index >= (uint)_size)
			{
				M68kRuntime.ThrowArgumentOutOfRangeException();
			}
			_items![index] = value;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Add(T item)
	{
		var items = _items;
		if (items is null || _size == items.Length)
		{
			Grow(items);
		}
		_items![_size] = item;
		_size++;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void Grow(T[]? items)
	{
		var capacity = items is null ? 4 : items.Length * 2;
		if (capacity < 0)
		{
			M68kRuntime.ThrowOutOfMemoryException();
		}
		var replacement = new T[capacity];
		for (var index = 0; index < _size; index++)
		{
			replacement[index] = items![index];
		}
		_items = replacement;
	}
}
