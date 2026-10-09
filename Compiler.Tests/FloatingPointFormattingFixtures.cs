/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Globalization;
using System.Text;
using System.Runtime.CompilerServices;
using CopperSharp.Compiler;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private static ReadOnlySpan<short> FloatingPointSignedTable() => [0x1234, -12345, short.MinValue, short.MaxValue];
	private static ReadOnlySpan<uint> FloatingPointWordTable() => [0x12345678, 0x89ABCDEF, 0, uint.MaxValue];
	private static ReadOnlySpan<ulong> FloatingPointWideTable() => [0x0123456789ABCDEF, 0xFEDCBA9876543210, 0, ulong.MaxValue];
	private static ReadOnlySpan<char> FloatingPointCharacterTable() => ['A', '\u03A9', '\0', '\uD800'];

	public static int CoreLibFloatingPointInitializedTablesEntry()
	{
		var small = FloatingPointSignedTable(); var words = FloatingPointWordTable();
		var wide = FloatingPointWideTable(); var characters = FloatingPointCharacterTable();
		System.GC.Collect();
		var pressure = new byte[128]; pressure[0] = 17;
		if (small.Length != 4 || small[0] != 0x1234 || small[1] != -12345 || small[2] != short.MinValue || small[3] != short.MaxValue) return 1;
		if (words.Length != 4 || words[0] != 0x12345678 || words[1] != 0x89ABCDEF || words[2] != 0 || words[3] != uint.MaxValue) return 2;
		if (wide.Length != 4 || wide[0] != 0x0123456789ABCDEF || wide[1] != 0xFEDCBA9876543210 || wide[2] != 0 || wide[3] != ulong.MaxValue) return 3;
		if (characters.Length != 4 || characters[0] != 'A' || characters[1] != '\u03A9' || characters[2] != 0 || characters[3] != '\uD800') return 4;
		var copy = Unsafe.BitCast<Span<char>, Span<char>>(new char[6].AsSpan(1, 4));
		characters.CopyTo(copy); System.GC.Collect();
		return copy[0] == 'A' && copy[1] == '\u03A9' && copy[2] == 0 && copy[3] == '\uD800' && pressure[0] == 17 ? 42 : 5;
	}

	public static int CoreLibFloatingPointBitProjectionEntry()
	{
		for (var scenario = 0; scenario < 10; scenario++)
		{
			var bits = scenario switch { 0 => 0u, 1 => 0x80000000u, 2 => 1u, 3 => 0x007FFFFFu, 4 => 0x00800000u,
				5 => 0x7F7FFFFFu, 6 => 0x7F800000u, 7 => 0xFF800000u, 8 => 0x7FC12345u, _ => 0xFFA12345u };
			var single = Unsafe.BitCast<uint, float>(bits);
			System.GC.Collect();
			if (M68kRuntime.SingleToUInt32Bits(single) != bits || Unsafe.BitCast<float, uint>(single) != bits ||
				unchecked((uint)Unsafe.BitCast<float, int>(single)) != bits || M68kRuntime.SingleToUInt32Bits(Unsafe.BitCast<int, float>(unchecked((int)bits))) != bits) return 100 + scenario;
			var high = scenario switch { 0 => 0u, 1 => 0x80000000u, 2 => 0u, 3 => 0x000FFFFFu, 4 => 0x00100000u,
				5 => 0x7FEFFFFFu, 6 => 0x7FF00000u, 7 => 0xFFF00000u, 8 => 0x7FF81234u, _ => 0xFFF01234u };
			var low = scenario is 2 or 8 or 9 ? 0x12345678u : scenario is 3 or 5 ? uint.MaxValue : 0u;
			var expected = ((ulong)high << 32) | low;
			if (M68kRuntime.SplitUInt64(~expected, out var inverseHigh) != ~low || inverseHigh != ~high) return 400 + scenario;
			const ulong mask = 0x1234567889ABCDEF;
			if (M68kRuntime.SplitUInt64(expected & mask, out var maskedHigh) != (low & 0x89ABCDEFu) || maskedHigh != (high & 0x12345678u)) return 600 + scenario;
			if (M68kRuntime.SplitUInt64(expected | mask, out maskedHigh) != (low | 0x89ABCDEFu) || maskedHigh != (high | 0x12345678u)) return 700 + scenario;
			if (M68kRuntime.SplitUInt64(expected ^ mask, out maskedHigh) != (low ^ 0x89ABCDEFu) || maskedHigh != (high ^ 0x12345678u)) return 800 + scenario;
			var expectedLow = unchecked(0u - low);
			var expectedHigh = unchecked(0u - high - (low == 0 ? 0u : 1u));
			if (M68kRuntime.SplitInt64(unchecked(-(long)expected), out var negativeHigh) != expectedLow || negativeHigh != expectedHigh) return 500 + scenario;
			var wide = Unsafe.BitCast<ulong, double>(expected);
			System.GC.Collect();
			if (Unsafe.BitCast<double, ulong>(wide) != expected || unchecked((ulong)Unsafe.BitCast<double, long>(wide)) != expected ||
				M68kRuntime.SplitDouble(wide, out var actualHigh) != low || actualHigh != high) return 200 + scenario;
			var signed = Unsafe.BitCast<long, double>(unchecked((long)expected));
			if (M68kRuntime.SplitDouble(signed, out actualHigh) != low || actualHigh != high) return 300 + scenario;
		}
		return 42;
	}

	public static int CoreLibFloatingPointArgumentStoresEntry()
	{
		if (FloatingPointOverwriteUInt64(0x0123456789ABCDEF, 0) != 42 ||
			FloatingPointOverwriteUInt64(0xFEDCBA9876543210, 1) != 42) return 1;
		return FloatingPointOverwriteDouble(0, 0) == 42 &&
			FloatingPointOverwriteDouble(-0d, 1) == 42 ? 42 : 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int FloatingPointOverwriteUInt64(ulong value, int scenario)
	{
		ref var alias = ref value;
		var high = scenario == 0 ? 0x12345678u : 0xFEDCBA98u;
		var low = scenario == 0 ? 0x89ABCDEFu : 0x76543210u;
		value = ((ulong)high << 32) | low;
		System.GC.Collect();
		if (M68kRuntime.SplitUInt64(alias, out var actualHigh) != low || actualHigh != high) return 1;
		value = ~value;
		return M68kRuntime.SplitUInt64(alias, out actualHigh) == ~low && actualHigh == ~high ? 42 : 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int FloatingPointOverwriteDouble(double value, int scenario)
	{
		ref var alias = ref value;
		var high = scenario == 0 ? 0x7FF81234u : 0x80000000u;
		var low = scenario == 0 ? 0x56789ABCu : 0u;
		value = M68kRuntime.CombineDouble(high, low);
		System.GC.Collect();
		if (M68kRuntime.SplitDouble(alias, out var actualHigh) != low || actualHigh != high) return 1;
		value = M68kRuntime.CombineDouble(~high, ~low);
		return M68kRuntime.SplitDouble(alias, out actualHigh) == ~low && actualHigh == ~high ? 42 : 2;
	}

	public static string CoreLibFloatingPointSubnormalTextEntry() =>
		float.Epsilon.ToString(CultureInfo.InvariantCulture) + "|" + double.Epsilon.ToString(CultureInfo.InvariantCulture);

	public static int CoreLibStringBuilderFloatingPointSmokeEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			for (var scenario = 0; scenario < 10; scenario++)
			{
				var single = scenario switch {
					0 => 0f, 1 => -0f, 2 => 0.1f, 3 => -123.5f, 4 => float.Epsilon,
					5 => float.MaxValue, 6 => float.PositiveInfinity, 7 => float.NegativeInfinity,
					8 => float.NaN, _ => 1.2345678f
				};
				var wide = scenario switch {
					0 => 0d, 1 => -0d, 2 => 0.1d, 3 => -123.5d, 4 => double.Epsilon,
					5 => double.MaxValue, 6 => double.PositiveInfinity, 7 => double.NegativeInfinity,
					8 => double.NaN, _ => 1.2345678901234567d
				};
				var expectedSingle = scenario switch {
					0 => "0", 1 => "-0", 2 => "0.1", 3 => "-123.5", 4 => "1E-45",
					5 => "3.4028235E+38", 6 => "Infinity", 7 => "-Infinity", 8 => "NaN", _ => "1.2345678"
				};
				var expectedWide = scenario switch {
					0 => "0", 1 => "-0", 2 => "0.1", 3 => "-123.5", 4 => "5E-324",
					5 => "1.7976931348623157E+308", 6 => "Infinity", 7 => "-Infinity", 8 => "NaN", _ => "1.2345678901234567"
				};
				if (single.ToString(CultureInfo.InvariantCulture) != expectedSingle || wide.ToString(CultureInfo.InvariantCulture) != expectedWide) return 100 + scenario;
				for (var roomy = 0; roomy < 2; roomy++)
				{
					var builder = new StringBuilder(roomy == 0 ? 1 : 80).Append('A');
					if (!ReferenceEquals(builder, builder.Append(single)) || builder.ToString() != "A" + expectedSingle) return 200 + scenario;
					var snapshot = builder.ToString();
					builder.Clear().Append('B').Append(wide);
					System.GC.Collect();
					if (builder.ToString() != "B" + expectedWide || snapshot != "A" + expectedSingle) return 300 + scenario;
					builder.Clear().Append("tail").Insert(0, single);
					if (builder.ToString() != expectedSingle + "tail") return 400 + scenario;
					builder.Clear().Append("tail").Insert(0, wide);
					if (builder.ToString() != expectedWide + "tail") return 500 + scenario;
					builder.Clear().Append("reuse");
					if (builder.ToString() != "reuse" || snapshot != "A" + expectedSingle) return 600 + scenario;
				}
				for (var boundary = 0; boundary < 4; boundary++)
				for (var kind = 0; kind < 2; kind++)
				{
					var expected = kind == 0 ? expectedSingle : expectedWide;
					var length = boundary == 0 ? 0 : boundary == 1 ? expected.Length - 1 : expected.Length + boundary - 2;
					var storage = new char[length + 2];
					for (var index = 0; index < storage.Length; index++) storage[index] = '#';
					var written = 77;
					var success = kind == 0 ? single.TryFormat(storage.AsSpan(1, length), out written, default, CultureInfo.InvariantCulture) :
						wide.TryFormat(storage.AsSpan(1, length), out written, default, CultureInfo.InvariantCulture);
					System.GC.Collect();
					if (success != (length >= expected.Length) || written != (success ? expected.Length : 0)) return 700 + scenario;
					for (var index = 0; index < storage.Length; index++)
						if (storage[index] != (success && index > 0 && index <= written ? expected[index - 1] : '#')) return 800 + scenario;
				}
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}
}
