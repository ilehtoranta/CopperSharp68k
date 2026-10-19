/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.CompilerServices;
using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

/// <summary>Read-only enum constants supplied by verified compilation metadata.</summary>
public sealed class ShadowEnumMetadata
{
#pragma warning disable CS0649
	public int Width;
	public int Signed;
	public int Flags;
	public ulong[] Values = null!;
	public string[] Names = null!;
#pragma warning restore CS0649
}

public static class ShadowEnumFormatting
{
	// Only exact constructed calls in this formatter are replaced by the
	// compiler. No host reflection or target initialization is required.
	private static uint Low<T>(T value) => throw new NotSupportedException();
	private static uint High<T>(T value) => throw new NotSupportedException();
	private static ShadowEnumMetadata Data<T>() => throw new NotSupportedException();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool TryFormat<T>(T value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
	{
		var data = Data<T>();
		var bits = (ulong)M68kRuntime.CombineInt64(High(value), Low(value));
		return TryFormatBits(bits, data, destination, out charsWritten, format);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static bool TryFormatBits(ulong rawBits, ShadowEnumMetadata data, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
	{
		charsWritten = 0;
		var specifier = format.Length == 0 ? 'g' : (char)(format[0] | 0x20);
		if (format.Length > 1 || specifier is not ('g' or 'd' or 'x' or 'f')) throw new FormatException();
		var low = M68kRuntime.SplitUInt64(rawBits, out var high);
		if (data.Width < 4) low &= (1u << (data.Width * 8)) - 1;
		if (data.Width < 8) high = 0;
		var bits = (ulong)M68kRuntime.CombineInt64(high, low);
		if (specifier == 'x')
		{
			var length = data.Width * 2;
			if (destination.Length < length) return false;
			for (var i = length - 1; i >= 0; i--)
			{
				var digit = (int)((length == 16 && i < 8 ? high : low) & 15);
				destination[i] = (char)(digit < 10 ? '0' + digit : 'A' + digit - 10);
				if (length == 16 && i < 8) high >>= 4;
				else low >>= 4;
			}
			charsWritten = length;
			return true;
		}
		if (specifier is 'g' or 'f')
		{
			if (specifier == 'f' || data.Flags != 0)
			{
				if (TryWriteFlags(bits, data, destination, out charsWritten, out var found)) return true;
				if (found) return false; // Names exist, but the destination is short.
			}
			else
			{
				var index = FindName(bits, data.Values);
				if (index >= 0) return TryWriteName(data.Names[index], destination, out charsWritten);
			}
		}
		if (data.Signed != 0)
		{
			if (data.Width == 1) low = unchecked((uint)(sbyte)low);
			else if (data.Width == 2) low = unchecked((uint)(short)low);
			if (data.Width < 8) high = (int)low < 0 ? uint.MaxValue : 0;
		}
		var lengthWritten = data.Signed != 0
			? ShadowIntegerFormatter.PackInt64(high, low, out var word0, out var word1, out var word2, out var word3, out var word4)
			: ShadowIntegerFormatter.PackUInt64(high, low, out word0, out word1, out word2, out word3, out word4);
		return ShadowNumberFormatting.TryWritePackedInteger(destination, lengthWritten, word0, word1, word2, word3, word4, out charsWritten);
	}

	internal static string FormatBits(ulong bits, ShadowEnumMetadata data, string? format)
	{
		Span<char> initial = stackalloc char[256];
		if (TryFormatBits(bits, data, initial, out var written, format.AsSpan())) return new string(initial.Slice(0, written));
		var length = 256;
		while (true)
		{
			if (length > int.MaxValue / 2) throw new OutOfMemoryException();
			length *= 2;
			var buffer = new char[length];
			if (TryFormatBits(bits, data, buffer, out written, format.AsSpan())) return new string(buffer.AsSpan(0, written));
		}
	}

	private static int FindName(ulong bits, ulong[] values)
	{
		if (values.Length <= 32)
		{
			for (var i = 0; i < values.Length; i++) if (values[i] == bits) return i;
			return -1;
		}
		var low = 0; var high = values.Length - 1;
		while (low <= high)
		{
			var middle = low + ((high - low) >> 1);
			if (values[middle] == bits) return middle;
			if (values[middle] < bits) low = middle + 1;
			else high = middle - 1;
		}
		return -1;
	}

	private static bool TryWriteName(string name, Span<char> destination, out int charsWritten)
	{
		charsWritten = 0;
		if (!name.TryCopyTo(destination)) return false;
		charsWritten = name.Length;
		return true;
	}

	private static bool TryWriteFlags(ulong bits, ShadowEnumMetadata data, Span<char> destination, out int charsWritten, out bool found)
	{
		charsWritten = 0; found = true;
		var values = data.Values; var names = data.Names;
		if (bits == 0)
			return TryWriteName(values.Length != 0 && values[0] == 0 ? names[0] : "0", destination, out charsWritten);
		var index = values.Length - 1;
		while (index >= 0 && values[index] > bits) index--;
		if (index >= 0 && values[index] == bits) return TryWriteName(names[index], destination, out charsWritten);
		Span<int> selected = stackalloc int[64];
		var remainingLow = M68kRuntime.SplitUInt64(bits, out var remainingHigh); var count = 0; var length = 0;
		for (; index >= 0; index--)
		{
			var candidate = values[index];
			if (candidate == 0) break;
			var candidateLow = M68kRuntime.SplitUInt64(candidate, out var candidateHigh);
			if ((remainingLow & candidateLow) != candidateLow || (remainingHigh & candidateHigh) != candidateHigh) continue;
			remainingLow &= ~candidateLow; remainingHigh &= ~candidateHigh;
			selected[count++] = index;
			length = checked(length + names[index].Length);
			if (remainingLow == 0 && remainingHigh == 0) break;
		}
		if (remainingLow != 0 || remainingHigh != 0) { found = false; return false; }
		length = checked(length + unchecked((count - 1) * 2));
		if (destination.Length < length) return false;
		var position = 0;
		for (var i = count - 1; i >= 0; i--)
		{
			var name = names[selected[i]];
			name.AsSpan().CopyTo(destination.Slice(position)); position += name.Length;
			if (i != 0) { destination[position++] = ','; destination[position++] = ' '; }
		}
		charsWritten = length;
		return true;
	}
}

/// <summary>Receiver adapter for boxed integral enums with the target object header.</summary>
public sealed class ShadowBoxedEnum
{
#pragma warning disable CS0649
	private readonly uint _first;
	private readonly uint _second;
#pragma warning restore CS0649
	private static ShadowEnumMetadata Data(object value) => throw new NotSupportedException();
	private ulong Bits(ShadowEnumMetadata data) => data.Width == 8 ? (ulong)M68kRuntime.CombineInt64(_first, _second) :
		data.Width == 1 ? _first >> 24 : data.Width == 2 ? _first >> 16 : _first;
	public override string ToString() => ToString((string?)null);
	public string ToString(IFormatProvider? provider) => ToString((string?)null);
	public string ToString(string? format, IFormatProvider? provider) => ToString(format);
	public string ToString(string? format)
	{
		var data = Data(this);
		return ShadowEnumFormatting.FormatBits(Bits(data), data, format);
	}
	public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
	{
		var data = Data(this);
		return ShadowEnumFormatting.TryFormatBits(Bits(data), data, destination, out charsWritten, format);
	}
}
