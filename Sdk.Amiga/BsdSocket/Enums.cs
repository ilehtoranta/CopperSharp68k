/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

[System.Flags]
public enum InterfaceFlags : ushort
{
	Up = 0x0001,
	Broadcast = 0x0002,
	Debug = 0x0004,
	Loopback = 0x0008,
	PointToPoint = 0x0010,
	NoTrailers = 0x0020,
	Running = 0x0040,
	NoArp = 0x0080,
	Promiscuous = 0x0100,
	AllMulti = 0x0200,
	OActive = 0x0400,
	Simplex = 0x0800,
	Link0 = 0x1000,
	Link1 = 0x2000,
	Sana = 0x4000,
	Multicast = 0x8000,
}

[System.Flags]
public enum RouteFlags : ushort
{
	Up = 0x0001,
	Gateway = 0x0002,
	Host = 0x0004,
	Reject = 0x0008,
	Dynamic = 0x0010,
	Modified = 0x0020,
	Done = 0x0040,
	Mask = 0x0080,
	Cloning = 0x0100,
	XResolve = 0x0200,
	LinkInfo = 0x0400,
	Protocol2 = 0x4000,
	Protocol1 = 0x8000,
}

public enum RouteMessageType : byte
{
	Add = 1,
	Delete = 2,
	Change = 3,
	Get = 4,
	Losing = 5,
	Redirect = 6,
	Miss = 7,
	Lock = 8,
	OldAdd = 9,
	OldDelete = 10,
	Resolve = 11,
}

[System.Flags]
public enum RouteAddressFlags : int
{
	Destination = 1,
	Gateway = 2,
	Netmask = 4,
	GenerationMask = 8,
	Interface = 16,
	InterfaceAddress = 32,
	Author = 64,
}


public static class NetworkInterfaceConstants
{
	public const ushort CantChange = (ushort)(InterfaceFlags.Up | InterfaceFlags.Broadcast |
		InterfaceFlags.PointToPoint | InterfaceFlags.Running | InterfaceFlags.OActive |
		InterfaceFlags.Simplex | InterfaceFlags.Multicast | InterfaceFlags.AllMulti |
		InterfaceFlags.Sana);
}


[System.Flags]
public enum RouteMetricFlags : int
{
	Mtu = 0x01,
	HopCount = 0x02,
	Expire = 0x04,
	ReceivePipe = 0x08,
	SendPipe = 0x10,
	SlowStartThreshold = 0x20,
	RoundTripTime = 0x40,
	RoundTripVariance = 0x80,
}
