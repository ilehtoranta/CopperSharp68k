/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler;

/// <summary>Raw target address used by managed runtime implementations.</summary>
public readonly struct M68kAddress
{
	private readonly uint _value;

	private M68kAddress(uint value)
	{
		_value = value;
	}

	public static M68kAddress FromUInt32(uint value) => new(value);

	public static uint ToUInt32(M68kAddress address) => address._value;

	public static uint ReadUInt32(M68kAddress address, int offset) =>
		throw new PlatformNotSupportedException(
			"M68kAddress.ReadUInt32 is lowered by CopperSharp.");

	public static void WriteUInt32(M68kAddress address, int offset, uint value) =>
		throw new PlatformNotSupportedException(
			"M68kAddress.WriteUInt32 is lowered by CopperSharp.");
}
