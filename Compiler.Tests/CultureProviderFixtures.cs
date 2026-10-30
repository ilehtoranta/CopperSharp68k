/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Globalization;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private class HandlerDerivedCulture : CultureInfo
	{
		public NumberFormatInfo Info;
		public int Queries;
		public int NumberReads;
		public HandlerDerivedCulture() : base("") => Info = new NumberFormatInfo { NegativeSign = "derived", NumberDecimalSeparator = "::" };
		public override object? GetFormat(Type? type)
		{
			Queries++; System.GC.Collect();
			return base.GetFormat(type);
		}
		public override NumberFormatInfo NumberFormat
		{
			get { NumberReads++; System.GC.Collect(); return Info; }
			set => Info = value;
		}
	}

	private sealed class HandlerCultureLeaf : HandlerDerivedCulture
	{
		public object? Extra = new object();
		public override DateTimeFormatInfo DateTimeFormat { get => null!; set { } }
		public override string ToString() => "culture";
	}

	private sealed class HandlerExplicitCulture : HandlerDerivedCulture, IFormatProvider
	{
		public int ExplicitQueries;
		object? IFormatProvider.GetFormat(Type? type)
		{
			ExplicitQueries++; System.GC.Collect();
			return type == typeof(NumberFormatInfo) ? Info : null;
		}
	}

	private sealed class HandlerReimplementedCulture : CultureInfo, IFormatProvider
	{
		public HandlerReimplementedCulture() : base("") { }
	}

	public static int CoreLibStringBuilderCultureExactEntry()
	{
		var provider = new CultureInfo("");
		provider.NumberFormat = new NumberFormatInfo { NegativeSign = "exact", NumberDecimalSeparator = "::" };
		var builder = new StringBuilder(1);
		var handler = new StringBuilder.AppendInterpolatedStringHandler(2, 2, builder, provider);
		handler.AppendLiteral("["); handler.AppendFormatted(-12, "F2"); handler.AppendLiteral("|");
		handler.AppendFormatted(-12, 16, "D4"); handler.AppendLiteral("]");
		System.GC.Collect(); builder.Append(ref handler);
		return builder.ToString() == "[exact12::00|       exact0012]" ? 42 : 1;
	}

	public static int CoreLibStringBuilderCultureInvariantEntry()
	{
		var provider = CultureInfo.InvariantCulture;
		if (provider != CultureInfo.InvariantCulture || !provider.IsReadOnly || provider.GetType() != typeof(CultureInfo) || provider.Name != "") return 1;
		System.GC.Collect();
		var info = provider.NumberFormat;
		if (!info.IsReadOnly || info != provider.NumberFormat || info != NumberFormatInfo.InvariantInfo) return 2;
		try { provider.NumberFormat = new NumberFormatInfo(); return 3; }
		catch (InvalidOperationException) { }
		try { provider.NumberFormat = null!; return 4; }
		catch (ArgumentNullException) { }
		try { info.NegativeSign = "changed"; return 5; }
		catch (InvalidOperationException) { }
		try { info.NumberGroupSizes = new int[] { 2 }; return 6; }
		catch (InvalidOperationException) { }
		try { info.PercentDecimalDigits = 101; return 7; }
		catch (ArgumentOutOfRangeException) { }
		var builder = new StringBuilder(1);
		builder.Append(provider, $"{-12:F2}|{-1234:N1}|{12:C0}|{12:P0}");
		if (builder.ToString() != "-12.00|-1,234.0|¤12|1,200 %") return 8;
		System.GC.Collect(); builder.Clear();
		builder.Append(provider, $"{long.MinValue:D20}");
		if (builder.ToString() != "-09223372036854775808") return 9;
		return CultureInfo.InvariantCulture == provider && provider.NumberFormat == info && info.NegativeSign == "-" ? 42 : 10;
	}

	public static int CoreLibStringBuilderCultureStateEntry()
	{
		var provider = new CultureInfo("", false);
		if (provider.GetType() != typeof(CultureInfo) || provider.Name != "") return 1;
		var info = provider.NumberFormat;
		if (info != provider.NumberFormat || info != provider.GetFormat(typeof(NumberFormatInfo)) || provider.GetFormat(typeof(ICustomFormatter)) is not null) return 2;
		info.NegativeSign = "neg";
		System.GC.Collect();
		var builder = new StringBuilder(1);
		builder.Append(provider, $"{-12:F2}");
		if (builder.ToString() != "neg12.00") return 3;
		try { provider.NumberFormat = null!; return 4; }
		catch (ArgumentNullException) { }
		if (provider.NumberFormat != info) return 5;
		var replacement = new NumberFormatInfo { NegativeSign = "other", NumberDecimalSeparator = ":" };
		provider.NumberFormat = replacement;
		System.GC.Collect(); builder.Clear();
		builder.Append(provider, $"{-12:F2}");
		if (builder.ToString() != "other12:00" || provider.NumberFormat != replacement) return 6;
		try { _ = new CultureInfo(null!); return 7; }
		catch (ArgumentNullException) { }
		try { _ = new CultureInfo(null!, false); return 8; }
		catch (ArgumentNullException) { }
		return 42;
	}

	public static int CoreLibStringBuilderCultureNumberCloneEntry()
	{
		var original = new NumberFormatInfo { NegativeSign = "old", NumberDecimalSeparator = ":", NumberGroupSizes = new int[] { 2, 3 } };
		var frozen = NumberFormatInfo.ReadOnly(original);
		if (frozen == original || !frozen.IsReadOnly || original.IsReadOnly || NumberFormatInfo.ReadOnly(frozen) != frozen) return 1;
		original.NegativeSign = "new";
		System.GC.Collect();
		var writable = (NumberFormatInfo)frozen.Clone();
		if (writable == frozen || writable.IsReadOnly || writable.NegativeSign != "old") return 2;
		writable.NegativeSign = "clone";
		writable.NumberGroupSizes = new int[] { 1 };
		System.GC.Collect();
		var builder = new StringBuilder(1);
		builder.Append(frozen, $"{-123456:N1}");
		if (builder.ToString() != "old1,234,56:0") return 3;
		builder.Clear(); builder.Append(writable, $"{-1234:N1}");
		if (builder.ToString() != "clone1,2,3,4:0" || frozen.NumberGroupSizes[0] != 2) return 4;
		try { frozen.NegativeSign = "bad"; return 5; }
		catch (InvalidOperationException) { }
		try { _ = NumberFormatInfo.ReadOnly(null!); return 6; }
		catch (ArgumentNullException) { }
		return 42;
	}

	public static int CoreLibStringBuilderCultureReadOnlyEntry()
	{
		var original = new CultureInfo("");
		original.NumberFormat.NegativeSign = "old";
		var frozen = CultureInfo.ReadOnly(original);
		if (frozen == original || !frozen.IsReadOnly || !frozen.NumberFormat.IsReadOnly || original.IsReadOnly || CultureInfo.ReadOnly(frozen) != frozen) return 1;
		original.NumberFormat.NegativeSign = "new";
		System.GC.Collect();
		var clone = (CultureInfo)frozen.Clone();
		if (clone.IsReadOnly || clone.NumberFormat.IsReadOnly || clone.NumberFormat == frozen.NumberFormat || clone.NumberFormat.NegativeSign != "old") return 2;
		clone.NumberFormat.NegativeSign = "clone";
		var builder = new StringBuilder(1);
		builder.Append(frozen, $"{-12:F2}");
		if (builder.ToString() != "old12.00") return 3;
		builder.Clear(); builder.Append(clone, $"{-12:F2}");
		if (builder.ToString() != "clone12.00" || frozen.NumberFormat.NegativeSign != "old") return 4;
		var lazy = CultureInfo.ReadOnly(new CultureInfo(""));
		System.GC.Collect();
		if (!lazy.NumberFormat.IsReadOnly || ((CultureInfo)lazy.Clone()).NumberFormat.IsReadOnly) return 5;
		try { _ = CultureInfo.ReadOnly(null!); return 6; }
		catch (ArgumentNullException) { }
		return 42;
	}

	public static int CoreLibStringBuilderCultureInterfacesEntry()
	{
		var explicitCulture = new HandlerExplicitCulture();
		IFormatProvider provider = explicitCulture;
		var builder = new StringBuilder(64);
		builder.Append(provider, $"{-12:F2}");
		if (builder.ToString() != "derived12::00" || explicitCulture.ExplicitQueries != 2 || explicitCulture.Queries != 0 || explicitCulture.NumberReads != 0) return 1;
		var reimplemented = new HandlerReimplementedCulture();
		reimplemented.NumberFormat = new NumberFormatInfo { NegativeSign = "impl", NumberDecimalSeparator = ";" };
		builder.Clear(); builder.Append(reimplemented, $"{-12:F2}");
		if (builder.ToString() != "impl12;00") return 2;
		CultureInfo leaf = new HandlerCultureLeaf();
		System.GC.Collect(); builder.Clear(); builder.Append(leaf, $"{-12:F2}");
		if (builder.ToString() != "derived12::00" || ((HandlerCultureLeaf)leaf).Extra is null) return 3;
		return leaf.GetFormat(typeof(DateTimeFormatInfo)) is null && leaf.ToString() == "culture" ? 42 : 4;
	}

	public static int CoreLibStringBuilderCultureDerivedEntry()
	{
		var provider = new HandlerDerivedCulture();
		var builder = new StringBuilder(64);
		var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder, provider);
		if (provider.Queries != 1 || provider.NumberReads != 0) return 1;
		handler.AppendFormatted(-12, "F2");
		if (builder.ToString() != "derived12::00" || provider.Queries != 2 || provider.NumberReads != 1) return 2;
		builder.Clear(); handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder, provider);
		handler.AppendFormatted(-12, 16, "D4");
		return builder.ToString() == "     derived0012" && provider.Queries == 5 && provider.NumberReads == 2 ? 42 : 3;
	}
}
