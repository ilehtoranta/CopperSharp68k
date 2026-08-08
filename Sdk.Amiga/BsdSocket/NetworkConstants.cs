/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public static class BsdNetworkConstants
{
	public const int InterfaceNameSize = 16;
	public const uint RoundTripTimeUnit = 1_000_000;
	public const byte RouteMessageVersion = 2;
	public const ushort RouteAddressDestination = 0x0001;
	public const ushort RouteAddressGateway = 0x0002;
	public const ushort RouteAddressNetmask = 0x0004;
	public const ushort RouteAddressGenerationMask = 0x0008;
	public const ushort RouteAddressInterface = 0x0010;
	public const ushort RouteAddressInterfaceAddress = 0x0020;
	public const ushort RouteAddressAuthor = 0x0040;
}
