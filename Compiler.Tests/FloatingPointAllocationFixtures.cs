/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Globalization;
using System.Text;
using CopperSharp.Sdk.Amiga;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static int CoreLibFloatingAlignedMutationOracleEntry()
	{
		var info = NumberFormatInfo.InvariantInfo;
		for (var precision = 0; precision < 2; precision++)
		for (var style = 0; style < 2; style++)
		{
			var format = style == 0 ? "F512" : new StringBuilder().Append('0', 320).Append(".0000").ToString();
			var value = style == 0 ? new StringBuilder("-123.5").Append('0', 511).ToString() : new StringBuilder("-").Append('0', 317).Append("123.5000").ToString();
			object box = precision == 0 ? (object)(-123.5f) : -123.5d;
			for (var direction = 0; direction < 2; direction++)
			{
				var alignment = (value.Length + 5) * (direction == 0 ? 1 : -1);
				var source = new StringBuilder("A{0,").Append(alignment).Append(':').Append(format).Append('}').ToString();
				var parsed = CompositeFormat.Parse(source);
				var expected = direction == 0 ? "seedA     " + value : "seedA" + value + "     ";
				for (var form = 0; form < 8; form++)
				for (var capacity = 0; capacity < 2; capacity++)
				{
					var builder = new StringBuilder(capacity == 0 ? 5 : 1024).Append("seed");
					var snapshot = builder.ToString();
					AppendFloatingContract(builder, info, precision != 0, box, parsed, source, format, form, alignment);
					if (builder.ToString() != expected || snapshot != "seed") return 100 + form;
				}
			}
		}
		return 42;
	}

	public static int CoreLibFloatingCrossChunkInsertAllocationContractsEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		try
		{
			var gap = new StringBuilder("seed").Append('\0', 6).Append("TAIL").ToString();
			var noValues = new object?[0];
			for (var precision = 0; precision < 2; precision++)
			{
				object box = precision == 0 ? (object)(-123.5f) : -123.5d;
				for (var boxed = 0; boxed < 2; boxed++)
				for (var position = 0; position < 2; position++)
				{
					var form = position * 2 + boxed;
					var expected = FloatingIndexedExpected(form);
					var warm = new StringBuilder(4).Append("seed").Append("TAIL");
					ApplyFloatingIndexedAllocation(warm, precision != 0, box, noValues, form);
					if (warm.ToString() != expected) return 100 + form;
					for (var failAt = 1; failAt <= 4; failAt++)
					{
						var builder = new StringBuilder(4).Append("seed").Append("TAIL");
						var snapshot = builder.ToString();
						var threw = false;
						SetStringBuilderAllocationFailure(failAt);
						try { ApplyFloatingIndexedAllocation(builder, precision != 0, box, noValues, form); }
						catch (OutOfMemoryException) { threw = true; }
						finally { SetStringBuilderAllocationFailure(0); }
						var failed = builder.ToString();
						if (threw != (failAt < 4) || snapshot != "seedTAIL") return 200 + form;
						if (failed != (failAt == 1 ? "seedTAIL" : failAt < 4 ? gap : expected)) return 300 + form;
						if (builder.Length != failed.Length) return 400 + form;
						builder.Clear().Append("seedTAIL");
						ApplyFloatingIndexedAllocation(builder, precision != 0, box, noValues, form);
						if (builder.ToString() != expected || snapshot != "seedTAIL") return 500 + form;
						if (failed != (failAt == 1 ? "seedTAIL" : failAt < 4 ? gap : expected)) return 600 + form;
					}
				}
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	public static int CoreLibFloatingFirstUseAllocationFailureEntry()
	{
		var info = NumberFormatInfo.InvariantInfo;
		TypeInitializationException? first = null;
		for (var precision = 0; precision < 2; precision++)
		{
			var builder = new StringBuilder(5).Append("seed");
			var snapshot = builder.ToString();
			object box = precision == 0 ? (object)(-123.5f) : -123.5d;
			TypeInitializationException? failure = null;
			SetStringBuilderAllocationFailure(1);
			try
			{
				var handler = new StringBuilder.AppendInterpolatedStringHandler(1, 1, builder, info);
				handler.AppendLiteral("A");
				handler.AppendFormatted(box, 522, "F512");
				builder.Append(ref handler);
			}
			catch (TypeInitializationException error) { failure = error; }
			catch (OutOfMemoryException) { return 100 + precision; }
			finally { SetStringBuilderAllocationFailure(0); }
			if (failure is null) return 200 + precision;
			if (precision == 0) first = failure;
			else if (!ReferenceEquals(first, failure)) return 300;
			if (snapshot != "seed" || builder.ToString() != "seedA") return 400 + precision;
			builder.Clear().Append("tail");
			if (builder.ToString() != "tail" || snapshot != "seed") return 500 + precision;
		}
		return 42;
	}

	private static string FloatingIndexedExpected(int form) => form < 2 ? "-123.5seedTAIL" : form < 4 ? "se-123.5edTAIL" :
		form < 6 ? "seedTAIL-123.5" : form == 6 ? "seedTAIL|-123.5" : "seedTAIL::-123.5";

	private static void ApplyFloatingIndexedAllocation(StringBuilder builder, bool wide, object box, object?[] values, int form)
	{
		if (form < 6)
		{
			var index = form < 2 ? 0 : form < 4 ? 2 : builder.Length;
			if ((form & 1) != 0) builder.Insert(index, box);
			else if (wide) builder.Insert(index, -123.5d);
			else builder.Insert(index, -123.5f);
		}
		else if (form == 6) builder.AppendJoin('|', new ReadOnlySpan<object?>(values));
		else builder.AppendJoin("::", new ReadOnlySpan<object?>(values));
	}

	public static int CoreLibFloatingIndexedMutationOracleEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		try
		{
			for (var precision = 0; precision < 2; precision++)
			{
				object box = precision == 0 ? (object)(-123.5f) : -123.5d;
				object?[] values = [null, box];
				for (var form = 0; form < 8; form++)
				for (var capacity = 0; capacity < 2; capacity++)
				{
					var builder = new StringBuilder(capacity == 0 ? 8 : 64).Append("seedTAIL");
					var snapshot = builder.ToString();
					ApplyFloatingIndexedAllocation(builder, precision != 0, box, values, form);
					if (builder.ToString() != FloatingIndexedExpected(form) || snapshot != "seedTAIL") return 100 + form;
				}
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	public static int CoreLibFloatingIndexedAllocationContractsEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		try
		{
			for (var precision = 0; precision < 2; precision++)
			{
				object box = precision == 0 ? (object)(-123.5f) : -123.5d;
				object?[] values = [null, box];
				for (var form = 0; form < 8; form++)
				for (var capacity = 0; capacity < 2; capacity++)
				{
					var expected = FloatingIndexedExpected(form);
					var warm = new StringBuilder(capacity == 0 ? 8 : 64).Append("seedTAIL");
					ApplyFloatingIndexedAllocation(warm, precision != 0, box, values, form);
					if (warm.ToString() != expected) return 100 + form;
					var completed = false;
					for (var failAt = 1; failAt <= 16; failAt++)
					{
						var builder = new StringBuilder(capacity == 0 ? 8 : 64).Append("seedTAIL");
						var snapshot = builder.ToString();
						var threw = false;
						SetStringBuilderAllocationFailure(failAt);
						try { ApplyFloatingIndexedAllocation(builder, precision != 0, box, values, form); }
						catch (OutOfMemoryException) { threw = true; }
						finally { SetStringBuilderAllocationFailure(0); }
						var failed = builder.ToString();
						if (snapshot != "seedTAIL") return 200 + form;
						if (form < 6)
						{
							if (failed != (threw ? "seedTAIL" : expected)) return 300 + form;
						}
						else
						{
							if (failed.Length < 8 || failed.Length > expected.Length || !threw && failed != expected) return 400 + form;
							for (var index = 0; index < failed.Length; index++) if (failed[index] != expected[index]) return 500 + form;
						}
						builder.Clear().Append("seedTAIL");
						ApplyFloatingIndexedAllocation(builder, precision != 0, box, values, form);
						if (builder.ToString() != expected || snapshot != "seedTAIL") return 600 + form;
						if (!threw) { completed = true; break; }
					}
					if (!completed) return 700 + form;
				}
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	public static int CoreLibFloatingSymbolAllocationContractsEntry()
	{
		for (var provider = 0; provider < 2; provider++)
		{
			var info = provider == 0 ? NumberFormatInfo.InvariantInfo : new NumberFormatInfo {
				NaNSymbol = "unknown\u03A9\0", PositiveInfinitySymbol = "forever\u03A9\0", NegativeInfinitySymbol = "below\u03A9\0"
			};
			for (var precision = 0; precision < 2; precision++)
			for (var value = 0; value < 3; value++)
			{
				var single = value == 0 ? float.NaN : value == 1 ? float.PositiveInfinity : float.NegativeInfinity;
				var wide = value == 0 ? double.NaN : value == 1 ? double.PositiveInfinity : double.NegativeInfinity;
				var expected = value == 0 ? info.NaNSymbol : value == 1 ? info.PositiveInfinitySymbol : info.NegativeInfinitySymbol;
				var warm = precision == 0 ? single.ToString("G", info) : wide.ToString("G", info);
				if (!ReferenceEquals(warm, expected)) return 100 + value;
				var buffer = new char[expected.Length + 2];
				for (var index = 0; index < buffer.Length; index++) buffer[index] = '#';
				var destination = buffer.AsSpan(1, expected.Length);
				var written = 77;
				SetStringBuilderAllocationFailure(1);
				try
				{
					var text = precision == 0 ? single.ToString("G", info) : wide.ToString("G", info);
					var success = precision == 0 ? single.TryFormat(destination, out written, "G", info) : wide.TryFormat(destination, out written, "G", info);
					if (!ReferenceEquals(text, expected) || !success || written != expected.Length) return 200 + value;
				}
				catch (OutOfMemoryException) { return 300 + value; }
				finally { SetStringBuilderAllocationFailure(0); }
				for (var index = 0; index < buffer.Length; index++)
					if (buffer[index] != (index > 0 && index <= expected.Length ? expected[index - 1] : '#')) return 400 + value;
				if (!ReferenceEquals(warm, expected)) return 500 + value;
			}
		}
		return 42;
	}

	[M68kImport("fixture.floating-allocation-case")]
	private static extern int GetFloatingAllocationCase();

	private static void AppendFloatingDefaultAllocation(StringBuilder builder, bool wide, object box,
		float[] singles, double[] doubles, List<float> singleList, List<double> doubleList,
		object?[] objects, List<object?> objectList, int form)
	{
		if (form == 0) { if (wide) builder.Append(-123.5d); else builder.Append(-123.5f); }
		else if (form == 1) builder.Append(box);
		else if (form == 2) { if (wide) builder.Insert(builder.Length, -123.5d); else builder.Insert(builder.Length, -123.5f); }
		else if (form == 3) builder.Insert(builder.Length, box);
		else if (form == 4) { if (wide) builder.AppendJoin('|', doubles); else builder.AppendJoin('|', singles); }
		else if (form == 5) { if (wide) builder.AppendJoin("::", doubleList); else builder.AppendJoin("::", singleList); }
		else if (form == 6) builder.AppendJoin('|', objects);
		else builder.AppendJoin("::", objectList);
	}

	public static int CoreLibStringBuilderFloatingDefaultAllocationContractsEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		try
		{
			for (var precision = 0; precision < 2; precision++)
			{
				object box = precision == 0 ? (object)(-123.5f) : -123.5d;
				var singles = new float[2]; singles[1] = -123.5f;
				var doubles = new double[2]; doubles[1] = -123.5d;
				var singleList = new List<float>(); singleList.Add(0); singleList.Add(-123.5f);
				var doubleList = new List<double>(); doubleList.Add(0); doubleList.Add(-123.5d);
				object?[] objects = [null, box];
				var objectList = new List<object?>(); objectList.Add(null); objectList.Add(box);
				for (var form = 0; form < 8; form++)
				for (var capacity = 0; capacity < 2; capacity++)
				{
					var expected = form < 4 ? "seed-123.5" : form == 4 ? "seed0|-123.5" : form == 5 ? "seed0::-123.5" : form == 6 ? "seed|-123.5" : "seed::-123.5";
					var completed = false;
					for (var failAt = 1; failAt <= 16; failAt++)
					{
						var builder = new StringBuilder(capacity == 0 ? 5 : 64).Append("seed");
						var snapshot = builder.ToString();
						var threw = false;
						SetStringBuilderAllocationFailure(failAt);
						try { AppendFloatingDefaultAllocation(builder, precision != 0, box, singles, doubles, singleList, doubleList, objects, objectList, form); }
						catch (OutOfMemoryException) { threw = true; }
						finally { SetStringBuilderAllocationFailure(0); }
						var failed = builder.ToString();
						if (snapshot != "seed" || failed.Length < 4 || failed.Length > expected.Length) return 100 + form;
						for (var index = 0; index < failed.Length; index++) if (failed[index] != expected[index]) return 200 + form;
						if (!threw && failed != expected) return 300 + form;
						builder.Length = 4;
						AppendFloatingDefaultAllocation(builder, precision != 0, box, singles, doubles, singleList, doubleList, objects, objectList, form);
						if (builder.ToString() != expected || snapshot != "seed") return 400 + form;
						builder.Clear().Append("tail");
						if (builder.ToString() != "tail" || snapshot != "seed") return 500 + form;
						if (!threw) { completed = true; break; }
					}
					if (!completed) return 600 + form;
				}
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	public static int CoreLibStringBuilderFloatingAllocationContractsEntry()
	{
		var selectedCase = GetFloatingAllocationCase();
		if (selectedCase < 0 || selectedCase >= 64) return 800;
		var info = NumberFormatInfo.InvariantInfo;
		for (var precision = 0; precision < 2; precision++)
		for (var style = 0; style < 2; style++)
		{
			var format = style == 0 ? "F512" : new StringBuilder().Append('0', 320).Append(".0000").ToString();
			var value = style == 0 ? new StringBuilder("-123.5").Append('0', 511).ToString() : new StringBuilder("-").Append('0', 317).Append("123.5000").ToString();
			var alignment = value.Length + 5;
			var source = new StringBuilder("A{0,").Append(alignment).Append(':').Append(format).Append('}').ToString();
			var parsed = CompositeFormat.Parse(source);
			var expected = "seedA     " + value;
			object box = precision == 0 ? (object)(-123.5f) : -123.5d;
			for (var form = 0; form < 8; form++)
			for (var capacity = 0; capacity < 2; capacity++)
			{
				if (((precision * 2 + style) * 8 + form) * 2 + capacity != selectedCase) continue;
				var warm = new StringBuilder(capacity == 0 ? 5 : 1024).Append("seed");
				AppendFloatingContract(warm, info, precision != 0, box, parsed, source, format, form, alignment);
				if (warm.ToString() != expected) return 900 + form;
				var completed = false;
				for (var failAt = 1; failAt <= 128; failAt++)
				{
					var builder = new StringBuilder(capacity == 0 ? 5 : 1024).Append("seed");
					var snapshot = builder.ToString();
					var threw = false;
					SetStringBuilderAllocationFailure(failAt);
					try { AppendFloatingContract(builder, info, precision != 0, box, parsed, source, format, form, alignment); }
					catch (OutOfMemoryException) { threw = true; }
					finally { SetStringBuilderAllocationFailure(0); }
					var failed = builder.ToString();
					if (snapshot != "seed" || failed.Length < 4 || failed.Length > expected.Length) return 100 + form;
					for (var index = 0; index < failed.Length; index++) if (failed[index] != expected[index]) return 200 + form;
					if (!threw && failed != expected) return 300 + form;
					builder.Length = 4;
					AppendFloatingContract(builder, info, precision != 0, box, parsed, source, format, form, alignment);
					if (builder.ToString() != expected || snapshot != "seed") return 400 + form;
					builder.Clear().Append("tail");
					if (builder.ToString() != "tail" || snapshot != "seed") return 500 + form;
					for (var index = 0; index < failed.Length; index++) if (failed[index] != expected[index]) return 600 + form;
					if (!threw) { completed = true; break; }
				}
				if (!completed) return 700 + form;
			}
		}
		return 42;
	}

	public static int CoreLibStringBuilderFloatingLeftAllocationContractsEntry()
	{
		var selectedCase = GetFloatingAllocationCase();
		if (selectedCase < 0 || selectedCase >= 64) return 800;
		var info = NumberFormatInfo.InvariantInfo;
		for (var precision = 0; precision < 2; precision++)
		for (var style = 0; style < 2; style++)
		{
			var format = style == 0 ? "F512" : new StringBuilder().Append('0', 320).Append(".0000").ToString();
			var value = style == 0 ? new StringBuilder("-123.5").Append('0', 511).ToString() : new StringBuilder("-").Append('0', 317).Append("123.5000").ToString();
			var alignment = -(value.Length + 5);
			var source = new StringBuilder("A{0,").Append(alignment).Append(':').Append(format).Append('}').ToString();
			var parsed = CompositeFormat.Parse(source);
			var expected = "seedA" + value + "     ";
			object box = precision == 0 ? (object)(-123.5f) : -123.5d;
			for (var form = 0; form < 8; form++)
			for (var capacity = 0; capacity < 2; capacity++)
			{
				if (((precision * 2 + style) * 8 + form) * 2 + capacity != selectedCase) continue;
				var warm = new StringBuilder(capacity == 0 ? 5 : 1024).Append("seed");
				AppendFloatingContract(warm, info, precision != 0, box, parsed, source, format, form, alignment);
				if (warm.ToString() != expected) return 900 + form;
				var completed = false;
				for (var failAt = 1; failAt <= 128; failAt++)
				{
					var builder = new StringBuilder(capacity == 0 ? 5 : 1024).Append("seed");
					var snapshot = builder.ToString();
					var threw = false;
					SetStringBuilderAllocationFailure(failAt);
					try { AppendFloatingContract(builder, info, precision != 0, box, parsed, source, format, form, alignment); }
					catch (OutOfMemoryException) { threw = true; }
					finally { SetStringBuilderAllocationFailure(0); }
					var failed = builder.ToString();
					if (snapshot != "seed" || failed.Length < 4 || failed.Length > expected.Length) return 100 + form;
					for (var index = 0; index < failed.Length; index++) if (failed[index] != expected[index]) return 200 + form;
					if (!threw && failed != expected) return 300 + form;
					builder.Length = 4;
					AppendFloatingContract(builder, info, precision != 0, box, parsed, source, format, form, alignment);
					if (builder.ToString() != expected || snapshot != "seed") return 400 + form;
					builder.Clear().Append("tail");
					if (builder.ToString() != "tail" || snapshot != "seed") return 500 + form;
					for (var index = 0; index < failed.Length; index++) if (failed[index] != expected[index]) return 600 + form;
					if (!threw) { completed = true; break; }
				}
				if (!completed) return 700 + form;
			}
		}
		return 42;
	}

	public static int CoreLibFloatingLargeAllocationContractsEntry()
	{
		var info = NumberFormatInfo.InvariantInfo;
		for (var precision = 0; precision < 2; precision++)
		for (var style = 0; style < 2; style++)
		{
			var format = style == 0 ? "F512" : new StringBuilder().Append('0', 320).Append(".0000").ToString();
			var expected = style == 0 ? new StringBuilder("-123.5").Append('0', 511).ToString() : new StringBuilder("-").Append('0', 317).Append("123.5000").ToString();
			var warm = precision == 0 ? (-123.5f).ToString(format, info) : (-123.5d).ToString(format, info);
			if (warm != expected) return 100;
			for (var operation = 0; operation < 3; operation++)
			{
				var length = expected.Length - (operation == 2 ? 1 : 0);
				var buffer = new char[length + 2];
				var destination = buffer.AsSpan(1, length);
				var completed = false;
				for (var failAt = 1; failAt <= 16; failAt++)
				{
					for (var index = 0; index < buffer.Length; index++) buffer[index] = '#';
					var written = 77;
					var threw = false;
					var success = false;
					string? text = null;
					SetStringBuilderAllocationFailure(failAt);
					try
					{
						if (operation == 0) text = precision == 0 ? (-123.5f).ToString(format, info) : (-123.5d).ToString(format, info);
						else success = precision == 0 ? (-123.5f).TryFormat(destination, out written, format, info) : (-123.5d).TryFormat(destination, out written, format, info);
					}
					catch (OutOfMemoryException) { threw = true; }
					finally { SetStringBuilderAllocationFailure(0); }
					if (threw && written != 77) return 200 + operation;
					if (!threw && (operation == 0 ? text != expected : operation == 1 ? !success || written != expected.Length : success || written != 0)) return 300 + operation;
					for (var index = 0; index < buffer.Length; index++)
						if (buffer[index] != (!threw && operation == 1 && index > 0 && index <= length ? expected[index - 1] : '#')) return 400 + operation;
					var retry = precision == 0 ? (-123.5f).ToString(format, info) : (-123.5d).ToString(format, info);
					if (retry != expected || warm != expected) return 500 + operation;
					var retrySuccess = precision == 0 ? (-123.5f).TryFormat(destination, out written, format, info) : (-123.5d).TryFormat(destination, out written, format, info);
					if (retrySuccess != (operation != 2) || written != (operation == 2 ? 0 : expected.Length)) return 600 + operation;
					if (!threw) { completed = true; break; }
				}
				if (!completed) return 700 + operation;
			}
		}
		return 42;
	}

	public static int CoreLibFloatingAllocationLeafContractsEntry()
	{
		var info = NumberFormatInfo.InvariantInfo;
		const string expected = "-123.50";
		for (var precision = 0; precision < 2; precision++)
		{
			var buffer = new char[9];
			for (var index = 0; index < buffer.Length; index++) buffer[index] = '#';
			var destination = buffer.AsSpan(1, 7);
			var warmText = precision == 0 ? (-123.5f).ToString("F2", info) : (-123.5d).ToString("F2", info);
			var warmSuccess = precision == 0 ? (-123.5f).TryFormat(destination, out var written, "F2", info) : (-123.5d).TryFormat(destination, out written, "F2", info);
			if (warmText != expected || !warmSuccess || written != 7) return 1;
			for (var index = 0; index < buffer.Length; index++) buffer[index] = '#';
			SetStringBuilderAllocationFailure(1);
			try
			{
				var success = precision == 0 ? (-123.5f).TryFormat(destination, out written, "F2", info) : (-123.5d).TryFormat(destination, out written, "F2", info);
				SetStringBuilderAllocationFailure(0);
				if (!success || written != 7) return 2;
			}
			catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); return 3; }
			for (var index = 0; index < buffer.Length; index++)
				if (buffer[index] != (index > 0 && index < 8 ? expected[index - 1] : '#')) return 4;
			SetStringBuilderAllocationFailure(2);
			string text;
			try { text = precision == 0 ? (-123.5f).ToString("F2", info) : (-123.5d).ToString("F2", info); SetStringBuilderAllocationFailure(0); }
			catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); return 5; }
			if (text != expected) return 6;
			var threw = false;
			SetStringBuilderAllocationFailure(1);
			try { _ = precision == 0 ? (-123.5f).ToString("F2", info) : (-123.5d).ToString("F2", info); SetStringBuilderAllocationFailure(0); }
			catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); threw = true; }
			if (!threw) return 7;
			var retry = precision == 0 ? (-123.5f).ToString("F2", info) : (-123.5d).ToString("F2", info);
			if (retry != expected || text != expected || warmText != expected) return 8;
		}
		return 42;
	}
}
