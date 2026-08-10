/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>
/// Public exec.library vector offsets used by callers, ROM builders, and
/// host-native compatibility gateways.
/// </summary>
public static class ExecLvo
{
	// Genuine private Exec scheduler vectors used by native switch paths.
	public const short Schedule = -42;
	public const short Switch = -54;

	// Classic Exec public vectors.
	public const short Supervisor = -30;
	public const short InitCode = -72;
	public const short InitStruct = -78;
	public const short MakeLibrary = -84;
	public const short MakeFunctions = -90;
	public const short FindResident = -96;
	public const short InitResident = -102;
	public const short Alert = -108;
	public const short Debug = -114;
	public const short Disable = -120;
	public const short Enable = -126;
	public const short Forbid = -132;
	public const short Permit = -138;
	public const short SetSR = -144;
	public const short SuperState = -150;
	public const short UserState = -156;
	public const short SetIntVector = -162;
	public const short AddIntServer = -168;
	public const short RemIntServer = -174;
	public const short Cause = -180;
	public const short Allocate = -186;
	public const short Deallocate = -192;
	public const short AllocMem = -198;
	public const short AllocAbs = -204;
	public const short FreeMem = -210;
	public const short AvailMem = -216;
	public const short AllocEntry = -222;
	public const short FreeEntry = -228;
	public const short Insert = -234;
	public const short AddHead = -240;
	public const short AddTail = -246;
	public const short Remove = -252;
	public const short RemHead = -258;
	public const short RemTail = -264;
	public const short Enqueue = -270;
	public const short FindName = -276;
	public const short AddTask = -282;
	public const short RemTask = -288;
	public const short FindTask = -294;
	public const short SetTaskPri = -300;
	public const short SetSignal = -306;
	public const short SetExcept = -312;
	public const short Wait = -318;
	public const short Signal = -324;
	public const short AllocSignal = -330;
	public const short FreeSignal = -336;
	public const short AllocTrap = -342;
	public const short FreeTrap = -348;
	public const short AddPort = -354;
	public const short RemPort = -360;
	public const short PutMsg = -366;
	public const short GetMsg = -372;
	public const short ReplyMsg = -378;
	public const short WaitPort = -384;
	public const short FindPort = -390;
	public const short AddLibrary = -396;
	public const short RemLibrary = -402;
	public const short OldOpenLibrary = -408;
	public const short CloseLibrary = -414;
	public const short SetFunction = -420;
	public const short SumLibrary = -426;
	public const short AddDevice = -432;
	public const short RemDevice = -438;
	public const short OpenDevice = -444;
	public const short CloseDevice = -450;
	public const short DoIO = -456;
	public const short SendIO = -462;
	public const short CheckIO = -468;
	public const short WaitIO = -474;
	public const short AbortIO = -480;
	public const short AddResource = -486;
	public const short RemResource = -492;
	public const short OpenResource = -498;
	public const short RawDoFmt = -522;
	public const short GetCC = -528;
	public const short TypeOfMem = -534;
	public const short Procure = -540;
	public const short Vacate = -546;
	public const short OpenLibrary = -552;
	public const short InitSemaphore = -558;
	public const short ObtainSemaphore = -564;
	public const short ReleaseSemaphore = -570;
	public const short AttemptSemaphore = -576;
	public const short ObtainSemaphoreList = -582;
	public const short ReleaseSemaphoreList = -588;
	public const short FindSemaphore = -594;
	public const short AddSemaphore = -600;
	public const short RemSemaphore = -606;
	public const short SumKickData = -612;
	public const short AddMemList = -618;
	public const short CopyMem = -624;
	public const short CopyMemQuick = -630;
	public const short CacheClearU = -636;
	public const short CacheClearE = -642;
	public const short CacheControl = -648;
	public const short CreateIORequest = -654;
	public const short DeleteIORequest = -660;
	public const short CreateMsgPort = -666;
	public const short DeleteMsgPort = -672;
	public const short ObtainSemaphoreShared = -678;
	public const short AllocVec = -684;
	public const short FreeVec = -690;
	public const short CreatePool = -696;
	public const short DeletePool = -702;
	public const short AllocPooled = -708;
	public const short FreePooled = -714;
	public const short AttemptSemaphoreShared = -720;
	public const short ColdReboot = -726;
	public const short StackSwap = -732;
	public const short ChildFree = -738;
	public const short ChildOrphan = -744;
	public const short ChildStatus = -750;
	public const short ChildWait = -756;
	public const short CachePreDMA = -762;
	public const short CachePostDMA = -768;
	public const short AddMemHandler = -774;
	public const short RemMemHandler = -780;
	public const short ObtainQuickVector = -786;

	// MorphOS m68k ABI extensions.
	public const short RawIOInit = -504;
	public const short RawMayGetChar = -510;
	public const short RawPutChar = -516;
	public const short NewGetTaskAttrsA = -738;
	public const short NewSetTaskAttrsA = -744;
	public const short NewSetFunction = -792;
	public const short NewCreateLibrary = -798;
	public const short NewPPCStackSwap = -804;
	public const short TaggedOpenLibrary = -810;
	public const short ReadGayle = -816;
	public const short CacheFlushDataArea = -828;
	public const short CacheInvalidInstArea = -834;
	public const short CacheInvalidDataArea = -840;
	public const short CacheFlushDataInstArea = -846;
	public const short CacheTrashCacheArea = -852;
	public const short AllocTaskPooled = -858;
	public const short FreeTaskPooled = -864;
	public const short AllocVecTaskPooled = -870;
	public const short FreeVecTaskPooled = -876;
	public const short FlushPool = -882;
	public const short FlushTaskPool = -888;

	// MorphOS m68k calls also exposed by CopperOS.
	public const short AllocVecPooled = -894;
	public const short FreeVecPooled = -900;

	// MorphOS m68k ABI extensions.
	public const short NewGetSystemAttrsA = -906;
	public const short NewSetSystemAttrsA = -912;
	public const short NewCreateTaskA = -918;

	// CopperOS 68k compatibility extensions.
	public const short AllocateAligned = -930;
	public const short AllocMemAligned = -936;
	public const short AllocVecAligned = -942;

	// MorphOS m68k calls also exposed by CopperOS.
	public const short FindExecNode = -960;
	public const short AddExecNodeA = -966;

	// MorphOS m68k ABI extensions.
	public const short AllocVecDMA = -972;
	public const short FreeVecDMA = -978;

	// CopperOS 68k compatibility extensions.
	public const short AllocPooledAligned = -984;
	public const short AddResident = -990;

	// MorphOS m68k ABI extensions.
	public const short DumpTaskState = -1026;

	// CopperOS 68k compatibility extensions.
	public const short AvailPool = -1050;
	public const short PutMsgHead = -1062;

	// MorphOS m68k ABI extensions.
	public const short NewGetTaskPIDAttrsA = -1068;
	public const short NewSetTaskPIDAttrsA = -1074;

	// Source-compatible aliases retained for existing consumers.
	public const short AllocVecDma = AllocVecDMA;
	public const short FreeVecDma = FreeVecDMA;
}
