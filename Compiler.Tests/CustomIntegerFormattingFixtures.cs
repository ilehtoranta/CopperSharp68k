/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Text;
using System.Globalization;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static NumberFormatInfo CustomIntegerInfo(bool custom) => custom ? new NumberFormatInfo
	{
		NegativeSign = new string("~\u03A9".AsSpan()), PositiveSign = "p", NumberDecimalSeparator = ":",
		NumberGroupSeparator = "_", NumberGroupSizes = [3, 2], PercentSymbol = "pct\u03A9", PerMilleSymbol = "m\u03A9"
	} : new NumberFormatInfo();

	public static string CustomIntegerPattern(int scenario) => scenario switch
	{
		0 => "000.0", 1 or 2 => "000.0;(000.0);'zero'", 3 => "0,;'neg';'zero'", 4 => "0,",
		5 => "#,##0.00", 6 => "0.0E+00", 7 => "0%", 8 => "0\u2030", 9 => "00000000000",
		10 => "0", 11 => "#,##0", 12 => "'\u03A9\uD83D\uDE00'000", 13 => "0'\uD800'", 14 => "00000000000000000000000000000000000000000000000000000000000000000000000000000000",
		15 => "\\#0\\;\\%", 16 => "0\0'ignored'", 17 => "#", 18 => "0.000E-000", 20 => "Qtext0", 21 => "D0.0", 22 => "Q999999999x0", _ => "#,##0"
	};

	public static long CustomIntegerSignedValue(int scenario) => scenario switch
	{
		1 => -42, 2 or 17 => 0, 3 => 499, 4 => 500, 5 => 1234567, 6 => -42, 9 => int.MinValue,
		10 => long.MinValue, 13 => 12, 18 => 99995, _ => 42
	};

	public static string CustomIntegerExpected(int scenario) => scenario switch
	{
		0 => "042.0", 1 => "(042.0)", 2 or 3 => "zero", 4 => "1", 5 => "12_34_567:00",
		6 => "~\u03A94:2Ep01", 7 => "4200pct\u03A9", 8 => "42000m\u03A9", 9 => "-02147483648",
		10 => "-9223372036854775808", 11 => "1_84_46_74_40_73_70_95_51_615", 12 => "\u03A9\uD83D\uDE00" + "4294967295",
		13 => "12\uD800", 14 => "00000000000000000000000000000000000000000000000000000000000000000000000000000042", 15 => "#42;%", 16 => "42", 17 => "",
		18 => "1.000E005", 20 => "Qtext42", 21 => "D42.0", 22 => "Q999999999x42", _ => "1_8_4_4_6_7_4_4_0_7_3_7_0_9_5_5_1_6_1_5"
	};

	public static int CoreLibCustomNumberEngineEntry()
	{
		for (int scenario = 0; scenario < 20; scenario++)
		{
			var info = CustomIntegerInfo(scenario is >= 5 and <= 8 || scenario == 11 || scenario == 19);
			if (scenario == 19) info.NumberGroupSizes = [1];
			var format = new string(CustomIntegerPattern(scenario).AsSpan());
			var expected = CustomIntegerExpected(scenario);
			var signed = CustomIntegerSignedValue(scenario);
			var text = scenario == 11 || scenario == 19 ? CopperSharp.Runtime.ShadowCustomNumberFormatting.FormatUInt64(ulong.MaxValue, format, info) :
				scenario == 12 ? CopperSharp.Runtime.ShadowCustomNumberFormatting.FormatUInt32(uint.MaxValue, format, info) :
				scenario == 10 ? CopperSharp.Runtime.ShadowCustomNumberFormatting.FormatInt64(signed, format, info) :
				CopperSharp.Runtime.ShadowCustomNumberFormatting.FormatInt32((int)signed, -1, format, info);
			System.GC.Collect();
			if (text != expected) return 100 + scenario * 10;
			for (int boundary = 0; boundary < 4; boundary++)
			{
				int length = boundary == 0 ? 0 : boundary == 1 && expected.Length > 0 ? expected.Length - 1 : expected.Length + boundary - 2;
				if (length < 0) length = 0;
				var storage = new char[length + 2];
				for (int index = 0; index < storage.Length; index++) storage[index] = '#';
				var destination = storage.AsSpan(1, length);
				bool success = scenario == 11 || scenario == 19 ? CopperSharp.Runtime.ShadowCustomNumberFormatting.TryFormatUInt64(ulong.MaxValue, format, info, destination, out var written) :
					scenario == 12 ? CopperSharp.Runtime.ShadowCustomNumberFormatting.TryFormatUInt32(uint.MaxValue, format, info, destination, out written) :
					scenario == 10 ? CopperSharp.Runtime.ShadowCustomNumberFormatting.TryFormatInt64(signed, format, info, destination, out written) :
					CopperSharp.Runtime.ShadowCustomNumberFormatting.TryFormatInt32((int)signed, -1, format, info, destination, out written);
				System.GC.Collect();
				if (success != (length >= expected.Length) || written != (success ? expected.Length : 0)) return 101 + scenario * 10;
				if (storage[0] != '#' || storage[storage.Length - 1] != '#') return 102 + scenario * 10;
				for (int index = 0; index < length; index++)
					if (destination[index] != (success && index < written ? expected[index] : '#')) return 103 + scenario * 10;
			}
		}
		return 42;
	}
	public static object CustomIntegerValue(int scenario) => scenario switch
	{
		1 or 13 => (sbyte)CustomIntegerSignedValue(scenario),
		2 or 14 => (short)CustomIntegerSignedValue(scenario),
		7 or 15 => (byte)42, 8 or 16 => (ushort)42,
		3 or 12 or 18 => scenario == 12 ? uint.MaxValue : (uint)CustomIntegerSignedValue(scenario),
		4 or 11 or 19 => scenario == 4 ? 500UL : ulong.MaxValue,
		6 or 10 or 17 => CustomIntegerSignedValue(scenario),
		_ => (int)CustomIntegerSignedValue(scenario)
	};

	private static void AppendTypedCustomInteger(ref StringBuilder.AppendInterpolatedStringHandler handler,
		object value, int alignment, string format)
	{
		if (value is sbyte sb) handler.AppendFormatted(sb, alignment, format);
		else if (value is byte b) handler.AppendFormatted(b, alignment, format);
		else if (value is short sh) handler.AppendFormatted(sh, alignment, format);
		else if (value is ushort us) handler.AppendFormatted(us, alignment, format);
		else if (value is int si) handler.AppendFormatted(si, alignment, format);
		else if (value is uint ui) handler.AppendFormatted(ui, alignment, format);
		else if (value is long sl) handler.AppendFormatted(sl, alignment, format);
		else handler.AppendFormatted((ulong)value, alignment, format);
	}

	private static bool CheckCustomIntegerBuilder(string text, string expected, int alignment, bool newline)
	{
		var padding = alignment == 0 ? 0 : 3;
		if (text.Length != 6 + padding + expected.Length + (newline ? Environment.NewLine.Length : 0) ||
			text[0] != 's' || text[1] != 'e' || text[2] != 'e' || text[3] != 'd' || text[4] != '[' ||
			text[5 + padding + expected.Length] != ']') return false;
		var start = 5 + (alignment > 0 ? padding : 0);
		for (int index = 0; index < expected.Length; index++) if (text[start + index] != expected[index]) return false;
		for (int index = 0; index < padding; index++)
			if (text[5 + (alignment > 0 ? index : expected.Length + index)] != ' ') return false;
		if (newline)
			for (int index = 0; index < Environment.NewLine.Length; index++)
				if (text[6 + padding + expected.Length + index] != Environment.NewLine[index]) return false;
		return true;
	}

	public static int CoreLibStringBuilderCustomIntegersEntry()
	{
		for (int scenario = 0; scenario < 23; scenario++)
		{
			var info = CustomIntegerInfo(scenario is >= 5 and <= 8 || scenario == 11 || scenario == 19);
			if (scenario == 19) info.NumberGroupSizes = [1];
			var value = CustomIntegerValue(scenario);
			var format = new string(CustomIntegerPattern(scenario).AsSpan());
			var expected = CustomIntegerExpected(scenario);
			for (int capacity = 0; capacity < 2; capacity++)
			for (int direction = 0; direction < 3; direction++)
			for (int form = 0; form < 8; form++)
			{
				var alignment = direction == 0 ? 0 : direction == 1 ? -(expected.Length + 3) : expected.Length + 3;
				var sourceBuilder = new StringBuilder(format.Length + 20).Append("[{0");
				if (direction != 0) sourceBuilder.Append(',').Append(alignment);
				var source = sourceBuilder.Append(':').Append(format).Append("}]").ToString();
				var parsed = CompositeFormat.Parse(source);
				var builder = new StringBuilder(capacity == 0 ? 1 : 160).Append("seed");
				bool newline = form < 2 && scenario % 4 >= 2;
				StringBuilder result;
				if (form < 2)
				{
					var handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, builder, info);
					handler.AppendLiteral("[");
					if (form == 0) AppendTypedCustomInteger(ref handler, value, alignment, format);
					else handler.AppendFormatted(value, alignment, format);
					handler.AppendLiteral("]");
					result = scenario % 4 == 0 ? builder.Append(ref handler) : scenario % 4 == 1 ? builder.Append(info, ref handler) :
						scenario % 4 == 2 ? builder.AppendLine(ref handler) : builder.AppendLine(info, ref handler);
				}
				else if (form == 2) result = builder.AppendFormat<object>(info, parsed, value);
				else if (form == 3) result = builder.AppendFormat<object, int>(info, parsed, value, 9);
				else if (form == 4) result = builder.AppendFormat<object, int, string>(info, parsed, value, 9, "extra");
				else if (form == 5) result = builder.AppendFormat(info, parsed, new object?[] { value });
				else if (form == 6) result = builder.AppendFormat(info, parsed, new ReadOnlySpan<object?>(new object?[] { value }));
				else result = builder.AppendFormat(info, source, value);
				System.GC.Collect();
				var snapshot = builder.ToString();
				if (!ReferenceEquals(result, builder) || !CheckCustomIntegerBuilder(snapshot, expected, alignment, newline))
					return 1000 + scenario * 100 + capacity * 40 + direction * 10 + form;
				builder.Clear().Append("tail");
				System.GC.Collect();
				if (builder.ToString() != "tail" || !CheckCustomIntegerBuilder(snapshot, expected, alignment, newline)) return 2;
			}
		}
		return 42;
	}

	private sealed class CustomIntegerProvider : IFormatProvider
	{
		public object? Result;
		public Exception? Error;
		public int NumberQueries, CustomQueries;
		public object? GetFormat(Type? type)
		{
			if (type == typeof(ICustomFormatter)) { CustomQueries++; return null; }
			if (type != typeof(NumberFormatInfo)) throw new InvalidOperationException();
			NumberQueries++;
			System.GC.Collect();
			if (Error is not null) throw Error;
			return Result;
		}
	}

	public static int CoreLibCustomIntegerProviderContractsEntry()
	{
		for (int scenario = 0; scenario < 23; scenario++)
		{
			var info = CustomIntegerInfo(scenario is >= 5 and <= 8 || scenario == 11 || scenario == 19);
			if (scenario == 19) info.NumberGroupSizes = [1];
			var provider = new CustomIntegerProvider { Result = info };
			info = null!;
			var value = CustomIntegerValue(scenario);
			var format = new string(CustomIntegerPattern(scenario).AsSpan());
			var expected = CustomIntegerExpected(scenario);
			if (FormatStandardInteger(value, format, provider) != expected || provider.NumberQueries != 1 || provider.CustomQueries != 0) return 1;
			for (int boundary = 0; boundary < 4; boundary++)
			{
				int length = boundary == 0 ? 0 : boundary == 1 && expected.Length > 0 ? expected.Length - 1 : expected.Length + boundary - 2;
				if (length < 0) length = 0;
				var storage = new char[length + 2];
				for (int index = 0; index < storage.Length; index++) storage[index] = '#';
				var before = provider.NumberQueries;
				var success = TryStandardInteger(value, format, storage.AsSpan(1, length), out var written, provider);
				System.GC.Collect();
				if (provider.NumberQueries != before + 1 || provider.CustomQueries != 0 ||
					success != (length >= expected.Length) || written != (success ? expected.Length : 0)) return 2;
				for (int index = 0; index < storage.Length; index++)
					if (storage[index] != (success && index > 0 && index <= written ? expected[index - 1] : '#')) return 3;
			}
		}
		foreach (var invalid in new[] { "Q", "Q0", "D1000000000", "Q1000000000." })
		{
			var provider = new CustomIntegerProvider { Result = CustomIntegerInfo(false) };
			var buffer = new char[1]; buffer[0] = '#'; int written = 77;
			try { TryStandardInteger(42, invalid, buffer, out written, provider); return 14; }
			catch (FormatException) { }
			if (provider.NumberQueries != 0 || provider.CustomQueries != 0 || buffer[0] != '#' || written != 77) return 15;
		}

		for (int mode = 0; mode < 3; mode++)
		{
			var error = new InvalidOperationException("custom-provider");
			var provider = new CustomIntegerProvider { Result = mode == 0 ? null : "wrong", Error = mode == 2 ? error : null };
			var storage = new char[20];
			for (int index = 0; index < storage.Length; index++) storage[index] = '#';
			int written = 77;
			var expected = 42.ToString("000.0", System.Globalization.CultureInfo.CurrentCulture);
			try
			{
				if (!TryStandardInteger(42, "000.0", storage, out written, provider) || mode == 2) return 4;
			}
			catch (InvalidOperationException caught) { if (mode != 2 || !ReferenceEquals(caught, error)) return 6; }
			if (written != (mode == 2 ? 77 : expected.Length) || provider.NumberQueries != 1 || provider.CustomQueries != 0) return 7;
			for (int index = 0; index < storage.Length; index++)
				if (storage[index] != (mode != 2 && index < written ? expected[index] : '#')) return 8;
		}
		for (int parsed = 0; parsed < 2; parsed++)
		{
			var error = new InvalidOperationException("custom-builder-provider");
			var provider = new CustomIntegerProvider { Result = CustomIntegerInfo(true), Error = error };
			var builder = new StringBuilder(80).Append("seed");
			var format = CompositeFormat.Parse("A{0:0.0}");
			try
			{
				if (parsed == 0) builder.AppendFormat(provider, "A{0:0.0}", 42);
				else builder.AppendFormat<int>(provider, format, 42);
				return 9;
			}
			catch (InvalidOperationException caught) { if (!ReferenceEquals(caught, error)) return 10; }
			if (builder.ToString() != "seedA" || provider.NumberQueries != 1 || provider.CustomQueries != 1) return 11;
			provider.Error = null;
			builder.AppendFormat(provider, "{0:0.0}", 42);
			System.GC.Collect();
			if (builder.ToString() != "seedA42:0") return 12;
			builder.Clear().Append("tail");
			if (builder.ToString() != "tail") return 13;
		}
		return 42;
	}

	private static void AppendCustomAllocationValue(StringBuilder builder, NumberFormatInfo info,
		string format, CompositeFormat parsed, object box, int form)
	{
		if (form == 0) builder.AppendFormat(info, parsed, new object?[] { box });
		else if (form == 1) builder.AppendFormat(info, "{0:" + format + "}", box);
		else
		{
			var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder, info);
			handler.AppendFormatted(42, form == 3 ? 83 : 0, format);
			builder.Append(ref handler);
		}
	}

	private static bool CustomAllocationPrefix(string actual, string expected)
	{
		if (actual.Length < 5 || actual.Length > expected.Length) return false;
		for (int index = 0; index < actual.Length; index++) if (actual[index] != expected[index]) return false;
		return true;
	}

	public static int CoreLibCustomIntegerAllocationContractsEntry()
	{
		var info = CustomIntegerInfo(false);
		var shortBuffer = new char[5];
		// Initialize CoreLib's formatting caches before checking per-call allocation.
		if (!CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt32(42, -1, "000.0", info, shortBuffer, out _)) return 18;
		SetStringBuilderAllocationFailure(1);
		var shortSuccess = CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt32(42, -1, "000.0", info, shortBuffer, out var shortWritten);
		SetStringBuilderAllocationFailure(0);
		if (!shortSuccess || shortWritten != 5 || shortBuffer[0] != '0' || shortBuffer[4] != '0') return 1;
		SetStringBuilderAllocationFailure(2);
		var shortText = CopperSharp.Runtime.ShadowNumberFormatting.FormatInt32(42, -1, "000.0", info);
		SetStringBuilderAllocationFailure(0);
		if (shortText != "042.0") return 2;
		SetStringBuilderAllocationFailure(1);
		try { _ = CopperSharp.Runtime.ShadowNumberFormatting.FormatInt32(42, -1, "000.0", info); SetStringBuilderAllocationFailure(0); return 3; }
		catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
		if (CopperSharp.Runtime.ShadowNumberFormatting.FormatInt32(42, -1, "000.0", info) != "042.0") return 4;

		var format = CustomIntegerPattern(14);
		for (int operation = 0; operation < 2; operation++)
		{
			bool reachedSuccess = false;
			for (int failAt = 1; failAt <= 12; failAt++)
			{
				var buffer = new char[82];
				for (int index = 0; index < buffer.Length; index++) buffer[index] = '#';
				int written = 77;
				bool threw = false;
				SetStringBuilderAllocationFailure(failAt);
				try
				{
					if (operation == 0)
					{
						if (!CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt32(42, -1, format, info, buffer.AsSpan(1, 80), out written) || written != 80)
						{ SetStringBuilderAllocationFailure(0); return 5; }
					}
					else if (CopperSharp.Runtime.ShadowNumberFormatting.FormatInt32(42, -1, format, info) != CustomIntegerExpected(14))
						{ SetStringBuilderAllocationFailure(0); return 6; }
					SetStringBuilderAllocationFailure(0);
				}
				catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); threw = true; }
				if (buffer[0] != '#' || buffer[81] != '#') return 7;
				if (threw)
				{
					if (written != 77) return 8;
					for (int index = 0; index < buffer.Length; index++) if (buffer[index] != '#') return 9;
					if (!CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt32(42, -1, format, info, buffer.AsSpan(1, 80), out written) || written != 80) return 10;
					for (int index = 0; index < 80; index++) if (buffer[index + 1] != CustomIntegerExpected(14)[index]) return 11;
				}
				else { reachedSuccess = true; break; }
			}
			if (!reachedSuccess) return 12;
		}
		object box = 42;
		for (int form = 0; form < 4; form++)
		for (int capacity = 0; capacity < 2; capacity++)
		{
			var parsed = CompositeFormat.Parse("{0:" + format + "}");
			var expected = form == 3 ? "seedA   " + CustomIntegerExpected(14) : "seedA" + CustomIntegerExpected(14);
			bool reachedSuccess = false;
			for (int failAt = 1; failAt <= 16; failAt++)
			{
				var builder = new StringBuilder(capacity == 0 ? 1 : 200).Append("seedA");
				bool threw = false;
				SetStringBuilderAllocationFailure(failAt);
				try { AppendCustomAllocationValue(builder, info, format, parsed, box, form); SetStringBuilderAllocationFailure(0); }
				catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); threw = true; }
				var snapshot = builder.ToString();
				if (threw)
				{
					if (!CustomAllocationPrefix(snapshot, expected)) return 13;
					builder.Length = 5;
					AppendCustomAllocationValue(builder, info, format, parsed, box, form);
					if (builder.ToString() != expected || !CustomAllocationPrefix(snapshot, expected)) return 14;
				}
				else if (snapshot != expected) return 15;
				builder.Clear().Append("tail");
				if (builder.ToString() != "tail") return 16;
				if (!threw) { reachedSuccess = true; break; }
			}
			if (!reachedSuccess) return 17;
		}
		return 42;
	}

	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	public static unsafe int PrimitiveGenericSize<T>() where T : unmanaged => sizeof(T);

	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	public static unsafe int PrimitiveGenericSizeBranch<T>() where T : unmanaged => sizeof(T) == 2 ? 17 : 29;

	public static int CoreLibPrimitiveGenericSizesEntry() =>
		PrimitiveGenericSize<bool>() == 1 && PrimitiveGenericSize<byte>() == 1 && PrimitiveGenericSize<char>() == 2 &&
		PrimitiveGenericSize<short>() == 2 && PrimitiveGenericSize<int>() == 4 &&
		PrimitiveGenericSizeBranch<char>() == 17 && PrimitiveGenericSizeBranch<byte>() == 29 && PrimitiveGenericSizeBranch<int>() == 29 ? 42 : 1;

	public static unsafe int CoreLibByteSpanProjectionOwnersEntry()
	{
		byte[] owner = new byte[3];
		owner[0] = 31; owner[1] = 42; owner[2] = 53;
		Span<byte> span = new(ref owner[1]);
		byte[] readonlyOwner = new byte[3];
		readonlyOwner[2] = 53;
		ReadOnlySpan<byte> readonlySpan = new(ref readonlyOwner[2]);
		ref byte readonlyBorrowed = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(readonlySpan);
		ref byte borrowed = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(span);
		owner = null!;
		readonlyOwner = null!;
		span = default;
		readonlySpan = default;
		System.GC.Collect();
		byte[] pressure = new byte[3];
		pressure[0] = 91; pressure[1] = 92; pressure[2] = 93;
		System.GC.Collect();
		if (borrowed != 42 || readonlyBorrowed != 53) return 1;
		borrowed = 71;
		System.GC.Collect();
		if (borrowed != 71 || readonlyBorrowed != 53) return 2;
		System.GC.KeepAlive(pressure);
		Span<byte> empty = default;
		ReadOnlySpan<byte> readonlyEmpty = default;
		return System.Runtime.CompilerServices.Unsafe.AsPointer(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(empty)) == null &&
			System.Runtime.CompilerServices.Unsafe.AsPointer(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(readonlyEmpty)) == null ? 42 : 3;
	}

}
