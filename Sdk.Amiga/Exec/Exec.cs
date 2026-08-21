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
		Swap = 1u << 11,
		ThirtyOneBit = 1u << 12,
		Clear = 1u << 16,
		Largest = 1u << 17,
		Reverse = 1u << 18,
		Total = 1u << 19,
		SemaphoreProtected = 1u << 20,
		NoExpunge = 1u << 31,
	}

	[AmigaLvo(ExecLvo.Supervisor)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint Supervisor(
		[M68kRegister(M68kRegister.A5)] APTR userFunction);

	[AmigaLvo(ExecLvo.InitCode)]
	public static extern void InitCode(
		[M68kRegister(M68kRegister.D0)] uint startClass,
		[M68kRegister(M68kRegister.D1)] uint version);

	[AmigaLvo(ExecLvo.InitStruct)]
	public static extern void InitStruct(
		[M68kRegister(M68kRegister.A1)] APTR initTable,
		[M68kRegister(M68kRegister.A2)] APTR memory,
		[M68kRegister(M68kRegister.D0)] uint size);

	[AmigaLvo(ExecLvo.MakeLibrary)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR MakeLibrary(
		[M68kRegister(M68kRegister.A0)] APTR vectors,
		[M68kRegister(M68kRegister.A1)] APTR structure,
		[M68kRegister(M68kRegister.A2)] APTR init,
		[M68kRegister(M68kRegister.D0)] uint dataSize,
		[M68kRegister(M68kRegister.D1)] BPTR segList);

	[AmigaLvo(ExecLvo.MakeFunctions)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint MakeFunctions(
		[M68kRegister(M68kRegister.A0)] APTR target,
		[M68kRegister(M68kRegister.A1)] APTR functionArray,
		[M68kRegister(M68kRegister.A2)] int functionDispBase);

	[AmigaLvo(ExecLvo.FindResident)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR FindResident(
		[M68kRegister(M68kRegister.A1)] CString name);

	[AmigaLvo(ExecLvo.InitResident)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR InitResident(
		[M68kRegister(M68kRegister.A1)] APTR resident,
		[M68kRegister(M68kRegister.D1)] BPTR segList);

	[AmigaLvo(ExecLvo.Alert)]
	public static extern void Alert(
		[M68kRegister(M68kRegister.D7)] uint alertNum);

	[AmigaLvo(ExecLvo.Debug)]
	public static extern void Debug(
		[M68kRegister(M68kRegister.D0)] uint flags);

	[AmigaLvo(ExecLvo.Disable)]
	public static extern void Disable();

	[AmigaLvo(ExecLvo.Enable)]
	public static extern void Enable();

	[AmigaLvo(ExecLvo.Forbid)]
	public static extern void Forbid();

	[AmigaLvo(ExecLvo.Permit)]
	public static extern void Permit();

	[AmigaLvo(ExecLvo.SetSR)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ushort SetSR(
		[M68kRegister(M68kRegister.D0)] ushort newStatus,
		[M68kRegister(M68kRegister.D1)] ushort mask);

	[AmigaLvo(ExecLvo.SuperState)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SuperState();

	[AmigaLvo(ExecLvo.UserState)]
	public static extern void UserState(
		[M68kRegister(M68kRegister.D0)] uint systemStack);

	[AmigaLvo(ExecLvo.SetIntVector)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR SetIntVector(
		[M68kRegister(M68kRegister.D0)] int intNumber,
		[M68kRegister(M68kRegister.A1)] APTR interrupt);

	[AmigaLvo(ExecLvo.AddIntServer)]
	public static extern void AddIntServer(
		[M68kRegister(M68kRegister.D0)] int intNumber,
		[M68kRegister(M68kRegister.A1)] APTR interrupt);

	[AmigaLvo(ExecLvo.RemIntServer)]
	public static extern void RemIntServer(
		[M68kRegister(M68kRegister.D0)] int intNumber,
		[M68kRegister(M68kRegister.A1)] APTR interrupt);

	[AmigaLvo(ExecLvo.Cause)]
	public static extern void Cause(
		[M68kRegister(M68kRegister.A1)] APTR interrupt);

	[AmigaLvo(ExecLvo.Allocate)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR Allocate(
		[M68kRegister(M68kRegister.A0)] APTR freeList,
		[M68kRegister(M68kRegister.D0)] uint byteSize);

	[AmigaLvo(ExecLvo.Deallocate)]
	public static extern void Deallocate(
		[M68kRegister(M68kRegister.A0)] APTR freeList,
		[M68kRegister(M68kRegister.A1)] APTR memoryBlock,
		[M68kRegister(M68kRegister.D0)] uint byteSize);

	[AmigaLvo(ExecLvo.AllocMem)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR AllocMem(
		[M68kRegister(M68kRegister.D0)] uint byteSize,
		[M68kRegister(M68kRegister.D1)] MemoryFlags attributes);

	[AmigaLvo(ExecLvo.AllocAbs)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR AllocAbs(
		[M68kRegister(M68kRegister.D0)] uint byteSize,
		[M68kRegister(M68kRegister.A1)] APTR location);

	[AmigaLvo(ExecLvo.FreeMem)]
	public static extern void FreeMem(
		[M68kRegister(M68kRegister.A1)] APTR memoryBlock,
		[M68kRegister(M68kRegister.D0)] uint byteSize);

	[AmigaLvo(ExecLvo.AvailMem)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AvailMem(
		[M68kRegister(M68kRegister.D1)] MemoryFlags attributes);

	[AmigaLvo(ExecLvo.AllocEntry)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR AllocEntry(
		[M68kRegister(M68kRegister.A0)] APTR memoryList);

	[AmigaLvo(ExecLvo.FreeEntry)]
	public static extern void FreeEntry(
		[M68kRegister(M68kRegister.A0)] APTR memoryList);

	[AmigaLvo(ExecLvo.Insert)]
	public static extern void Insert(
		[M68kRegister(M68kRegister.A0)] APTR list,
		[M68kRegister(M68kRegister.A1)] APTR node,
		[M68kRegister(M68kRegister.A2)] APTR listNode);

	[AmigaLvo(ExecLvo.AddHead)]
	public static extern void AddHead(
		[M68kRegister(M68kRegister.A0)] APTR list,
		[M68kRegister(M68kRegister.A1)] APTR node);

	[AmigaLvo(ExecLvo.AddTail)]
	public static extern void AddTail(
		[M68kRegister(M68kRegister.A0)] APTR list,
		[M68kRegister(M68kRegister.A1)] APTR node);

	[AmigaLvo(ExecLvo.Remove)]
	public static extern void Remove(
		[M68kRegister(M68kRegister.A1)] APTR node);

	[AmigaLvo(ExecLvo.RemHead)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR RemHead(
		[M68kRegister(M68kRegister.A0)] APTR list);

	[AmigaLvo(ExecLvo.RemTail)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR RemTail(
		[M68kRegister(M68kRegister.A0)] APTR list);

	[AmigaLvo(ExecLvo.Enqueue)]
	public static extern void Enqueue(
		[M68kRegister(M68kRegister.A0)] APTR list,
		[M68kRegister(M68kRegister.A1)] APTR node);

	[AmigaLvo(ExecLvo.FindName)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR FindName(
		[M68kRegister(M68kRegister.A0)] APTR list,
		[M68kRegister(M68kRegister.A1)] CString name);

	[AmigaLvo(ExecLvo.AddTask)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR AddTask(
		[M68kRegister(M68kRegister.A1)] APTR task,
		[M68kRegister(M68kRegister.A2)] APTR initialPc,
		[M68kRegister(M68kRegister.A3)] APTR finalPc);

	[AmigaLvo(ExecLvo.RemTask)]
	public static extern void RemTask(
		[M68kRegister(M68kRegister.A1)] APTR task);

	[AmigaLvo(ExecLvo.FindTask)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR FindTask(
		[M68kRegister(M68kRegister.A1)] CString name);

	[AmigaLvo(ExecLvo.SetTaskPri)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern sbyte SetTaskPri(
		[M68kRegister(M68kRegister.A1)] APTR task,
		[M68kRegister(M68kRegister.D0)] sbyte priority);

	[AmigaLvo(ExecLvo.SetSignal)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetSignal(
		[M68kRegister(M68kRegister.D0)] uint newSignals,
		[M68kRegister(M68kRegister.D1)] uint signalMask);

	[AmigaLvo(ExecLvo.SetExcept)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetExcept(
		[M68kRegister(M68kRegister.D0)] uint newSignals,
		[M68kRegister(M68kRegister.D1)] uint signalMask);

	[AmigaLvo(ExecLvo.Wait)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint Wait(
		[M68kRegister(M68kRegister.D0)] uint signalSet);

	[AmigaLvo(ExecLvo.Signal)]
	public static extern void Signal(
		[M68kRegister(M68kRegister.A1)] APTR task,
		[M68kRegister(M68kRegister.D0)] uint signalSet);

	[AmigaLvo(ExecLvo.AllocSignal)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern sbyte AllocSignal(
		[M68kRegister(M68kRegister.D0)] int signalNum);

	[AmigaLvo(ExecLvo.FreeSignal)]
	public static extern void FreeSignal(
		[M68kRegister(M68kRegister.D0)] int signalNum);

	[AmigaLvo(ExecLvo.AllocTrap)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern sbyte AllocTrap(
		[M68kRegister(M68kRegister.D0)] int trapNum);

	[AmigaLvo(ExecLvo.FreeTrap)]
	public static extern void FreeTrap(
		[M68kRegister(M68kRegister.D0)] int trapNum);

	[AmigaLvo(ExecLvo.AddPort)]
	public static extern void AddPort(
		[M68kRegister(M68kRegister.A1)] APTR port);

	[AmigaLvo(ExecLvo.RemPort)]
	public static extern void RemPort(
		[M68kRegister(M68kRegister.A1)] APTR port);

	[AmigaLvo(ExecLvo.PutMsg)]
	public static extern void PutMsg(
		[M68kRegister(M68kRegister.A0)] APTR port,
		[M68kRegister(M68kRegister.A1)] APTR message);

	[AmigaLvo(ExecLvo.GetMsg)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR GetMsg(
		[M68kRegister(M68kRegister.A0)] APTR port);

	[AmigaLvo(ExecLvo.ReplyMsg)]
	public static extern void ReplyMsg(
		[M68kRegister(M68kRegister.A1)] APTR message);

	[AmigaLvo(ExecLvo.WaitPort)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR WaitPort(
		[M68kRegister(M68kRegister.A0)] APTR port);

	[AmigaLvo(ExecLvo.FindPort)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR FindPort(
		[M68kRegister(M68kRegister.A1)] CString name);

	[AmigaLvo(ExecLvo.AddLibrary)]
	public static extern void AddLibrary(
		[M68kRegister(M68kRegister.A1)] APTR library);

	[AmigaLvo(ExecLvo.RemLibrary)]
	public static extern void RemLibrary(
		[M68kRegister(M68kRegister.A1)] APTR library);

	[AmigaLvo(ExecLvo.OldOpenLibrary)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR? OldOpenLibrary(
		[M68kRegister(M68kRegister.A1)] CString name);

	[AmigaLvo(ExecLvo.CloseLibrary)]
	public static extern void CloseLibrary(
		[M68kRegister(M68kRegister.A1)] APTR library);

	[AmigaLvo(ExecLvo.SetFunction)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR SetFunction(
		[M68kRegister(M68kRegister.A1)] APTR library,
		[M68kRegister(M68kRegister.A0)] int functionOffset,
		[M68kRegister(M68kRegister.D0)] APTR newFunction);

	[AmigaLvo(ExecLvo.SumLibrary)]
	public static extern void SumLibrary(
		[M68kRegister(M68kRegister.A1)] APTR library);

	[AmigaLvo(ExecLvo.AddDevice)]
	public static extern void AddDevice(
		[M68kRegister(M68kRegister.A1)] APTR device);

	[AmigaLvo(ExecLvo.RemDevice)]
	public static extern void RemDevice(
		[M68kRegister(M68kRegister.A1)] APTR device);

	[AmigaLvo(ExecLvo.OpenDevice)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern sbyte OpenDevice(
		[M68kRegister(M68kRegister.A0)] CString deviceName,
		[M68kRegister(M68kRegister.D0)] uint unit,
		[M68kRegister(M68kRegister.A1)] APTR ioRequest,
		[M68kRegister(M68kRegister.D1)] uint flags);

	[AmigaLvo(ExecLvo.CloseDevice)]
	public static extern void CloseDevice(
		[M68kRegister(M68kRegister.A1)] APTR ioRequest);

	[AmigaLvo(ExecLvo.DoIO)]
	public static extern void DoIO(
		[M68kRegister(M68kRegister.A1)] APTR ioRequest);

	[AmigaLvo(ExecLvo.SendIO)]
	public static extern void SendIO(
		[M68kRegister(M68kRegister.A1)] APTR ioRequest);

	[AmigaLvo(ExecLvo.CheckIO)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR CheckIO(
		[M68kRegister(M68kRegister.A1)] APTR ioRequest);

	[AmigaLvo(ExecLvo.WaitIO)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern sbyte WaitIO(
		[M68kRegister(M68kRegister.A1)] APTR ioRequest);

	[AmigaLvo(ExecLvo.AbortIO)]
	public static extern void AbortIO(
		[M68kRegister(M68kRegister.A1)] APTR ioRequest);

	[AmigaLvo(ExecLvo.AddResource)]
	public static extern void AddResource(
		[M68kRegister(M68kRegister.A1)] APTR resource);

	[AmigaLvo(ExecLvo.RemResource)]
	public static extern void RemResource(
		[M68kRegister(M68kRegister.A1)] APTR resource);

	[AmigaLvo(ExecLvo.OpenResource)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR OpenResource(
		[M68kRegister(M68kRegister.A1)] CString resourceName);

	[AmigaLvo(ExecLvo.RawDoFmt)]
	public static extern void RawDoFmt(
		[M68kRegister(M68kRegister.A0)] CString formatString,
		[M68kRegister(M68kRegister.A1)] APTR dataStream,
		[M68kRegister(M68kRegister.A2)] APTR putCharProc,
		[M68kRegister(M68kRegister.A3)] APTR putCharData);

	[AmigaLvo(ExecLvo.GetCC)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ushort GetCC();

	[AmigaLvo(ExecLvo.TypeOfMem)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint TypeOfMem(
		[M68kRegister(M68kRegister.A1)] APTR address);

	[AmigaLvo(ExecLvo.Procure)]
	public static extern void Procure(
		[M68kRegister(M68kRegister.A0)] APTR signalSemaphore,
		[M68kRegister(M68kRegister.A1)] APTR semaphoreMessage);

	[AmigaLvo(ExecLvo.Vacate)]
	public static extern void Vacate(
		[M68kRegister(M68kRegister.A0)] APTR signalSemaphore,
		[M68kRegister(M68kRegister.A1)] APTR semaphoreMessage);

	[AmigaLvo(ExecLvo.OpenLibrary)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR? OpenLibrary(
		[M68kRegister(M68kRegister.A1)] CString name,
		[M68kRegister(M68kRegister.D0)] uint minimumVersion);

	/// <summary>
	/// Opens a library and returns the native pointer value directly. This is the
	/// allocation-free resident form of <see cref="OpenLibrary(CString,uint)"/>;
	/// callers must test <see cref="APTR.IsNull"/> before use.
	/// </summary>
	[AmigaLvo(ExecLvo.OpenLibrary)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR OpenLibraryRaw(
		[M68kRegister(M68kRegister.A1)] CString name,
		[M68kRegister(M68kRegister.D0)] uint minimumVersion);

	[AmigaLvo(ExecLvo.InitSemaphore)]
	public static extern void InitSemaphore(
		[M68kRegister(M68kRegister.A0)] APTR signalSemaphore);

	[AmigaLvo(ExecLvo.ObtainSemaphore)]
	public static extern void ObtainSemaphore(
		[M68kRegister(M68kRegister.A0)] APTR signalSemaphore);

	[AmigaLvo(ExecLvo.ReleaseSemaphore)]
	public static extern void ReleaseSemaphore(
		[M68kRegister(M68kRegister.A0)] APTR signalSemaphore);

	[AmigaLvo(ExecLvo.AttemptSemaphore)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AttemptSemaphore(
		[M68kRegister(M68kRegister.A0)] APTR signalSemaphore);

	[AmigaLvo(ExecLvo.ObtainSemaphoreList)]
	public static extern void ObtainSemaphoreList(
		[M68kRegister(M68kRegister.A0)] APTR signalSemaphore);

	[AmigaLvo(ExecLvo.ReleaseSemaphoreList)]
	public static extern void ReleaseSemaphoreList(
		[M68kRegister(M68kRegister.A0)] APTR signalSemaphore);

	[AmigaLvo(ExecLvo.FindSemaphore)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR FindSemaphore(
		[M68kRegister(M68kRegister.A1)] CString name);

	[AmigaLvo(ExecLvo.AddSemaphore)]
	public static extern void AddSemaphore(
		[M68kRegister(M68kRegister.A1)] APTR signalSemaphore);

	[AmigaLvo(ExecLvo.RemSemaphore)]
	public static extern void RemSemaphore(
		[M68kRegister(M68kRegister.A1)] APTR signalSemaphore);

	[AmigaLvo(ExecLvo.SumKickData)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SumKickData();

	[AmigaLvo(ExecLvo.AddMemList)]
	public static extern void AddMemList(
		[M68kRegister(M68kRegister.D0)] uint size,
		[M68kRegister(M68kRegister.D1)] uint attributes,
		[M68kRegister(M68kRegister.D2)] int priority,
		[M68kRegister(M68kRegister.A0)] APTR baseAddress,
		[M68kRegister(M68kRegister.A1)] CString name);

	[AmigaLvo(ExecLvo.CopyMem)]
	public static extern void CopyMem(
		[M68kRegister(M68kRegister.A0)] APTR source,
		[M68kRegister(M68kRegister.A1)] APTR destination,
		[M68kRegister(M68kRegister.D0)] uint size);

	[AmigaLvo(ExecLvo.CopyMemQuick)]
	public static extern void CopyMemQuick(
		[M68kRegister(M68kRegister.A0)] APTR source,
		[M68kRegister(M68kRegister.A1)] APTR destination,
		[M68kRegister(M68kRegister.D0)] uint size);

	[AmigaLvo(ExecLvo.CacheClearU)]
	public static extern void CacheClearU();

	[AmigaLvo(ExecLvo.CacheClearE)]
	public static extern void CacheClearE(
		[M68kRegister(M68kRegister.A0)] APTR address,
		[M68kRegister(M68kRegister.D0)] uint length,
		[M68kRegister(M68kRegister.D1)] uint caches);

	[AmigaLvo(ExecLvo.CacheControl)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CacheControl(
		[M68kRegister(M68kRegister.D0)] uint cacheBits,
		[M68kRegister(M68kRegister.D1)] uint cacheMask);

	[AmigaLvo(ExecLvo.CreateIORequest)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR CreateIORequest(
		[M68kRegister(M68kRegister.A0)] APTR port,
		[M68kRegister(M68kRegister.D0)] uint size);

	[AmigaLvo(ExecLvo.DeleteIORequest)]
	public static extern void DeleteIORequest(
		[M68kRegister(M68kRegister.A0)] APTR ioRequest);

	[AmigaLvo(ExecLvo.CreateMsgPort)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR CreateMsgPort();

	[AmigaLvo(ExecLvo.DeleteMsgPort)]
	public static extern void DeleteMsgPort(
		[M68kRegister(M68kRegister.A0)] APTR port);

	[AmigaLvo(ExecLvo.ObtainSemaphoreShared)]
	public static extern void ObtainSemaphoreShared(
		[M68kRegister(M68kRegister.A0)] APTR signalSemaphore);

	[AmigaLvo(ExecLvo.AllocVec)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR AllocVec(
		[M68kRegister(M68kRegister.D0)] uint byteSize,
		[M68kRegister(M68kRegister.D1)] uint attributes);

	[AmigaLvo(ExecLvo.FreeVec)]
	public static extern void FreeVec(
		[M68kRegister(M68kRegister.A1)] APTR memoryBlock);

	[AmigaLvo(ExecLvo.CreatePool)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR CreatePool(
		[M68kRegister(M68kRegister.D0)] uint requirements,
		[M68kRegister(M68kRegister.D1)] uint puddleSize,
		[M68kRegister(M68kRegister.D2)] uint threshold);

	[AmigaLvo(ExecLvo.DeletePool)]
	public static extern void DeletePool(
		[M68kRegister(M68kRegister.A0)] APTR poolHeader);

	[AmigaLvo(ExecLvo.AllocPooled)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR AllocPooled(
		[M68kRegister(M68kRegister.A0)] APTR poolHeader,
		[M68kRegister(M68kRegister.D0)] uint memorySize);

	[AmigaLvo(ExecLvo.FreePooled)]
	public static extern void FreePooled(
		[M68kRegister(M68kRegister.A0)] APTR poolHeader,
		[M68kRegister(M68kRegister.A1)] APTR memory,
		[M68kRegister(M68kRegister.D0)] uint memorySize);

	[AmigaLvo(ExecLvo.AttemptSemaphoreShared)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AttemptSemaphoreShared(
		[M68kRegister(M68kRegister.A0)] APTR signalSemaphore);

	[AmigaLvo(ExecLvo.ColdReboot)]
	public static extern void ColdReboot();

	[AmigaLvo(ExecLvo.StackSwap)]
	public static extern void StackSwap(
		[M68kRegister(M68kRegister.A0)] APTR newStack);

	[AmigaLvo(ExecLvo.CachePreDMA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CachePreDMA(
		[M68kRegister(M68kRegister.A0)] APTR address,
		[M68kRegister(M68kRegister.A1)] uint length,
		[M68kRegister(M68kRegister.D0)] uint flags);

	[AmigaLvo(ExecLvo.CachePostDMA)]
	public static extern void CachePostDMA(
		[M68kRegister(M68kRegister.A0)] APTR address,
		[M68kRegister(M68kRegister.A1)] uint length,
		[M68kRegister(M68kRegister.D0)] uint flags);

	[AmigaLvo(ExecLvo.AddMemHandler)]
	public static extern void AddMemHandler(
		[M68kRegister(M68kRegister.A1)] APTR memHandler);

	[AmigaLvo(ExecLvo.RemMemHandler)]
	public static extern void RemMemHandler(
		[M68kRegister(M68kRegister.A1)] APTR memHandler);

	[AmigaLvo(ExecLvo.ObtainQuickVector)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ObtainQuickVector(
		[M68kRegister(M68kRegister.A0)] uint interruptCode);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.RawIOInit)]
	public static extern void RawIOInit();

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.RawMayGetChar)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern byte RawMayGetChar();

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.RawPutChar)]
	public static extern void RawPutChar(
		[M68kRegister(M68kRegister.D0)] byte character);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.NewGetTaskAttrsA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewGetTaskAttrsA(
		[M68kRegister(M68kRegister.A0)] APTR task,
		[M68kRegister(M68kRegister.A1), M68kWritesBuffer] APTR data,
		[M68kRegister(M68kRegister.D0)] uint dataSize,
		[M68kRegister(M68kRegister.D1)] uint type,
		[M68kRegister(M68kRegister.A2)] APTR tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.NewSetTaskAttrsA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewSetTaskAttrsA(
		[M68kRegister(M68kRegister.A0)] APTR task,
		[M68kRegister(M68kRegister.A1)] APTR data,
		[M68kRegister(M68kRegister.D0)] uint dataSize,
		[M68kRegister(M68kRegister.D1)] uint type,
		[M68kRegister(M68kRegister.A2)] APTR tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.NewSetFunction)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewSetFunction(
		[M68kRegister(M68kRegister.A0)] APTR library,
		[M68kRegister(M68kRegister.A1)] APTR function,
		[M68kRegister(M68kRegister.D0)] int offset,
		[M68kRegister(M68kRegister.A2)] APTR tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.NewCreateLibrary)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR NewCreateLibrary(
		[M68kRegister(M68kRegister.A0)] APTR tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.NewPPCStackSwap)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewPPCStackSwap(
		[M68kRegister(M68kRegister.A0)] APTR newStack,
		[M68kRegister(M68kRegister.A1)] APTR function,
		[M68kRegister(M68kRegister.A2)] APTR args);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.TaggedOpenLibrary)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR? TaggedOpenLibrary(
		[M68kRegister(M68kRegister.D0)] int tag);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.ReadGayle)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ReadGayle();

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.CacheFlushDataArea)]
	public static extern void CacheFlushDataArea(
		[M68kRegister(M68kRegister.A0)] APTR address,
		[M68kRegister(M68kRegister.D0)] uint length);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.CacheInvalidInstArea)]
	public static extern void CacheInvalidInstArea(
		[M68kRegister(M68kRegister.A0)] APTR address,
		[M68kRegister(M68kRegister.D0)] uint length);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.CacheInvalidDataArea)]
	public static extern void CacheInvalidDataArea(
		[M68kRegister(M68kRegister.A0)] APTR address,
		[M68kRegister(M68kRegister.D0)] uint length);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.CacheFlushDataInstArea)]
	public static extern void CacheFlushDataInstArea(
		[M68kRegister(M68kRegister.A0)] APTR address,
		[M68kRegister(M68kRegister.D0)] uint length);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.CacheTrashCacheArea)]
	public static extern void CacheTrashCacheArea(
		[M68kRegister(M68kRegister.A0)] APTR address,
		[M68kRegister(M68kRegister.D0)] uint length);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.AllocTaskPooled)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR AllocTaskPooled(
		[M68kRegister(M68kRegister.D0)] uint byteSize);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.FreeTaskPooled)]
	public static extern void FreeTaskPooled(
		[M68kRegister(M68kRegister.A1)] APTR memory,
		[M68kRegister(M68kRegister.D0)] uint byteSize);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.AllocVecTaskPooled)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR AllocVecTaskPooled(
		[M68kRegister(M68kRegister.D0)] uint byteSize);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.FreeVecTaskPooled)]
	public static extern void FreeVecTaskPooled(
		[M68kRegister(M68kRegister.A1)] APTR memory);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.FlushPool)]
	public static extern void FlushPool(
		[M68kRegister(M68kRegister.A0)] APTR poolHeader);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.FlushTaskPool)]
	public static extern void FlushTaskPool();

	// MorphOS m68k ABI call. CopperOS call.
	[AmigaLvo(ExecLvo.AllocVecPooled)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR AllocVecPooled(
		[M68kRegister(M68kRegister.A0)] APTR poolHeader,
		[M68kRegister(M68kRegister.D0)] uint byteSize);

	// MorphOS m68k ABI call. CopperOS call.
	[AmigaLvo(ExecLvo.FreeVecPooled)]
	public static extern void FreeVecPooled(
		[M68kRegister(M68kRegister.A0)] APTR poolHeader,
		[M68kRegister(M68kRegister.A1)] APTR memory);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.NewGetSystemAttrsA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewGetSystemAttrsA(
		[M68kRegister(M68kRegister.A0)] APTR data,
		[M68kRegister(M68kRegister.D0)] uint dataSize,
		[M68kRegister(M68kRegister.D1)] uint type,
		[M68kRegister(M68kRegister.A1)] APTR tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.NewSetSystemAttrsA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewSetSystemAttrsA(
		[M68kRegister(M68kRegister.A0)] APTR data,
		[M68kRegister(M68kRegister.D0)] uint dataSize,
		[M68kRegister(M68kRegister.D1)] uint type,
		[M68kRegister(M68kRegister.A1)] APTR tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.NewCreateTaskA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR NewCreateTaskA(
		[M68kRegister(M68kRegister.A0)] APTR tags);

	// CopperOS call. MorphOS exposes this slot through native function-pointer ABI, not m68k ABI.
	[AmigaLvo(ExecLvo.AllocateAligned)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR AllocateAligned(
		[M68kRegister(M68kRegister.A0)] APTR freeList,
		[M68kRegister(M68kRegister.D0)] uint byteSize,
		[M68kRegister(M68kRegister.D1)] uint alignment,
		[M68kRegister(M68kRegister.D2)] uint alignmentOffset);

	// CopperOS call. MorphOS exposes this slot through native function-pointer ABI, not m68k ABI.
	[AmigaLvo(ExecLvo.AllocMemAligned)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR AllocMemAligned(
		[M68kRegister(M68kRegister.D0)] uint byteSize,
		[M68kRegister(M68kRegister.D1)] uint attributes,
		[M68kRegister(M68kRegister.D2)] uint alignment,
		[M68kRegister(M68kRegister.D3)] uint alignmentOffset);

	// CopperOS call. MorphOS exposes this slot through native function-pointer ABI, not m68k ABI.
	[AmigaLvo(ExecLvo.AllocVecAligned)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR AllocVecAligned(
		[M68kRegister(M68kRegister.D0)] uint byteSize,
		[M68kRegister(M68kRegister.D1)] uint attributes,
		[M68kRegister(M68kRegister.D2)] uint alignment,
		[M68kRegister(M68kRegister.D3)] uint alignmentOffset);

	// MorphOS m68k ABI call. CopperOS call.
	[AmigaLvo(ExecLvo.FindExecNode)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR FindExecNode(
		[M68kRegister(M68kRegister.D0)] uint type,
		[M68kRegister(M68kRegister.A0)] CString name);

	// MorphOS m68k ABI call. CopperOS call.
	[AmigaLvo(ExecLvo.AddExecNodeA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AddExecNodeA(
		[M68kRegister(M68kRegister.A0)] APTR node,
		[M68kRegister(M68kRegister.A1)] APTR tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.AllocVecDMA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR AllocVecDMA(
		[M68kRegister(M68kRegister.D0)] uint byteSize,
		[M68kRegister(M68kRegister.D1)] uint attributes);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.FreeVecDMA)]
	public static extern void FreeVecDMA(
		[M68kRegister(M68kRegister.A1)] APTR memory);

	// CopperOS call. MorphOS exposes this slot through native function-pointer ABI, not m68k ABI.
	[AmigaLvo(ExecLvo.AllocPooledAligned)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR AllocPooledAligned(
		[M68kRegister(M68kRegister.A0)] APTR poolHeader,
		[M68kRegister(M68kRegister.D0)] uint byteSize,
		[M68kRegister(M68kRegister.D1)] uint alignment,
		[M68kRegister(M68kRegister.D2)] uint alignmentOffset);

	// CopperOS call. MorphOS exposes this slot through native function-pointer ABI, not m68k ABI.
	[AmigaLvo(ExecLvo.AddResident)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AddResident(
		[M68kRegister(M68kRegister.A1)] APTR resident);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.DumpTaskState)]
	public static extern void DumpTaskState(
		[M68kRegister(M68kRegister.A0)] APTR task);

	// CopperOS call. MorphOS exposes this slot through native function-pointer ABI, not m68k ABI.
	[AmigaLvo(ExecLvo.AvailPool)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AvailPool(
		[M68kRegister(M68kRegister.A0)] APTR poolHeader,
		[M68kRegister(M68kRegister.D0)] uint flags);

	// CopperOS call. MorphOS exposes this slot through native function-pointer ABI, not m68k ABI.
	[AmigaLvo(ExecLvo.PutMsgHead)]
	public static extern void PutMsgHead(
		[M68kRegister(M68kRegister.A0)] APTR port,
		[M68kRegister(M68kRegister.A1)] APTR message);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.NewGetTaskPIDAttrsA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewGetTaskPIDAttrsA(
		[M68kRegister(M68kRegister.D0)] uint pid,
		[M68kRegister(M68kRegister.A0)] APTR data,
		[M68kRegister(M68kRegister.D1)] uint dataSize,
		[M68kRegister(M68kRegister.D2)] uint type,
		[M68kRegister(M68kRegister.A1)] APTR tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(ExecLvo.NewSetTaskPIDAttrsA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewSetTaskPIDAttrsA(
		[M68kRegister(M68kRegister.D0)] uint pid,
		[M68kRegister(M68kRegister.A0)] APTR data,
		[M68kRegister(M68kRegister.D1)] uint dataSize,
		[M68kRegister(M68kRegister.D2)] uint type,
		[M68kRegister(M68kRegister.A1)] APTR tags);
}
