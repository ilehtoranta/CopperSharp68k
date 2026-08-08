/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

[System.Flags]
public enum SocketBaseTagFlags : ushort
{
	Value = 0,
	Reference = 0x8000,
	Set = 0x0001,
}

public enum SocketBaseTagCode : ushort
{
	BreakMask = 1,
	SignalIoMask = 2,
	SignalUrgentMask = 3,
	SignalEventMask = 4,
	Errno = 6,
	HostErrno = 7,
	DescriptorTableSize = 8,
	FileDescriptorCallback = 9,
	LogStatus = 10,
	LogTagPointer = 11,
	LogFacility = 12,
	LogMask = 13,
	ErrnoStringPointer = 14,
	HostErrnoStringPointer = 15,
	IoErrorStringPointer = 16,
	Sana2ErrorStringPointer = 17,
	Sana2WriteErrorStringPointer = 18,
	ErrnoBytePointer = 21,
	ErrnoWordPointer = 22,
	ErrnoLongPointer = 24,
	HostErrnoLongPointer = 25,
	ReleaseStringPointer = 29,
	PacketFilterChannelCount = 40,
	ErrnoFunctionPointer = 80,
	HostErrnoFunctionPointer = 81,
}

public enum SocketBaseFileDescriptorAction : uint
{
	Free = 0,
	Allocate = 1,
	Check = 2,
}

public static class BsdSocketConstants
{
	public const uint TagUser = 0x80000000u;
	public const ushort TagCodeBit = 1;
	public const ushort TagCodeMask = 0x3fff;
	public const ushort Get = 0;
	public const ushort Set = 1;

	public static uint GetReference(SocketBaseTagCode code) =>
		TagUser | 0x8000u | ((uint)code << TagCodeBit);

	public static uint GetValue(SocketBaseTagCode code) =>
		TagUser | ((uint)code << TagCodeBit);

	public static uint SetReference(SocketBaseTagCode code) =>
		TagUser | 0x8000u | ((uint)code << TagCodeBit) | Set;

	public static uint SetValue(SocketBaseTagCode code) =>
		TagUser | ((uint)code << TagCodeBit) | Set;

	public static uint ExtractCode(uint tag) => (tag >> TagCodeBit) & TagCodeMask;
}
