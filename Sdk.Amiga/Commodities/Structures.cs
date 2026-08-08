/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct NewBroker
{
	public const uint Size = 26;

	public byte Version;
	public STRPTR Name;
	public STRPTR Title;
	public STRPTR Description;
	public short Unique;
	public short Flags;
	public sbyte Priority;
	public APTR Port;
	public short ReservedChannel;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct InputXpression
{
	public const uint Size = 12;

	public byte Version;
	public byte Class;
	public ushort Code;
	public ushort CodeMask;
	public ushort Qualifier;
	public ushort QualifierMask;
	public ushort QualifierSame;
}
