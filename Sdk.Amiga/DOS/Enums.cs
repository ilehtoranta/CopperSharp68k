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

/// <summary>Buffering modes from dos/stdio.h used by SetVBuf.</summary>
public enum DosBufferMode : int
{
	Line = 0,
	Full = 1,
	None = 2,
}

/// <summary>Progressive ExAllData prefixes from dos/exall.h.</summary>
public enum DosExAllDataLevel : int
{
	Name = 1,
	Type = 2,
	Size = 3,
	Protection = 4,
	Date = 5,
	Comment = 6,
	Owner = 7,
}

/// <summary>Token bytes emitted by ParsePattern from dos/dosasl.h.</summary>
public enum DosPatternToken : byte
{
	Any = 0x80,
	Single = 0x81,
	OrStart = 0x82,
	OrNext = 0x83,
	OrEnd = 0x84,
	Not = 0x85,
	NotEnd = 0x86,
	NotClass = 0x87,
	Class = 0x88,
	RepeatBegin = 0x89,
	RepeatEnd = 0x8A,
	Stop = 0x8B,
}

[System.Flags]
public enum AnchorPathFlags : byte
{
	None = 0,
	DoWild = 1 << 0,
	IsWild = 1 << 1,
	DoDirectory = 1 << 2,
	DidDirectory = 1 << 3,
	NoMemoryError = 1 << 4,
	DoDot = 1 << 5,
	DirectoryChanged = 1 << 6,
	FollowHardLinks = 1 << 7,
}

[System.Flags]
public enum AChainFlags : byte
{
	None = 0,
	Pattern = 1 << 0,
	Examined = 1 << 1,
	Completed = 1 << 2,
	All = 1 << 3,
	Single = 1 << 4,
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

public enum LocalVariableType : byte
{
	Variable = 0,
	Alias = 1,
}

[System.Flags]
public enum GlobalVariableFlags : uint
{
	None = 0,
	Ignore = 1u << 7,
	GlobalOnly = 1u << 8,
	LocalOnly = 1u << 9,
	BinaryVariable = 1u << 10,
	DoNotNullTerminate = 1u << 11,
	SaveVariable = 1u << 12,
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
	DeviceNodeName = 0x8000_0000u + 2030u,
	DeviceNodeMessagePort = 0x8000_0000u + 2031u,
	DeviceNodeLock = 0x8000_0000u + 2032u,
	DeviceNodeHandler = 0x8000_0000u + 2033u,
	DeviceNodeSegmentList = 0x8000_0000u + 2034u,
	DeviceNodeStackSize = 0x8000_0000u + 2035u,
	DeviceNodePriority = 0x8000_0000u + 2036u,
	DeviceNodeStartup = 0x8000_0000u + 2037u,
	DeviceNodeGlobalVector = 0x8000_0000u + 2038u,
	DeviceNodeSerialId = 0x8000_0000u + 2039u,
	DeviceNodeStartupValue = 0x8000_0000u + 2040u,
	DeviceNodeFlags = 0x8000_0000u + 2041u,
	DeviceNodeStatus = 0x8000_0000u + 2042u,
	DeviceNodeExitNotifyMessage = 0x8000_0000u + 2043u,
	FileSysStartupMessage = 0x8000_0000u + 2050u,
	FileSysStartupDevice = 0x8000_0000u + 2051u,
	FileSysStartupUnit = 0x8000_0000u + 2052u,
	FileSysStartupFlags = 0x8000_0000u + 2053u,
	DosEnvironment = 0x8000_0000u + 2060u,
	DosEnvironmentTableSize = 0x8000_0000u + 2061u,
	DosEnvironmentSizeBlock = 0x8000_0000u + 2062u,
	DosEnvironmentSectorOrigin = 0x8000_0000u + 2063u,
	DosEnvironmentSurfaces = 0x8000_0000u + 2064u,
	DosEnvironmentNumberOfHeads = DosEnvironmentSurfaces,
	DosEnvironmentSectorsPerBlock = 0x8000_0000u + 2065u,
	DosEnvironmentBlocksPerTrack = 0x8000_0000u + 2066u,
	DosEnvironmentReservedBlocks = 0x8000_0000u + 2067u,
	DosEnvironmentPreAlloc = 0x8000_0000u + 2068u,
	DosEnvironmentPreFac = DosEnvironmentPreAlloc,
	DosEnvironmentInterleave = 0x8000_0000u + 2069u,
	DosEnvironmentLowCylinder = 0x8000_0000u + 2070u,
	DosEnvironmentHighCylinder = 0x8000_0000u + 2071u,
	DosEnvironmentUpperCylinder = DosEnvironmentHighCylinder,
	DosEnvironmentNumberOfBuffers = 0x8000_0000u + 2072u,
	DosEnvironmentBufferMemoryType = 0x8000_0000u + 2073u,
	DosEnvironmentMaximumTransfer = 0x8000_0000u + 2074u,
	DosEnvironmentMask = 0x8000_0000u + 2075u,
	DosEnvironmentBootPriority = 0x8000_0000u + 2076u,
	DosEnvironmentDosType = 0x8000_0000u + 2077u,
	DosEnvironmentBaud = 0x8000_0000u + 2078u,
	DosEnvironmentControl = 0x8000_0000u + 2079u,
	DosEnvironmentBootBlocks = 0x8000_0000u + 2080u,
	FileSystemDosType = 0x8000_0000u + 2090u,
	VolumeNodeName = 0x8000_0000u + 2100u,
	VolumeNodeMessagePort = 0x8000_0000u + 2101u,
	VolumeNodeLock = 0x8000_0000u + 2102u,
	VolumeNodeLockList = 0x8000_0000u + 2103u,
	VolumeNodeDate = 0x8000_0000u + 2104u,
	VolumeNodeDiskType = 0x8000_0000u + 2105u,
	AssignNodeName = 0x8000_0000u + 2120u,
	AssignNodeMessagePort = 0x8000_0000u + 2121u,
	AssignNodeLock = 0x8000_0000u + 2122u,
	AssignNodeType = 0x8000_0000u + 2123u,
	AssignNodeAssignName = 0x8000_0000u + 2124u,
	AssignNodeAssignList = 0x8000_0000u + 2125u,
}

/// <summary>SystemTagList selectors from dos/dostags.h.</summary>
public enum DosSystemTag : uint
{
	Input = 0x8000_0000u + 33u,
	Output = 0x8000_0000u + 34u,
	Asynchronous = 0x8000_0000u + 35u,
	UserShell = 0x8000_0000u + 36u,
	CustomShell = 0x8000_0000u + 37u,
	FilterTags = 0x8000_0000u + 38u,
}

/// <summary>CreateNewProc selectors from dos/dostags.h.</summary>
public enum DosNewProcessTag : uint
{
	SegmentList = 0x8000_0000u + 1001u,
	FreeSegmentList = 0x8000_0000u + 1002u,
	Entry = 0x8000_0000u + 1003u,
	Input = 0x8000_0000u + 1004u,
	Output = 0x8000_0000u + 1005u,
	CloseInput = 0x8000_0000u + 1006u,
	CloseOutput = 0x8000_0000u + 1007u,
	Error = 0x8000_0000u + 1008u,
	CloseError = 0x8000_0000u + 1009u,
	CurrentDirectory = 0x8000_0000u + 1010u,
	StackSize = 0x8000_0000u + 1011u,
	Name = 0x8000_0000u + 1012u,
	Priority = 0x8000_0000u + 1013u,
	ConsoleTask = 0x8000_0000u + 1014u,
	WindowPointer = 0x8000_0000u + 1015u,
	HomeDirectory = 0x8000_0000u + 1016u,
	CopyVariables = 0x8000_0000u + 1017u,
	Cli = 0x8000_0000u + 1018u,
	Path = 0x8000_0000u + 1019u,
	CommandName = 0x8000_0000u + 1020u,
	Arguments = 0x8000_0000u + 1021u,
	NotifyOnDeath = 0x8000_0000u + 1022u,
	Synchronous = 0x8000_0000u + 1023u,
	ExitCode = 0x8000_0000u + 1024u,
	ExitData = 0x8000_0000u + 1025u,
	SegmentListArray = 0x8000_0000u + 1026u,
	UserData = 0x8000_0000u + 1027u,
	StartupMessage = 0x8000_0000u + 1028u,
	TaskMessagePort = 0x8000_0000u + 1029u,
	TaskFlags = 0x8000_0000u + 1030u,
	CodeType = 0x8000_0000u + 1100u,
	PpcArgument1 = 0x8000_0000u + 1101u,
	PpcArgument2 = 0x8000_0000u + 1102u,
	PpcArgument3 = 0x8000_0000u + 1103u,
	PpcArgument4 = 0x8000_0000u + 1104u,
	PpcArgument5 = 0x8000_0000u + 1105u,
	PpcArgument6 = 0x8000_0000u + 1106u,
	PpcArgument7 = 0x8000_0000u + 1107u,
	PpcArgument8 = 0x8000_0000u + 1108u,
	PpcStackSize = 0x8000_0000u + 1109u,
}

/// <summary>MorphOS V50 AddSegmentTagList selectors from dos/dostags.h.</summary>
public enum DosAddSegmentTag : uint
{
	Name = 0x8000_0000u + 3001u,
	SegmentList = 0x8000_0000u + 3002u,
	FileName = 0x8000_0000u + 3003u,
	Type = 0x8000_0000u + 3004u,
}

/// <summary>MorphOS V50 FindSegmentTagList selectors from dos/dostags.h.</summary>
public enum DosFindSegmentTag : uint
{
	Name = 0x8000_0000u + 3101u,
	From = 0x8000_0000u + 3102u,
	System = 0x8000_0000u + 3103u,
	Load = 0x8000_0000u + 3104u,
	MatchPattern = 0x8000_0000u + 3105u,
}

[System.Flags]
public enum DeviceNodeFlags : uint
{
	None = 0,
	StartOnce = 1u << 0,
	UnloadSegmentList = 1u << 1,
	RemoveDosList = 1u << 2,
}

/// <summary>MorphOS V51.51 QueryCLIDataTagList selectors.</summary>
public enum CLIDataTag : uint
{
	CLINumber = 0x8000_0000u + 3501u,
	CommandName = 0x8000_0000u + 3502u,
	Sorted = 0x8000_0000u + 3503u,
}

/// <summary>MorphOS segment-list attributes and selector tags.</summary>
public enum SegmentListTag : uint
{
	ObjectData = 0x8000_0000u + 3401u,
	SegmentListType = 0x8000_0000u + 3402u,
	DosSegmentIndex = 0x8000_0000u + 3403u,
	ElfSegmentIndex = 0x8000_0000u + 3404u,
	SegmentStart = 0x8000_0000u + 3405u,
	SegmentSize = 0x8000_0000u + 3406u,
	ElfSegmentType = 0x8000_0000u + 3407u,
	ElfSegmentOffset = 0x8000_0000u + 3408u,
	ElfSegmentFlags = 0x8000_0000u + 3409u,
	ElfSegmentAddressAlignment = 0x8000_0000u + 3410u,
	ElfSegmentName = 0x8000_0000u + 3411u,
}

public enum SegmentListType : uint
{
	Elf = 1,
	PowerUp = 2,
	Amiga = 3,
}

/// <summary>MorphOS V51 ACTION_QUERY_ATTR/GetFileSysAttr selectors.</summary>
public enum FileSystemQueryAttribute : int
{
	MaxFileNameLength = 0,
	MaxVolumeNameLength = 1,
	MaxFileSize = 2,
	IsCaseSensitive = 3,
	DeviceType = 4,
	Reserved1 = 5,
	NumBlocks = 6,
	NumBlocksUsed = 7,
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

/// <summary>Published CHANGE_LOCK/CHANGE_FH selector for ChangeMode().</summary>
public enum DosChangeModeTarget : int
{
	Lock = 0,
	FileHandle = 1,
}

public enum DosDateFormat : byte
{
	Dos = 0,
	International = 1,
	Usa = 2,
	Canadian = 3,
	Default = 4,
}

[System.Flags]
public enum DosDateTimeFlags : byte
{
	None = 0,
	Substitute = 1 << 0,
	Future = 1 << 1,
}

/// <summary>Classic and MorphOS record-lock modes from dos/record.h.</summary>
public enum DosRecordMode : uint
{
	Exclusive = 0,
	ExclusiveImmediate = 1,
	Shared = 2,
	SharedImmediate = 3,
}

/// <summary>Classic dos/notify.h request flags.</summary>
[System.Flags]
public enum DosNotifyFlags : uint
{
	None = 0,
	SendMessage = 1u << 0,
	SendSignal = 1u << 1,
	WaitReply = 1u << 3,
	NotifyInitial = 1u << 4,
	HandlerMagic = 1u << 31,
	HandlerMask = 0xFFFF_0000u,
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
	ScreenMode = 994,
	ChangeSignal = 995,
	ReadReturn = 1001,
	WriteReturn = 1002,
	FindUpdate = 1004,
	FindInput = 1005,
	FindOutput = 1006,
	End = 1007,
	Seek = 1008,
	Format = 1020,
	MakeLink = 1021,
	SetFileSize = 1022,
	WriteProtect = 1023,
	ReadLink = 1024,
	FileHandleFromLock = 1026,
	IsFileSystem = 1027,
	ChangeMode = 1028,
	CopyDirectoryFromFileHandle = 1030,
	ParentFileHandle = 1031,
	ExamineAll = 1033,
	ExamineFileHandle = 1034,
	ExamineAllEnd = 1035,
	SetOwner = 1036,
	LockRecord = 2008,
	FreeRecord = 2009,
	AddNotify = 4097,
	RemoveNotify = 4098,
	NewReadLink = 26406,
	QueryAttribute = 26407,
	ExamineObject64 = 26408,
	ExamineNext64 = 26409,
	ExamineFileHandle64 = 26410,
	SetPosixDate = 26411,
}
