/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>
/// Public dos.library vector offsets for the AmigaOS 3.1 V40 and MorphOS M68k ABIs.
/// Multiple names may intentionally identify one vector when the ABI publishes aliases.
/// </summary>
public static class DosLvo
{
	// AmigaOS 3.1 V40 public vectors.
	public const short Open = -30, Close = -36, Read = -42, Write = -48, Input = -54, Output = -60;
	public const short Seek = -66, DeleteFile = -72, Rename = -78, Lock = -84, UnLock = -90, DupLock = -96;
	public const short Examine = -102, ExNext = -108, Info = -114, CreateDir = -120, CurrentDir = -126, IoErr = -132;
	public const short CreateProc = -138, Exit = -144, LoadSeg = -150, UnLoadSeg = -156, DeviceProc = -174;
	public const short SetComment = -180, SetProtection = -186, DateStamp = -192, Delay = -198, WaitForChar = -204;
	public const short ParentDir = -210, IsInteractive = -216, Execute = -222, AllocDosObject = -228;
	public const short FreeDosObject = -234, DoPkt = -240, SendPkt = -246, WaitPkt = -252, ReplyPkt = -258;
	public const short AbortPkt = -264, LockRecord = -270, LockRecords = -276, UnLockRecord = -282;
	public const short UnLockRecords = -288, SelectInput = -294, SelectOutput = -300, FGetC = -306, FPutC = -312;
	public const short UnGetC = -318, FRead = -324, FWrite = -330, FGets = -336, FPuts = -342, VFWritef = -348;
	public const short VFPrintf = -354, Flush = -360, SetVBuf = -366, DupLockFromFH = -372, OpenFromLock = -378;
	public const short ParentOfFH = -384, ExamineFH = -390, SetFileDate = -396, NameFromLock = -402;
	public const short NameFromFH = -408, SplitName = -414, SameLock = -420, SetMode = -426, ExAll = -432;
	public const short ReadLink = -438, MakeLink = -444, ChangeMode = -450, SetFileSize = -456, SetIoErr = -462;
	public const short Fault = -468, PrintFault = -474, ErrorReport = -480, Cli = -492, CreateNewProc = -498;
	public const short RunCommand = -504, GetConsoleTask = -510, SetConsoleTask = -516, GetFileSysTask = -522;
	public const short SetFileSysTask = -528, GetArgStr = -534, SetArgStr = -540, FindCliProc = -546, MaxCli = -552;
	public const short SetCurrentDirName = -558, GetCurrentDirName = -564, SetProgramName = -570;
	public const short GetProgramName = -576, SetPrompt = -582, GetPrompt = -588, SetProgramDir = -594;
	public const short GetProgramDir = -600, SystemTagList = -606, AssignLock = -612, AssignLate = -618;
	public const short AssignPath = -624, AssignAdd = -630, RemAssignList = -636, GetDeviceProc = -642;
	public const short FreeDeviceProc = -648, LockDosList = -654, UnLockDosList = -660, AttemptLockDosList = -666;
	public const short RemDosEntry = -672, AddDosEntry = -678, FindDosEntry = -684, NextDosEntry = -690;
	public const short MakeDosEntry = -696, FreeDosEntry = -702, IsFileSystem = -708, Format = -714, Relabel = -720;
	public const short Inhibit = -726, AddBuffers = -732, CompareDates = -738, DateToStr = -744, StrToDate = -750;
	public const short InternalLoadSeg = -756, InternalUnLoadSeg = -762, NewLoadSeg = -768, AddSegment = -774;
	public const short FindSegment = -780, RemSegment = -786, CheckSignal = -792, ReadArgs = -798, FindArg = -804;
	public const short ReadItem = -810, StrToLong = -816, MatchFirst = -822, MatchNext = -828, MatchEnd = -834;
	public const short ParsePattern = -840, MatchPattern = -846, FreeArgs = -858, FilePart = -870, PathPart = -876;
	public const short AddPart = -882, StartNotify = -888, EndNotify = -894, SetVar = -900, GetVar = -906;
	public const short DeleteVar = -912, FindVar = -918, CliInitNewcli = -930, CliInitRun = -936;
	public const short WriteChars = -942, PutStr = -948, VPrintf = -954, Printf = VPrintf;
	public const short ParsePatternNoCase = -966, MatchPatternNoCase = -972, SameDevice = -984;
	public const short ExAllEnd = -990, SetOwner = -996;

	// MorphOS M68k aliases for existing vectors.
	public const short AllocDosObjectTagList = AllocDosObject;
	public const short DoPkt0 = DoPkt, DoPkt1 = DoPkt, DoPkt2 = DoPkt, DoPkt3 = DoPkt, DoPkt4 = DoPkt;
	public const short CreateNewProcTagList = CreateNewProc, System = SystemTagList, NewLoadSegTagList = NewLoadSeg;

	// MorphOS M68k V50/V51 extensions. Reserved vector gaps are intentionally absent.
	public const short AddSegmentTagList = -1002, FindSegmentTagList = -1008;
	public const short Seek64 = -1066, SetFileSize64 = -1072, LockRecord64 = -1078, LockRecords64 = -1084;
	public const short UnLockRecord64 = -1090, UnLockRecords64 = -1096, NewReadLink = -1114, GetFileSysAttr = -1120;
	public const short GetSegListAttr = -1126, SetDosObjectAttr = -1132, SetDosObjectAttrTagList = SetDosObjectAttr;
	public const short GetDosObjectAttr = -1138, GetDosObjectAttrTagList = GetDosObjectAttr;
	public const short Examine64 = -1144, Examine64TagList = Examine64, ExNext64 = -1150, ExNext64TagList = ExNext64;
	public const short ExamineFH64 = -1156, ExamineFH64TagList = ExamineFH64, ReleaseCLINumber = -1162;
	public const short QueryCLIDataTagList = -1168, QueryCLIData = QueryCLIDataTagList, FreeCLIData = -1174;
	public const short GetSegListAttrTagList = -1180, SetFilePosixDate = -1186;
	public const short SetFilePosixDateTagList = SetFilePosixDate, PosixDateStamp = -1192;
	public const short PosixDateStampToDateStamp = -1198, DateStampToPosixDateStamp = -1204;
}
