/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[System.Runtime.InteropServices.StructLayout(
	System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 2)]
public struct ExAllControl
{
	public const uint Size = 16;
	public uint Entries;
	public uint LastKey;
	public APTR MatchString;
	public APTR MatchFunction;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DosAttrBuffer
{
	public const uint Size = 8;

	public APTR Pointer;
	public uint Length;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DateStamp
{
	public const uint Size = 12;

	public int Days;
	public int Minutes;
	public int Ticks;
}

/// <summary>Published dos/datetime.h DateTime record.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DosDateTime
{
	public const uint Size = 26;
	public const uint StringLength = 16;

	public DateStamp Stamp;
	public DosDateFormat Format;
	public DosDateTimeFlags Flags;
	public STRPTR Day;
	public STRPTR Date;
	public STRPTR Time;
}

/// <summary>MorphOS UTC timestamp used by the m68k DOS ABI.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct PosixDateStamp
{
	public const uint Size = 12;

	public long Seconds;
	public int Nanoseconds;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct CSource
{
	public const uint Size = 12;

	public APTR Buffer;
	public int Length;
	public int CurrentCharacter;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct RDArgs
{
	public const uint Size = 32;

	public CSource Source;
	public APTR AllocationList;
	public APTR Buffer;
	public int BufferSize;
	public APTR ExtendedHelp;
	public int Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct RecordLock
{
	public const uint Size = 16;

	public BPTR File;
	public uint Offset;
	public uint Length;
	public uint Mode;
}

/// <summary>MorphOS 64-bit record-lock descriptor used by the m68k DOS ABI.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct RecordLock64
{
	public const uint Size = 24;

	public BPTR File;
	public ulong Offset;
	public ulong Length;
	public uint Mode;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct InfoData
{
	public const uint Size = 36;

	public int NumberOfSoftErrors;
	public int UnitNumber;
	public int DiskState;
	public int NumberOfBlocks;
	public int NumberOfBlocksUsed;
	public int BytesPerBlock;
	public int DiskType;
	public BPTR VolumeNode;
	public int InUse;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DosEnvec
{
	public const uint Size = 80;

	public uint TableSize;
	public uint SizeBlock;
	public uint SectorOrigin;
	public uint Surfaces;
	public uint SectorsPerBlock;
	public uint BlocksPerTrack;
	public uint Reserved;
	public uint PreAlloc;
	public uint Interleave;
	public uint LowCylinder;
	public uint HighCylinder;
	public uint NumberOfBuffers;
	public uint BufferMemoryType;
	public uint MaximumTransfer;
	public uint Mask;
	public int BootPriority;
	public uint DosType;
	public uint Baud;
	public uint Control;
	public uint BootBlocks;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct FileSysStartupMsg
{
	public const uint Size = 16;

	public uint Unit;
	public BPTR Device;
	public BPTR Environment;
	public uint Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DeviceNode
{
	public const uint Size = 44;

	public BPTR Next;
	public uint Type;
	public APTR Task;
	public BPTR Lock;
	public BPTR Handler;
	public uint StackSize;
	public int Priority;
	public BPTR Startup;
	public BPTR SegmentList;
	public BPTR GlobalVector;
	public BPTR Name;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct FileHandle
{
	public const uint Size = 44;

	public APTR Link;
	public APTR Port;
	public APTR Type;
	public int Buffer;
	public int Position;
	public int End;
	public int Functions;
	public int Function2;
	public int Function3;
	public int Arguments;
	public int Argument2;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DosPacket
{
	public const uint Size = 48;

	public APTR Link;
	public APTR Port;
	public int Type;
	public int Result1;
	public int Result2;
	public int Argument1;
	public int Argument2;
	public int Argument3;
	public int Argument4;
	public int Argument5;
	public int Argument6;
	public int Argument7;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct StandardPacket
{
	public const uint Size = 68;

	public Message Message;
	public DosPacket Packet;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ErrorString
{
	public const uint Size = 8;

	public APTR ErrorNumbers;
	public APTR ErrorStrings;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct RootNode
{
	public const uint Size = 56;

	public BPTR TaskArray;
	public BPTR ConsoleSegment;
	public DateStamp Time;
	public int RestartSegment;
	public BPTR Info;
	public BPTR FileHandlerSegment;
	public MinList CliList;
	public APTR BootProcess;
	public BPTR ShellSegment;
	public int Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct CliProcList
{
	public const uint Size = 16;

	public MinNode Node;
	public int First;
	public APTR Array;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DosInfo
{
	public const uint Size = 158;

	public BPTR ResidentList;
	public BPTR DeviceInfo;
	public BPTR Devices;
	public BPTR Handlers;
	public APTR NetworkHandler;
	public SignalSemaphore DeviceLock;
	public SignalSemaphore EntryLock;
	public SignalSemaphore DeleteLock;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Segment
{
	public const uint Size = 16;

	public BPTR Next;
	public int UseCount;
	public BPTR SegmentList;
	public unsafe fixed byte Name[4];
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct CommandLineInterface
{
	public const uint Size = 64;

	public int Result2;
	public BPTR CurrentDirectoryName;
	public BPTR CommandDirectory;
	public int ReturnCode;
	public BPTR CommandName;
	public int FailLevel;
	public BPTR Prompt;
	public BPTR StandardInput;
	public BPTR CurrentInput;
	public BPTR CommandFile;
	public int Interactive;
	public int Background;
	public BPTR CurrentOutput;
	public int DefaultStack;
	public BPTR StandardOutput;
	public BPTR Module;
}

/// <summary>MorphOS V51.51 snapshot of one command-line process.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct CLIDataItem
{
	/// <summary>Fixed prefix including the first byte of the variable command.</summary>
	public const uint MinimumSize = 19;
	public const uint Size = 20;

	public int CLINumber;
	public int DefaultStack;
	public int GlobalVector;
	public uint Future;
	public sbyte Priority;
	public byte Flags;
	public fixed byte Command[1];
}

/// <summary>MorphOS V51.51 CLI snapshot and variable pointer table.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct CLIData
{
	/// <summary>Fixed prefix including the first CLI pointer slot.</summary>
	public const uint MinimumSize = 8;
	public const uint Size = 8;

	public uint NumberOfCLIs;
	public fixed uint CLIs[1];
}

/// <summary>
/// Header immediately preceding classic Amiga HUNK segment data. A public seglist
/// BPTR addresses <see cref="Next"/>; <see cref="AllocationSize"/> is one longword
/// before it and the segment data starts one longword after it.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DosHunkSegmentHeader
{
	public const uint Size = 8;

	public uint AllocationSize;
	public BPTR Next;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DeviceList
{
	public const uint Size = 44;

	public BPTR Next;
	public int Type;
	public APTR Task;
	public BPTR Lock;
	public DateStamp VolumeDate;
	public BPTR LockList;
	public int DiskType;
	public int Unused;
	public BPTR Name;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DevInfo
{
	public const uint Size = 44;

	public BPTR Next;
	public int Type;
	public APTR Task;
	public BPTR Lock;
	public BPTR Handler;
	public int StackSize;
	public int Priority;
	public int Startup;
	public BPTR SegmentList;
	public BPTR GlobalVector;
	public BPTR Name;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DosListHandler
{
	public const uint Size = 24;

	public BPTR Handler;
	public int StackSize;
	public int Priority;
	public uint Startup;
	public BPTR SegmentList;
	public BPTR GlobalVector;
}

/// <summary>Named view of the 24-byte assign arm in the public DosList union.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DosListAssignData
{
	public const uint Size = 24;

	public APTR AssignName;
	public APTR List;
	public uint Reserved0;
	public uint Reserved1;
	public uint Reserved2;
	public uint Reserved3;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DosList
{
	public const uint Size = 44;

	public BPTR Next;
	public int Type;
	public APTR Task;
	public BPTR Lock;
	/// <summary>24-byte union: handler, volume, or assign data.</summary>
	public DosListHandler Misc;
	public BPTR Name;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct AssignList
{
	public const uint Size = 8;

	public APTR Next;
	public BPTR Lock;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DevProc
{
	public const uint Size = 16;

	public APTR Port;
	public BPTR Lock;
	public uint Flags;
	public APTR DeviceNode;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct FileLock
{
	public const uint Size = 20;

	public BPTR Link;
	public int Key;
	public int Access;
	public APTR Task;
	public BPTR Volume;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Process
{
	public const uint Size = 228;

	public Task Task;
	public MsgPort MessagePort;
	public short Padding;
	public BPTR SegmentList;
	public int StackSize;
	public APTR GlobalVector;
	public int TaskNumber;
	public BPTR StackBase;
	public int Result2;
	public BPTR CurrentDirectory;
	public BPTR CurrentInput;
	public BPTR CurrentOutput;
	public APTR ConsoleTask;
	public APTR FileSystemTask;
	public BPTR CommandLineInterface;
	public APTR ReturnAddress;
	public APTR PacketWait;
	public APTR WindowPointer;
	public BPTR HomeDirectory;
	public int Flags;
	public APTR ExitCode;
	public int ExitData;
	public APTR Arguments;
	public MinList LocalVariables;
	public uint ShellPrivate;
	public BPTR CurrentError;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct LocalVar
{
	public const uint Size = 24;

	public Node Node;
	public ushort Flags;
	public APTR Value;
	public uint Length;
}
