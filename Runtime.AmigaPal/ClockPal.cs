/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using Amiga;
using CopperSharp.Compiler;

namespace CopperSharp.Runtime.AmigaPal;

/// <summary>Private timer.device implementation for the admitted Stopwatch slice.</summary>
public static class ClockPal
{
	private const uint MsgPortMessageListOffset = 20;
	private const int IORequestDeviceOffset = 20;
	private const uint TimerRequestSize = 40;
	private static ClockMsgPort _cachedPort;
	private static ClockIORequest _cachedRequest;
	private static uint _cachedDeviceBase;
	private static uint _cachedFrequency;
	private static bool _applicationLifetimeActive;

	/// <summary>Enables lazy application-lifetime timer.device ownership.</summary>
	public static void Initialize()
	{
		_applicationLifetimeActive = true;
	}

	/// <summary>Releases cached timer.device state. Safe to call repeatedly.</summary>
	public static void Shutdown()
	{
		_applicationLifetimeActive = false;
		if (_cachedDeviceBase != 0)
		{
			var request = APTR.ToUInt32(AddressOf(ref _cachedRequest));
			_cachedDeviceBase = 0;
			Exec.CloseDevice(request);
		}
	}

	public static long GetFrequency()
	{
		if (_cachedFrequency != 0)
		{
			return _cachedFrequency;
		}
		var frequency = ReadClock(out _, out _);
		return frequency;
	}

	public static long GetTimestamp()
	{
		ReadClock(out var high, out var low);
		return M68kRuntime.CombineInt64(high, low);
	}

	private static uint ReadClock(out uint high, out uint low)
	{
		if (_cachedDeviceBase == 0)
		{
			if (!_applicationLifetimeActive)
			{
				return ReadScopedClock(out high, out low);
			}
			OpenCachedDevice();
		}

		var frequency = ReadOpenedClock(_cachedDeviceBase, out high, out low);
		if (frequency == 0)
		{
			M68kRuntime.ThrowInvalidOperationException();
			return 0;
		}
		_cachedFrequency = frequency;
		return frequency;
	}

	private static void OpenCachedDevice()
	{
		_cachedPort = default;
		_cachedRequest = default;
		var portAddress = APTR.ToUInt32(AddressOf(ref _cachedPort));
		var request = APTR.ToUInt32(AddressOf(ref _cachedRequest));
		InitializeRecords(portAddress, request);

		var openResult = Exec.OpenDevice(
			TimerDevice.Name,
			TimerDevice.UnitEClock,
			request,
			0);
		if (openResult != 0)
		{
			M68kRuntime.ThrowInvalidOperationException();
			return;
		}

		var deviceBase = APTR.ReadUInt32(
			APTR.FromPointer(request),
			IORequestDeviceOffset);
		if (deviceBase == 0)
		{
			Exec.CloseDevice(request);
			M68kRuntime.ThrowInvalidOperationException();
			return;
		}
		_cachedDeviceBase = deviceBase;
	}

	private static uint ReadScopedClock(out uint high, out uint low)
	{
		ClockMsgPort port = default;
		ClockIORequest requestRecord = default;
		var portAddress = APTR.ToUInt32(AddressOf(ref port));
		var request = APTR.ToUInt32(AddressOf(ref requestRecord));
		InitializeRecords(portAddress, request);

		var openResult = Exec.OpenDevice(
			TimerDevice.Name,
			TimerDevice.UnitEClock,
			request,
			0);
		if (openResult != 0)
		{
			high = 0;
			low = 0;
			M68kRuntime.ThrowInvalidOperationException();
			return 0;
		}

		var deviceBase = APTR.ReadUInt32(
			APTR.FromPointer(request),
			IORequestDeviceOffset);
		if (deviceBase == 0)
		{
			Exec.CloseDevice(request);
			high = 0;
			low = 0;
			M68kRuntime.ThrowInvalidOperationException();
			return 0;
		}

		var frequency = ReadOpenedClock(deviceBase, out high, out low);
		Exec.CloseDevice(request);
		if (frequency == 0)
		{
			M68kRuntime.ThrowInvalidOperationException();
			return 0;
		}
		_cachedFrequency = frequency;
		return frequency;
	}

	/// <summary>Converts raw EClock ticks to 100-nanosecond TimeSpan ticks.</summary>
	public static long ScaleToTimeSpanTicks(long value)
	{
		var low = M68kRuntime.SplitInt64(value, out var high);
		var negative = (high & 0x8000_0000u) != 0;
		if (negative)
		{
			Negate(ref high, ref low);
		}

		var frequency = _cachedFrequency;
		if (frequency == 0)
		{
			frequency = (uint)GetFrequency();
		}
		Divide(high, low, frequency, out var quotientHigh, out var quotientLow, out var remainder);
		Multiply(quotientHigh, quotientLow, 10_000_000u, out var resultHigh, out var resultLow);
		Multiply(0, remainder, 10_000_000u, out var fractionHigh, out var fractionLow);
		Divide(fractionHigh, fractionLow, frequency, out _, out var fraction, out _);
		Add(ref resultHigh, ref resultLow, 0, fraction);
		if (negative)
		{
			Negate(ref resultHigh, ref resultLow);
		}
		return M68kRuntime.CombineInt64(resultHigh, resultLow);
	}

	/// <summary>Converts raw EClock ticks to whole elapsed milliseconds.</summary>
	public static long ScaleToMilliseconds(long value)
	{
		var ticksLow = M68kRuntime.SplitInt64(ScaleToTimeSpanTicks(value), out var ticksHigh);
		var negative = (ticksHigh & 0x8000_0000u) != 0;
		if (negative)
		{
			Negate(ref ticksHigh, ref ticksLow);
		}
		Divide(ticksHigh, ticksLow, 10_000u, out var high, out var low, out _);
		if (negative)
		{
			Negate(ref high, ref low);
		}
		return M68kRuntime.CombineInt64(high, low);
	}

	private static void Divide(uint high, uint low, uint divisor, out uint quotientHigh, out uint quotientLow, out uint remainder)
	{
		quotientHigh = 0;
		quotientLow = 0;
		remainder = 0;
		for (var bit = 63; bit >= 0; bit--)
		{
			var inputBit = bit >= 32
				? (high >> (bit - 32)) & 1u
				: (low >> bit) & 1u;
			var overflow = (remainder & 0x8000_0000u) != 0;
			remainder = (remainder << 1) | inputBit;
			if (!overflow && remainder < divisor)
			{
				continue;
			}
			remainder -= divisor;
			if (bit >= 32)
			{
				quotientHigh |= 1u << (bit - 32);
			}
			else
			{
				quotientLow |= 1u << bit;
			}
		}
	}

	private static void Multiply(uint high, uint low, uint factor, out uint resultHigh, out uint resultLow)
	{
		resultHigh = 0;
		resultLow = 0;
		var currentHigh = high;
		var currentLow = low;
		var currentFactor = factor;
		while (currentFactor != 0)
		{
			if ((currentFactor & 1u) != 0)
			{
				Add(ref resultHigh, ref resultLow, currentHigh, currentLow);
			}
			currentFactor >>= 1;
			currentHigh = (currentHigh << 1) | (currentLow >> 31);
			currentLow <<= 1;
		}
	}

	private static void Add(ref uint high, ref uint low, uint addHigh, uint addLow)
	{
		var previousLow = low;
		low += addLow;
		high += addHigh + (low < previousLow ? 1u : 0u);
	}

	private static void Negate(ref uint high, ref uint low)
	{
		low = ~low + 1u;
		high = ~high + (low == 0 ? 1u : 0u);
	}

	private static uint ReadOpenedClock(
		uint deviceBase,
		out uint high,
		out uint low)
	{
		EClockValue value = default;
		var frequency = TimerDevice.ReadEClock(
			APTR.FromPointer(deviceBase),
			EClockValue.AddressOf(ref value));
		high = value.High;
		low = value.Low;
		return frequency;
	}

	private static void InitializeRecords(uint port, uint request)
	{
		// Use explicit ABI offsets because these address-escaped records are
		// consumed by Exec, not as managed values. PA_IGNORE deliberately leaves
		// the signal fields at zero; timer I/O must use a signal-bearing port.
		var portAddress = APTR.FromPointer(port);
		var messageListAddress = port + MsgPortMessageListOffset;
		APTR.WriteUInt32(portAddress, 8, (uint)NodeType.MessagePort << 24);
		APTR.WriteUInt32(portAddress, 14, (uint)PortFlags.Ignore << 24);
		APTR.WriteUInt32(portAddress, 20, messageListAddress + 4);
		APTR.WriteUInt32(portAddress, 28, messageListAddress);

		var requestAddress = APTR.FromPointer(request);
		APTR.WriteUInt32(requestAddress, 8, (uint)NodeType.ReplyMessage << 24);
		APTR.WriteUInt32(requestAddress, 14, port);
		APTR.WriteUInt32(requestAddress, 18, TimerRequestSize << 16);
	}

	private static APTR AddressOf(ref ClockMsgPort port) =>
		throw new System.NotSupportedException(
			"ClockPal.AddressOf is lowered by CopperSharp.");

	private static APTR AddressOf(ref ClockIORequest request) =>
		throw new System.NotSupportedException(
			"ClockPal.AddressOf is lowered by CopperSharp.");

	[System.Runtime.InteropServices.StructLayout(
		System.Runtime.InteropServices.LayoutKind.Sequential,
		Pack = 2)]
	private struct ClockMsgPort
	{
		public uint NodeSuccessor;
		public uint NodePredecessor;
		public byte NodeType;
		public sbyte NodePriority;
		public uint NodeName;
		public byte Flags;
		public byte SignalBit;
		public uint SignalTask;
		public uint MessageListHead;
		public uint MessageListTail;
		public uint MessageListTailPred;
		public byte MessageListType;
		public byte MessageListPadding;
	}

	[System.Runtime.InteropServices.StructLayout(
		System.Runtime.InteropServices.LayoutKind.Sequential,
		Pack = 2)]
	private struct ClockIORequest
	{
		public uint NodeSuccessor;
		public uint NodePredecessor;
		public byte NodeType;
		public sbyte NodePriority;
		public uint NodeName;
		public uint ReplyPort;
		public ushort Length;
		public uint Device;
		public uint Unit;
		public ushort Command;
		public byte Flags;
		public sbyte Error;
		public uint TimeSeconds;
		public uint TimeMicroseconds;
	}
}

/// <summary>Private storage backing Stopwatch.Frequency.</summary>
public static class StopwatchFrequencyField
{
	public static readonly long Frequency = ClockPal.GetFrequency();
}

/// <summary>Private storage backing Stopwatch.IsHighResolution.</summary>
public static class StopwatchHighResolutionField
{
	public static readonly bool IsHighResolution = true;
}
