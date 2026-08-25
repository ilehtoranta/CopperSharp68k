/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

/// <summary>Public prefix of an AHI-owned audio-control handle.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2, Size = Size)]
public struct AHIAudioCtrl
{
	public const int Size = 4;
	public APTR UserData;
}

[StructLayout(LayoutKind.Sequential, Pack = 2, Size = Size)]
public struct AHISoundMessage
{
	public const int Size = 2;
	public ushort Channel;
}

[StructLayout(LayoutKind.Sequential, Pack = 2, Size = Size)]
public struct AHIRecordMessage
{
	public const int Size = 12;
	public uint Type;
	public APTR Buffer;
	public uint Length;
}

[StructLayout(LayoutKind.Sequential, Pack = 2, Size = Size)]
public struct AHISampleInfo
{
	public const int Size = 12;
	public uint Type;
	public APTR Address;
	public uint Length;
}

/// <summary>Documented read-only prefix of an AHI-owned mode requester.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2, Size = Size)]
public struct AHIAudioModeRequester
{
	public const int Size = 32;
	public uint AudioId;
	public uint MixFrequency;
	public short LeftEdge;
	public short TopEdge;
	public short Width;
	public short Height;
	public int InfoOpened;
	public short InfoLeftEdge;
	public short InfoTopEdge;
	public short InfoWidth;
	public short InfoHeight;
	public APTR UserData;
}
