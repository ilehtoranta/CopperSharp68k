/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ExpansionRom
{
	public const uint Size = 16;

	public byte Type;
	public byte Product;
	public byte Flags;
	public byte Reserved03;
	public ushort Manufacturer;
	public uint SerialNumber;
	public ushort InitDiagnosticVector;
	public byte Reserved0C;
	public byte Reserved0D;
	public byte Reserved0E;
	public byte Reserved0F;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct ExpansionControl
{
	public const uint Size = 16;

	public byte Interrupt;
	public byte Z3HighBase;
	public byte BaseAddress;
	public byte ShutUp;
	public fixed byte Reserved[12];
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DiagArea
{
	public const uint Size = 14;

	public byte Config;
	public byte Flags;
	public ushort SizeInBytes;
	public ushort DiagnosticPoint;
	public ushort BootPoint;
	public ushort Name;
	public ushort Reserved01;
	public ushort Reserved02;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ConfigDev
{
	public const uint Size = 68;

	public Node Node;
	public byte Flags;
	public byte Padding;
	public ExpansionRom Rom;
	public APTR BoardAddress;
	public uint BoardSize;
	public ushort SlotAddress;
	public ushort SlotSize;
	public APTR Driver;
	public APTR NextConfigDev;
	public unsafe fixed uint Unused[4];
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct CurrentBinding
{
	public const uint Size = 16;

	public APTR ConfigDev;
	public STRPTR FileName;
	public STRPTR ProductString;
	public APTR ToolTypes;
}
