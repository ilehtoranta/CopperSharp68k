/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct SockAddr
{
	public const uint Size = 16;

	public byte Length;
	public byte Family;
	public fixed byte Data[14];
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct IfReq
{
	public const uint Size = 32;

	public fixed byte Name[16];
	/// <summary>16-byte ioctl union: sockaddr, flags, metric, MTU, or data pointer.</summary>
	public fixed byte RequestData[16];
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct IfAliasReq
{
	public const uint Size = 64;

	public unsafe fixed byte Name[16];
	public SockAddr Address;
	public SockAddr BroadcastAddress;
	public SockAddr Mask;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct IfConf
{
	public const uint Size = 8;

	public int Length;
	public APTR Request;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct RouteMetrics
{
	public const uint Size = 36;

	public uint Locks;
	public uint Mtu;
	public uint HopCount;
	public uint Expire;
	public uint ReceivePipe;
	public uint SendPipe;
	public uint SlowStartThreshold;
	public uint RoundTripTime;
	public uint RoundTripVariance;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct RouteStat
{
	public const uint Size = 10;

	public short BadRedirect;
	public short Dynamic;
	public short NewGateway;
	public short Unreachable;
	public short Wildcard;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct RouteEntry
{
	public const uint Size = 48;

	public uint Hash;
	public SockAddr Destination;
	public SockAddr Gateway;
	public short Flags;
	public short ReferenceCount;
	public uint Use;
	public APTR Interface;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct RouteMessageHeader
{
	public const uint Size = 70;

	public ushort MessageLength;
	public byte Version;
	public byte Type;
	public ushort Index;
	public int ProcessId;
	public int Addresses;
	public int Sequence;
	public int Error;
	public int Flags;
	public int Use;
	public uint Initializers;
	public RouteMetrics Metrics;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct RouteControlBlock
{
	public const uint Size = 16;

	public int IpCount;
	public int NsCount;
	public int IsoCount;
	public int AnyCount;
}
