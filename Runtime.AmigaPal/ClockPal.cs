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
	private static ClockMsgPort _cachedPort;
	private static ClockIORequest _cachedRequest;
	private static uint _cachedDeviceBase;
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
		return frequency;
	}

	private static uint ReadOpenedClock(
		uint deviceBase,
		out uint high,
		out uint low)
	{
		EClockValue value = default;
		var previousTimerBase = APTR.ToUInt32(TimerDevice.TimerDeviceLibraryBase);
		TimerDevice.TimerDeviceLibraryBase = APTR.FromPointer(deviceBase);
		var frequency = TimerDevice.ReadEClock(EClockValue.AddressOf(ref value));
		TimerDevice.TimerDeviceLibraryBase = APTR.FromPointer(previousTimerBase);
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
		APTR.WriteUInt32(requestAddress, 18, IORequest.Size << 16);
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
