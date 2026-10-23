/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

public unsafe partial struct ShadowGuid
{
	public string FormatDefault() => Format(null);

	public string Format(string? format)
	{
		if (format is not null && format.Length > 1) M68kRuntime.ThrowFormatException();
		var code = format is null || format.Length == 0 ? 'd' : (char)(format[0] | 0x20);
		Span<char> buffer = stackalloc char[68];
		TryFormatCore(buffer, out var written, code);
		return new string(buffer.Slice(0, written));
	}

	public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default)
	{
		if (format.Length > 1) M68kRuntime.ThrowFormatException();
		return TryFormatCore(destination, out charsWritten, format.Length == 0 ? 'd' : (char)(format[0] | 0x20));
	}

	private bool TryFormatCore(Span<char> destination, out int charsWritten, char code)
	{
		charsWritten = 0;
		var required = code switch { 'n' => 32, 'd' => 36, 'b' or 'p' => 38, 'x' => 68, _ => 0 };
		if (required == 0) M68kRuntime.ThrowFormatException();
		if (destination.Length < required) return false;
		var position = 0;
		if (code == 'x')
		{
			destination[position++] = '{';
			WritePrefix(destination, ref position);
			WriteHex(destination, ref position, (uint)_a, 8);
			destination[position++] = ',';
			WritePrefix(destination, ref position);
			WriteHex(destination, ref position, (ushort)_b, 4);
			destination[position++] = ',';
			WritePrefix(destination, ref position);
			WriteHex(destination, ref position, (ushort)_c, 4);
			destination[position++] = ',';
			destination[position++] = '{';
			for (var index = 8; index < 16; index++)
			{
				if (index > 8) destination[position++] = ',';
				WritePrefix(destination, ref position);
				WriteHex(destination, ref position, GetByte(index, true), 2);
			}
			destination[position++] = '}';
			destination[position++] = '}';
		}
		else
		{
			if (code is 'b' or 'p') destination[position++] = code == 'b' ? '{' : '(';
			for (var index = 0; index < 16; index++)
			{
				if (code != 'n' && index is 4 or 6 or 8 or 10) destination[position++] = '-';
				WriteHex(destination, ref position, GetByte(index, true), 2);
			}
			if (code is 'b' or 'p') destination[position++] = code == 'b' ? '}' : ')';
		}
		charsWritten = position;
		return true;
	}

	private static void WritePrefix(Span<char> destination, ref int position)
	{
		destination[position++] = '0';
		destination[position++] = 'x';
	}

	private static void WriteHex(Span<char> destination, ref int position, uint value, int digits)
	{
		for (var index = digits - 1; index >= 0; index--)
		{
			var digit = (int)((value >> (index * 4)) & 15);
			destination[position++] = (char)(digit < 10 ? '0' + digit : 'a' + digit - 10);
		}
	}
}
