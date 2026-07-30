/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name, AmigaLibraryBasePolicy.ExecBase)]
public static class Exec
{
	public const string Name = "exec.library";

	[System.Flags]
	public enum MemoryFlags : uint
	{
		Any = 0,
		Public = 1u << 0,
		Chip = 1u << 1,
		Fast = 1u << 2,
		Local = 1u << 8,
		TwentyFourBitDma = 1u << 9,
		Kick = 1u << 10,
		Clear = 1u << 16,
		Largest = 1u << 17,
		Reverse = 1u << 18,
		Total = 1u << 19,
		NoExpunge = 1u << 31,
	}

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint Supervisor(
		[M68kRegister(M68kRegister.A5)] uint userFunction);

	[AmigaLvo(-72)]
	public static extern void InitCode(
		[M68kRegister(M68kRegister.D0)] uint startClass,
		[M68kRegister(M68kRegister.D1)] uint version);

	[AmigaLvo(-78)]
	public static extern void InitStruct(
		[M68kRegister(M68kRegister.A1)] uint initTable,
		[M68kRegister(M68kRegister.A2)] uint memory,
		[M68kRegister(M68kRegister.D0)] uint size);

	[AmigaLvo(-84)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint MakeLibrary(
		[M68kRegister(M68kRegister.A0)] uint vectors,
		[M68kRegister(M68kRegister.A1)] uint structure,
		[M68kRegister(M68kRegister.A2)] uint init,
		[M68kRegister(M68kRegister.D0)] uint dataSize,
		[M68kRegister(M68kRegister.D1)] uint segList);

	[AmigaLvo(-90)]
	public static extern void MakeFunctions(
		[M68kRegister(M68kRegister.A0)] uint target,
		[M68kRegister(M68kRegister.A1)] uint functionArray,
		[M68kRegister(M68kRegister.A2)] uint functionDispBase);

	[AmigaLvo(-96)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint FindResident(
		[M68kRegister(M68kRegister.A1)] CString name);

	[AmigaLvo(-102)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint InitResident(
		[M68kRegister(M68kRegister.A1)] uint resident,
		[M68kRegister(M68kRegister.D1)] uint segList);

	[AmigaLvo(-108)]
	public static extern void Alert(
		[M68kRegister(M68kRegister.D7)] uint alertNum);

	[AmigaLvo(-114)]
	public static extern void Debug(
		[M68kRegister(M68kRegister.D0)] uint flags);

	[AmigaLvo(-120)]
	public static extern void Disable();

	[AmigaLvo(-126)]
	public static extern void Enable();

	[AmigaLvo(-132)]
	public static extern void Forbid();

	[AmigaLvo(-138)]
	public static extern void Permit();

	[AmigaLvo(-144)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ushort SetSR(
		[M68kRegister(M68kRegister.D0)] ushort newStatus,
		[M68kRegister(M68kRegister.D1)] ushort mask);

	[AmigaLvo(-150)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SuperState();

	[AmigaLvo(-156)]
	public static extern void UserState(
		[M68kRegister(M68kRegister.D0)] uint systemStack);

	[AmigaLvo(-162)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetIntVector(
		[M68kRegister(M68kRegister.D0)] int intNumber,
		[M68kRegister(M68kRegister.A1)] uint interrupt);

	[AmigaLvo(-168)]
	public static extern void AddIntServer(
		[M68kRegister(M68kRegister.D0)] int intNumber,
		[M68kRegister(M68kRegister.A1)] uint interrupt);

	[AmigaLvo(-174)]
	public static extern void RemIntServer(
		[M68kRegister(M68kRegister.D0)] int intNumber,
		[M68kRegister(M68kRegister.A1)] uint interrupt);

	[AmigaLvo(-180)]
	public static extern void Cause(
		[M68kRegister(M68kRegister.A1)] uint interrupt);

	[AmigaLvo(-186)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint Allocate(
		[M68kRegister(M68kRegister.A0)] uint freeList,
		[M68kRegister(M68kRegister.D0)] uint byteSize);

	[AmigaLvo(-192)]
	public static extern void Deallocate(
		[M68kRegister(M68kRegister.A0)] uint freeList,
		[M68kRegister(M68kRegister.A1)] uint memoryBlock,
		[M68kRegister(M68kRegister.D0)] uint byteSize);

	[AmigaLvo(-198)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocMem(
		[M68kRegister(M68kRegister.D0)] uint byteSize,
		[M68kRegister(M68kRegister.D1)] MemoryFlags attributes);

	[AmigaLvo(-204)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocAbs(
		[M68kRegister(M68kRegister.D0)] uint byteSize,
		[M68kRegister(M68kRegister.A1)] uint location);

	[AmigaLvo(-210)]
	public static extern void FreeMem(
		[M68kRegister(M68kRegister.A1)] uint memoryBlock,
		[M68kRegister(M68kRegister.D0)] uint byteSize);

	[AmigaLvo(-216)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AvailMem(
		[M68kRegister(M68kRegister.D1)] MemoryFlags attributes);

	[AmigaLvo(-222)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocEntry(
		[M68kRegister(M68kRegister.A0)] uint memoryList);

	[AmigaLvo(-228)]
	public static extern void FreeEntry(
		[M68kRegister(M68kRegister.A0)] uint memoryList);

	[AmigaLvo(-234)]
	public static extern void Insert(
		[M68kRegister(M68kRegister.A0)] uint list,
		[M68kRegister(M68kRegister.A1)] uint node,
		[M68kRegister(M68kRegister.A2)] uint listNode);

	[AmigaLvo(-240)]
	public static extern void AddHead(
		[M68kRegister(M68kRegister.A0)] uint list,
		[M68kRegister(M68kRegister.A1)] uint node);

	[AmigaLvo(-246)]
	public static extern void AddTail(
		[M68kRegister(M68kRegister.A0)] uint list,
		[M68kRegister(M68kRegister.A1)] uint node);

	[AmigaLvo(-252)]
	public static extern void Remove(
		[M68kRegister(M68kRegister.A1)] uint node);

	[AmigaLvo(-258)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint RemHead(
		[M68kRegister(M68kRegister.A0)] uint list);

	[AmigaLvo(-264)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint RemTail(
		[M68kRegister(M68kRegister.A0)] uint list);

	[AmigaLvo(-270)]
	public static extern void Enqueue(
		[M68kRegister(M68kRegister.A0)] uint list,
		[M68kRegister(M68kRegister.A1)] uint node);

	[AmigaLvo(-276)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint FindName(
		[M68kRegister(M68kRegister.A0)] uint list,
		[M68kRegister(M68kRegister.A1)] CString name);

	[AmigaLvo(-282)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AddTask(
		[M68kRegister(M68kRegister.A1)] uint task,
		[M68kRegister(M68kRegister.A2)] uint initialPc,
		[M68kRegister(M68kRegister.A3)] uint finalPc);

	[AmigaLvo(-288)]
	public static extern void RemTask(
		[M68kRegister(M68kRegister.A1)] uint task);

	[AmigaLvo(-294)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint FindTask(
		[M68kRegister(M68kRegister.A1)] CString name);

	[AmigaLvo(-300)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern sbyte SetTaskPri(
		[M68kRegister(M68kRegister.A1)] uint task,
		[M68kRegister(M68kRegister.D0)] sbyte priority);

	[AmigaLvo(-306)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetSignal(
		[M68kRegister(M68kRegister.D0)] uint newSignals,
		[M68kRegister(M68kRegister.D1)] uint signalMask);

	[AmigaLvo(-312)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetExcept(
		[M68kRegister(M68kRegister.D0)] uint newSignals,
		[M68kRegister(M68kRegister.D1)] uint signalMask);

	[AmigaLvo(-318)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint Wait(
		[M68kRegister(M68kRegister.D0)] uint signalSet);

	[AmigaLvo(-324)]
	public static extern void Signal(
		[M68kRegister(M68kRegister.A1)] uint task,
		[M68kRegister(M68kRegister.D0)] uint signalSet);

	[AmigaLvo(-330)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern sbyte AllocSignal(
		[M68kRegister(M68kRegister.D0)] int signalNum);

	[AmigaLvo(-336)]
	public static extern void FreeSignal(
		[M68kRegister(M68kRegister.D0)] int signalNum);

	[AmigaLvo(-342)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern sbyte AllocTrap(
		[M68kRegister(M68kRegister.D0)] int trapNum);

	[AmigaLvo(-348)]
	public static extern void FreeTrap(
		[M68kRegister(M68kRegister.D0)] int trapNum);

	[AmigaLvo(-354)]
	public static extern void AddPort(
		[M68kRegister(M68kRegister.A1)] uint port);

	[AmigaLvo(-360)]
	public static extern void RemPort(
		[M68kRegister(M68kRegister.A1)] uint port);

	[AmigaLvo(-366)]
	public static extern void PutMsg(
		[M68kRegister(M68kRegister.A0)] uint port,
		[M68kRegister(M68kRegister.A1)] uint message);

	[AmigaLvo(-372)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetMsg(
		[M68kRegister(M68kRegister.A0)] uint port);

	[AmigaLvo(-378)]
	public static extern void ReplyMsg(
		[M68kRegister(M68kRegister.A1)] uint message);

	[AmigaLvo(-384)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint WaitPort(
		[M68kRegister(M68kRegister.A0)] uint port);

	[AmigaLvo(-390)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint FindPort(
		[M68kRegister(M68kRegister.A1)] CString name);

	[AmigaLvo(-396)]
	public static extern void AddLibrary(
		[M68kRegister(M68kRegister.A1)] uint library);

	[AmigaLvo(-402)]
	public static extern void RemLibrary(
		[M68kRegister(M68kRegister.A1)] uint library);

	[AmigaLvo(-408)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR? OldOpenLibrary(
		[M68kRegister(M68kRegister.A1)] CString name);

	[AmigaLvo(-414)]
	public static extern void CloseLibrary(
		[M68kRegister(M68kRegister.A1)] APTR library);

	[AmigaLvo(-420)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetFunction(
		[M68kRegister(M68kRegister.A1)] uint library,
		[M68kRegister(M68kRegister.A0)] int functionOffset,
		[M68kRegister(M68kRegister.D0)] uint newFunction);

	[AmigaLvo(-426)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SumLibrary(
		[M68kRegister(M68kRegister.A1)] uint library);

	[AmigaLvo(-432)]
	public static extern void AddDevice(
		[M68kRegister(M68kRegister.A1)] uint device);

	[AmigaLvo(-438)]
	public static extern void RemDevice(
		[M68kRegister(M68kRegister.A1)] uint device);

	[AmigaLvo(-444)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern sbyte OpenDevice(
		[M68kRegister(M68kRegister.A0)] CString deviceName,
		[M68kRegister(M68kRegister.D0)] uint unit,
		[M68kRegister(M68kRegister.A1)] uint ioRequest,
		[M68kRegister(M68kRegister.D1)] uint flags);

	[AmigaLvo(-450)]
	public static extern void CloseDevice(
		[M68kRegister(M68kRegister.A1)] uint ioRequest);

	[AmigaLvo(-456)]
	public static extern void DoIO(
		[M68kRegister(M68kRegister.A1)] uint ioRequest);

	[AmigaLvo(-462)]
	public static extern void SendIO(
		[M68kRegister(M68kRegister.A1)] uint ioRequest);

	[AmigaLvo(-468)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CheckIO(
		[M68kRegister(M68kRegister.A1)] uint ioRequest);

	[AmigaLvo(-474)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern sbyte WaitIO(
		[M68kRegister(M68kRegister.A1)] uint ioRequest);

	[AmigaLvo(-480)]
	public static extern void AbortIO(
		[M68kRegister(M68kRegister.A1)] uint ioRequest);

	[AmigaLvo(-486)]
	public static extern void AddResource(
		[M68kRegister(M68kRegister.A1)] uint resource);

	[AmigaLvo(-492)]
	public static extern void RemResource(
		[M68kRegister(M68kRegister.A1)] uint resource);

	[AmigaLvo(-498)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint OpenResource(
		[M68kRegister(M68kRegister.A1)] CString resourceName);

	[AmigaLvo(-522)]
	public static extern void RawDoFmt(
		[M68kRegister(M68kRegister.A0)] CString formatString,
		[M68kRegister(M68kRegister.A1)] uint dataStream,
		[M68kRegister(M68kRegister.A2)] uint putCharProc,
		[M68kRegister(M68kRegister.A3)] uint putCharData);

	[AmigaLvo(-528)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ushort GetCC();

	[AmigaLvo(-534)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint TypeOfMem(
		[M68kRegister(M68kRegister.A1)] uint address);

	[AmigaLvo(-540)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint Procure(
		[M68kRegister(M68kRegister.A0)] uint signalSemaphore,
		[M68kRegister(M68kRegister.A1)] uint semaphoreMessage);

	[AmigaLvo(-546)]
	public static extern void Vacate(
		[M68kRegister(M68kRegister.A0)] uint signalSemaphore,
		[M68kRegister(M68kRegister.A1)] uint semaphoreMessage);

	[AmigaLvo(-552)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR? OpenLibrary(
		[M68kRegister(M68kRegister.A1)] CString name,
		[M68kRegister(M68kRegister.D0)] uint minimumVersion);

	[AmigaLvo(-558)]
	public static extern void InitSemaphore(
		[M68kRegister(M68kRegister.A0)] uint signalSemaphore);

	[AmigaLvo(-564)]
	public static extern void ObtainSemaphore(
		[M68kRegister(M68kRegister.A0)] uint signalSemaphore);

	[AmigaLvo(-570)]
	public static extern void ReleaseSemaphore(
		[M68kRegister(M68kRegister.A0)] uint signalSemaphore);

	[AmigaLvo(-576)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AttemptSemaphore(
		[M68kRegister(M68kRegister.A0)] uint signalSemaphore);

	[AmigaLvo(-582)]
	public static extern void ObtainSemaphoreList(
		[M68kRegister(M68kRegister.A0)] uint signalSemaphore);

	[AmigaLvo(-588)]
	public static extern void ReleaseSemaphoreList(
		[M68kRegister(M68kRegister.A0)] uint signalSemaphore);

	[AmigaLvo(-594)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint FindSemaphore(
		[M68kRegister(M68kRegister.A1)] CString name);

	[AmigaLvo(-600)]
	public static extern void AddSemaphore(
		[M68kRegister(M68kRegister.A1)] uint signalSemaphore);

	[AmigaLvo(-606)]
	public static extern void RemSemaphore(
		[M68kRegister(M68kRegister.A1)] uint signalSemaphore);

	[AmigaLvo(-612)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SumKickData();

	[AmigaLvo(-618)]
	public static extern void AddMemList(
		[M68kRegister(M68kRegister.D0)] uint size,
		[M68kRegister(M68kRegister.D1)] uint attributes,
		[M68kRegister(M68kRegister.D2)] int priority,
		[M68kRegister(M68kRegister.A0)] uint baseAddress,
		[M68kRegister(M68kRegister.A1)] CString name);

	[AmigaLvo(-624)]
	public static extern void CopyMem(
		[M68kRegister(M68kRegister.A0)] uint source,
		[M68kRegister(M68kRegister.A1)] uint destination,
		[M68kRegister(M68kRegister.D0)] uint size);

	[AmigaLvo(-630)]
	public static extern void CopyMemQuick(
		[M68kRegister(M68kRegister.A0)] uint source,
		[M68kRegister(M68kRegister.A1)] uint destination,
		[M68kRegister(M68kRegister.D0)] uint size);

	[AmigaLvo(-636)]
	public static extern void CacheClearU();

	[AmigaLvo(-642)]
	public static extern void CacheClearE(
		[M68kRegister(M68kRegister.A0)] uint address,
		[M68kRegister(M68kRegister.D0)] uint length,
		[M68kRegister(M68kRegister.D1)] uint caches);

	[AmigaLvo(-648)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CacheControl(
		[M68kRegister(M68kRegister.D0)] uint cacheBits,
		[M68kRegister(M68kRegister.D1)] uint cacheMask);

	[AmigaLvo(-654)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CreateIORequest(
		[M68kRegister(M68kRegister.A0)] uint port,
		[M68kRegister(M68kRegister.D0)] uint size);

	[AmigaLvo(-660)]
	public static extern void DeleteIORequest(
		[M68kRegister(M68kRegister.A0)] uint ioRequest);

	[AmigaLvo(-666)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CreateMsgPort();

	[AmigaLvo(-672)]
	public static extern void DeleteMsgPort(
		[M68kRegister(M68kRegister.A0)] uint port);

	[AmigaLvo(-678)]
	public static extern void ObtainSemaphoreShared(
		[M68kRegister(M68kRegister.A0)] uint signalSemaphore);

	[AmigaLvo(-684)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocVec(
		[M68kRegister(M68kRegister.D0)] uint byteSize,
		[M68kRegister(M68kRegister.D1)] uint attributes);

	[AmigaLvo(-690)]
	public static extern void FreeVec(
		[M68kRegister(M68kRegister.A1)] uint memoryBlock);

	[AmigaLvo(-696)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CreatePool(
		[M68kRegister(M68kRegister.D0)] uint requirements,
		[M68kRegister(M68kRegister.D1)] uint puddleSize,
		[M68kRegister(M68kRegister.D2)] uint threshold);

	[AmigaLvo(-702)]
	public static extern void DeletePool(
		[M68kRegister(M68kRegister.A0)] uint poolHeader);

	[AmigaLvo(-708)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocPooled(
		[M68kRegister(M68kRegister.A0)] uint poolHeader,
		[M68kRegister(M68kRegister.D0)] uint memorySize);

	[AmigaLvo(-714)]
	public static extern void FreePooled(
		[M68kRegister(M68kRegister.A0)] uint poolHeader,
		[M68kRegister(M68kRegister.A1)] uint memory,
		[M68kRegister(M68kRegister.D0)] uint memorySize);

	[AmigaLvo(-720)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AttemptSemaphoreShared(
		[M68kRegister(M68kRegister.A0)] uint signalSemaphore);

	[AmigaLvo(-726)]
	public static extern void ColdReboot();

	[AmigaLvo(-732)]
	public static extern void StackSwap(
		[M68kRegister(M68kRegister.A0)] uint newStack);

	[AmigaLvo(-738)]
	public static extern void ChildFree(
		[M68kRegister(M68kRegister.D0)] uint tid);

	[AmigaLvo(-744)]
	public static extern void ChildOrphan(
		[M68kRegister(M68kRegister.D0)] uint tid);

	[AmigaLvo(-750)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ChildStatus(
		[M68kRegister(M68kRegister.D0)] uint tid);

	[AmigaLvo(-756)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ChildWait(
		[M68kRegister(M68kRegister.D0)] uint tid);

	[AmigaLvo(-762)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CachePreDMA(
		[M68kRegister(M68kRegister.A0)] uint address,
		[M68kRegister(M68kRegister.A1)] uint length,
		[M68kRegister(M68kRegister.D0)] uint flags);

	[AmigaLvo(-768)]
	public static extern void CachePostDMA(
		[M68kRegister(M68kRegister.A0)] uint address,
		[M68kRegister(M68kRegister.A1)] uint length,
		[M68kRegister(M68kRegister.D0)] uint flags);

	[AmigaLvo(-774)]
	public static extern void AddMemHandler(
		[M68kRegister(M68kRegister.A1)] uint memHandler);

	[AmigaLvo(-780)]
	public static extern void RemMemHandler(
		[M68kRegister(M68kRegister.A1)] uint memHandler);

	[AmigaLvo(-786)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ObtainQuickVector(
		[M68kRegister(M68kRegister.A0)] uint interruptCode);

	// MorphOS m68k ABI call.
	[AmigaLvo(-504)]
	public static extern void RawIOInit();

	// MorphOS m68k ABI call.
	[AmigaLvo(-510)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern byte RawMayGetChar();

	// MorphOS m68k ABI call.
	[AmigaLvo(-516)]
	public static extern void RawPutChar(
		[M68kRegister(M68kRegister.D0)] byte character);

	// MorphOS m68k ABI call.
	[AmigaLvo(-738)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewGetTaskAttrsA(
		[M68kRegister(M68kRegister.A0)] uint task,
		[M68kRegister(M68kRegister.A1), M68kWritesBuffer] uint data,
		[M68kRegister(M68kRegister.D0)] uint dataSize,
		[M68kRegister(M68kRegister.D1)] uint type,
		[M68kRegister(M68kRegister.A2)] uint tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(-744)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewSetTaskAttrsA(
		[M68kRegister(M68kRegister.A0)] uint task,
		[M68kRegister(M68kRegister.A1)] uint data,
		[M68kRegister(M68kRegister.D0)] uint dataSize,
		[M68kRegister(M68kRegister.D1)] uint type,
		[M68kRegister(M68kRegister.A2)] uint tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(-792)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewSetFunction(
		[M68kRegister(M68kRegister.A0)] uint library,
		[M68kRegister(M68kRegister.A1)] uint function,
		[M68kRegister(M68kRegister.D0)] int offset,
		[M68kRegister(M68kRegister.A2)] uint tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(-798)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewCreateLibrary(
		[M68kRegister(M68kRegister.A0)] uint tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(-804)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewPPCStackSwap(
		[M68kRegister(M68kRegister.A0)] uint newStack,
		[M68kRegister(M68kRegister.A1)] uint function,
		[M68kRegister(M68kRegister.A2)] uint args);

	// MorphOS m68k ABI call.
	[AmigaLvo(-810)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR? TaggedOpenLibrary(
		[M68kRegister(M68kRegister.D0)] int tag);

	// MorphOS m68k ABI call.
	[AmigaLvo(-816)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ReadGayle();

	// MorphOS m68k ABI call.
	[AmigaLvo(-828)]
	public static extern void CacheFlushDataArea(
		[M68kRegister(M68kRegister.A0)] uint address,
		[M68kRegister(M68kRegister.D0)] uint length);

	// MorphOS m68k ABI call.
	[AmigaLvo(-834)]
	public static extern void CacheInvalidInstArea(
		[M68kRegister(M68kRegister.A0)] uint address,
		[M68kRegister(M68kRegister.D0)] uint length);

	// MorphOS m68k ABI call.
	[AmigaLvo(-840)]
	public static extern void CacheInvalidDataArea(
		[M68kRegister(M68kRegister.A0)] uint address,
		[M68kRegister(M68kRegister.D0)] uint length);

	// MorphOS m68k ABI call.
	[AmigaLvo(-846)]
	public static extern void CacheFlushDataInstArea(
		[M68kRegister(M68kRegister.A0)] uint address,
		[M68kRegister(M68kRegister.D0)] uint length);

	// MorphOS m68k ABI call.
	[AmigaLvo(-852)]
	public static extern void CacheTrashCacheArea(
		[M68kRegister(M68kRegister.A0)] uint address,
		[M68kRegister(M68kRegister.D0)] uint length);

	// MorphOS m68k ABI call.
	[AmigaLvo(-858)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocTaskPooled(
		[M68kRegister(M68kRegister.D0)] uint byteSize);

	// MorphOS m68k ABI call.
	[AmigaLvo(-864)]
	public static extern void FreeTaskPooled(
		[M68kRegister(M68kRegister.A1)] uint memory,
		[M68kRegister(M68kRegister.D0)] uint byteSize);

	// MorphOS m68k ABI call.
	[AmigaLvo(-870)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocVecTaskPooled(
		[M68kRegister(M68kRegister.D0)] uint byteSize);

	// MorphOS m68k ABI call.
	[AmigaLvo(-876)]
	public static extern void FreeVecTaskPooled(
		[M68kRegister(M68kRegister.A1)] uint memory);

	// MorphOS m68k ABI call.
	[AmigaLvo(-882)]
	public static extern void FlushPool(
		[M68kRegister(M68kRegister.A0)] uint poolHeader);

	// MorphOS m68k ABI call.
	[AmigaLvo(-888)]
	public static extern void FlushTaskPool();

	// MorphOS m68k ABI call. CopperOS call.
	[AmigaLvo(-894)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocVecPooled(
		[M68kRegister(M68kRegister.A0)] uint poolHeader,
		[M68kRegister(M68kRegister.D0)] uint byteSize);

	// MorphOS m68k ABI call. CopperOS call.
	[AmigaLvo(-900)]
	public static extern void FreeVecPooled(
		[M68kRegister(M68kRegister.A0)] uint poolHeader,
		[M68kRegister(M68kRegister.A1)] uint memory);

	// MorphOS m68k ABI call.
	[AmigaLvo(-906)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewGetSystemAttrsA(
		[M68kRegister(M68kRegister.A0)] uint data,
		[M68kRegister(M68kRegister.D0)] uint dataSize,
		[M68kRegister(M68kRegister.D1)] uint type,
		[M68kRegister(M68kRegister.A1)] uint tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(-912)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewSetSystemAttrsA(
		[M68kRegister(M68kRegister.A0)] uint data,
		[M68kRegister(M68kRegister.D0)] uint dataSize,
		[M68kRegister(M68kRegister.D1)] uint type,
		[M68kRegister(M68kRegister.A1)] uint tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(-918)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewCreateTaskA(
		[M68kRegister(M68kRegister.A0)] uint tags);

	// CopperOS call. MorphOS exposes this slot through native function-pointer ABI, not m68k ABI.
	[AmigaLvo(-930)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocateAligned(
		[M68kRegister(M68kRegister.A0)] uint freeList,
		[M68kRegister(M68kRegister.D0)] uint byteSize,
		[M68kRegister(M68kRegister.D1)] uint alignment,
		[M68kRegister(M68kRegister.D2)] uint alignmentOffset);

	// CopperOS call. MorphOS exposes this slot through native function-pointer ABI, not m68k ABI.
	[AmigaLvo(-936)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocMemAligned(
		[M68kRegister(M68kRegister.D0)] uint byteSize,
		[M68kRegister(M68kRegister.D1)] uint attributes,
		[M68kRegister(M68kRegister.D2)] uint alignment,
		[M68kRegister(M68kRegister.D3)] uint alignmentOffset);

	// CopperOS call. MorphOS exposes this slot through native function-pointer ABI, not m68k ABI.
	[AmigaLvo(-942)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocVecAligned(
		[M68kRegister(M68kRegister.D0)] uint byteSize,
		[M68kRegister(M68kRegister.D1)] uint attributes,
		[M68kRegister(M68kRegister.D2)] uint alignment,
		[M68kRegister(M68kRegister.D3)] uint alignmentOffset);

	// MorphOS m68k ABI call. CopperOS call.
	[AmigaLvo(-960)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint FindExecNode(
		[M68kRegister(M68kRegister.D0)] uint type,
		[M68kRegister(M68kRegister.A0)] CString name);

	// MorphOS m68k ABI call. CopperOS call.
	[AmigaLvo(-966)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AddExecNodeA(
		[M68kRegister(M68kRegister.A0)] uint node,
		[M68kRegister(M68kRegister.A1)] uint tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(-972)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocVecDMA(
		[M68kRegister(M68kRegister.D0)] uint byteSize,
		[M68kRegister(M68kRegister.D1)] uint attributes);

	// MorphOS m68k ABI call.
	[AmigaLvo(-978)]
	public static extern void FreeVecDMA(
		[M68kRegister(M68kRegister.A1)] uint memory);

	// CopperOS call. MorphOS exposes this slot through native function-pointer ABI, not m68k ABI.
	[AmigaLvo(-984)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocPooledAligned(
		[M68kRegister(M68kRegister.A0)] uint poolHeader,
		[M68kRegister(M68kRegister.D0)] uint byteSize,
		[M68kRegister(M68kRegister.D1)] uint alignment,
		[M68kRegister(M68kRegister.D2)] uint alignmentOffset);

	// CopperOS call. MorphOS exposes this slot through native function-pointer ABI, not m68k ABI.
	[AmigaLvo(-990)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AddResident(
		[M68kRegister(M68kRegister.A1)] uint resident);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1026)]
	public static extern void DumpTaskState(
		[M68kRegister(M68kRegister.A0)] uint task);

	// CopperOS call. MorphOS exposes this slot through native function-pointer ABI, not m68k ABI.
	[AmigaLvo(-1050)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AvailPool(
		[M68kRegister(M68kRegister.A0)] uint poolHeader,
		[M68kRegister(M68kRegister.D0)] uint flags);

	// CopperOS call. MorphOS exposes this slot through native function-pointer ABI, not m68k ABI.
	[AmigaLvo(-1062)]
	public static extern void PutMsgHead(
		[M68kRegister(M68kRegister.A0)] uint port,
		[M68kRegister(M68kRegister.A1)] uint message);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1068)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewGetTaskPIDAttrsA(
		[M68kRegister(M68kRegister.D0)] uint pid,
		[M68kRegister(M68kRegister.A0)] uint data,
		[M68kRegister(M68kRegister.D1)] uint dataSize,
		[M68kRegister(M68kRegister.D2)] uint type,
		[M68kRegister(M68kRegister.A1)] uint tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1074)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewSetTaskPIDAttrsA(
		[M68kRegister(M68kRegister.D0)] uint pid,
		[M68kRegister(M68kRegister.A0)] uint data,
		[M68kRegister(M68kRegister.D1)] uint dataSize,
		[M68kRegister(M68kRegister.D2)] uint type,
		[M68kRegister(M68kRegister.A1)] uint tags);
}
