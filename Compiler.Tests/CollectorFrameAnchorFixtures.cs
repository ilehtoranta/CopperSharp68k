/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CollectorFrameAnchorRetainsCallerArrayEntry()
	{
		var retained = new byte[16]; retained[0] = 17; retained[15] = 25;
		if (CollectUnderDynamicFrame(3) != 800) return 1;
		return ReadRetainedCollectorBytes(retained) == 42 ? 42 : 2;
	}

	private static int _collectorAnchorFinalizerCount;
	private sealed class CollectorAnchorFinalizable
	{
		~CollectorAnchorFinalizable() => _collectorAnchorFinalizerCount++;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int FinalizableCollectorFrameAnchorRetainsCallerArrayEntry()
	{
		var finalizable = new CollectorAnchorFinalizable();
		var result = CollectorFrameAnchorRetainsCallerArrayEntry();
		System.GC.KeepAlive(finalizable);
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int CollectUnderDynamicFrame(int length)
	{
		Span<int> scratch = stackalloc int[length]; scratch[0] = 19; scratch[length - 1] = 23;
		System.GC.Collect();
		var total = 0;
		for (var index = 0; index < 8; index++)
		{
			var pressure = new byte[16]; pressure[0] = 100; pressure[15] = 101;
			total += pressure[0];
		}
		return ReadCollectorScratch(scratch, length) == 42 ? total : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ReadRetainedCollectorBytes(byte[] retained) => retained[0] + retained[15];

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ReadCollectorScratch(Span<int> scratch, int length) => scratch[0] + scratch[length - 1];
}
