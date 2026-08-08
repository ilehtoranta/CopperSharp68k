/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct AmigaGuideMessage
{
	public const uint Size = 52;

	public Message Message;
	public uint Type;
	public APTR Data;
	public uint DataSize;
	public uint DataType;
	public uint PrimaryReturn;
	public uint SecondaryReturn;
	public APTR System1;
	public APTR System2;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct NewAmigaGuide
{
	public const uint Size = 52;

	public BPTR Lock;
	public STRPTR Name;
	public APTR Screen;
	public STRPTR PublicScreen;
	public STRPTR HostPort;
	public STRPTR ClientPort;
	public STRPTR BaseName;
	public uint Flags;
	public APTR Context;
	public STRPTR Node;
	public int Line;
	public APTR Extensions;
	public APTR Client;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct AmigaGuideXRef
{
	public const uint Size = 40;

	public Node Node;
	public ushort Padding;
	public APTR DocumentFile;
	public STRPTR File;
	public STRPTR Name;
	public int Line;
	public unsafe fixed uint Reserved[2];
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct AmigaGuideHost
{
	public const uint Size = 40;

	public Hook Dispatcher;
	public uint Reserved;
	public uint Flags;
	public uint UseCount;
	public APTR SystemData;
	public APTR UserData;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct AmigaGuideFindHostMessage
{
	public const uint Size = 28;

	public uint MethodId;
	public APTR Attributes;
	public STRPTR Node;
	public STRPTR TableOfContents;
	public STRPTR Title;
	public STRPTR Next;
	public STRPTR Previous;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct AmigaGuideNodeIoMessage
{
	public const uint Size = 28;

	public uint MethodId;
	public APTR Attributes;
	public STRPTR Node;
	public STRPTR FileName;
	public STRPTR DocumentBuffer;
	public uint BufferLength;
	public uint Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct AmigaGuideExpungeMessage
{
	public const uint Size = 8;

	public uint MethodId;
	public APTR Attributes;
}
