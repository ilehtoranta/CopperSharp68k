/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public static class ZeroComparisonFixtures
{
	public static uint Entry() => unchecked((uint)SignedWordAndZeroCompareForwarder(
		ReadVector(),
		ReadToken(),
		ReadCallbackSucceeded(),
		ReadLibraryBase()));

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ulong SignedWordAndZeroCompareForwarder(
		uint vector,
		uint token,
		uint callbackSucceeded,
		uint libraryBase) =>
		Resume(
			unchecked((short)vector),
			token,
			callbackSucceeded != 0,
			libraryBase);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ulong Resume(
		short vector,
		uint token,
		bool callbackSucceeded,
		uint libraryBase) => token;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint ReadVector() => 0x1234_8001;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint ReadToken() => 0x0102_0304;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint ReadCallbackSucceeded() => 1;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint ReadLibraryBase() => 0x0000_4000;
}
