/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Amiga;

namespace CopperSharp.Compiler.Tests;

/// <summary>
/// Reproduces the private-call shape used by CopperStart DOS position64 direct
/// vectors: a 44-byte reference-free result is ignored or inspected before the
/// caller returns a D0:D1 pair.
/// </summary>
public static class DiscardedAggregateReturnPairFixtures
{
	private const uint ExpectedHigh = 0x1234_5678;
	private const uint ExpectedLow = 0x9ABC_DEF0;
	private const uint ResultMarker = 0xC001_CAFE;
	private const uint ExpectedState = 0x0000_2000;
	private const uint ExpectedFrame = 0x0000_3000;
	private const uint ExpectedLibrary = 0x0000_4000;
	private static ExactPlatform _staticPlatform;

	private enum Disposition : byte
	{
		Completed,
		Declined,
	}

	private enum CallbackKind : byte
	{
		None,
		Callback,
	}

	[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 44)]
	private struct GatewayResult
	{
		public Disposition Disposition;
		public uint Result;
		public uint ContinuationToken;
		public CallbackKind CallbackKind;
		public uint CallbackEntry;
		public uint CallbackD0;
		public uint CallbackD1;
		public uint CallbackA0;
		public uint CallbackA1;
		public uint CallbackA2;
		public uint CallbackA6;
	}

	private struct RegisterPair
	{
		public uint D0;
		public uint D1;
	}

	private interface IExactProvider
	{
		bool SetFileSize64(uint providerHandle, ulong size);
	}

	[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 4)]
	private struct ExactPlatform : IExactProvider
	{
		private const uint OwnerHeaderSize = 0x100;
		private uint _state;

		public ExactPlatform(uint state) => _state = state;

		private readonly APTR State => APTR.FromPointer(_state);
		private readonly APTR Owner => _state < OwnerHeaderSize
			? APTR.Null : APTR.FromPointer(_state - OwnerHeaderSize);

		[MethodImpl(MethodImplOptions.NoInlining)]
		bool IExactProvider.SetFileSize64(uint providerHandle, ulong size)
		{
			var call = BeginProviderCall();
			if (call.IsNull) return false;
			var value = SetFileSize64(providerHandle, size);
			EndProviderCall(call);
			return value;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public bool SetFileSize64(uint providerHandle, ulong size)
		{
			var low = M68kRuntime.SplitUInt64(size, out var high);
			return TryReadNativeProviderState(out var observedState) &&
				observedState.Raw == ExpectedState &&
				providerHandle == ResultMarker &&
				high == ExpectedHigh && low == ExpectedLow;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private APTR BeginProviderCall() =>
			TryReadNativeProviderState(out var state) ? state : APTR.Null;

		[MethodImpl(MethodImplOptions.NoInlining)]
		private readonly void EndProviderCall(APTR call) => _ = call;

		[MethodImpl(MethodImplOptions.NoInlining)]
		private readonly bool TryReadNativeProviderState(out APTR observedState)
		{
			observedState = APTR.FromPointer(_state);
			return _state == ExpectedState;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public readonly uint ReadStoredState() => _state;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 68)]
	private struct ExactRegisterFrame
	{
		public uint D0;
		public uint D1;
		public uint D2;
		public uint D3;
		public uint D4;
		public uint D5;
		public uint D6;
		public uint D7;
		public uint A0;
		public uint A1;
		public uint A2;
		public uint A3;
		public uint A4;
		public uint A5;
		public uint A6;
		public uint ProgramCounter;
		public ushort StatusRegister;
	}

	// Exact size and field order of DosNativeDirectBoundary.Call: a four-byte
	// native platform, APTR state, 68-byte register frame, and original D0.
	[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 80)]
	private struct ExactCall
	{
		public ExactPlatform Platform;
		public APTR State;
		public ExactRegisterFrame Registers;
		public uint OriginalD0;
	}

	public static int AggregateSize => Marshal.SizeOf<GatewayResult>();
	public static int ExactCallSize => Marshal.SizeOf<ExactCall>();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ulong DiscardedAggregateEntry()
	{
		RegisterPair registers = default;
		DispatchAggregate(ref registers);
		return unchecked((ulong)M68kRuntime.CombineInt64(
			registers.D0, registers.D1));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ulong MaterializedAggregateEntry()
	{
		RegisterPair registers = default;
		var result = DispatchAggregate(ref registers);
		if (result.Disposition != Disposition.Completed ||
			result.Result != ResultMarker) return 0;
		return unchecked((ulong)M68kRuntime.CombineInt64(
			registers.D0, registers.D1));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ulong DiscardedScalarEntry()
	{
		RegisterPair registers = default;
		DispatchScalar(ref registers);
		return unchecked((ulong)M68kRuntime.CombineInt64(
			registers.D0, registers.D1));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ulong DiscardedPairEntry()
	{
		RegisterPair registers = default;
		DispatchPair(ref registers);
		return unchecked((ulong)M68kRuntime.CombineInt64(
			registers.D0, registers.D1));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ulong ExactDiscardedAggregateEntry(APTR state, APTR frame,
		APTR library)
	{
		if (library.Raw != ExpectedLibrary ||
			!PrepareKnown(state, frame, out var call)) return 0;
		DispatchExactAggregate(ref call.Platform, call.State, -1072,
			ref call.Registers);
		return ReturnPair(call.Registers.D0, call.Registers.D1);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ulong ExactMaterializedAggregateEntry(APTR state, APTR frame,
		APTR library)
	{
		if (library.Raw != ExpectedLibrary ||
			!PrepareKnown(state, frame, out var call)) return 0;
		var result = DispatchExactAggregate(ref call.Platform, call.State, -1072,
			ref call.Registers);
		if (result.Disposition != Disposition.Completed ||
			result.Result != ResultMarker) return 0;
		return ReturnPair(call.Registers.D0, call.Registers.D1);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint TransparentConstructorStoredInFieldEntry()
	{
		ExactCall call = default;
		call.Platform = new ExactPlatform(ExpectedState);
		return call.Platform.ReadStoredState();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint TransparentConstructorStoredInStaticFieldEntry()
	{
		_staticPlatform = new ExactPlatform(ExpectedState);
		return _staticPlatform.ReadStoredState();
	}

	// Makes all four direct entries reachable in one production compile. The
	// execution test invokes their symbols independently with an RTS sentinel.
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ReachabilityEntry()
	{
		_ = DiscardedAggregateEntry();
		_ = MaterializedAggregateEntry();
		_ = DiscardedScalarEntry();
		_ = DiscardedPairEntry();
		_ = ExactDiscardedAggregateEntry(ExpectedState, ExpectedFrame,
			ExpectedLibrary);
		_ = ExactMaterializedAggregateEntry(ExpectedState, ExpectedFrame,
			ExpectedLibrary);
		_ = TransparentConstructorStoredInFieldEntry();
		_ = TransparentConstructorStoredInStaticFieldEntry();
		return 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool PrepareKnown(APTR state, APTR frame, out ExactCall call)
	{
		call = default;
		if (state.Raw != ExpectedState || frame.Raw != ExpectedFrame) return false;
		call.Platform = new ExactPlatform(state.Raw);
		call.State = state;
		call.OriginalD0 = ExpectedLibrary;
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static GatewayResult DispatchExactAggregate(
		ref ExactPlatform platform, APTR state, short lvo,
		ref ExactRegisterFrame registers) => DispatchExactAggregateGeneric(
			ref platform, state, lvo, ref registers);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static GatewayResult DispatchExactAggregateGeneric<TPlatform>(
		ref TPlatform platform, APTR state, short lvo,
		ref ExactRegisterFrame registers)
		where TPlatform : struct, IExactProvider
	{
		var position = M68kRuntime.CombineInt64(ExpectedHigh, ExpectedLow);
		if (state.Raw != ExpectedState || lvo != -1072)
			return CreateGatewayResult(Disposition.Declined, 0);
		var value = CoreSetFileSize64(ref platform, state, ResultMarker,
			position, 1);
		var low = M68kRuntime.SplitInt64(value, out var high);
		if (high != ExpectedHigh || low != ExpectedLow)
			return CreateGatewayResult(Disposition.Declined, 0);
		registers.D0 = high;
		registers.D1 = low;
		return CreateGatewayResult(Disposition.Completed, ResultMarker);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static long CoreSetFileSize64<TPlatform>(ref TPlatform platform,
		APTR state, uint providerHandle, long position, int mode)
		where TPlatform : struct, IExactProvider
	{
		if (!EnsureState(ref platform, state) || mode != 1) return -1;
		return platform.SetFileSize64(providerHandle,
			unchecked((ulong)position)) ? position : -1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool EnsureState<TPlatform>(ref TPlatform platform, APTR state)
		where TPlatform : struct, IExactProvider => state.Raw == ExpectedState;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static GatewayResult CreateGatewayResult(Disposition disposition,
		uint result)
	{
		GatewayResult value = default;
		value.Disposition = disposition;
		value.Result = result;
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ulong ReturnPair(uint d0, uint d1) => unchecked((ulong)
		M68kRuntime.CombineInt64(d0, d1));

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static GatewayResult DispatchAggregate(ref RegisterPair registers)
	{
		SetPair(ref registers);
		GatewayResult result = default;
		result.Disposition = Disposition.Completed;
		result.Result = ResultMarker;
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint DispatchScalar(ref RegisterPair registers)
	{
		SetPair(ref registers);
		return ResultMarker;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ulong DispatchPair(ref RegisterPair registers)
	{
		SetPair(ref registers);
		return unchecked((ulong)M68kRuntime.CombineInt64(
			ResultMarker, ~ResultMarker));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void SetPair(ref RegisterPair registers)
	{
		registers.D0 = ExpectedHigh;
		registers.D1 = ExpectedLow;
	}
}
