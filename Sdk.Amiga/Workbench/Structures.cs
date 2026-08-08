/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct WBArg
{
	public const uint Size = 8;

	public BPTR Lock;
	public STRPTR Name;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct WBStartup
{
	public const uint Size = 40;

	public Message Message;
	public APTR Process;
	public BPTR Segment;
	public int NumberOfArguments;
	public STRPTR ToolWindow;
	public APTR ArgumentList;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct OldDrawerData
{
	public const uint Size = 56;

	public NewWindow NewWindow;
	public int CurrentX;
	public int CurrentY;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DrawerData
{
	public const uint Size = 62;

	public NewWindow NewWindow;
	public int CurrentX;
	public int CurrentY;
	public uint Flags;
	public ushort ViewModes;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DiskObject
{
	public const uint Size = 78;

	public ushort Magic;
	public ushort Version;
	public Gadget Gadget;
	public byte Type;
	public STRPTR DefaultTool;
	public APTR ToolTypes;
	public int CurrentX;
	public int CurrentY;
	public APTR DrawerData;
	public STRPTR ToolWindow;
	public int StackSize;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct FreeList
{
	public const uint Size = 16;

	public short NumberOfFree;
	public List MemoryList;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct AppMessage
{
	public const uint Size = 86;

	public Message Message;
	public ushort Type;
	public uint UserData;
	public uint Id;
	public int NumberOfArguments;
	public APTR ArgumentList;
	public ushort Version;
	public ushort Class;
	public short MouseX;
	public short MouseY;
	public uint Seconds;
	public uint Micros;
	public unsafe fixed uint Reserved[8];
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct SetupCleanupHookMessage
{
	public const uint Size = 8;

	public uint Length;
	public int State;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct AppIconRenderMessage
{
	public const uint Size = 28;

	public APTR RastPort;
	public APTR Icon;
	public STRPTR Label;
	public APTR Tags;
	public short Left;
	public short Top;
	public short Width;
	public short Height;
	public uint State;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct AppWindowDropZoneMessage
{
	public const uint Size = 24;

	public APTR RastPort;
	public IBox DropZoneBox;
	public uint Id;
	public uint UserData;
	public int Action;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct IconSelectMessage
{
	public const uint Size = 38;

	public uint Length;
	public BPTR Drawer;
	public STRPTR Name;
	public ushort Type;
	public int Selected;
	public APTR Tags;
	public APTR DrawerWindow;
	public APTR ParentWindow;
	public short Left;
	public short Top;
	public short Width;
	public short Height;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct CopyBeginMessage
{
	public const uint Size = 16;

	public uint Length;
	public int Action;
	public BPTR SourceDrawer;
	public BPTR DestinationDrawer;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct CopyDataMessage
{
	public const uint Size = 32;

	public uint Length;
	public int Action;
	public BPTR SourceLock;
	public STRPTR SourceName;
	public BPTR DestinationLock;
	public STRPTR DestinationName;
	public int DestinationX;
	public int DestinationY;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct CopyEndMessage
{
	public const uint Size = 8;

	public uint Length;
	public int Action;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DeleteBeginMessage
{
	public const uint Size = 8;

	public uint Length;
	public int Action;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DeleteDataMessage
{
	public const uint Size = 16;

	public uint Length;
	public int Action;
	public BPTR Lock;
	public STRPTR Name;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DeleteEndMessage
{
	public const uint Size = 8;

	public uint Length;
	public int Action;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct TextInputMessage
{
	public const uint Size = 12;

	public uint Length;
	public int Action;
	public STRPTR Prompt;
}
