/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Runtime.InteropServices;

namespace Amiga;

/// <summary>Classic string-gadget edit operations, modes, actions, and hooks.</summary>
public static class IntuitionStringGadgetHooks
{
	public const ushort EO_NOOP = 0x0001;
	public const ushort EO_DELBACKWARD = 0x0002;
	public const ushort EO_DELFORWARD = 0x0003;
	public const ushort EO_MOVECURSOR = 0x0004;
	public const ushort EO_ENTER = 0x0005;
	public const ushort EO_RESET = 0x0006;
	public const ushort EO_REPLACECHAR = 0x0007;
	public const ushort EO_INSERTCHAR = 0x0008;
	public const ushort EO_BADFORMAT = 0x0009;
	public const ushort EO_BIGCHANGE = 0x000A;
	public const ushort EO_UNDO = 0x000B;
	public const ushort EO_CLEAR = 0x000C;
	public const ushort EO_SPECIAL = 0x000D;

	public const uint SGM_REPLACE = 1u << 0;
	public const uint SGM_FIXEDFIELD = 1u << 1;
	public const uint SGM_NOFILTER = 1u << 2;
	public const uint SGM_NOCHANGE = 1u << 3;
	public const uint SGM_NOWORKB = 1u << 4;
	public const uint SGM_CONTROL = 1u << 5;
	public const uint SGM_LONGINT = 1u << 6;
	public const uint SGM_EXITHELP = 1u << 7;

	public const uint SGA_USE = 1u << 0;
	public const uint SGA_END = 1u << 1;
	public const uint SGA_BEEP = 1u << 2;
	public const uint SGA_REUSE = 1u << 3;
	public const uint SGA_REDISPLAY = 1u << 4;
	public const uint SGA_NEXTACTIVE = 1u << 5;
	public const uint SGA_PREVACTIVE = 1u << 6;

	public const uint SGH_KEY = 1u;
	public const uint SGH_CLICK = 2u;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct StringExtend
{
	public const uint Size = 36;
	public APTR Font;
	public fixed byte Pens[2];
	public fixed byte ActivePens[2];
	public uint InitialModes;
	public APTR EditHook;
	public APTR WorkBuffer;
	public fixed uint Reserved[4];
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct SGWork
{
	public const uint Size = 44;
	public APTR Gadget;
	public APTR StringInfo;
	public APTR WorkBuffer;
	public APTR PrevBuffer;
	public uint Modes;
	public APTR IEvent;
	public ushort Code;
	public short BufferPos;
	public short NumChars;
	public uint Actions;
	public int LongInt;
	public APTR GadgetInfo;
	public ushort EditOp;
}
