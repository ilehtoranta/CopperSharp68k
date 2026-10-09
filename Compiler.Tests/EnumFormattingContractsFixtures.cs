/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private enum HandlerAliases : uint { ZeroA = 0, ZeroB = 0, OneA = 1, OneB = 1, Two = 2 }
	private enum HandlerLargeNames : uint { E00 = 0, E01 = 1, E02 = 2, E03 = 3, E04 = 4, E05 = 5, E06 = 6, E07 = 7, E08 = 8, E09 = 9, E10 = 10, E11 = 11, E12 = 12, E13 = 13, E14 = 14, E15 = 15, E16 = 16, E17 = 17, E18 = 18, E19 = 19, E20 = 20, E21 = 21, E22 = 22, E23 = 23, E24 = 24, E25 = 25, E26 = 26, E27 = 27, E28 = 28, E29 = 29, E30 = 30, E31 = 31, E32 = 32, E33 = 33, E34 = 34, E35 = 35, E36 = 36, E37 = 37, E38 = 38, E39 = 39 }
	private enum HandlerEmpty : int { }
	[Flags] private enum HandlerHighFlags : ulong { Read = 1, High = 9223372036854775808 }
	private enum HandlerLongName : byte { LongNameNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNN = 1 }

	private sealed class HandlerEnumProvider : IFormatProvider, ICustomFormatter
	{
		public bool Custom;
		public int Queries;
		public int Calls;
		public object? GetFormat(Type? type)
		{
			Queries++;
			if (type != typeof(ICustomFormatter)) throw new InvalidOperationException();
			return Custom ? this : null;
		}
		public string Format(string? format, object? value, IFormatProvider? provider)
		{
			System.GC.Collect(); Calls++;
			if (value is not HandlerSignedLong code || code != HandlerSignedLong.Minimum || provider != this || format != "x") throw new InvalidOperationException();
			return "CUSTOM";
		}
	}

	public static int CoreLibStringBuilderEnumContractsEntry()
	{
		var storage = new char[32];
		for (var test = 0; test < 4; test++)
		{
			var value = (HandlerAliases)(test < 2 ? 1 : test == 2 ? 0 : 3);
			var format = test == 0 ? "G" : "F";
			var expected = test == 0 ? "OneA" : test == 1 ? "OneB" : test == 2 ? "ZeroA" : "OneB, Two";
			for (var length = 0; length <= expected.Length; length++)
			{
				for (var i = 0; i < storage.Length; i++) storage[i] = '~';
				var success = Enum.TryFormat(value, storage.AsSpan(1, length), out var written, format.AsSpan());
				System.GC.Collect();
				if (success != (length == expected.Length) || written != (success ? expected.Length : 0)) return 1;
				for (var i = 0; i < storage.Length; i++)
					if (storage[i] != (success && i >= 1 && i <= written ? expected[i - 1] : '~')) return 2;
			}
		}
		if (!Enum.TryFormat(HandlerLargeNames.E37, storage, out var count, "g")) return 30;
		if (count != 3) return 300 + count;
		if (storage[0] != 'E') return 400 + storage[0];
		if (storage[1] != '3') return 500 + storage[1];
		if (storage[2] != '7') return 600 + storage[2];
		if (!Enum.TryFormat((HandlerEmpty)(-42), storage, out count) || count != 3 || storage[0] != '-' || storage[1] != '4' || storage[2] != '2') return 4;
		if (!Enum.TryFormat(HandlerHighFlags.Read | HandlerHighFlags.High, storage, out count, "f") || count != 10 || new string(storage.AsSpan(0, count)) != "Read, High") return 5;
		if (!Enum.TryFormat(HandlerSignedLong.Minimum, storage, out count, "d") || new string(storage.AsSpan(0, count)) != "-9223372036854775808") return 6;
		foreach (var format in new[] { "Q", "D2", "XX" })
		{
			for (var i = 0; i < storage.Length; i++) storage[i] = '~';
			try { Enum.TryFormat(HandlerAliases.OneA, storage, out count, format); return 7; } catch (FormatException) { }
			for (var i = 0; i < storage.Length; i++) if (storage[i] != '~') return 8;
		}
		object boxed = HandlerHighFlags.Read | HandlerHighFlags.High;
		System.GC.Collect();
		if (boxed is not Enum) return 90;
		if (boxed is not IFormattable) return 91;
		if (boxed is not ISpanFormattable) return 92;
		if (boxed.ToString() != "Read, High") return 93;
		if (!((ISpanFormattable)boxed).TryFormat(storage, out count, "x", null) || new string(storage.AsSpan(0, count)) != "8000000000000001") return 10;
		if (((IFormattable)boxed).ToString("D", null) != "9223372036854775809") return 11;
		var expectedName = "LongNameNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNN";
		var builder = new StringBuilder(1);
		var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder);
		handler.AppendFormatted(HandlerLongName.LongNameNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNNN, -610, "G");
		System.GC.Collect();
		var text = builder.ToString();
		if (text.Length != 610) return 12;
		for (var i = 0; i < expectedName.Length; i++) if (text[i] != expectedName[i]) return 13;
		if (text[608] != ' ' || text[609] != ' ') return 14;
		builder.Clear();
		var provider = new HandlerEnumProvider();
		handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder, provider);
		handler.AppendFormatted(HandlerSignedLong.Minimum, "D");
		handler.AppendFormatted((object)HandlerSignedLong.Minimum, "D");
		if (builder.ToString() != "-9223372036854775808-9223372036854775808" || provider.Queries != 1 || provider.Calls != 0) return 15;
		builder.Clear(); provider = new HandlerEnumProvider { Custom = true };
		handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder, provider);
		handler.AppendFormatted(HandlerSignedLong.Minimum, -8, "x");
		if (builder.ToString() != "CUSTOM  " || provider.Queries != 2 || provider.Calls != 1) return 16;
		handler.AppendLiteral("!");
		return builder.ToString() == "CUSTOM  !" ? 42 : 17;
	}
}
