/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public enum NodeType : byte
{
	Unknown = 0,
	Task = 1,
	Interrupt = 2,
	Device = 3,
	MessagePort = 4,
	Message = 5,
	FreeMessage = 6,
	ReplyMessage = 7,
	Resource = 8,
	Library = 9,
	Memory = 10,
	SoftInterrupt = 11,
	Font = 12,
	Process = 13,
	Semaphore = 14,
	SignalSemaphore = 15,
	BootNode = 16,
	KickMemory = 17,
	Graphics = 18,
	DeathMessage = 19,
	User = 254,
	Extended = 255,
}

[System.Flags]
public enum LibraryFlags : byte
{
	None = 0,
	Summing = 1 << 0,
	Changed = 1 << 1,
	SumUsed = 1 << 2,
	DelayedExpunge = 1 << 3,
	RamLib = 1 << 4,
	QueryInfo = 1 << 5,
}

[System.Flags]
public enum PortFlags : byte
{
	Signal = 0,
	SoftInterrupt = 1,
	Ignore = 2,
	ActionMask = 3,
}

[System.Flags]
public enum UnitFlags : byte
{
	None = 0,
	Active = 1 << 0,
	InTask = 1 << 1,
}

[System.Flags]
public enum TaskFlags : byte
{
	None = 0,
	ProcessTime = 1 << 0,
	ExtendedTask = 1 << 3,
	StackCheck = 1 << 4,
	Exception = 1 << 5,
	Switch = 1 << 6,
	Launch = 1 << 7,
}

public enum TaskState : byte
{
	Invalid = 0,
	Added = 1,
	Running = 2,
	Ready = 3,
	Waiting = 4,
	Exception = 5,
	Removed = 6,
}

[System.Flags]
public enum SignalFlags : uint
{
	None = 0,
	Abort = 1u << 0,
	Child = 1u << 1,
	Blit = 1u << 4,
	Single = 1u << 4,
	Intuition = 1u << 5,
	Network = 1u << 7,
	Dos = 1u << 8,
}

[System.Flags]
public enum InterruptFlags : uint
{
	None = 0,
	NonMaskable = 1u << 15,
}

[System.Flags]
public enum ResidentFlags : byte
{
	None = 0,
	ColdStart = 1 << 0,
	SingleTask = 1 << 1,
	AfterDos = 1 << 2,
	AutoInit = 1 << 7,
}

[System.Flags]
public enum IOFlags : byte
{
	None = 0,
	Quick = 1 << 0,
}

public enum DeviceCommand : ushort
{
	Invalid = 0,
	Reset = 1,
	Read = 2,
	Write = 3,
	Update = 4,
	Clear = 5,
	Stop = 6,
	Start = 7,
	Flush = 8,
	NonStandard = 9,
}

[System.Flags]
public enum MemoryHandlerFlags : uint
{
	None = 0,
	Recycle = 1u << 0,
}

public enum SemaphoreMode : uint
{
	Exclusive = 0,
	Shared = 1,
}

public enum ExecListType : uint
{
	Device = 0,
	Interrupt = 1,
	Library = 2,
	MemoryHandler = 3,
	MemoryHeader = 4,
	Port = 5,
	Resource = 6,
	Semaphore = 7,
	Task = 8,
	RunCommand = 9,
}

[System.Flags]
public enum ExecNotifyFlags : uint
{
	None = 0,
	Remove = 1u << 0,
	Post = 1u << 1,
}

public enum TaskCodeType : uint
{
	M68k = 0,
	PowerPc = 1,
}

public enum TaskError : uint
{
	Ok = 0,
	NoMemory = 1,
}

public static class ExecConstants
{
	public const int LibraryVectorSize = 6;
	public const int LibraryReservedVectors = 4;
	public const short LibraryBase = -6;
	public const short LibraryUserDefined = -30;

	public const ushort ResidentMatchWord = 0x4AFC;
	public const byte SoftInterruptPriorityMask = 0xF0;
	public const int NonMaskableInterruptBit = 15;
	public const uint MemoryBlockSize = 8;
	public const uint MemoryBlockMask = MemoryBlockSize - 1;

	public const uint VersionTaskTrapMessage = 0;
	public const uint VersionTaskTrapMessage68k = 0;
	public const uint TaskTrapMessageVersion = VersionTaskTrapMessage;
	public const uint TaskTrapMessage68kVersion = VersionTaskTrapMessage68k;

	public const uint DefaultPowerPcStackSize = 32768;
	public const uint DefaultM68kStackSize = 2048;
	public const uint DefaultTaskPuddleSize = 4096;
	public const uint DefaultTaskThresholdSize = 4096;
	public const uint CurrentTaskId = 0;
	public const uint InvalidTlsIndex = 0xFFFF_FFFFu;
}
