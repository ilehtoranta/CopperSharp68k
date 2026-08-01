/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.CompilerServices;
using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

/// <summary>Compact target implementations for supported <see cref="Math"/> members.</summary>
public static class ShadowMath
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int Abs(int value)
	{
		if (value == int.MinValue)
		{
			M68kRuntime.ThrowOverflowException();
		}

		return Identity(value < 0 ? -value : value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static T Identity<T>(T value) => value;
}

/// <summary>
/// Big-endian target implementations for supported <see cref="BitConverter"/> members.
/// </summary>
public static class ShadowBitConverter
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static byte[] GetBytes(int value)
	{
		var bytes = new byte[4];
		bytes[0] = (byte)(value >> 24);
		bytes[1] = (byte)(value >> 16);
		bytes[2] = (byte)(value >> 8);
		bytes[3] = (byte)value;
		return bytes;
	}
}
