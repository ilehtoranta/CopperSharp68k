/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Globalization;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static int CoreLibStringBuilderBoxedDecimalEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			var culture = new CultureInfo("");
			culture.NumberFormat = new NumberFormatInfo { NegativeSign = "minus", NumberDecimalSeparator = ":" };
			CultureInfo.CurrentCulture = culture;
			var builder = new StringBuilder(1);
			for (int scenario = 0; scenario < 4; scenario++)
			{
				var value = scenario switch {
					0 => new decimal(0, 0, 0, true, 3),
					1 => new decimal(1234500, 0, 0, true, 4),
					2 => new decimal(1, 0, 0, false, 28),
					_ => new decimal(-1, -1, -1, false, 0)
				};
				var expected = scenario switch { 0 => "0:000", 1 => "minus123:4500", 2 => "0:0000000000000000000000000001", _ => "79228162514264337593543950335" };
				object boxed = value;
				System.GC.Collect();
				if (boxed.ToString() != expected) return 10 + scenario;
				builder.Clear().Append("seed").Append(boxed);
				if (builder.ToString() != "seed" + expected) return 20 + scenario;
				builder.Clear().Append("tail").Insert(0, boxed);
				if (builder.ToString() != expected + "tail") return 30 + scenario;
				object?[] objects = [boxed, null, boxed];
				builder.Clear().AppendJoin("|", objects);
				if (builder.ToString() != string.Concat(string.Concat(expected, "||"), expected)) return 40 + scenario;
				builder.Clear().AppendJoin('|', new List<object?> { boxed, null, boxed });
				if (builder.ToString() != string.Concat(string.Concat(expected, "||"), expected)) return 50 + scenario;
				builder.Clear().AppendJoin("|", new decimal[] { value, value });
				if (builder.ToString() != string.Concat(string.Concat(expected, "|"), expected)) return 60 + scenario;
				builder.Clear().AppendJoin('|', new List<decimal> { value, value });
				System.GC.Collect();
				if (builder.ToString() != string.Concat(string.Concat(expected, "|"), expected)) return 70 + scenario;
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	public static int CoreLibStringBuilderDecimalEnumerableJoinEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			var culture = new CultureInfo("");
			culture.NumberFormat = new NumberFormatInfo { NegativeSign = "minus", NumberDecimalSeparator = ":" };
			CultureInfo.CurrentCulture = culture;
			var builder = new StringBuilder(1);
			for (int scenario = 0; scenario < 4; scenario++)
			{
				var value = scenario switch {
					0 => new decimal(0, 0, 0, true, 3),
					1 => new decimal(1234500, 0, 0, true, 4),
					2 => new decimal(1, 0, 0, false, 28),
					_ => new decimal(-1, -1, -1, false, 0)
				};
				var expected = scenario switch { 0 => "0:000", 1 => "minus123:4500", 2 => "0:0000000000000000000000000001", _ => "79228162514264337593543950335" };
				builder.Clear().AppendJoin("|", new decimal[] { value, value });
				if (builder.ToString() != string.Concat(string.Concat(expected, "|"), expected)) return 60 + scenario;
				builder.Clear().AppendJoin('|', new List<decimal> { value, value });
				System.GC.Collect();
				if (builder.ToString() != string.Concat(string.Concat(expected, "|"), expected)) return 70 + scenario;
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	public static int CoreLibStringBuilderDecimalJoinLifetimeEntry()
	{
		for (var useList = 0; useList < 2; useList++)
		{
			IEnumerable<decimal>? source = CreateRetainedDecimalJoinSource(useList != 0);
			var enumerator = CopperSharp.Runtime.ShadowDecimalJoinEnumeration.GetEnumerator(source);
			source = null;
			System.GC.Collect();
			var pressure = new int[64]; pressure[0] = 123;
			if (!enumerator.MoveNext()) return 1;
			System.GC.Collect();
			if (enumerator.Current.ToString(NumberFormatInfo.InvariantInfo) != "-123.4500") return 2;
			if (!enumerator.MoveNext()) return 3;
			System.GC.Collect();
			if (enumerator.Current.ToString(NumberFormatInfo.InvariantInfo) != "0.0000000000000000000000000001") return 4;
			if (enumerator.MoveNext() || enumerator.Current.ToString(NumberFormatInfo.InvariantInfo) != "0" || enumerator.MoveNext()) return 5;
			enumerator.Dispose();
			System.GC.Collect();
			if (enumerator.MoveNext() || enumerator.Current.ToString(NumberFormatInfo.InvariantInfo) != "0" || pressure[0] != 123) return 6;
		}
		for (var mutation = 0; mutation < 4; mutation++)
		{
			var list = new List<decimal>(4) { new decimal(1234500, 0, 0, true, 4), new decimal(1, 0, 0, false, 28) };
			var enumerator = CopperSharp.Runtime.ShadowDecimalJoinEnumeration.GetEnumerator(list);
			if (!enumerator.MoveNext()) return 7;
			if (mutation == 3 && (!enumerator.MoveNext() || enumerator.MoveNext())) return 8;
			if (mutation == 1) list[0] = new decimal(7); else if (mutation == 2) list.Clear(); else list.Add(new decimal(9));
			try { enumerator.MoveNext(); return 9; } catch (InvalidOperationException) { }
			enumerator.Dispose();
			if (enumerator.MoveNext() || enumerator.Current.ToString(NumberFormatInfo.InvariantInfo) != "0") return 10;
		}
		var builder = new StringBuilder(1).Append("seed");
		for (var character = 0; character < 2; character++)
		{
			try
			{
				if (character == 0) builder.AppendJoin<decimal>("|", (IEnumerable<decimal>)null!);
				else builder.AppendJoin<decimal>('|', (IEnumerable<decimal>)null!);
				return 11;
			}
			catch (ArgumentNullException exception) { if (exception.ParamName != "values") return 12; }
			if (builder.ToString() != "seed") return 13;
			try
			{
				if (character == 0) builder.AppendJoin<decimal>("|", new ThrowingDecimalJoinEnumerable());
				else builder.AppendJoin<decimal>('|', new ThrowingDecimalJoinEnumerable());
				return 14;
			}
			catch (InvalidOperationException) { }
			if (builder.ToString() != "seed") return 15;
		}
		builder.AppendJoin<decimal>("|", Array.Empty<decimal>()).AppendJoin<decimal>('|', new List<decimal>());
		return builder.ToString() == "seed" ? 42 : 16;
	}

	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	private static IEnumerable<decimal> CreateRetainedDecimalJoinSource(bool useList)
	{
		var first = new decimal(1234500, 0, 0, true, 4);
		var second = new decimal(1, 0, 0, false, 28);
		if (useList) return new List<decimal> { first, second };
		return new decimal[] { first, second };
	}

	public sealed class ThrowingDecimalJoinEnumerable : IEnumerable<decimal>
	{
		public IEnumerator<decimal> GetEnumerator() => throw new InvalidOperationException("Decimal producer failure.");
		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
	}

	public static int CoreLibStringBuilderDecimalSmokeEntry()
	{
		var info = new NumberFormatInfo();
		var value = new decimal(12300, 0, 0, true, 2);
		var builder = new StringBuilder(1).Append("seed");
		builder.AppendFormat(info, "{0}", value);
		if (builder.ToString() != "seed-123.00") return 1;
		builder.Clear().Append(value);
		if (builder.ToString() != value.ToString()) return 2;
		builder.Clear().Append("x").Insert(0, value);
		if (builder.ToString() != value.ToString() + "x") return 3;
		builder.Clear();
		var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder, info);
		handler.AppendFormatted(value, "N1");
		builder.Append(ref handler);
		System.GC.Collect();
		return builder.ToString() == "-123.0" ? 42 : 4;
	}

	public static int CoreLibStringBuilderDecimalMatrixEntry()
	{
		var info = NumberFormatInfo.InvariantInfo;
		string[] formats = ["G", "G5", "N2", "E3", "C1", "P1", "0.000", "#,##0.##;[neg]#,##0.##;zero", "0.00E+00"];
		string[] expected = [
			"123.4500",
			"123.45",
			"123.45",
			"1.235E+002",
			"¤123.5",
			"12,345.0 %",
			"123.450",
			"123.45",
			"1.23E+02",
			"-0.005",
			"-0.005",
			"-0.01",
			"-5.000E-003",
			"¤0.0",
			"-0.5 %",
			"-0.005",
			"[neg]0.01",
			"-5.00E-03",
			"0.000",
			"0",
			"0.00",
			"0.000E+000",
			"¤0.0",
			"0.0 %",
			"0.000",
			"zero",
			"0.00E+00",
			"0.0000000000000000000000000001",
			"1E-28",
			"0.00",
			"1.000E-028",
			"¤0.0",
			"0.0 %",
			"0.000",
			"zero",
			"1.00E-28",
			"79228162514264337593543950335",
			"7.9228E+28",
			"79,228,162,514,264,337,593,543,950,335.00",
			"7.923E+028",
			"¤79,228,162,514,264,337,593,543,950,335.0",
			"7,922,816,251,426,433,759,354,395,033,500.0 %",
			"79228162514264337593543950335.000",
			"79,228,162,514,264,337,593,543,950,335",
			"7.92E+28",
			"-7.9228162514264337593543950335",
			"-7.9228",
			"-7.92",
			"-7.923E+000",
			"(¤7.9)",
			"-792.3 %",
			"-7.923",
			"[neg]7.92",
			"-7.92E+00"

		];
		var builder = new StringBuilder(1);
		Span<char> destination = stackalloc char[80];
		for (int scenario = 0; scenario < 6; scenario++)
		{
			var value = scenario switch {
				0 => new decimal(1234500, 0, 0, false, 4),
				1 => new decimal(5, 0, 0, true, 3),
				2 => new decimal(0, 0, 0, true, 3),
				3 => new decimal(1, 0, 0, false, 28),
				4 => new decimal(-1, -1, -1, false, 0),
				_ => new decimal(-1, -1, -1, true, 28)
			};
			for (int style = 0; style < formats.Length; style++)
			{
				var format = formats[style];
				var text = expected[scenario * formats.Length + style];
				builder.Clear().Append("seed").AppendFormat(info, string.Concat(string.Concat("{0:", format), "}"), value);
				if (builder.ToString() != "seed" + text) return 100 + scenario * 10 + style;
				builder.Clear();
				var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder, info);
				handler.AppendFormatted(value, format);
				builder.Append(ref handler);
				System.GC.Collect();
				if (builder.ToString() != text) return 200 + scenario * 10 + style;
				for (int index = 0; index < destination.Length; index++) destination[index] = '~';
				if (!value.TryFormat(destination.Slice(1, text.Length), out var written, format, info) || written != text.Length) return 300 + scenario * 10 + style;
				if (new string(destination.Slice(1, written)) != text || destination[0] != '~' || destination[written + 1] != '~') return 400 + scenario * 10 + style;
				for (int index = 0; index < destination.Length; index++) destination[index] = '~';
				if (value.TryFormat(destination.Slice(1, text.Length - 1), out written, format, info) || written != 0) return 500 + scenario * 10 + style;
				for (int index = 0; index < destination.Length; index++) if (destination[index] != '~') return 600 + scenario * 10 + style;
			}
		}
		try { new decimal(0, 0, 0, false, 29); return 700; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "scale") return 701; }
		try { builder.Clear().AppendFormat(info, "prefix{0:D}", new decimal(1)); return 702; }
		catch (FormatException) { if (builder.ToString() != "prefix") return 703; }
		return 42;
	}
}
