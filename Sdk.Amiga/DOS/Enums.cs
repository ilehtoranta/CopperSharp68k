/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public enum DosBoolean : int
{
	False = 0,
	True = -1,
}

public enum DosObjectType : uint
{
	FileHandle = 0,
	ExAllControl = 1,
	FileInfoBlock = 2,
	StandardPacket = 3,
	CommandLineInterface = 4,
	RdArgs = 5,
	DeviceNode = 6,
	FileSystemContext = 7,
	VolumeNode = 8,
	AssignNode = 9,
}

public enum DosObjectTag : uint
{
	FileHandleMode = 0x8000_0000u + 2001u,
	DirectoryLength = 0x8000_0000u + 2002u,
	CommandNameLength = 0x8000_0000u + 2003u,
	CommandFileLength = 0x8000_0000u + 2004u,
	PromptLength = 0x8000_0000u + 2005u,
	CliDirectory = 0x8000_0000u + 2020u,
	CliCommandName = 0x8000_0000u + 2021u,
	CliCommandFile = 0x8000_0000u + 2022u,
	CliPrompt = 0x8000_0000u + 2023u,
	CliResult2 = 0x8000_0000u + 2024u,
	CliReturnCode = 0x8000_0000u + 2025u,
	CliFailLevel = 0x8000_0000u + 2026u,
	CliInteractive = 0x8000_0000u + 2027u,
	CliBackground = 0x8000_0000u + 2028u,
	CliDefaultStack = 0x8000_0000u + 2029u,
}

public enum DosDiskState : int
{
	WriteProtected = 80,
	Validating = 81,
	Validated = 82,
}

public enum DosListType : int
{
	Device = 0,
	Directory = 1,
	Volume = 2,
	LateBinding = 3,
	NonBinding = 4,
}

[System.Flags]
public enum ProcessFlags : int
{
	FreeSegmentList = 1 << 0,
	FreeCurrentDirectory = 1 << 1,
	FreeCli = 1 << 2,
	CloseInput = 1 << 3,
	CloseOutput = 1 << 4,
	FreeArguments = 1 << 5,
}

[System.Flags]
public enum DosListLockFlags : uint
{
	Devices = 1u << 2,
	Volumes = 1u << 3,
	Assigns = 1u << 4,
	Entry = 1u << 5,
	Delete = 1u << 6,
	Read = 1u << 0,
	Write = 1u << 1,
}

[System.Flags]
public enum DevProcFlags : uint
{
	Unlock = 1u << 0,
	Assign = 1u << 1,
}

[System.Flags]
public enum FileProtection : int
{
	Delete = 1 << 0,
	Execute = 1 << 1,
	Write = 1 << 2,
	Read = 1 << 3,
	Archive = 1 << 4,
	Pure = 1 << 5,
	Script = 1 << 6,
	GroupDelete = 1 << 8,
	GroupExecute = 1 << 9,
	GroupWrite = 1 << 10,
	GroupRead = 1 << 11,
	OtherDelete = 1 << 12,
	OtherExecute = 1 << 13,
	OtherWrite = 1 << 14,
	OtherRead = 1 << 15,
}

public enum DosConstants : int
{
	OldFile = 1005,
	NewFile = 1006,
	ReadWrite = 1004,
	OffsetBeginning = -1,
	OffsetCurrent = 0,
	OffsetEnd = 1,
	SharedLock = -2,
	ExclusiveLock = -1,
	TicksPerSecond = 50,
	Root = 1,
	UserDirectory = 2,
	SoftLink = 3,
	LinkDirectory = 4,
	File = -3,
	LinkFile = -4,
	PipeFile = -5,
}

/// <summary>Classic and MorphOS record-lock modes from dos/record.h.</summary>
public enum DosRecordMode : uint
{
	Exclusive = 0,
	ExclusiveImmediate = 1,
	Shared = 2,
	SharedImmediate = 3,
}

/// <summary>MorphOS V51 Examine64 tag identifiers from dos/dostags.h.</summary>
public enum DosExamine64Tag : uint
{
	PosixDate = 0x8000_0000u + 3601u,
}

[System.Flags]
public enum FileInfoExtensionFlags : byte
{
	None = 0,
	PosixDate = 1 << 0,
}

public enum DosPacketAction : int
{
	Nil = 0,
	Startup = 0,
	GetBlock = 2,
	SetMap = 4,
	Die = 5,
	Event = 6,
	CurrentVolume = 7,
	LocateObject = 8,
	RenameDisk = 9,
	Write = 'W',
	Read = 'R',
	FreeLock = 15,
	DeleteObject = 16,
	RenameObject = 17,
	MoreCache = 18,
	CopyDirectory = 19,
	WaitChar = 20,
	SetProtection = 21,
	CreateDirectory = 22,
	ExamineObject = 23,
	ExamineNext = 24,
	DiskInfo = 25,
	Info = 26,
	Flush = 27,
	SetComment = 28,
	Parent = 29,
	Timer = 30,
	Inhibit = 31,
	DiskType = 32,
	DiskChange = 33,
	SetDate = 34,
	SameLock = 40,
	ExamineAll = 1033,
	ExamineFileHandle = 1034,
	LockRecord = 2008,
	FreeRecord = 2009,
	AddNotify = 4097,
	RemoveNotify = 4098,
}
