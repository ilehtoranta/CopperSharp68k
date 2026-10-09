/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Globalization;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private sealed class ArrayFormatProvider : IFormatProvider, ICustomFormatter
	{
		public int Queries, Calls, FailureAt;
		public bool Valid = true;
		public InvalidOperationException? Error;
		public object? GetFormat(Type? type)
		{
			Queries++; GC.Collect();
			if (type != typeof(ICustomFormatter)) Valid = false;
			return this;
		}
		public string Format(string? format, object? value, IFormatProvider? provider)
		{
			Calls++; GC.Collect();
			if (!ReferenceEquals(provider, this)) Valid = false;
			if (Calls == FailureAt) throw Error!;
			if (value is int number)
			{
				if (number != -12 || format is not (null or "D4")) Valid = false;
				return new string("numΩ".AsSpan());
			}
			if (value is string text)
			{
				if (text != "RΩ\0\uD800" || format != null) Valid = false;
				return new string("TΩ\0\uD800".AsSpan());
			}
			if (value != null || format != null) Valid = false;
			return new string("<null>".AsSpan());
		}
	}

	private static object?[] ProviderArrayArguments()
	{
		var result = new object?[3];
		result[0] = -12; result[1] = new string("RΩ\0\uD800".AsSpan());
		return result;
	}

	public static int CoreLibStringBuilderProviderArrayFormatEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			for (var roomy = 0; roomy < 2; roomy++)
			for (var kind = 0; kind < 3; kind++)
			for (var form = 0; form < 4; form++)
			{
				var custom = new ArrayFormatProvider();
				IFormatProvider? provider = kind == 0 ? null : kind == 1 ? new NumberFormatInfo { NegativeSign = "~Ω" } : custom;
				var arguments = ProviderArrayArguments(); GC.Collect();
				var builder = new StringBuilder(roomy == 0 ? 1 : 64).Append('A');
				var format = form == 0 ? "B{1}|{0:D4}|{2}C" : form == 1 ? "{{{0,8:D4}}}|{1,-6}|{0}|{2}" : form == 2 ? "literalΩ\0\uD800" : "";
				var result = builder.AppendFormat(provider, format, arguments);
				var expected = form == 0 ? kind == 2 ? "ABTΩ\0\uD800|numΩ|<null>C" : kind == 1 ? "ABRΩ\0\uD800|~Ω0012|C" : "ABRΩ\0\uD800|-0012|C" :
					form == 1 ? kind == 2 ? "A{    numΩ}|TΩ\0\uD800  |numΩ|<null>" : kind == 1 ? "A{  ~Ω0012}|RΩ\0\uD800  |~Ω12|" : "A{   -0012}|RΩ\0\uD800  |-12|" :
					form == 2 ? "AliteralΩ\0\uD800" : "A";
				var snapshot = builder.ToString(); arguments[0] = null; arguments[1] = null;
				CollectReferenceFormattingGarbage();
				if (!ReferenceEquals(result, builder) || snapshot != expected || builder.ToString() != expected) return 10 + form;
				if (kind == 2 && (!custom.Valid || custom.Queries != 1 || custom.Calls != (form == 0 ? 3 : form == 1 ? 4 : 0))) return 20 + form;
				builder.Clear().AppendFormat(provider, "Z", arguments); GC.Collect();
				if (snapshot != expected || builder.ToString() != "Z") return 30 + form;
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	public static int CoreLibStringBuilderProviderArrayFormatAllocationEntry()
	{
		for (var roomy = 0; roomy < 2; roomy++)
		for (var align = 0; align < 2; align++)
		{
			var format = align == 0 ? "[{0,8:D4}|{1,-8}|{2}]" : "[{0,-8:D4}|{1,8}|{2}]";
			var text = align == 0 ? "[    num\u03A9|T\u03A9\0\uD800    |<null>]" : "[num\u03A9    |    T\u03A9\0\uD800|<null>]";
			var expected = "S" + text;
			var failures = 0;
			var succeeded = false;
			for (var failAt = 1; failAt <= 64; failAt++)
			{
				var provider = new ArrayFormatProvider();
				var arguments = ProviderArrayArguments();
				var builder = new StringBuilder(roomy == 0 ? 1 : 80).Append('S');
				var snapshot = builder.ToString();
				SetStringBuilderAllocationFailure(failAt);
				try { builder.AppendFormat(provider, format, arguments); SetStringBuilderAllocationFailure(0); succeeded = true; }
				catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); failures++; }
				if (!provider.Valid || snapshot != "S" || (int)arguments[0]! != -12 || (string?)arguments[1] != "R\u03A9\0\uD800" || arguments[2] != null) return 1;
				if (builder.Length < 1 || builder.Length > expected.Length) return 2;
				for (var index = 0; index < builder.Length; index++) if (builder[index] != expected[index]) return 3;
				if (succeeded && (builder.ToString() != expected || failures < 3 || provider.Calls != 3 || provider.Queries != 1)) return 4;
				provider.Calls = provider.Queries = 0;
				builder.Clear().AppendFormat(provider, format, arguments);
				if (builder.ToString() != text || snapshot != "S" || provider.Calls != 3 || provider.Queries != 1 || !provider.Valid) return 5;
				if (succeeded) break;
			}
			if (!succeeded) return 6;
		}
		return 42;
	}

	public static int CoreLibStringBuilderProviderArrayFormatContractsEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			for (var failure = 0; failure < 3; failure++)
			{
				var provider = new ArrayFormatProvider();
				var arguments = failure == 1 ? ProviderArrayArguments() : null;
				var format = failure < 2 ? null : "valid";
				var builder = new StringBuilder(1).Append('A');
				try { builder.AppendFormat(provider, format!, arguments!); return 1; }
				catch (ArgumentNullException error) { if (error.ParamName != (failure < 2 ? "format" : "args")) return 2; }
				if (provider.Queries != 0 || provider.Calls != 0 || builder.ToString() != "A") return 3;
			}
			for (var roomy = 0; roomy < 2; roomy++)
			for (var kind = 0; kind < 3; kind++)
			{
				var provider = new ArrayFormatProvider();
				IFormatProvider? supplied = kind == 0 ? null : kind == 1 ? new NumberFormatInfo { NegativeSign = "~Ω" } : provider;
				var arguments = ProviderArrayArguments();
				var builder = new StringBuilder(roomy == 0 ? 1 : 64).Append('A');
				var original = builder.ToString();
				try { builder.AppendFormat(supplied, "{3}", arguments); return 4; } catch (FormatException) { }
				if (builder.ToString() != "A" || kind == 2 && (provider.Queries != 1 || provider.Calls != 0)) return 5;
				try { builder.AppendFormat(supplied, "B{0}C{", arguments); return 6; } catch (FormatException) { }
				var expected = kind == 2 ? "ABnumΩC" : kind == 1 ? "AB~Ω12C" : "AB-12C";
				var snapshot = builder.ToString(); GC.Collect();
				if (snapshot != expected || original != "A" || kind == 2 && (provider.Queries != 2 || provider.Calls != 1 || !provider.Valid)) return 7;
				builder.Clear().AppendFormat(supplied, "Z{1}!", arguments);
				if (builder.ToString() != (kind == 2 ? "ZTΩ\0\uD800!" : "ZRΩ\0\uD800!") || snapshot != expected) return 8;
				var limited = new StringBuilder(roomy == 0 ? 1 : 4, 4).Append('A');
				try { limited.AppendFormat(supplied, "B{0}C", arguments); return 9; }
				catch (ArgumentOutOfRangeException error) { if (error.ParamName != "valueCount" || error.ActualValue != null) return 10; }
				var prefix = limited.ToString(); GC.Collect();
				if (prefix != "AB" || limited.Length != 2 || limited.MaxCapacity != 4) return 11;
				limited.Clear().AppendFormat(supplied, "Z", arguments);
				if (limited.ToString() != "Z" || prefix != "AB") return 12;
			}
			for (var roomy = 0; roomy < 2; roomy++)
			{
				var error = new InvalidOperationException("array formatter");
				var provider = new ArrayFormatProvider { FailureAt = 2, Error = error };
				var arguments = ProviderArrayArguments();
				var builder = new StringBuilder(roomy == 0 ? 1 : 64).Append('A');
				try { builder.AppendFormat(provider, "B{0}C{1}D", arguments); return 13; }
				catch (InvalidOperationException caught) { if (!ReferenceEquals(caught, error)) return 14; }
				var snapshot = builder.ToString(); CollectReferenceFormattingGarbage();
				if (snapshot != "ABnumΩC" || provider.Calls != 2 || provider.Queries != 1 || !provider.Valid) return 15;
				provider.FailureAt = 0;
				builder.Clear().AppendFormat(provider, "Z{1}!", arguments);
				if (builder.ToString() != "ZTΩ\0\uD800!" || provider.Calls != 3 || provider.Queries != 2 || !provider.Valid || snapshot != "ABnumΩC") return 16;
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}
}
