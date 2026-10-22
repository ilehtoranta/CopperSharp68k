/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private static string StringBuilderStableSnapshot(StringBuilder builder) => builder.ToString();
	private static string StringBuilderUnknownSnapshot(object value) => value.ToString()!;
	private static string StringBuilderProducedSnapshot() => new StringBuilder(1).Append("x").ToString();
	private static string StringBuilderConstructedSnapshot() => new StringBuilder(1).ToString();
	private static object StringBuilderObjectProducer() => new StringBuilder(1);
	private static string StringBuilderUnknownProducedSnapshot() => StringBuilderObjectProducer().ToString()!;

	public static int StringBuilderStableNullSnapshotEntry()
	{
		try { StringBuilderStableSnapshot(null!); return 1; }
		catch (NullReferenceException) { return 42; }
	}

	public static int StringBuilderStableArgumentSnapshotEntry()
	{
		var builder = new StringBuilder(1);
		builder.Append("a\0Ω\uD800");
		var first = StringBuilderStableSnapshot(builder);
		builder.Append("tail");
		GC.Collect();
		return first == "a\0Ω\uD800" && StringBuilderStableSnapshot(builder) == "a\0Ω\uD800tail" ? 42 : 1;
	}

	public static int StringBuilderStableConstructorValidationEntry()
	{
		for (var scenario = 0; scenario < 6; scenario++)
		{
			var capacity = scenario == 0 ? -1 : scenario == 1 ? int.MinValue : scenario == 5 ? 2 : 1;
			var maximum = scenario == 2 ? 0 : scenario == 3 ? -1 : scenario == 4 ? int.MinValue : scenario == 5 ? 1 : int.MaxValue;
			try { new StringBuilder(capacity, maximum); return 10 + scenario; }
			catch (ArgumentOutOfRangeException) { GC.Collect(); }
		}
		return 42;
	}

	public static int StringBuilderStableEditingEntry()
	{
		var source = new[] { '!', 'a', '\0', 'Ω', '\uD800', '?' };
		var builder = new StringBuilder(1);
		builder.Append(source, 1, 4);
		builder.Insert(1, source, 2, 2);
		builder.Replace('Ω', 'x');
		builder.Remove(0, 1);
		GC.Collect();
		if (builder.ToString() != "\0x\0x\uD800" || builder.ToString(1, 3) != "x\0x") return 1;
		var destination = new char[7];
		destination[0] = '!';
		destination[6] = '?';
		builder.CopyTo(0, destination, 1, 5);
		GC.Collect();
		if (destination[0] != '!' || destination[1] != '\0' || destination[2] != 'x' || destination[3] != '\0' ||
			destination[4] != 'x' || destination[5] != '\uD800' || destination[6] != '?') return 3;
		source[3] = 'z';
		return builder.ToString() == "\0x\0x\uD800" ? 42 : 2;
	}

	public static int StringBuilderStableEditingValidationEntry()
	{
		var source = new[] { 'a', 'b', 'c', 'd' };
		var builder = new StringBuilder(8);
		builder.Append(source, 0, 4);
		try { builder.Append((char[]?)null, 0, 1); return 1; }
		catch (ArgumentNullException) { GC.Collect(); }
		for (var scenario = 0; scenario < 4; scenario++)
		{
			try
			{
				if (scenario == 0) builder.Append(source, -1, 1);
				else if (scenario == 1) builder.Append(source, 0, -1);
				else if (scenario == 2) builder.Append(source, 1, 4);
				else builder.ToString(-1, 1);
				return 10 + scenario;
			}
			catch (ArgumentOutOfRangeException) { GC.Collect(); }
		}
		try { builder.CopyTo(0, new char[2], 0, 3); return 2; }
		catch (ArgumentOutOfRangeException) { return 3; }
		catch (ArgumentException) { GC.Collect(); }
		return builder.ToString() == "abcd" ? 42 : 4;
	}

	public static int StringBuilderStablePrimitiveTextEntry()
	{
		var builder = new StringBuilder(1);
		builder.Append(true).Append('|').Append(false);
		builder.Append("\0Ω\uD800".AsSpan());
		GC.Collect();
		if (!builder.Equals("True|False\0Ω\uD800".AsSpan())) return 1;
		if (builder.Equals("True|False\0Ω\uD801".AsSpan())) return 2;
		return builder.ToString() == "True|False\0Ω\uD800" ? 42 : 3;
	}

	public static int StringBuilderStableCultureEntry()
	{
		var previous = System.Globalization.CultureInfo.CurrentCulture;
		var previousDefault = System.Globalization.CultureInfo.DefaultThreadCurrentCulture;
		try
		{
			var invariant = System.Globalization.CultureInfo.InvariantCulture;
			var invariantFormat = invariant.NumberFormat;
			if (!ReferenceEquals(invariant.NumberFormat, invariantFormat)) return 4;
			try { invariant.NumberFormat = invariantFormat; return 5; }
			catch (InvalidOperationException) { GC.Collect(); }
			var culture = new System.Globalization.CultureInfo("");
			var derived = new StringBuilderStableDerivedCulture();
			System.Globalization.CultureInfo derivedReceiver = derived;
			var derivedFormat = derivedReceiver.NumberFormat;
			GC.Collect();
			if (!ReferenceEquals(derivedReceiver.NumberFormat, derivedFormat) || derived.Calls != 2 || derived.Marker != 73) return 6;
			System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
			System.Globalization.CultureInfo.CurrentCulture = culture;
			GC.Collect();
			if (!ReferenceEquals(System.Globalization.CultureInfo.CurrentCulture, culture) ||
				!ReferenceEquals(System.Globalization.CultureInfo.DefaultThreadCurrentCulture, culture) ||
				!ReferenceEquals(System.Globalization.CultureInfo.InvariantCulture, invariant)) return 1;
			try { System.Globalization.CultureInfo.CurrentCulture = null!; return 2; }
			catch (ArgumentNullException) { GC.Collect(); }
			if (StringBuilderProducedSnapshot() != "x") return 7;
			if (StringBuilderConstructedSnapshot() != "") return 8;
			var builder = new StringBuilder(1).Append("culture");
			return builder.ToString() == "culture" && ReferenceEquals(System.Globalization.CultureInfo.CurrentCulture, culture) ? 42 : 3;
		}
		finally
		{
			System.Globalization.CultureInfo.CurrentCulture = previous;
			System.Globalization.CultureInfo.DefaultThreadCurrentCulture = previousDefault;
		}
	}

	private sealed class StringBuilderStableDerivedCulture : System.Globalization.CultureInfo
	{
		public int Calls;
		public int Marker = 73;
		public StringBuilderStableDerivedCulture() : base("") { }
		public override System.Globalization.NumberFormatInfo NumberFormat {
			get { Calls++; return base.NumberFormat; }
			set { base.NumberFormat = value; }
		}
	}
}
