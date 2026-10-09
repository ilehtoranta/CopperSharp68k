/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private sealed class HandlerCloneValue
	{
		public static int Constructed;
		public static int Finalized;
		public string Text;
		public object Shared;
		public long Bits;
		public HandlerCloneValue(string text, object shared)
		{
			Constructed++; Text = text; Shared = shared; Bits = unchecked((long)0x8123_4567_89AB_CDEFul);
		}
		~HandlerCloneValue() => Finalized++;
		public HandlerCloneValue Copy() => (HandlerCloneValue)MemberwiseClone();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int CheckMemberwiseCloneValues()
	{
		var shared = new object();
		var text = new char[3]; text[0] = 'n'; text[1] = '\0'; text[2] = '\uD800';
		var original = new HandlerCloneValue(new string(new ReadOnlySpan<char>(text)), shared);
		var copy = original.Copy();
		GC.Collect();
		if (HandlerCloneValue.Constructed != 1 || HandlerCloneValue.Finalized != 0 || copy == original || copy.GetType() != original.GetType()) return 1;
		if (copy.Text != original.Text || copy.Shared != shared || copy.Bits != original.Bits || copy.Text[2] != '\uD800') return 2;
		copy.Bits = 17; copy.Text = "copy";
		if (original.Bits != unchecked((long)0x8123_4567_89AB_CDEFul) || original.Text.Length != 3) return 3;
		GC.KeepAlive(original); GC.KeepAlive(copy);
		return 42;
	}

	public static int CoreLibStringBuilderMemberwiseCloneEntry()
	{
		HandlerCloneValue.Constructed = 0; HandlerCloneValue.Finalized = 0;
		if (CheckMemberwiseCloneValues() != 42) return 1;
		GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
		if (HandlerCloneValue.Finalized != 2) return 2;
		var values = new byte[3]; values[0] = 1; values[1] = 2; values[2] = 255;
		var bytes = (byte[])values.Clone();
		if (bytes == values || bytes.Length != 3 || bytes[2] != 255) return 3;
		bytes[0] = 9;
		if (values[0] != 1) return 4;
		var shared = new object();
		var references = new object[] { shared, "text", null! };
		var copy = (object[])references.Clone();
		GC.Collect();
		if (copy == references || copy.Length != 3 || copy[0] != shared || copy[1] != references[1] || copy[2] is not null) return 5;
		copy[1] = "changed";
		if ((string)references[1] != "text") return 6;
		var builder = new System.Text.StringBuilder(1);
		builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"{bytes[0]:D2}");
		return builder.ToString() == "09" ? 42 : 7;
	}

	public static int CoreLibNumberProviderCloneAllocationFailureEntry()
	{
		var original = new System.Globalization.NumberFormatInfo { NegativeSign = "source" };
		SetStringBuilderAllocationFailure(1);
		try { _ = original.Clone(); SetStringBuilderAllocationFailure(0); return 1; }
		catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
		if (original.IsReadOnly || original.NegativeSign != "source") return 2;
		SetStringBuilderAllocationFailure(1);
		try { _ = System.Globalization.NumberFormatInfo.ReadOnly(original); SetStringBuilderAllocationFailure(0); return 3; }
		catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
		if (original.IsReadOnly || original.NegativeSign != "source") return 4;
		var frozen = System.Globalization.NumberFormatInfo.ReadOnly(original);
		SetStringBuilderAllocationFailure(1);
		var same = System.Globalization.NumberFormatInfo.ReadOnly(frozen);
		SetStringBuilderAllocationFailure(0);
		if (same != frozen || !frozen.IsReadOnly) return 5;
		var clone = (System.Globalization.NumberFormatInfo)frozen.Clone();
		clone.NegativeSign = "copy";
		var builder = new System.Text.StringBuilder(1);
		builder.Append(clone, $"{-12:F2}");
		return builder.ToString() == "copy12.00" && frozen.NegativeSign == "source" ? 42 : 6;
	}
}
