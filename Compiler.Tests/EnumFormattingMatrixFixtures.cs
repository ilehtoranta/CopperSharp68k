/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private enum HandlerSignedByte : sbyte { Minimum = -128, Zero = 0, Read = 1, Write = 2, Maximum = 127 }

	private static int CheckHandlerSignedByte()
	{
		var values = new HandlerSignedByte[6];
		values[0] = HandlerSignedByte.Zero;
		values[1] = HandlerSignedByte.Read;
		values[2] = (HandlerSignedByte)3;
		values[3] = HandlerSignedByte.Maximum;
		values[4] = (HandlerSignedByte)(-1);
		values[5] = HandlerSignedByte.Minimum;
		var formats = new string?[] { null, "g", "G", "d", "D", "x", "X", "f", "F" };
		var expected = new string[] { "Zero", "Zero", "Zero", "0", "0", "00", "00", "Zero", "Zero", "Read", "Read", "Read", "1", "1", "01", "01", "Read", "Read", "3", "3", "3", "3", "3", "03", "03", "Read, Write", "Read, Write", "Maximum", "Maximum", "Maximum", "127", "127", "7F", "7F", "Maximum", "Maximum", "-1", "-1", "-1", "-1", "-1", "FF", "FF", "Maximum, Minimum", "Maximum, Minimum", "Minimum", "Minimum", "Minimum", "-128", "-128", "80", "80", "Minimum", "Minimum" };
		for (var valueIndex = 0; valueIndex < values.Length; valueIndex++)
		for (var formatIndex = 0; formatIndex < formats.Length; formatIndex++)
		for (var boxed = 0; boxed < 2; boxed++)
		{
			var builder = new StringBuilder(valueIndex % 2 == 0 ? 1 : 64);
			var handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, builder);
			handler.AppendLiteral("[");
			var alignment = formatIndex % 3 == 0 ? 0 : formatIndex % 3 == 1 ? -24 : 24;
			if (boxed == 0) handler.AppendFormatted(values[valueIndex], alignment, formats[formatIndex]);
			else handler.AppendFormatted((object)values[valueIndex], alignment, formats[formatIndex]);
			handler.AppendLiteral("]");
			builder.Append(ref handler);
			System.GC.Collect();
			var text = builder.ToString(); var name = expected[valueIndex * formats.Length + formatIndex];
			var padding = alignment == 0 || name.Length >= 24 ? 0 : 24 - name.Length;
			if (text.Length != name.Length + padding + 2 || text[0] != '[' || text[text.Length - 1] != ']') return 1;
			var start = alignment > 0 ? padding + 1 : 1;
			for (var i = 0; i < name.Length; i++) if (text[start + i] != name[i]) return 2;
			for (var i = 0; i < padding; i++) if (text[alignment > 0 ? i + 1 : name.Length + i + 1] != ' ') return 3;
		}
		return 42;
	}
	[Flags] private enum HandlerUnsignedByte : byte { Zero = 0, Read = 1, Write = 2, Maximum = 255 }

	private static int CheckHandlerUnsignedByte()
	{
		var values = new HandlerUnsignedByte[5];
		values[0] = HandlerUnsignedByte.Zero;
		values[1] = HandlerUnsignedByte.Read;
		values[2] = (HandlerUnsignedByte)3;
		values[3] = HandlerUnsignedByte.Maximum;
		values[4] = (HandlerUnsignedByte)(254);
		var formats = new string?[] { null, "g", "G", "d", "D", "x", "X", "f", "F" };
		var expected = new string[] { "Zero", "Zero", "Zero", "0", "0", "00", "00", "Zero", "Zero", "Read", "Read", "Read", "1", "1", "01", "01", "Read", "Read", "Read, Write", "Read, Write", "Read, Write", "3", "3", "03", "03", "Read, Write", "Read, Write", "Maximum", "Maximum", "Maximum", "255", "255", "FF", "FF", "Maximum", "Maximum", "254", "254", "254", "254", "254", "FE", "FE", "254", "254" };
		for (var valueIndex = 0; valueIndex < values.Length; valueIndex++)
		for (var formatIndex = 0; formatIndex < formats.Length; formatIndex++)
		for (var boxed = 0; boxed < 2; boxed++)
		{
			var builder = new StringBuilder(valueIndex % 2 == 0 ? 1 : 64);
			var handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, builder);
			handler.AppendLiteral("[");
			var alignment = formatIndex % 3 == 0 ? 0 : formatIndex % 3 == 1 ? -24 : 24;
			if (boxed == 0) handler.AppendFormatted(values[valueIndex], alignment, formats[formatIndex]);
			else handler.AppendFormatted((object)values[valueIndex], alignment, formats[formatIndex]);
			handler.AppendLiteral("]");
			builder.Append(ref handler);
			System.GC.Collect();
			var text = builder.ToString(); var name = expected[valueIndex * formats.Length + formatIndex];
			var padding = alignment == 0 || name.Length >= 24 ? 0 : 24 - name.Length;
			if (text.Length != name.Length + padding + 2 || text[0] != '[' || text[text.Length - 1] != ']') return 1;
			var start = alignment > 0 ? padding + 1 : 1;
			for (var i = 0; i < name.Length; i++) if (text[start + i] != name[i]) return 2;
			for (var i = 0; i < padding; i++) if (text[alignment > 0 ? i + 1 : name.Length + i + 1] != ' ') return 3;
		}
		return 42;
	}
	private enum HandlerSignedShort : short { Minimum = -32768, Zero = 0, Read = 1, Write = 2, Maximum = 32767 }

	private static int CheckHandlerSignedShort()
	{
		var values = new HandlerSignedShort[6];
		values[0] = HandlerSignedShort.Zero;
		values[1] = HandlerSignedShort.Read;
		values[2] = (HandlerSignedShort)3;
		values[3] = HandlerSignedShort.Maximum;
		values[4] = (HandlerSignedShort)(-1);
		values[5] = HandlerSignedShort.Minimum;
		var formats = new string?[] { null, "g", "G", "d", "D", "x", "X", "f", "F" };
		var expected = new string[] { "Zero", "Zero", "Zero", "0", "0", "0000", "0000", "Zero", "Zero", "Read", "Read", "Read", "1", "1", "0001", "0001", "Read", "Read", "3", "3", "3", "3", "3", "0003", "0003", "Read, Write", "Read, Write", "Maximum", "Maximum", "Maximum", "32767", "32767", "7FFF", "7FFF", "Maximum", "Maximum", "-1", "-1", "-1", "-1", "-1", "FFFF", "FFFF", "Maximum, Minimum", "Maximum, Minimum", "Minimum", "Minimum", "Minimum", "-32768", "-32768", "8000", "8000", "Minimum", "Minimum" };
		for (var valueIndex = 0; valueIndex < values.Length; valueIndex++)
		for (var formatIndex = 0; formatIndex < formats.Length; formatIndex++)
		for (var boxed = 0; boxed < 2; boxed++)
		{
			var builder = new StringBuilder(valueIndex % 2 == 0 ? 1 : 64);
			var handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, builder);
			handler.AppendLiteral("[");
			var alignment = formatIndex % 3 == 0 ? 0 : formatIndex % 3 == 1 ? -24 : 24;
			if (boxed == 0) handler.AppendFormatted(values[valueIndex], alignment, formats[formatIndex]);
			else handler.AppendFormatted((object)values[valueIndex], alignment, formats[formatIndex]);
			handler.AppendLiteral("]");
			builder.Append(ref handler);
			System.GC.Collect();
			var text = builder.ToString(); var name = expected[valueIndex * formats.Length + formatIndex];
			var padding = alignment == 0 || name.Length >= 24 ? 0 : 24 - name.Length;
			if (text.Length != name.Length + padding + 2 || text[0] != '[' || text[text.Length - 1] != ']') return 1;
			var start = alignment > 0 ? padding + 1 : 1;
			for (var i = 0; i < name.Length; i++) if (text[start + i] != name[i]) return 2;
			for (var i = 0; i < padding; i++) if (text[alignment > 0 ? i + 1 : name.Length + i + 1] != ' ') return 3;
		}
		return 42;
	}
	[Flags] private enum HandlerUnsignedShort : ushort { Zero = 0, Read = 1, Write = 2, Maximum = 65535 }

	private static int CheckHandlerUnsignedShort()
	{
		var values = new HandlerUnsignedShort[5];
		values[0] = HandlerUnsignedShort.Zero;
		values[1] = HandlerUnsignedShort.Read;
		values[2] = (HandlerUnsignedShort)3;
		values[3] = HandlerUnsignedShort.Maximum;
		values[4] = (HandlerUnsignedShort)(65534);
		var formats = new string?[] { null, "g", "G", "d", "D", "x", "X", "f", "F" };
		var expected = new string[] { "Zero", "Zero", "Zero", "0", "0", "0000", "0000", "Zero", "Zero", "Read", "Read", "Read", "1", "1", "0001", "0001", "Read", "Read", "Read, Write", "Read, Write", "Read, Write", "3", "3", "0003", "0003", "Read, Write", "Read, Write", "Maximum", "Maximum", "Maximum", "65535", "65535", "FFFF", "FFFF", "Maximum", "Maximum", "65534", "65534", "65534", "65534", "65534", "FFFE", "FFFE", "65534", "65534" };
		for (var valueIndex = 0; valueIndex < values.Length; valueIndex++)
		for (var formatIndex = 0; formatIndex < formats.Length; formatIndex++)
		for (var boxed = 0; boxed < 2; boxed++)
		{
			var builder = new StringBuilder(valueIndex % 2 == 0 ? 1 : 64);
			var handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, builder);
			handler.AppendLiteral("[");
			var alignment = formatIndex % 3 == 0 ? 0 : formatIndex % 3 == 1 ? -24 : 24;
			if (boxed == 0) handler.AppendFormatted(values[valueIndex], alignment, formats[formatIndex]);
			else handler.AppendFormatted((object)values[valueIndex], alignment, formats[formatIndex]);
			handler.AppendLiteral("]");
			builder.Append(ref handler);
			System.GC.Collect();
			var text = builder.ToString(); var name = expected[valueIndex * formats.Length + formatIndex];
			var padding = alignment == 0 || name.Length >= 24 ? 0 : 24 - name.Length;
			if (text.Length != name.Length + padding + 2 || text[0] != '[' || text[text.Length - 1] != ']') return 1;
			var start = alignment > 0 ? padding + 1 : 1;
			for (var i = 0; i < name.Length; i++) if (text[start + i] != name[i]) return 2;
			for (var i = 0; i < padding; i++) if (text[alignment > 0 ? i + 1 : name.Length + i + 1] != ' ') return 3;
		}
		return 42;
	}
	private enum HandlerSignedInt : int { Minimum = -2147483648, Zero = 0, Read = 1, Write = 2, Maximum = 2147483647 }

	private static int CheckHandlerSignedInt()
	{
		var values = new HandlerSignedInt[6];
		values[0] = HandlerSignedInt.Zero;
		values[1] = HandlerSignedInt.Read;
		values[2] = (HandlerSignedInt)3;
		values[3] = HandlerSignedInt.Maximum;
		values[4] = (HandlerSignedInt)(-1);
		values[5] = HandlerSignedInt.Minimum;
		var formats = new string?[] { null, "g", "G", "d", "D", "x", "X", "f", "F" };
		var expected = new string[] { "Zero", "Zero", "Zero", "0", "0", "00000000", "00000000", "Zero", "Zero", "Read", "Read", "Read", "1", "1", "00000001", "00000001", "Read", "Read", "3", "3", "3", "3", "3", "00000003", "00000003", "Read, Write", "Read, Write", "Maximum", "Maximum", "Maximum", "2147483647", "2147483647", "7FFFFFFF", "7FFFFFFF", "Maximum", "Maximum", "-1", "-1", "-1", "-1", "-1", "FFFFFFFF", "FFFFFFFF", "Maximum, Minimum", "Maximum, Minimum", "Minimum", "Minimum", "Minimum", "-2147483648", "-2147483648", "80000000", "80000000", "Minimum", "Minimum" };
		for (var valueIndex = 0; valueIndex < values.Length; valueIndex++)
		for (var formatIndex = 0; formatIndex < formats.Length; formatIndex++)
		for (var boxed = 0; boxed < 2; boxed++)
		{
			var builder = new StringBuilder(valueIndex % 2 == 0 ? 1 : 64);
			var handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, builder);
			handler.AppendLiteral("[");
			var alignment = formatIndex % 3 == 0 ? 0 : formatIndex % 3 == 1 ? -24 : 24;
			if (boxed == 0) handler.AppendFormatted(values[valueIndex], alignment, formats[formatIndex]);
			else handler.AppendFormatted((object)values[valueIndex], alignment, formats[formatIndex]);
			handler.AppendLiteral("]");
			builder.Append(ref handler);
			System.GC.Collect();
			var text = builder.ToString(); var name = expected[valueIndex * formats.Length + formatIndex];
			var padding = alignment == 0 || name.Length >= 24 ? 0 : 24 - name.Length;
			if (text.Length != name.Length + padding + 2 || text[0] != '[' || text[text.Length - 1] != ']') return 1;
			var start = alignment > 0 ? padding + 1 : 1;
			for (var i = 0; i < name.Length; i++) if (text[start + i] != name[i]) return 2;
			for (var i = 0; i < padding; i++) if (text[alignment > 0 ? i + 1 : name.Length + i + 1] != ' ') return 3;
		}
		return 42;
	}
	[Flags] private enum HandlerUnsignedInt : uint { Zero = 0, Read = 1, Write = 2, Maximum = 4294967295 }

	private static int CheckHandlerUnsignedInt()
	{
		var values = new HandlerUnsignedInt[5];
		values[0] = HandlerUnsignedInt.Zero;
		values[1] = HandlerUnsignedInt.Read;
		values[2] = (HandlerUnsignedInt)3;
		values[3] = HandlerUnsignedInt.Maximum;
		values[4] = (HandlerUnsignedInt)(4294967294);
		var formats = new string?[] { null, "g", "G", "d", "D", "x", "X", "f", "F" };
		var expected = new string[] { "Zero", "Zero", "Zero", "0", "0", "00000000", "00000000", "Zero", "Zero", "Read", "Read", "Read", "1", "1", "00000001", "00000001", "Read", "Read", "Read, Write", "Read, Write", "Read, Write", "3", "3", "00000003", "00000003", "Read, Write", "Read, Write", "Maximum", "Maximum", "Maximum", "4294967295", "4294967295", "FFFFFFFF", "FFFFFFFF", "Maximum", "Maximum", "4294967294", "4294967294", "4294967294", "4294967294", "4294967294", "FFFFFFFE", "FFFFFFFE", "4294967294", "4294967294" };
		for (var valueIndex = 0; valueIndex < values.Length; valueIndex++)
		for (var formatIndex = 0; formatIndex < formats.Length; formatIndex++)
		for (var boxed = 0; boxed < 2; boxed++)
		{
			var builder = new StringBuilder(valueIndex % 2 == 0 ? 1 : 64);
			var handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, builder);
			handler.AppendLiteral("[");
			var alignment = formatIndex % 3 == 0 ? 0 : formatIndex % 3 == 1 ? -24 : 24;
			if (boxed == 0) handler.AppendFormatted(values[valueIndex], alignment, formats[formatIndex]);
			else handler.AppendFormatted((object)values[valueIndex], alignment, formats[formatIndex]);
			handler.AppendLiteral("]");
			builder.Append(ref handler);
			System.GC.Collect();
			var text = builder.ToString(); var name = expected[valueIndex * formats.Length + formatIndex];
			var padding = alignment == 0 || name.Length >= 24 ? 0 : 24 - name.Length;
			if (text.Length != name.Length + padding + 2 || text[0] != '[' || text[text.Length - 1] != ']') return 1;
			var start = alignment > 0 ? padding + 1 : 1;
			for (var i = 0; i < name.Length; i++) if (text[start + i] != name[i]) return 2;
			for (var i = 0; i < padding; i++) if (text[alignment > 0 ? i + 1 : name.Length + i + 1] != ' ') return 3;
		}
		return 42;
	}
	private enum HandlerSignedLong : long { Minimum = -9223372036854775808, Zero = 0, Read = 1, Write = 2, Maximum = 9223372036854775807 }

	private static int CheckHandlerSignedLong()
	{
		var values = new HandlerSignedLong[6];
		values[0] = HandlerSignedLong.Zero;
		values[1] = HandlerSignedLong.Read;
		values[2] = (HandlerSignedLong)3;
		values[3] = HandlerSignedLong.Maximum;
		values[4] = (HandlerSignedLong)(-1);
		values[5] = HandlerSignedLong.Minimum;
		var formats = new string?[] { null, "g", "G", "d", "D", "x", "X", "f", "F" };
		var expected = new string[] { "Zero", "Zero", "Zero", "0", "0", "0000000000000000", "0000000000000000", "Zero", "Zero", "Read", "Read", "Read", "1", "1", "0000000000000001", "0000000000000001", "Read", "Read", "3", "3", "3", "3", "3", "0000000000000003", "0000000000000003", "Read, Write", "Read, Write", "Maximum", "Maximum", "Maximum", "9223372036854775807", "9223372036854775807", "7FFFFFFFFFFFFFFF", "7FFFFFFFFFFFFFFF", "Maximum", "Maximum", "-1", "-1", "-1", "-1", "-1", "FFFFFFFFFFFFFFFF", "FFFFFFFFFFFFFFFF", "Maximum, Minimum", "Maximum, Minimum", "Minimum", "Minimum", "Minimum", "-9223372036854775808", "-9223372036854775808", "8000000000000000", "8000000000000000", "Minimum", "Minimum" };
		for (var valueIndex = 0; valueIndex < values.Length; valueIndex++)
		for (var formatIndex = 0; formatIndex < formats.Length; formatIndex++)
		for (var boxed = 0; boxed < 2; boxed++)
		{
			var builder = new StringBuilder(valueIndex % 2 == 0 ? 1 : 64);
			var handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, builder);
			handler.AppendLiteral("[");
			var alignment = formatIndex % 3 == 0 ? 0 : formatIndex % 3 == 1 ? -24 : 24;
			if (boxed == 0) handler.AppendFormatted(values[valueIndex], alignment, formats[formatIndex]);
			else handler.AppendFormatted((object)values[valueIndex], alignment, formats[formatIndex]);
			handler.AppendLiteral("]");
			builder.Append(ref handler);
			System.GC.Collect();
			var text = builder.ToString(); var name = expected[valueIndex * formats.Length + formatIndex];
			var padding = alignment == 0 || name.Length >= 24 ? 0 : 24 - name.Length;
			if (text.Length != name.Length + padding + 2 || text[0] != '[' || text[text.Length - 1] != ']') return 1;
			var start = alignment > 0 ? padding + 1 : 1;
			for (var i = 0; i < name.Length; i++) if (text[start + i] != name[i]) return 2;
			for (var i = 0; i < padding; i++) if (text[alignment > 0 ? i + 1 : name.Length + i + 1] != ' ') return 3;
		}
		return 42;
	}
	[Flags] private enum HandlerUnsignedLong : ulong { Zero = 0, Read = 1, Write = 2, Maximum = 18446744073709551615 }

	private static int CheckHandlerUnsignedLong()
	{
		var values = new HandlerUnsignedLong[5];
		values[0] = HandlerUnsignedLong.Zero;
		values[1] = HandlerUnsignedLong.Read;
		values[2] = (HandlerUnsignedLong)3;
		values[3] = HandlerUnsignedLong.Maximum;
		values[4] = (HandlerUnsignedLong)(18446744073709551614);
		var formats = new string?[] { null, "g", "G", "d", "D", "x", "X", "f", "F" };
		var expected = new string[] { "Zero", "Zero", "Zero", "0", "0", "0000000000000000", "0000000000000000", "Zero", "Zero", "Read", "Read", "Read", "1", "1", "0000000000000001", "0000000000000001", "Read", "Read", "Read, Write", "Read, Write", "Read, Write", "3", "3", "0000000000000003", "0000000000000003", "Read, Write", "Read, Write", "Maximum", "Maximum", "Maximum", "18446744073709551615", "18446744073709551615", "FFFFFFFFFFFFFFFF", "FFFFFFFFFFFFFFFF", "Maximum", "Maximum", "18446744073709551614", "18446744073709551614", "18446744073709551614", "18446744073709551614", "18446744073709551614", "FFFFFFFFFFFFFFFE", "FFFFFFFFFFFFFFFE", "18446744073709551614", "18446744073709551614" };
		for (var valueIndex = 0; valueIndex < values.Length; valueIndex++)
		for (var formatIndex = 0; formatIndex < formats.Length; formatIndex++)
		for (var boxed = 0; boxed < 2; boxed++)
		{
			var builder = new StringBuilder(valueIndex % 2 == 0 ? 1 : 64);
			var handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, builder);
			handler.AppendLiteral("[");
			var alignment = formatIndex % 3 == 0 ? 0 : formatIndex % 3 == 1 ? -24 : 24;
			if (boxed == 0) handler.AppendFormatted(values[valueIndex], alignment, formats[formatIndex]);
			else handler.AppendFormatted((object)values[valueIndex], alignment, formats[formatIndex]);
			handler.AppendLiteral("]");
			builder.Append(ref handler);
			System.GC.Collect();
			var text = builder.ToString(); var name = expected[valueIndex * formats.Length + formatIndex];
			var padding = alignment == 0 || name.Length >= 24 ? 0 : 24 - name.Length;
			if (text.Length != name.Length + padding + 2 || text[0] != '[' || text[text.Length - 1] != ']') return 1;
			var start = alignment > 0 ? padding + 1 : 1;
			for (var i = 0; i < name.Length; i++) if (text[start + i] != name[i]) return 2;
			for (var i = 0; i < padding; i++) if (text[alignment > 0 ? i + 1 : name.Length + i + 1] != ' ') return 3;
		}
		return 42;
	}

	public static int CoreLibStringBuilderEnumMatrixEntry()
	{
		if (CheckHandlerSignedByte() != 42) return 1;
		if (CheckHandlerUnsignedByte() != 42) return 2;
		if (CheckHandlerSignedShort() != 42) return 3;
		if (CheckHandlerUnsignedShort() != 42) return 4;
		if (CheckHandlerSignedInt() != 42) return 5;
		if (CheckHandlerUnsignedInt() != 42) return 6;
		if (CheckHandlerSignedLong() != 42) return 7;
		if (CheckHandlerUnsignedLong() != 42) return 8;
		return 42;
	}
	public static int CoreLibStringBuilderEnumSignedByteEntry() => CheckHandlerSignedByte();
	public static int CoreLibStringBuilderEnumUnsignedByteEntry() => CheckHandlerUnsignedByte();
	public static int CoreLibStringBuilderEnumSignedShortEntry() => CheckHandlerSignedShort();
	public static int CoreLibStringBuilderEnumUnsignedShortEntry() => CheckHandlerUnsignedShort();
	public static int CoreLibStringBuilderEnumSignedIntEntry() => CheckHandlerSignedInt();
	public static int CoreLibStringBuilderEnumUnsignedIntEntry() => CheckHandlerUnsignedInt();
	public static int CoreLibStringBuilderEnumSignedLongEntry() => CheckHandlerSignedLong();
	public static int CoreLibStringBuilderEnumUnsignedLongEntry() => CheckHandlerUnsignedLong();
}
