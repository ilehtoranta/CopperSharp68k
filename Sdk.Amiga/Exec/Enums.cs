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

public enum IoError : sbyte
{
	Ok = 0,
	OpenFail = -1,
	Aborted = -2,
	NoCommand = -3,
	BadAddress = -4,
	BadLength = -5,
}

[System.Flags]
public enum MemoryHandlerFlags : uint
{
	None = 0,
	Recycle = 1u << 0,
}

public enum MemoryHandlerResult : int
{
	AllDone = -1,
	DidNothing = 0,
	TryAgain = 1,
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

/// <summary>Classic V39 child-task status and error values.</summary>
public enum ChildTaskStatus : uint
{
	NotNew = 1,
	NotFound = 2,
	Exited = 3,
	Active = 4,
}

/// <summary>MorphOS V50 task attribute selectors accepted by NewGet/SetTaskAttrsA.</summary>
public enum TaskInfoType : uint
{
	AllTasks = 0x00,
	Name = 0x01,
	Priority = 0x02,
	Type = 0x03,
	State = 0x04,
	Flags = 0x05,
	SignalAllocated = 0x06,
	SignalWait = 0x07,
	SignalReceived = 0x08,
	SignalException = 0x09,
	ExceptionData = 0x0A,
	ExceptionCode = 0x0B,
	TrapData = 0x0C,
	TrapCode = 0x0D,
	StackSizeM68k = 0x0E,
	StackLowerM68k = 0x28,
	StackUpperM68k = 0x29,
	NameCopy = 0x2A,
	UserData = 0x2B,
	ProcessId = 0x33,
}

/// <summary>MorphOS V50 system attribute selectors implemented by portable consumers.</summary>
public enum SystemInfoType : uint
{
	System = 0x000,
	Machine = 0x001,
	PageSize = 0x100,
	CpuCount = 0x101,
	Magic1 = 0x238,
	Magic2 = 0x239,
	NewScheduler = 0x242,
}

/// <summary>MorphOS Exec registry list identifiers used by FindExecNode.</summary>
public enum ExecNodeListType : uint
{
	Device = 0,
	Library = 2,
	MemoryHeader = 4,
	MessagePort = 5,
	Resource = 6,
	SignalSemaphore = 7,
	Task = 8,
}

public static class ExecConstants
{
	public const uint TagDone = 0;
	public const uint TagIgnore = 1;
	public const uint TagMore = 2;
	public const uint TagSkip = 3;
	public const uint TagUser = 0x8000_0000u;
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

	public const uint TaskTagBase = TagUser + 0x0010_0000u;
	public const uint TaskTagError = TaskTagBase + 0x00;
	public const uint TaskTagCodeType = TaskTagBase + 0x01;
	public const uint TaskTagProgramCounter = TaskTagBase + 0x02;
	public const uint TaskTagFinalProgramCounter = TaskTagBase + 0x03;
	public const uint TaskTagStackSize = TaskTagBase + 0x04;
	public const uint TaskTagM68kStackSize = TaskTagBase + 0x05;
	public const uint TaskTagName = TaskTagBase + 0x06;
	public const uint TaskTagUserData = TaskTagBase + 0x07;
	public const uint TaskTagPriority = TaskTagBase + 0x08;
	public const uint TaskTagPoolPuddle = TaskTagBase + 0x09;
	public const uint TaskTagPoolThreshold = TaskTagBase + 0x0A;
	public const uint TaskTagFlags = TaskTagBase + 0x1A;

	public const uint LibraryTagBase = TagUser + 0x0100_0100u;
	public const uint LibraryTagFunctionInit = LibraryTagBase + 0x00;
	public const uint LibraryTagStructInit = LibraryTagBase + 0x01;
	public const uint LibraryTagLibraryInit = LibraryTagBase + 0x02;
	public const uint LibraryTagMachine = LibraryTagBase + 0x03;
	public const uint LibraryTagBaseSize = LibraryTagBase + 0x04;
	public const uint LibraryTagSegmentList = LibraryTagBase + 0x05;
	public const uint LibraryTagPriority = LibraryTagBase + 0x06;
	public const uint LibraryTagType = LibraryTagBase + 0x07;
	public const uint LibraryTagVersion = LibraryTagBase + 0x08;
	public const uint LibraryTagFlags = LibraryTagBase + 0x09;
	public const uint LibraryTagName = LibraryTagBase + 0x0A;
	public const uint LibraryTagIdString = LibraryTagBase + 0x0B;
	public const uint LibraryTagPublic = LibraryTagBase + 0x0C;
	public const uint LibraryTagRevision = LibraryTagBase + 0x0D;

	public const uint ExecNodeTagType = TagUser + 1001;
	public const uint ExecNodeTagPriority = TagUser + 1002;
	public const uint ExecNodeTagName = TagUser + 1003;
}
