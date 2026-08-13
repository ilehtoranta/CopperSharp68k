/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct List
{
	public const uint Size = 14;

	public APTR Head;
	public APTR Tail;
	public APTR TailPred;
	public NodeType Type;
	public byte Padding;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Library
{
	public const uint Size = 34;

	public Node Node;
	public LibraryFlags Flags;
	public byte Padding;
	public ushort NegativeSize;
	public ushort PositiveSize;
	public ushort Version;
	public ushort Revision;
	public APTR IdString;
	public uint Checksum;
	public ushort OpenCount;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Device
{
	public const uint Size = 34;

	public Library Library;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MsgPort
{
	public const uint Size = 34;

	public Node Node;
	public PortFlags Flags;
	public byte SignalBit;
	public APTR SignalTask;
	public List MessageList;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Unit
{
	public const uint Size = 38;

	public MsgPort MessagePort;
	public UnitFlags Flags;
	public byte Padding;
	public ushort OpenCount;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Task
{
	public const uint Size = 92;

	public Node Node;
	public TaskFlags Flags;
	public TaskState State;
	public sbyte IDNestCount;
	public sbyte TaskDisableNestCount;
	public uint SignalAllocated;
	public uint SignalWait;
	public uint SignalReceived;
	public uint SignalException;
	public ushort TrapAllocated;
	public ushort TrapEnabled;
	public APTR ExceptionData;
	public APTR ExceptionCode;
	public APTR TrapData;
	public APTR TrapCode;
	public APTR StackPointer;
	public APTR StackLower;
	public APTR StackUpper;
	public APTR Switch;
	public APTR Launch;
	public List MemoryEntries;
	public APTR UserData;
}

/// <summary>
/// MorphOS m68k Task layout. MorphOS replaces the classic trap words with
/// an extension pointer while retaining the same 92-byte record size.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MorphOSTask
{
	public const uint Size = 92;

	public Node Node;
	public TaskFlags Flags;
	public TaskState State;
	public sbyte IDNestCount;
	public sbyte TaskDisableNestCount;
	public uint SignalAllocated;
	public uint SignalWait;
	public uint SignalReceived;
	public uint SignalException;
	public APTR ETask;
	public APTR ExceptionData;
	public APTR ExceptionCode;
	public APTR TrapData;
	public APTR TrapCode;
	public APTR StackPointer;
	public APTR StackLower;
	public APTR StackUpper;
	public APTR Switch;
	public APTR Launch;
	public List MemoryEntries;
	public APTR UserData;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct StackSwapStruct
{
	public const uint Size = 12;

	public APTR Lower;
	public uint Upper;
	public APTR Pointer;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Interrupt
{
	public const uint Size = 22;

	public Node Node;
	public APTR Data;
	public APTR Code;
}

/// <summary>Exec's internal hardware interrupt vector record.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct IntVector
{
	public const uint Size = 12;

	public APTR Data;
	public APTR Code;
	public APTR Node;
}

/// <summary>Exec's software-interrupt priority queue record.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct SoftIntList
{
	public const uint Size = 16;

	public List List;
	public ushort Padding;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MemChunk
{
	public const uint Size = 8;

	public APTR Next;
	public uint Bytes;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MemHeader
{
	public const uint Size = 32;

	public Node Node;
	public ushort Attributes;
	public APTR First;
	public APTR Lower;
	public APTR Upper;
	public uint Free;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MemEntry
{
	public const uint Size = 8;

	/// <summary>
	/// The classic me_Un union: allocation requirements or the memory address.
	/// Interpret this 32-bit slot according to the owning API operation.
	/// </summary>
	public APTR AddressOrRequirements;
	public uint Length;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MemList
{
	public const uint Size = 24;

	public Node Node;
	public ushort NumberOfEntries;
	public MemEntry FirstEntry;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MemHandlerData
{
	public const uint Size = 12;

	public uint RequestSize;
	public uint RequestFlags;
	public MemoryHandlerFlags Flags;
}

/// <summary>MorphOS V51 free-block query result with its first flexible entry.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct FreeBlocksData
{
	public const uint Size = 12;

	public uint NumberOfBlocks;
	public MemEntry FirstBlock;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct IORequest
{
	public const uint Size = 32;

	public Message Message;
	public APTR Device;
	public APTR Unit;
	public DeviceCommand Command;
	public IOFlags Flags;
	public sbyte Error;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct IOStdReq
{
	public const uint Size = 48;

	public Message Message;
	public APTR Device;
	public APTR Unit;
	public DeviceCommand Command;
	public IOFlags Flags;
	public sbyte Error;
	public uint Actual;
	public uint Length;
	public APTR Data;
	public uint Offset;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Resident
{
	public const uint Size = 26;

	public ushort MatchWord;
	public APTR MatchTag;
	public APTR EndSkip;
	public ResidentFlags Flags;
	public byte Version;
	public byte Type;
	public sbyte Priority;
	public STRPTR Name;
	public STRPTR IdString;
	public APTR Init;
}

/// <summary>
/// Four-longword table referenced by an <see cref="ResidentFlags.AutoInit"/>
/// resident. Exec uses it to allocate the positive library base, install the
/// function table, apply the optional InitStruct table, and call InitFunction.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ResidentAutoInit
{
	public const uint Size = 16;

	public uint DataSize;
	public APTR FunctionTable;
	public APTR StructureTable;
	public APTR InitFunction;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct SemaphoreMessage
{
	public const uint Size = 24;

	public Message Message;
	public APTR Semaphore;
}

/// <summary>Obsolete Procure/Vacate semaphore record retained for ABI compatibility.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Semaphore
{
	public const uint Size = 36;

	public MsgPort MessagePort;
	public short Bids;
}

/// <summary>MorphOS trap notification message for native task exceptions.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct TaskTrapMessage
{
	public const uint Size = 40;

	public Message Message;
	public APTR Task;
	public uint Version;
	public uint Type;
	public uint DataAddressRegister;
	public uint DataStorageInterruptStatusRegister;
}

/// <summary>MorphOS V50.67 68k-emulation trap notification message.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct TaskTrapMessage68k
{
	public const uint Size = 48;

	public Message Message;
	public APTR Task;
	public uint Version;
	public uint Type;
	public uint StackFrameFormat;
	public APTR Address;
	public uint FLSW;
	public APTR EmulHandle;
}

/// <summary>MorphOS task creation extension record.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct TaskInitExtension
{
	public const uint Size = 8;

	public ushort Trap;
	public ushort Extension;
	public APTR Tags;
}

/// <summary>MorphOS PPC stack-swap argument block.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct PPCStackSwapArgs
{
	public const uint Size = 32;

	public fixed uint Arguments[8];
}

/// <summary>MorphOS 68k exception-frame record.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct TaskFrame68k
{
	public const uint Size = 66;

	public APTR ProgramCounter;
	public ushort StatusRegister;
	public fixed uint Registers[15];
}

/// <summary>MorphOS task stack-history entry.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct TaskStackHistoryEntry
{
	public const uint Size = 8;

	public uint Type;
	public uint Address;
}

/// <summary>MorphOS exec notification hook message.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ExecNotifyMessage
{
	public const uint Size = 16;

	public ExecListType Type;
	public ExecNotifyFlags Flags;
	public uint Extra;
	public APTR Extension;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct ExecBase
{
	public const uint Size = 632;

	public Library LibNode;
	public ushort SoftVer;
	public short LowMemChkSum;
	public uint ChkBase;
	public APTR ColdCapture;
	public APTR CoolCapture;
	public APTR WarmCapture;
	public APTR SysStkUpper;
	public APTR SysStkLower;
	public uint MaxLocMem;
	public APTR DebugEntry;
	public APTR DebugData;
	public APTR AlertData;
	public APTR MaxExtMem;
	public ushort ChkSum;
	public IntVector IntVector0;
	public IntVector IntVector1;
	public IntVector IntVector2;
	public IntVector IntVector3;
	public IntVector IntVector4;
	public IntVector IntVector5;
	public IntVector IntVector6;
	public IntVector IntVector7;
	public IntVector IntVector8;
	public IntVector IntVector9;
	public IntVector IntVector10;
	public IntVector IntVector11;
	public IntVector IntVector12;
	public IntVector IntVector13;
	public IntVector IntVector14;
	public IntVector IntVector15;
	public APTR ThisTask;
	public uint IdleCount;
	public uint DispCount;
	public ushort Quantum;
	public ushort Elapsed;
	public ushort SysFlags;
	public sbyte IDNestCount;
	public sbyte TaskDisableNestCount;
	public ushort AttentionFlags;
	public ushort AttentionReschedule;
	public APTR ResModules;
	public APTR TaskTrapCode;
	public APTR TaskExceptionCode;
	public APTR TaskExitCode;
	public uint TaskSignalAllocated;
	public ushort TaskTrapAllocated;
	public List MemList;
	public List ResourceList;
	public List DeviceList;
	public List InterruptList;
	public List LibraryList;
	public List PortList;
	public List TaskReady;
	public List TaskWait;
	public SoftIntList SoftInt0;
	public SoftIntList SoftInt1;
	public SoftIntList SoftInt2;
	public SoftIntList SoftInt3;
	public SoftIntList SoftInt4;
	public fixed int LastAlert[4];
	public byte VBlankFrequency;
	public byte PowerSupplyFrequency;
	public List SemaphoreList;
	public APTR KickMemPtr;
	public APTR KickTagPtr;
	public APTR KickCheckSum;
	public ushort ExPad0;
	public uint ExLaunchPoint;
	public APTR ExRamLibPrivate;
	public uint ExEClockFrequency;
	public uint ExCacheControl;
	public uint ExTaskId;
	public fixed uint ExReserved1[5];
	public APTR ExMmuLock;
	public fixed uint ExReserved2[3];
	public MinList ExMemHandlers;
	public APTR ExMemHandler;
}

/// <summary>
/// MorphOS ExecBase layout. The MorphOS extension region replaces the
/// classic reserved tail with the documented ABox and debug fields.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct MorphOSExecBase
{
	public const uint Size = 632;

	public Library LibNode;
	public ushort SoftVer;
	public short LowMemChkSum;
	public uint ChkBase;
	public APTR ColdCapture;
	public APTR CoolCapture;
	public APTR WarmCapture;
	public APTR SysStkUpper;
	public APTR SysStkLower;
	public uint MaxLocMem;
	public APTR DebugEntry;
	public APTR DebugData;
	public APTR AlertData;
	public APTR MaxExtMem;
	public ushort ChkSum;
	public IntVector IntVector0;
	public IntVector IntVector1;
	public IntVector IntVector2;
	public IntVector IntVector3;
	public IntVector IntVector4;
	public IntVector IntVector5;
	public IntVector IntVector6;
	public IntVector IntVector7;
	public IntVector IntVector8;
	public IntVector IntVector9;
	public IntVector IntVector10;
	public IntVector IntVector11;
	public IntVector IntVector12;
	public IntVector IntVector13;
	public IntVector IntVector14;
	public IntVector IntVector15;
	public APTR ThisTask;
	public uint IdleCount;
	public uint DispCount;
	public ushort Quantum;
	public ushort Elapsed;
	public ushort SysFlags;
	public sbyte IDNestCount;
	public sbyte TaskDisableNestCount;
	public ushort AttentionFlags;
	public ushort AttentionReschedule;
	public APTR ResModules;
	public APTR TaskTrapCode;
	public APTR TaskExceptionCode;
	public APTR TaskExitCode;
	public uint TaskSignalAllocated;
	public ushort TaskTrapAllocated;
	public List MemList;
	public List ResourceList;
	public List DeviceList;
	public List InterruptList;
	public List LibraryList;
	public List PortList;
	public List TaskReady;
	public List TaskWait;
	public SoftIntList SoftInt0;
	public SoftIntList SoftInt1;
	public SoftIntList SoftInt2;
	public SoftIntList SoftInt3;
	public SoftIntList SoftInt4;
	public fixed int LastAlert[4];
	public byte VBlankFrequency;
	public byte PowerSupplyFrequency;
	public List SemaphoreList;
	public APTR KickMemPtr;
	public APTR KickTagPtr;
	public APTR KickCheckSum;
	public ushort ExPad0;
	public uint ExLaunchPoint;
	public APTR ExRamLibPrivate;
	public uint ExEClockFrequency;
	public uint ExCacheControl;
	public uint ExTaskId;
	public uint ExEmulHandleSize;
	public APTR ExPpcTrapMsgPort;
	public fixed uint ExReserved1[3];
	public APTR ExMmuLock;
	public APTR ExPatchPool;
	public APTR ExPpcTaskExitCode;
	public uint ExDebugFlags;
	public MinList ExMemHandlers;
	public APTR ExMemHandler;
}
