/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Amiga;

namespace CopperSharp.Compiler.Tests;

/// <summary>
/// Minimal form of the CopperStart DOS VPrintf call chain.  The outer generic
/// method feeds a pointer-wrapper result from one internal call directly into a
/// second generic call whose body has locals and constrained interface calls.
/// Release CIL keeps the calls adjacent while Debug CIL normally materializes
/// the scalar return in a local.
/// </summary>
public static class NestedAddressReturnChainFixtures
{
	private const uint ExpectedHandle = 0x0000_2345;
	private const uint ExpectedState = 0x0000_3000;
	private const uint ExpectedFormat = 0x0000_3100;
	private const uint ExpectedArguments = 0x0000_3200;

	private interface IOutputPlatform
	{
		BPTR Output(APTR state);
		int Put(BPTR handle, uint value);
		int SetError(int error);
	}

	[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 4)]
	private struct ExactPlatform : IOutputPlatform
	{
		private uint _handle;

		public ExactPlatform(uint handle) => _handle = handle;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public readonly BPTR Output(APTR state) => state.Raw == ExpectedState
			? BPTR.FromRaw(_handle)
			: BPTR.Null;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public readonly int Put(BPTR handle, uint value) =>
			handle.Raw == _handle ? unchecked((int)value) : -1;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public readonly int SetError(int error) => error;
	}

	public static uint ManagedEntry() => Entry();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint NativeEntry() => Entry();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint Entry()
	{
		var platform = new ExactPlatform(ExpectedHandle);
		var count = VPrint(ref platform, APTR.FromPointer(ExpectedState),
			APTR.FromPointer(ExpectedFormat),
			APTR.FromPointer(ExpectedArguments));
		return count == 10 ? unchecked((uint)count) : 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int VPrint<TPlatform>(ref TPlatform platform, APTR state,
		APTR format, APTR arguments) where TPlatform : struct, IOutputPlatform =>
		VFPrint(ref platform, state, Output(ref platform, state), format,
			arguments);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static BPTR Output<TPlatform>(ref TPlatform platform, APTR state)
		where TPlatform : struct, IOutputPlatform => platform.Output(state);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int VFPrint<TPlatform>(ref TPlatform platform, APTR state,
		BPTR handle, APTR format, APTR arguments)
		where TPlatform : struct, IOutputPlatform
	{
		if (state.Raw != ExpectedState || format.Raw != ExpectedFormat ||
			arguments.Raw != ExpectedArguments) return 0;

		var count = 0u;
		var pending = 1u;
		while (count < 10)
		{
			if (platform.Put(handle, pending) < 0) return 0;
			count++;
			pending++;
		}
		SetError(ref platform, 0);
		return unchecked((int)count);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int SetError<TPlatform>(ref TPlatform platform, int error)
		where TPlatform : struct, IOutputPlatform => platform.SetError(error);
}
