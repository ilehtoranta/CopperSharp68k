/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Diagnostics;
using System.Runtime.CompilerServices;
using CopperSharp.Compiler;

namespace CopperSharp.Runtime.AmigaPal;

/// <summary>
/// Deterministic no-pack fallback instance state and managed bodies for the
/// admitted Stopwatch surface. A verified implementation pack selects the
/// preferred pinned CoreLib bodies instead. The four UInt32 lanes are
/// representation-compatible with the two Int64 fields in the pinned .NET
/// implementation on the big-endian target.
/// </summary>
public sealed class ShadowStopwatch
{
	private uint _elapsedHigh;
	private uint _elapsedLow;
	private uint _startTimestampHigh;
	private uint _startTimestampLow;
	private bool _isRunning;

	/// <summary>Initializes an already zeroed managed Stopwatch allocation.</summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Initialize()
	{
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Start()
	{
		if (_isRunning)
		{
			return;
		}

		var low = M68kRuntime.SplitInt64(
			ClockPal.GetTimestamp(),
			out var high);
		_startTimestampHigh = high;
		_startTimestampLow = low;
		_isRunning = true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Stop()
	{
		if (!_isRunning)
		{
			return;
		}

		var nowLow = M68kRuntime.SplitInt64(
			ClockPal.GetTimestamp(),
			out var nowHigh);
		AccumulateElapsed(nowHigh, nowLow);
		_isRunning = false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Reset()
	{
		_elapsedHigh = 0;
		_elapsedLow = 0;
		_startTimestampHigh = 0;
		_startTimestampLow = 0;
		_isRunning = false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Restart()
	{
		_elapsedHigh = 0;
		_elapsedLow = 0;
		var low = M68kRuntime.SplitInt64(
			ClockPal.GetTimestamp(),
			out var high);
		_startTimestampHigh = high;
		_startTimestampLow = low;
		_isRunning = true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Stopwatch StartNew()
	{
		var stopwatch = new Stopwatch();
		stopwatch.Start();
		return stopwatch;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public bool GetIsRunning() => _isRunning;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public long GetElapsedTicks()
	{
		var high = _elapsedHigh;
		var low = _elapsedLow;
		if (_isRunning)
		{
			var nowLow = M68kRuntime.SplitInt64(
				ClockPal.GetTimestamp(),
				out var nowHigh);
			AddDelta(
				ref high,
				ref low,
				nowHigh,
				nowLow,
				_startTimestampHigh,
				_startTimestampLow);
		}
		return M68kRuntime.CombineInt64(high, low);
	}

	private void AccumulateElapsed(uint nowHigh, uint nowLow)
	{
		var high = _elapsedHigh;
		var low = _elapsedLow;
		AddDelta(
			ref high,
			ref low,
			nowHigh,
			nowLow,
			_startTimestampHigh,
			_startTimestampLow);
		_elapsedHigh = high;
		_elapsedLow = low;
	}

	private static void AddDelta(
		ref uint elapsedHigh,
		ref uint elapsedLow,
		uint nowHigh,
		uint nowLow,
		uint startHigh,
		uint startLow)
	{
		var deltaLow = nowLow - startLow;
		var borrow = nowLow < startLow ? 1u : 0u;
		var deltaHigh = nowHigh - startHigh - borrow;
		var previousLow = elapsedLow;
		elapsedLow = previousLow + deltaLow;
		var carry = elapsedLow < previousLow ? 1u : 0u;
		elapsedHigh = elapsedHigh + deltaHigh + carry;
	}
}
