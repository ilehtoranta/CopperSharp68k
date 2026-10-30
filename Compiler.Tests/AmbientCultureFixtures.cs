/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Globalization;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static int CoreLibAmbientCultureDefaultsEntry()
	{
		if (CultureInfo.DefaultThreadCurrentCulture is not null ||
			CultureInfo.CurrentCulture != CultureInfo.InvariantCulture) return 1;
		var fallback = new CultureInfo("");
		fallback.NumberFormat.NegativeSign = "default";
		CultureInfo.DefaultThreadCurrentCulture = fallback;
		System.GC.Collect();
		if (CultureInfo.CurrentCulture != fallback || (-12).ToString() != "default12") return 2;
		CultureInfo.DefaultThreadCurrentCulture = null;
		if (CultureInfo.CurrentCulture != CultureInfo.InvariantCulture) return 3;
		return 42;
	}

	private static object AmbientInteger(int width) => width switch
	{
		0 => (sbyte)-123, 1 => (byte)123, 2 => (short)-123, 3 => (ushort)123,
		4 => -123, 5 => 123u, 6 => -123L, _ => 123UL
	};

	private static string AmbientPattern(int scenario) => scenario switch
	{
		0 => "", 1 => "D4", 2 => "F1", 3 => "N1", 4 => "E1", 5 => "G2", 6 => "R", _ => "000.0"
	};

	private static string AmbientExpected(bool negative, int scenario, bool custom)
	{
		var digits = scenario switch
		{
			1 => "0123", 2 or 7 => custom ? "123:0" : "123.0",
			3 => custom ? "1_2_3:0" : "123.0",
			4 => custom ? "1:2Ep002" : "1.2E+002",
			5 => custom ? "1:2Ep02" : "1.2E+02", _ => "123"
		};
		return negative ? (custom ? "n" : "-") + digits : digits;
	}

	private static void AppendAmbientInteger(StringBuilder builder, object value)
	{
		if (value is sbyte a) builder.Append(a);
		else if (value is byte b) builder.Append(b);
		else if (value is short c) builder.Append(c);
		else if (value is ushort d) builder.Append(d);
		else if (value is int e) builder.Append(e);
		else if (value is uint f) builder.Append(f);
		else if (value is long g) builder.Append(g);
		else if (value is ulong h) builder.Append(h);
	}

	private static void InsertAmbientInteger(StringBuilder builder, object value)
	{
		if (value is sbyte a) builder.Insert(0, a);
		else if (value is byte b) builder.Insert(0, b);
		else if (value is short c) builder.Insert(0, c);
		else if (value is ushort d) builder.Insert(0, d);
		else if (value is int e) builder.Insert(0, e);
		else if (value is uint f) builder.Insert(0, f);
		else if (value is long g) builder.Insert(0, g);
		else if (value is ulong h) builder.Insert(0, h);
	}

	public static int CoreLibStringBuilderAmbientInsertJoinEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			for (int style = 0; style < 3; style++)
			{
				CultureInfo culture = style == 2 ? new HandlerDerivedCulture() : new CultureInfo("");
				culture.NumberFormat.NegativeSign = style == 0 ? "-" : "n";
				CultureInfo.CurrentCulture = culture;
				culture = null!;
				System.GC.Collect();
				for (int width = 0; width < 8; width++)
				{
					var builder = new StringBuilder(1).Append("x");
					InsertAmbientInteger(builder, AmbientInteger(width));
					var snapshot = builder.ToString();
					System.GC.Collect(); builder.Clear().Append("tail");
					if (snapshot != AmbientExpected((width & 1) == 0, 0, style != 0) + "x" || builder.ToString() != "tail") return 1;
				}
				for (int list = 0; list < 2; list++)
				for (int character = 0; character < 2; character++)
				{
					System.Collections.Generic.IEnumerable<int> values = list == 0 ? new int[] { -12, 0, 34 }
						: new System.Collections.Generic.List<int> { -12, 0, 34 };
					var builder = new StringBuilder(1).Append("seed");
					if (character == 0) builder.AppendJoin<int>("|", values);
					else builder.AppendJoin<int>('|', values);
					values = null!;
					var snapshot = builder.ToString();
					System.GC.Collect(); builder.Clear().Append("tail");
					if (snapshot != (style == 0 ? "seed-12|0|34" : "seedn12|0|34") || builder.ToString() != "tail") return 2;
				}
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	private sealed class AmbientSwitchingCulture : CultureInfo
	{
		public readonly CultureInfo Next;
		public AmbientSwitchingCulture() : base("")
		{
			NumberFormat.NegativeSign = "old";
			Next = new CultureInfo("");
			Next.NumberFormat.NegativeSign = "new";
		}
		public override object? GetFormat(Type? type)
		{
			if (type != typeof(NumberFormatInfo)) return base.GetFormat(type);
			var result = NumberFormat;
			CultureInfo.CurrentCulture = Next;
			System.GC.Collect();
			var pressure = new byte[64];
			pressure[0] = 1;
			if (pressure[0] != 1) return null;
			return result;
		}
	}

	public static int CoreLibStringBuilderAmbientFallbackEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			for (int width = 0; width < 2; width++)
			for (int form = 0; form < 3; form++)
			for (int roomy = 0; roomy < 2; roomy++)
			{
				var source = new AmbientSwitchingCulture();
				var next = source.Next;
				CultureInfo.CurrentCulture = source;
				source = null!;
				System.GC.Collect();
				var builder = new StringBuilder(roomy == 0 ? 1 : 32).Append("seed");
				if (form == 0)
				{
					var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder);
					if (width == 0) handler.AppendFormatted(-12);
					else handler.AppendFormatted(-12L);
					builder.Append(ref handler);
				}
				else if (form == 1) builder.AppendFormat("{0}", width == 0 ? (object)(-12) : -12L);
				else if (width == 0) builder.Append(-12);
				else builder.Append(-12L);
				var snapshot = builder.ToString();
				System.GC.Collect(); builder.Clear().Append("tail");
				if (snapshot != (roomy == 0 ? "seednew12" : "seedold12") || builder.ToString() != "tail") return 1;
				if (CultureInfo.CurrentCulture != next) return 2;
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	public static int CoreLibStringBuilderAmbientCultureEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		var previousDefault = CultureInfo.DefaultThreadCurrentCulture;
		try
		{
			for (int style = 0; style < 3; style++)
			{
				var info = new NumberFormatInfo();
				if (style != 0)
				{
					info.NegativeSign = "n"; info.PositiveSign = "p";
					info.NumberDecimalSeparator = ":"; info.NumberGroupSeparator = "_";
					info.NumberGroupSizes = [1];
				}
				var inherited = style == 2 ? new HandlerDerivedCulture() : null;
				CultureInfo culture = inherited is null ? new CultureInfo("") : inherited;
				culture.NumberFormat = info;
				CultureInfo.CurrentCulture = culture;
				CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
				culture = null!; info = null!;
				System.GC.Collect();
				if (CultureInfo.CurrentCulture == CultureInfo.DefaultThreadCurrentCulture) return 1;
				for (int width = 0; width < 8; width++)
				for (int scenario = 0; scenario < 8; scenario++)
				{
					var value = AmbientInteger(width);
					var format = AmbientPattern(scenario);
					var expected = AmbientExpected((width & 1) == 0, scenario, style != 0);
					if (((IFormattable)value).ToString(format, null) != expected) return 100 + width * 8 + scenario;
					for (int boundary = 0; boundary < 4; boundary++)
					{
						int length = boundary == 0 ? 0 : boundary == 1 ? expected.Length - 1 : expected.Length + boundary - 2;
						var storage = new char[length + 2];
						for (int index = 0; index < storage.Length; index++) storage[index] = '#';
						int queries = inherited is null ? 0 : inherited.Queries;
						bool success = TryStandardInteger(value, format, storage.AsSpan(1, length), out int written, null);
						if (inherited is not null && inherited.Queries != queries + (((width & 1) != 0 && scenario <= 1) ? 0 : 1)) return 2;
						if (success != (length >= expected.Length) || written != (success ? expected.Length : 0)) return 3;
						for (int index = 0; index < storage.Length; index++)
							if (storage[index] != (success && index > 0 && index <= written ? expected[index - 1] : '#')) return 4;
					}
					for (int form = 0; form < 4; form++)
					{
						var builder = new StringBuilder(1).Append("seed");
						if (form == 0)
						{
							var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder);
							AppendTypedCustomInteger(ref handler, value, 0, format);
							builder.Append(ref handler);
						}
						else if (form == 1) builder.AppendFormat("{0:" + format + "}", value);
						else if (form == 2) builder.AppendFormat<object>(null, CompositeFormat.Parse("{0:" + format + "}"), value);
						else builder.Append(value);
						var snapshot = builder.ToString();
						var text = form == 3 ? AmbientExpected((width & 1) == 0, 0, style != 0) : expected;
						System.GC.Collect(); builder.Clear().Append("tail");
						if (snapshot != "seed" + text || builder.ToString() != "tail") return 5000 + style * 1000 + width * 100 + scenario * 10 + form;
					}
					var primitive = new StringBuilder(1); AppendAmbientInteger(primitive, value);
					if (primitive.ToString() != AmbientExpected((width & 1) == 0, 0, style != 0)) return 6;
					if (scenario == 0)
					{
						primitive.Clear().Append("x").Insert(0, value);
						if (primitive.ToString() != expected + "x") return 8;
						primitive.Clear().AppendJoin("|", new object?[] { value, value });
						System.GC.Collect();
						if (primitive.ToString() != expected + "|" + expected) return 9;
					}
				}
			}
			try { CultureInfo.CurrentCulture = null!; return 7; }
			catch (ArgumentNullException) { }
			return 42;
		}
		finally
		{
			CultureInfo.CurrentCulture = previous;
			CultureInfo.DefaultThreadCurrentCulture = previousDefault;
		}
	}
}
