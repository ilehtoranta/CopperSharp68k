/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>ABI profiles which publish an exec.library vector slot.</summary>
[Flags]
public enum ExecAbiProfileMask : byte
{
	None = 0,
	Classic = 1 << 0,
	MorphOsM68k = 1 << 1,
	CopperOs = 1 << 2,
	Unified = Classic | MorphOsM68k | CopperOs,
}

/// <summary>Whether a vector is part of the public ABI or Exec's scheduler ABI.</summary>
public enum ExecVectorVisibility : byte
{
	Public,
	PrivateScheduler,
}

/// <summary>
/// SDK-owned profile metadata for every canonical Exec LVO. The installed
/// CopperStart surface is unified, but keeping the source profile explicit
/// prevents a Classic, MorphOS m68k, or CopperOS compatibility slot from being
/// silently reclassified by a build manifest.
/// </summary>
public static class ExecVectorContractMetadata
{
	public static bool TryDescribe(short lvo, out ExecAbiProfileMask profiles,
		out ExecVectorVisibility visibility)
	{
		visibility = ExecVectorVisibility.Public;
		switch (lvo)
		{
			case ExecLvo.Schedule:
			case ExecLvo.Switch:
				profiles = ExecAbiProfileMask.Unified;
				visibility = ExecVectorVisibility.PrivateScheduler;
				return true;

			case ExecLvo.Supervisor:
			case ExecLvo.InitCode:
			case ExecLvo.InitStruct:
			case ExecLvo.MakeLibrary:
			case ExecLvo.MakeFunctions:
			case ExecLvo.FindResident:
			case ExecLvo.InitResident:
			case ExecLvo.Alert:
			case ExecLvo.Debug:
			case ExecLvo.Disable:
			case ExecLvo.Enable:
			case ExecLvo.Forbid:
			case ExecLvo.Permit:
			case ExecLvo.SetSR:
			case ExecLvo.SuperState:
			case ExecLvo.UserState:
			case ExecLvo.SetIntVector:
			case ExecLvo.AddIntServer:
			case ExecLvo.RemIntServer:
			case ExecLvo.Cause:
			case ExecLvo.Allocate:
			case ExecLvo.Deallocate:
			case ExecLvo.AllocMem:
			case ExecLvo.AllocAbs:
			case ExecLvo.FreeMem:
			case ExecLvo.AvailMem:
			case ExecLvo.AllocEntry:
			case ExecLvo.FreeEntry:
			case ExecLvo.Insert:
			case ExecLvo.AddHead:
			case ExecLvo.AddTail:
			case ExecLvo.Remove:
			case ExecLvo.RemHead:
			case ExecLvo.RemTail:
			case ExecLvo.Enqueue:
			case ExecLvo.FindName:
			case ExecLvo.AddTask:
			case ExecLvo.RemTask:
			case ExecLvo.FindTask:
			case ExecLvo.SetTaskPri:
			case ExecLvo.SetSignal:
			case ExecLvo.SetExcept:
			case ExecLvo.Wait:
			case ExecLvo.Signal:
			case ExecLvo.AllocSignal:
			case ExecLvo.FreeSignal:
			case ExecLvo.AllocTrap:
			case ExecLvo.FreeTrap:
			case ExecLvo.AddPort:
			case ExecLvo.RemPort:
			case ExecLvo.PutMsg:
			case ExecLvo.GetMsg:
			case ExecLvo.ReplyMsg:
			case ExecLvo.WaitPort:
			case ExecLvo.FindPort:
			case ExecLvo.AddLibrary:
			case ExecLvo.RemLibrary:
			case ExecLvo.OldOpenLibrary:
			case ExecLvo.CloseLibrary:
			case ExecLvo.SetFunction:
			case ExecLvo.SumLibrary:
			case ExecLvo.AddDevice:
			case ExecLvo.RemDevice:
			case ExecLvo.OpenDevice:
			case ExecLvo.CloseDevice:
			case ExecLvo.DoIO:
			case ExecLvo.SendIO:
			case ExecLvo.CheckIO:
			case ExecLvo.WaitIO:
			case ExecLvo.AbortIO:
			case ExecLvo.AddResource:
			case ExecLvo.RemResource:
			case ExecLvo.OpenResource:
			case ExecLvo.RawDoFmt:
			case ExecLvo.GetCC:
			case ExecLvo.TypeOfMem:
			case ExecLvo.Procure:
			case ExecLvo.Vacate:
			case ExecLvo.OpenLibrary:
			case ExecLvo.InitSemaphore:
			case ExecLvo.ObtainSemaphore:
			case ExecLvo.ReleaseSemaphore:
			case ExecLvo.AttemptSemaphore:
			case ExecLvo.ObtainSemaphoreList:
			case ExecLvo.ReleaseSemaphoreList:
			case ExecLvo.FindSemaphore:
			case ExecLvo.AddSemaphore:
			case ExecLvo.RemSemaphore:
			case ExecLvo.SumKickData:
			case ExecLvo.AddMemList:
			case ExecLvo.CopyMem:
			case ExecLvo.CopyMemQuick:
			case ExecLvo.CacheClearU:
			case ExecLvo.CacheClearE:
			case ExecLvo.CacheControl:
			case ExecLvo.CreateIORequest:
			case ExecLvo.DeleteIORequest:
			case ExecLvo.CreateMsgPort:
			case ExecLvo.DeleteMsgPort:
			case ExecLvo.ObtainSemaphoreShared:
			case ExecLvo.AllocVec:
			case ExecLvo.FreeVec:
			case ExecLvo.CreatePool:
			case ExecLvo.DeletePool:
			case ExecLvo.AllocPooled:
			case ExecLvo.FreePooled:
			case ExecLvo.AttemptSemaphoreShared:
			case ExecLvo.ColdReboot:
			case ExecLvo.StackSwap:
			case ExecLvo.CachePreDMA:
			case ExecLvo.CachePostDMA:
			case ExecLvo.AddMemHandler:
			case ExecLvo.RemMemHandler:
			case ExecLvo.ObtainQuickVector:
				profiles = ExecAbiProfileMask.Unified;
				return true;

			case ExecLvo.RawIOInit:
			case ExecLvo.RawMayGetChar:
			case ExecLvo.RawPutChar:
			case ExecLvo.NewGetTaskAttrsA:
			case ExecLvo.NewSetTaskAttrsA:
			case ExecLvo.NewSetFunction:
			case ExecLvo.NewCreateLibrary:
			case ExecLvo.NewPPCStackSwap:
			case ExecLvo.TaggedOpenLibrary:
			case ExecLvo.ReadGayle:
			case ExecLvo.CacheFlushDataArea:
			case ExecLvo.CacheInvalidInstArea:
			case ExecLvo.CacheInvalidDataArea:
			case ExecLvo.CacheFlushDataInstArea:
			case ExecLvo.CacheTrashCacheArea:
			case ExecLvo.AllocTaskPooled:
			case ExecLvo.FreeTaskPooled:
			case ExecLvo.AllocVecTaskPooled:
			case ExecLvo.FreeVecTaskPooled:
			case ExecLvo.FlushPool:
			case ExecLvo.FlushTaskPool:
			case ExecLvo.NewGetSystemAttrsA:
			case ExecLvo.NewSetSystemAttrsA:
			case ExecLvo.NewCreateTaskA:
			case ExecLvo.AllocVecDMA:
			case ExecLvo.FreeVecDMA:
			case ExecLvo.DumpTaskState:
			case ExecLvo.NewGetTaskPIDAttrsA:
			case ExecLvo.NewSetTaskPIDAttrsA:
				profiles = ExecAbiProfileMask.MorphOsM68k;
				return true;

			case ExecLvo.AllocVecPooled:
			case ExecLvo.FreeVecPooled:
			case ExecLvo.FindExecNode:
			case ExecLvo.AddExecNodeA:
				profiles = ExecAbiProfileMask.MorphOsM68k |
					ExecAbiProfileMask.CopperOs;
				return true;

			case ExecLvo.AllocateAligned:
			case ExecLvo.AllocMemAligned:
			case ExecLvo.AllocVecAligned:
			case ExecLvo.AllocPooledAligned:
			case ExecLvo.AddResident:
			case ExecLvo.AvailPool:
			case ExecLvo.PutMsgHead:
				profiles = ExecAbiProfileMask.CopperOs;
				return true;

			default:
				profiles = ExecAbiProfileMask.None;
				return false;
		}
	}
}
