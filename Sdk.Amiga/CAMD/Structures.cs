/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MidiMessage
{
	public const uint Size = 8;

	public uint Message;
	public uint Time;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct SysExFilter
{
	public const uint Size = 4;

	public uint Packed;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MidiCluster
{
	public const uint Size = 48;

	public Node Node;
	public short Participants;
	public List Receivers;
	public List Senders;
	public short PublicParticipants;
	public ushort Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MidiLink
{
	public const uint Size = 56;

	public Node Node;
	public short Padding;
	public MinNode OwnerNode;
	public APTR MidiNode;
	public APTR Location;
	public STRPTR ClusterComment;
	public byte Flags;
	public byte PortId;
	public ushort ChannelMask;
	public uint EventTypeMask;
	public SysExFilter SysExFilter;
	public APTR ParserData;
	public APTR UserData;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MidiNode
{
	public const uint Size = 72;

	public Node Node;
	public ushort ClientType;
	public APTR Image;
	public MinList OutLinks;
	public MinList InLinks;
	public APTR SignalTask;
	public APTR ReceiveHook;
	public APTR ParticipantHook;
	public sbyte ReceiveSignalBit;
	public sbyte ParticipantSignalBit;
	public byte ErrorFilter;
	public byte Alignment;
	public APTR TimeStamp;
	public uint MessageQueueSize;
	public uint SysExQueueSize;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct CamdLinkMessage
{
	public const uint Size = 8;

	public uint MethodId;
	public uint Action;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ClusterNotifyNode
{
	public const uint Size = 16;

	public MinNode Node;
	public APTR Task;
	public sbyte SignalBit;
	public byte Padding0;
	public byte Padding1;
	public byte Padding2;
}
