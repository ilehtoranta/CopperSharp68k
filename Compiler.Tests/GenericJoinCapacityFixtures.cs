/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static int CoreLibGenericEnumJoinCapacityContractsEntry()
	{
		var values = new JoinInt64[2]; values[0] = JoinInt64.Named; values[1] = JoinInt64.First | JoinInt64.Second;
		for (var boxed = 0; boxed < 2; boxed++)
		for (var character = 0; character < 2; character++)
		for (var boundary = 0; boundary < 3; boundary++)
		{
			var maximum = boundary == 0 ? 4 : boundary == 1 ? 9 : 10;
			var state = new GenericJoinState();
			IEnumerable<JoinInt64> source = boxed == 0 ? new GenericJoinSource<JoinInt64>(values, state) : new StructGenericJoinSource<JoinInt64>(values, state);
			var builder = new StringBuilder(1, maximum).Append("seed"); var snapshot = builder.ToString();
			try { if (character == 0) builder.AppendJoin("|", source); else builder.AppendJoin('|', source); return 100 + boundary; }
			catch (ArgumentOutOfRangeException exception) { if (exception.ParamName != "valueCount" || exception.ActualValue != null) return 200 + boundary; }
			var expected = boundary == 0 ? "seed" : boundary == 1 ? "seedNamed" : "seedNamed|";
			// CoreLib allows the one-character separator to expand a nine-char
			// chunk to capacity eighteen even with MaxCapacity ten.
			var expectedCapacity = boundary == 0 ? 4 : boundary == 1 ? 9 : 18;
			if (builder.ToString() != expected || snapshot != "seed" || builder.Capacity != expectedCapacity || builder.MaxCapacity != maximum) return 300 + boundary;
			if (state.Creations != 1 || state.Moves != (boundary == 0 ? 1 : 2) || state.Currents != (boundary == 2 ? 2 : 1) || state.Disposals != 1) return 400 + boundary;
			builder.Clear(); var retry = new GenericJoinState(); var shorter = new JoinInt64[2];
			builder.AppendJoin('|', new StructGenericJoinSource<JoinInt64>(shorter, retry)); GC.Collect();
			if (builder.ToString() != "0|0" || snapshot != "seed" || retry.Disposals != 1) return 500 + boundary;
		}
		return 42;
	}
}
