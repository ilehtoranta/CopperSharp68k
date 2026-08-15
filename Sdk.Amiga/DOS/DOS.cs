/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class DOS
{
	public enum Error : int
	{
		None = 0,

		NoFreeStore = 103,
		TaskTableFull = 105,

		BadTemplate = 114,
		BadNumber = 115,
		RequiredArgumentMissing = 116,
		KeyNeedsArgument = 117,
		TooManyArguments = 118,
		UnmatchedQuotes = 119,
		LineTooLong = 120,
		FileNotObject = 121,
		InvalidResidentLibrary = 122,

		NoDefaultDirectory = 201,
		ObjectInUse = 202,
		ObjectExists = 203,
		DirectoryNotFound = 204,
		ObjectNotFound = 205,
		BadStreamName = 206,
		ObjectTooLarge = 207,
		ActionNotKnown = 209,
		InvalidComponentName = 210,
		InvalidLock = 211,
		ObjectWrongType = 212,
		DiskNotValidated = 213,
		DiskWriteProtected = 214,
		RenameAcrossDevices = 215,
		DirectoryNotEmpty = 216,
		TooManyLevels = 217,
		DeviceNotMounted = 218,
		SeekError = 219,
		CommentTooBig = 220,
		DiskFull = 221,
		DeleteProtected = 222,
		WriteProtected = 223,
		ReadProtected = 224,
		NotADosDisk = 225,
		NoDisk = 226,
		NoMoreEntries = 232,
		IsSoftLink = 233,
		ObjectLinked = 234,
		BadHunk = 235,
		NotImplemented = 236,
		RecordNotLocked = 240,
		LockCollision = 241,
		LockTimeout = 242,
		UnlockError = 243,

		BufferOverflow = 303,
		Break = 304,
		NotExecutable = 305,
	}

	public enum FileMode : int
	{
		ReadWrite = 1004,
		OldFile = 1005,
		NewFile = 1006,
	}

	public enum LockMode : int
	{
		Shared = -2,
		Read = Shared,
		Exclusive = -1,
		Write = Exclusive,
	}

	public enum ItemKind : int
	{
		Equal = -2,
		Error = -1,
		Nothing = 0,
		Unquoted = 1,
		Quoted = 2,
	}

	public const string Name = "dos.library";

	// Return codes from <dos/dos.h>.
	public const int RETURN_OK = 0;
	public const int RETURN_WARN = 5;
	public const int RETURN_ERROR = 10;
	public const int RETURN_FAIL = 20;

	public static APTR DOSLibraryBase
	{
		get => throw new System.NotSupportedException(
			"DOSLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"DOSLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(DosLvo.Open)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern BPTR? Open(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] FileMode accessMode);

	[AmigaLvo(DosLvo.Close)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Close(
		[M68kRegister(M68kRegister.D1)] BPTR file);

	[AmigaLvo(DosLvo.Read)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Read(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] uint buffer,
		[M68kRegister(M68kRegister.D3)] int length);

	[AmigaLvo(DosLvo.Write)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Write(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] uint buffer,
		[M68kRegister(M68kRegister.D3)] int length);

	[AmigaLvo(DosLvo.Input)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern BPTR Input();

	[AmigaLvo(DosLvo.Output)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern BPTR Output();

	[AmigaLvo(DosLvo.Seek)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Seek(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] int position,
		[M68kRegister(M68kRegister.D3)] int offset);

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int DeleteFile(
		[M68kRegister(M68kRegister.D1)] CString name);

	[AmigaLvo(-78)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Rename(
		[M68kRegister(M68kRegister.D1)] CString oldName,
		[M68kRegister(M68kRegister.D2)] CString newName);

	[AmigaLvo(DosLvo.Lock)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern BPTR? Lock(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] LockMode type);

	[AmigaLvo(DosLvo.UnLock)]
	public static extern void UnLock(
		[M68kRegister(M68kRegister.D1)] BPTR lock_);

	[AmigaLvo(DosLvo.DupLock)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern BPTR? DupLock(
		[M68kRegister(M68kRegister.D1)] BPTR lock_);

	[AmigaLvo(DosLvo.Examine)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Examine(
		[M68kRegister(M68kRegister.D1)] BPTR lock_,
		[M68kRegister(M68kRegister.D2), M68kWritesEntireBuffer] uint fileInfoBlock);

	[AmigaLvo(-108)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ExNext(
		[M68kRegister(M68kRegister.D1)] BPTR lock_,
		[M68kRegister(M68kRegister.D2), M68kWritesEntireBuffer] uint fileInfoBlock);

	[AmigaLvo(-114)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Info(
		[M68kRegister(M68kRegister.D1)] BPTR lock_,
		[M68kRegister(M68kRegister.D2), M68kWritesEntireBuffer] uint parameterBlock);

	[AmigaLvo(-120)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern BPTR? CreateDir(
		[M68kRegister(M68kRegister.D1)] CString name);

	[AmigaLvo(DosLvo.CurrentDir)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern BPTR? CurrentDir(
		[M68kRegister(M68kRegister.D1)] BPTR lock_);

	[AmigaLvo(DosLvo.IoErr)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern Error IoErr();

	[AmigaLvo(-138)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CreateProc(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] int priority,
		[M68kRegister(M68kRegister.D3)] BPTR segList,
		[M68kRegister(M68kRegister.D4)] int stackSize);

	[AmigaLvo(-144)]
	public static extern void Exit(
		[M68kRegister(M68kRegister.D1)] int returnCode);

	[AmigaLvo(-150)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern BPTR? LoadSeg(
		[M68kRegister(M68kRegister.D1)] CString name);

	[AmigaLvo(-156)]
	public static extern void UnLoadSeg(
		[M68kRegister(M68kRegister.D1)] BPTR segList);

	[AmigaLvo(-174)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint DeviceProc(
		[M68kRegister(M68kRegister.D1)] CString name);

	[AmigaLvo(-180)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetComment(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] CString comment);

	[AmigaLvo(-186)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetProtection(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] int protect);

	[AmigaLvo(DosLvo.DateStamp)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint DateStamp(
		[M68kRegister(M68kRegister.D1)] uint date);

	[AmigaLvo(DosLvo.Delay)]
	public static extern void Delay(
		[M68kRegister(M68kRegister.D1)] int timeout);

	[AmigaLvo(-204)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int WaitForChar(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] int timeout);

	[AmigaLvo(DosLvo.ParentDir)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern BPTR? ParentDir(
		[M68kRegister(M68kRegister.D1)] BPTR lock_);

	[AmigaLvo(DosLvo.IsInteractive)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int IsInteractive(
		[M68kRegister(M68kRegister.D1)] BPTR file);

	[AmigaLvo(-222)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Execute(
		[M68kRegister(M68kRegister.D1)] CString command,
		[M68kRegister(M68kRegister.D2)] BPTR input,
		[M68kRegister(M68kRegister.D3)] BPTR output);

	[AmigaLvo(-228)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocDosObject(
		[M68kRegister(M68kRegister.D1)] uint type,
		[M68kRegister(M68kRegister.D2)] uint tags);

	[AmigaLvo(-234)]
	public static extern void FreeDosObject(
		[M68kRegister(M68kRegister.D1)] uint type,
		[M68kRegister(M68kRegister.D2)] uint ptr);

	[AmigaLvo(-240)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int DoPkt(
		[M68kRegister(M68kRegister.D1)] uint port,
		[M68kRegister(M68kRegister.D2)] int action,
		[M68kRegister(M68kRegister.D3)] int arg1,
		[M68kRegister(M68kRegister.D4)] int arg2,
		[M68kRegister(M68kRegister.D5)] int arg3,
		[M68kRegister(M68kRegister.D6)] int arg4,
		[M68kRegister(M68kRegister.D7)] int arg5);

	[AmigaLvo(-246)]
	public static extern void SendPkt(
		[M68kRegister(M68kRegister.D1)] uint dosPacket,
		[M68kRegister(M68kRegister.D2)] uint port,
		[M68kRegister(M68kRegister.D3)] uint replyPort);

	[AmigaLvo(-252)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint WaitPkt();

	[AmigaLvo(-258)]
	public static extern void ReplyPkt(
		[M68kRegister(M68kRegister.D1)] uint dosPacket,
		[M68kRegister(M68kRegister.D2)] int result1,
		[M68kRegister(M68kRegister.D3)] int result2);

	[AmigaLvo(-264)]
	public static extern void AbortPkt(
		[M68kRegister(M68kRegister.D1)] uint port,
		[M68kRegister(M68kRegister.D2)] uint dosPacket);

	[AmigaLvo(-270)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int LockRecord(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] uint offset,
		[M68kRegister(M68kRegister.D3)] uint length,
		[M68kRegister(M68kRegister.D4)] uint mode,
		[M68kRegister(M68kRegister.D5)] uint timeout);

	[AmigaLvo(-276)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int LockRecords(
		[M68kRegister(M68kRegister.D1)] uint recordArray,
		[M68kRegister(M68kRegister.D2)] uint timeout);

	[AmigaLvo(-282)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int UnLockRecord(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] uint offset,
		[M68kRegister(M68kRegister.D3)] uint length);

	[AmigaLvo(-288)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int UnLockRecords(
		[M68kRegister(M68kRegister.D1)] uint recordArray);

	[AmigaLvo(DosLvo.SelectInput)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern BPTR SelectInput(
		[M68kRegister(M68kRegister.D1)] BPTR file);

	[AmigaLvo(DosLvo.SelectOutput)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern BPTR SelectOutput(
		[M68kRegister(M68kRegister.D1)] BPTR file);

	[AmigaLvo(DosLvo.FGetC)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int FGetC(
		[M68kRegister(M68kRegister.D1)] BPTR file);

	[AmigaLvo(DosLvo.FPutC)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int FPutC(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] int character);

	[AmigaLvo(DosLvo.UnGetC)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int UnGetC(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] int character);

	[AmigaLvo(DosLvo.FRead)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int FRead(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] APTR block,
		[M68kRegister(M68kRegister.D3)] uint blockLength,
		[M68kRegister(M68kRegister.D4)] uint number);

	[AmigaLvo(DosLvo.FWrite)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int FWrite(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] APTR block,
		[M68kRegister(M68kRegister.D3)] uint blockLength,
		[M68kRegister(M68kRegister.D4)] uint number);

	[AmigaLvo(DosLvo.FGets)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR FGets(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] APTR buffer,
		[M68kRegister(M68kRegister.D3)] uint bufferLength);

	[AmigaLvo(DosLvo.FPuts)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int FPuts(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] CString text);

	[AmigaLvo(-348)]
	public static extern void VFWritef(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] CString format,
		[M68kRegister(M68kRegister.D3)] uint argArray);

	[AmigaLvo(-354)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int VFPrintf(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] CString format,
		[M68kRegister(M68kRegister.D3)] uint argArray);

	[AmigaLvo(DosLvo.Flush)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Flush(
		[M68kRegister(M68kRegister.D1)] BPTR file);

	[AmigaLvo(-366)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetVBuf(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] uint buffer,
		[M68kRegister(M68kRegister.D3)] int type,
		[M68kRegister(M68kRegister.D4)] int size);

	[AmigaLvo(DosLvo.DupLockFromFH)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern BPTR? DupLockFromFH(
		[M68kRegister(M68kRegister.D1)] BPTR file);

	[AmigaLvo(DosLvo.OpenFromLock)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern BPTR? OpenFromLock(
		[M68kRegister(M68kRegister.D1)] BPTR lock_);

	[AmigaLvo(DosLvo.ParentOfFH)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern BPTR? ParentOfFH(
		[M68kRegister(M68kRegister.D1)] BPTR file);

	[AmigaLvo(DosLvo.ExamineFH)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ExamineFH(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2), M68kWritesEntireBuffer] APTR fileInfoBlock);

	[AmigaLvo(-396)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetFileDate(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] uint date);

	[AmigaLvo(DosLvo.NameFromLock)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int NameFromLock(
		[M68kRegister(M68kRegister.D1)] BPTR lock_,
		[M68kRegister(M68kRegister.D2)] APTR buffer,
		[M68kRegister(M68kRegister.D3)] int length);

	[AmigaLvo(DosLvo.NameFromFH)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int NameFromFH(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] APTR buffer,
		[M68kRegister(M68kRegister.D3)] int length);

	[AmigaLvo(-414)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern short SplitName(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] uint separator,
		[M68kRegister(M68kRegister.D3)] uint buffer,
		[M68kRegister(M68kRegister.D4)] int oldPosition,
		[M68kRegister(M68kRegister.D5)] int size);

	[AmigaLvo(DosLvo.SameLock)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SameLock(
		[M68kRegister(M68kRegister.D1)] BPTR lock1,
		[M68kRegister(M68kRegister.D2)] BPTR lock2);

	[AmigaLvo(-426)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetMode(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] int mode);

	[AmigaLvo(-432)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ExAll(
		[M68kRegister(M68kRegister.D1)] BPTR lock_,
		[M68kRegister(M68kRegister.D2)] uint buffer,
		[M68kRegister(M68kRegister.D3)] int size,
		[M68kRegister(M68kRegister.D4)] int data,
		[M68kRegister(M68kRegister.D5)] uint control);

	[AmigaLvo(-438)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ReadLink(
		[M68kRegister(M68kRegister.D1)] uint port,
		[M68kRegister(M68kRegister.D2)] BPTR lock_,
		[M68kRegister(M68kRegister.D3)] CString path,
		[M68kRegister(M68kRegister.D4)] uint buffer,
		[M68kRegister(M68kRegister.D5)] uint size);

	[AmigaLvo(-444)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MakeLink(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] int destination,
		[M68kRegister(M68kRegister.D3)] int soft);

	[AmigaLvo(-450)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ChangeMode(
		[M68kRegister(M68kRegister.D1)] int type,
		[M68kRegister(M68kRegister.D2)] BPTR file,
		[M68kRegister(M68kRegister.D3)] int newMode);

	[AmigaLvo(-456)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetFileSize(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] int position,
		[M68kRegister(M68kRegister.D3)] int mode);

	[AmigaLvo(DosLvo.SetIoErr)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern Error SetIoErr(
		[M68kRegister(M68kRegister.D1)] Error result);

	[AmigaLvo(-468)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Fault(
		[M68kRegister(M68kRegister.D1)] Error code,
		[M68kRegister(M68kRegister.D2)] CString header,
		[M68kRegister(M68kRegister.D3)] uint buffer,
		[M68kRegister(M68kRegister.D4)] int length);

	[AmigaLvo(-474)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int PrintFault(
		[M68kRegister(M68kRegister.D1)] Error code,
		[M68kRegister(M68kRegister.D2)] CString header);

	[AmigaLvo(-480)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ErrorReport(
		[M68kRegister(M68kRegister.D1)] Error code,
		[M68kRegister(M68kRegister.D2)] int type,
		[M68kRegister(M68kRegister.D3)] uint arg1,
		[M68kRegister(M68kRegister.D4)] uint device);

	[AmigaLvo(-492)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint Cli();

	[AmigaLvo(-498)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CreateNewProc(
		[M68kRegister(M68kRegister.D1)] uint tags);

	[AmigaLvo(-504)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int RunCommand(
		[M68kRegister(M68kRegister.D1)] uint segment,
		[M68kRegister(M68kRegister.D2)] int stack,
		[M68kRegister(M68kRegister.D3)] uint parameters,
		[M68kRegister(M68kRegister.D4)] int parameterLength);

	[AmigaLvo(-510)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetConsoleTask();

	[AmigaLvo(-516)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetConsoleTask(
		[M68kRegister(M68kRegister.D1)] uint task);

	[AmigaLvo(-522)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetFileSysTask();

	[AmigaLvo(-528)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetFileSysTask(
		[M68kRegister(M68kRegister.D1)] uint task);

	[AmigaLvo(-534)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetArgStr();

	[AmigaLvo(-540)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetArgStr(
		[M68kRegister(M68kRegister.D1)] CString text);

	[AmigaLvo(-546)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint FindCliProc(
		[M68kRegister(M68kRegister.D1)] uint number);

	[AmigaLvo(-552)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint MaxCli();

	[AmigaLvo(-558)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetCurrentDirName(
		[M68kRegister(M68kRegister.D1)] CString name);

	[AmigaLvo(-564)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetCurrentDirName(
		[M68kRegister(M68kRegister.D1)] uint buffer,
		[M68kRegister(M68kRegister.D2)] int length);

	[AmigaLvo(-570)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetProgramName(
		[M68kRegister(M68kRegister.D1)] CString name);

	[AmigaLvo(-576)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetProgramName(
		[M68kRegister(M68kRegister.D1)] uint buffer,
		[M68kRegister(M68kRegister.D2)] int length);

	[AmigaLvo(-582)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetPrompt(
		[M68kRegister(M68kRegister.D1)] CString name);

	[AmigaLvo(-588)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetPrompt(
		[M68kRegister(M68kRegister.D1)] uint buffer,
		[M68kRegister(M68kRegister.D2)] int length);

	[AmigaLvo(-594)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern BPTR? SetProgramDir(
		[M68kRegister(M68kRegister.D1)] BPTR lock_);

	[AmigaLvo(-600)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern BPTR? GetProgramDir();

	[AmigaLvo(-606)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SystemTagList(
		[M68kRegister(M68kRegister.D1)] CString command,
		[M68kRegister(M68kRegister.D2)] uint tags);

	[AmigaLvo(-612)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AssignLock(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] BPTR lock_);

	[AmigaLvo(-618)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AssignLate(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] CString path);

	[AmigaLvo(-624)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AssignPath(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] CString path);

	[AmigaLvo(-630)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AssignAdd(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] BPTR lock_);

	[AmigaLvo(-636)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int RemAssignList(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] BPTR lock_);

	[AmigaLvo(-642)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetDeviceProc(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] uint devProc);

	[AmigaLvo(-648)]
	public static extern void FreeDeviceProc(
		[M68kRegister(M68kRegister.D1)] uint devProc);

	[AmigaLvo(-654)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint LockDosList(
		[M68kRegister(M68kRegister.D1)] uint flags);

	[AmigaLvo(-660)]
	public static extern void UnLockDosList(
		[M68kRegister(M68kRegister.D1)] uint flags);

	[AmigaLvo(-666)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AttemptLockDosList(
		[M68kRegister(M68kRegister.D1)] uint flags);

	[AmigaLvo(-672)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int RemDosEntry(
		[M68kRegister(M68kRegister.D1)] uint dosList);

	[AmigaLvo(-678)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AddDosEntry(
		[M68kRegister(M68kRegister.D1)] uint dosList);

	[AmigaLvo(-684)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint FindDosEntry(
		[M68kRegister(M68kRegister.D1)] uint dosList,
		[M68kRegister(M68kRegister.D2)] CString name,
		[M68kRegister(M68kRegister.D3)] uint flags);

	[AmigaLvo(-690)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NextDosEntry(
		[M68kRegister(M68kRegister.D1)] uint dosList,
		[M68kRegister(M68kRegister.D2)] uint flags);

	[AmigaLvo(-696)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint MakeDosEntry(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] int type);

	[AmigaLvo(-702)]
	public static extern void FreeDosEntry(
		[M68kRegister(M68kRegister.D1)] uint dosList);

	[AmigaLvo(-708)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int IsFileSystem(
		[M68kRegister(M68kRegister.D1)] CString name);

	[AmigaLvo(-714)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Format(
		[M68kRegister(M68kRegister.D1)] CString fileSystem,
		[M68kRegister(M68kRegister.D2)] CString volumeName,
		[M68kRegister(M68kRegister.D3)] uint dosType);

	[AmigaLvo(-720)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Relabel(
		[M68kRegister(M68kRegister.D1)] CString drive,
		[M68kRegister(M68kRegister.D2)] CString newName);

	[AmigaLvo(-726)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Inhibit(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] int onOff);

	[AmigaLvo(-732)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AddBuffers(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] int number);

	[AmigaLvo(-738)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int CompareDates(
		[M68kRegister(M68kRegister.D1)] uint date1,
		[M68kRegister(M68kRegister.D2)] uint date2);

	[AmigaLvo(-744)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int DateToStr(
		[M68kRegister(M68kRegister.D1)] uint dateTime);

	[AmigaLvo(-750)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int StrToDate(
		[M68kRegister(M68kRegister.D1)] uint dateTime);

	[AmigaLvo(-756)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern BPTR? InternalLoadSeg(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] uint table,
		[M68kRegister(M68kRegister.A0)] uint functionArray,
		[M68kRegister(M68kRegister.A1)] uint stack);

	[AmigaLvo(-762)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int InternalUnLoadSeg(
		[M68kRegister(M68kRegister.D1)] BPTR segList,
		[M68kRegister(M68kRegister.A0)] uint freeFunction);

	[AmigaLvo(-768)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern BPTR? NewLoadSeg(
		[M68kRegister(M68kRegister.D1)] CString file,
		[M68kRegister(M68kRegister.D2)] uint tags);

	[AmigaLvo(-774)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AddSegment(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] BPTR segment,
		[M68kRegister(M68kRegister.D3)] int system);

	[AmigaLvo(-780)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern BPTR? FindSegment(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] BPTR segment,
		[M68kRegister(M68kRegister.D3)] int system);

	[AmigaLvo(-786)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int RemSegment(
		[M68kRegister(M68kRegister.D1)] BPTR segment);

	[AmigaLvo(-792)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int CheckSignal(
		[M68kRegister(M68kRegister.D1)] int mask);

	[AmigaLvo(DosLvo.ReadArgs)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ReadArgs(
		[M68kRegister(M68kRegister.D1)] CString template_,
		[M68kRegister(M68kRegister.D2)] uint array,
		[M68kRegister(M68kRegister.D3)] uint args);

	[AmigaLvo(-804)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int FindArg(
		[M68kRegister(M68kRegister.D1)] CString template_,
		[M68kRegister(M68kRegister.D2)] CString keyword);

	[AmigaLvo(-810)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ReadItem(
		[M68kRegister(M68kRegister.D1)] APTR name,
		[M68kRegister(M68kRegister.D2)] int maxChars,
		[M68kRegister(M68kRegister.D3)] APTR cSource);

	[AmigaLvo(-816)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int StrToLong(
		[M68kRegister(M68kRegister.D1)] CString text,
		[M68kRegister(M68kRegister.D2)] APTR value);

	[AmigaLvo(-822)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MatchFirst(
		[M68kRegister(M68kRegister.D1)] CString pattern,
		[M68kRegister(M68kRegister.D2)] uint anchor);

	[AmigaLvo(-828)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MatchNext(
		[M68kRegister(M68kRegister.D1)] uint anchor);

	[AmigaLvo(-834)]
	public static extern void MatchEnd(
		[M68kRegister(M68kRegister.D1)] uint anchor);

	[AmigaLvo(-840)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ParsePattern(
		[M68kRegister(M68kRegister.D1)] CString pattern,
		[M68kRegister(M68kRegister.D2)] uint buffer,
		[M68kRegister(M68kRegister.D3)] int bufferLength);

	[AmigaLvo(-846)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MatchPattern(
		[M68kRegister(M68kRegister.D1)] CString pattern,
		[M68kRegister(M68kRegister.D2)] CString text);

	[AmigaLvo(-858)]
	public static extern void FreeArgs(
		[M68kRegister(M68kRegister.D1)] uint args);

	[AmigaLvo(-870)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint FilePart(
		[M68kRegister(M68kRegister.D1)] CString path);

	[AmigaLvo(-876)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint PathPart(
		[M68kRegister(M68kRegister.D1)] CString path);

	[AmigaLvo(-882)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AddPart(
		[M68kRegister(M68kRegister.D1)] CString directoryName,
		[M68kRegister(M68kRegister.D2)] CString fileName,
		[M68kRegister(M68kRegister.D3)] uint size);

	[AmigaLvo(-888)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int StartNotify(
		[M68kRegister(M68kRegister.D1)] uint notify);

	[AmigaLvo(-894)]
	public static extern void EndNotify(
		[M68kRegister(M68kRegister.D1)] uint notify);

	[AmigaLvo(-900)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetVar(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] uint buffer,
		[M68kRegister(M68kRegister.D3)] int size,
		[M68kRegister(M68kRegister.D4)] int flags);

	[AmigaLvo(-906)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetVar(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] uint buffer,
		[M68kRegister(M68kRegister.D3)] int size,
		[M68kRegister(M68kRegister.D4)] int flags);

	[AmigaLvo(-912)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int DeleteVar(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] uint flags);

	[AmigaLvo(-918)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint FindVar(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] uint type);

	[AmigaLvo(-930)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int CliInitNewcli(
		[M68kRegister(M68kRegister.A0)] uint dosPacket);

	[AmigaLvo(-936)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int CliInitRun(
		[M68kRegister(M68kRegister.A0)] uint dosPacket);

	[AmigaLvo(-942)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int WriteChars(
		[M68kRegister(M68kRegister.D1)] uint buffer,
		[M68kRegister(M68kRegister.D2)] uint bufferLength);

	[AmigaLvo(-948)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int PutStr(
		[M68kRegister(M68kRegister.D1)] CString text);

	[AmigaLvo(-954)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int VPrintf(
		[M68kRegister(M68kRegister.D1)] CString format,
		[M68kRegister(M68kRegister.D2)] uint argArray);

	[AmigaLvo(-954)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int Printf(
		[M68kRegister(M68kRegister.D1)] CString format,
		[M68kRegister(M68kRegister.D2)]
		[AmigaStackVarargs]
		params AmigaVarArg[] arguments) =>
		throw new System.NotSupportedException(
			"DOS.Printf stack varargs are lowered by CopperSharp.");

	[AmigaLvo(-966)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ParsePatternNoCase(
		[M68kRegister(M68kRegister.D1)] CString pattern,
		[M68kRegister(M68kRegister.D2)] uint buffer,
		[M68kRegister(M68kRegister.D3)] int bufferLength);

	[AmigaLvo(-972)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MatchPatternNoCase(
		[M68kRegister(M68kRegister.D1)] CString pattern,
		[M68kRegister(M68kRegister.D2)] CString text);

	[AmigaLvo(-984)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SameDevice(
		[M68kRegister(M68kRegister.D1)] BPTR lock1,
		[M68kRegister(M68kRegister.D2)] BPTR lock2);

	[AmigaLvo(-990)]
	public static extern void ExAllEnd(
		[M68kRegister(M68kRegister.D1)] BPTR lock_,
		[M68kRegister(M68kRegister.D2)] uint buffer,
		[M68kRegister(M68kRegister.D3)] int size,
		[M68kRegister(M68kRegister.D4)] int data,
		[M68kRegister(M68kRegister.D5)] uint control);

	[AmigaLvo(-996)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetOwner(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] int ownerInfo);

	// MorphOS m68k ABI call alias.
	[AmigaLvo(-228)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocDosObjectTagList(
		[M68kRegister(M68kRegister.D1)] uint type,
		[M68kRegister(M68kRegister.D2)] uint tags);

	// MorphOS m68k ABI call alias.
	[AmigaLvo(-240)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int DoPkt0(
		[M68kRegister(M68kRegister.D1)] uint port,
		[M68kRegister(M68kRegister.D2)] int action);

	// MorphOS m68k ABI call alias.
	[AmigaLvo(-240)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int DoPkt1(
		[M68kRegister(M68kRegister.D1)] uint port,
		[M68kRegister(M68kRegister.D2)] int action,
		[M68kRegister(M68kRegister.D3)] int arg1);

	// MorphOS m68k ABI call alias.
	[AmigaLvo(-240)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int DoPkt2(
		[M68kRegister(M68kRegister.D1)] uint port,
		[M68kRegister(M68kRegister.D2)] int action,
		[M68kRegister(M68kRegister.D3)] int arg1,
		[M68kRegister(M68kRegister.D4)] int arg2);

	// MorphOS m68k ABI call alias.
	[AmigaLvo(-240)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int DoPkt3(
		[M68kRegister(M68kRegister.D1)] uint port,
		[M68kRegister(M68kRegister.D2)] int action,
		[M68kRegister(M68kRegister.D3)] int arg1,
		[M68kRegister(M68kRegister.D4)] int arg2,
		[M68kRegister(M68kRegister.D5)] int arg3);

	// MorphOS m68k ABI call alias.
	[AmigaLvo(-240)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int DoPkt4(
		[M68kRegister(M68kRegister.D1)] uint port,
		[M68kRegister(M68kRegister.D2)] int action,
		[M68kRegister(M68kRegister.D3)] int arg1,
		[M68kRegister(M68kRegister.D4)] int arg2,
		[M68kRegister(M68kRegister.D5)] int arg3,
		[M68kRegister(M68kRegister.D6)] int arg4);

	// MorphOS m68k ABI call alias.
	[AmigaLvo(-498)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CreateNewProcTagList(
		[M68kRegister(M68kRegister.D1)] uint tags);

	// MorphOS m68k ABI call alias.
	[AmigaLvo(-606)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int System(
		[M68kRegister(M68kRegister.D1)] CString command,
		[M68kRegister(M68kRegister.D2)] uint tags);

	// MorphOS m68k ABI call alias.
	[AmigaLvo(-768)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern BPTR? NewLoadSegTagList(
		[M68kRegister(M68kRegister.D1)] CString file,
		[M68kRegister(M68kRegister.D2)] uint tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1002)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AddSegmentTagList(
		[M68kRegister(M68kRegister.A0)] uint tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1008)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern BPTR? FindSegmentTagList(
		[M68kRegister(M68kRegister.A0)] uint tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1066)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern long Seek64(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] long position,
		[M68kRegister(M68kRegister.D4)] int mode);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1072)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern long SetFileSize64(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] long position,
		[M68kRegister(M68kRegister.D4)] int mode);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1078)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int LockRecord64(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] ulong offset,
		[M68kRegister(M68kRegister.D4)] ulong length,
		[M68kRegister(M68kRegister.D6)] uint mode,
		[M68kRegister(M68kRegister.D7)] uint timeout);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1084)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int LockRecords64(
		[M68kRegister(M68kRegister.D1)] uint recordArray,
		[M68kRegister(M68kRegister.D2)] uint timeout);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1090)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int UnLockRecord64(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2)] ulong offset,
		[M68kRegister(M68kRegister.D4)] ulong length);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1096)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int UnLockRecords64(
		[M68kRegister(M68kRegister.D1)] uint recordArray);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1114)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int NewReadLink(
		[M68kRegister(M68kRegister.D1)] uint port,
		[M68kRegister(M68kRegister.D2)] BPTR lock_,
		[M68kRegister(M68kRegister.D3)] CString path,
		[M68kRegister(M68kRegister.D4)] uint buffer,
		[M68kRegister(M68kRegister.D5)] int bufferSize);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1120)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetFileSysAttr(
		[M68kRegister(M68kRegister.D1)] CString deviceName,
		[M68kRegister(M68kRegister.D2)] int attribute,
		[M68kRegister(M68kRegister.D3)] uint storage,
		[M68kRegister(M68kRegister.D4)] int storageSize);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1126)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetSegListAttr(
		[M68kRegister(M68kRegister.D1)] BPTR segmentList,
		[M68kRegister(M68kRegister.D2)] SegmentListTag attribute,
		[M68kRegister(M68kRegister.D3)] APTR storage,
		[M68kRegister(M68kRegister.D4)] int storageSize);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1132)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetDosObjectAttr(
		[M68kRegister(M68kRegister.D1)] uint type,
		[M68kRegister(M68kRegister.D2)] uint ptr,
		[M68kRegister(M68kRegister.D3)] uint tags);

	// MorphOS m68k ABI call alias.
	[AmigaLvo(-1132)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetDosObjectAttrTagList(
		[M68kRegister(M68kRegister.D1)] uint type,
		[M68kRegister(M68kRegister.D2)] uint ptr,
		[M68kRegister(M68kRegister.D3)] uint tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1138)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetDosObjectAttr(
		[M68kRegister(M68kRegister.D1)] uint type,
		[M68kRegister(M68kRegister.D2)] uint ptr,
		[M68kRegister(M68kRegister.D3)] uint tags);

	// MorphOS m68k ABI call alias.
	[AmigaLvo(-1138)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetDosObjectAttrTagList(
		[M68kRegister(M68kRegister.D1)] uint type,
		[M68kRegister(M68kRegister.D2)] uint ptr,
		[M68kRegister(M68kRegister.D3)] uint tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1144)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Examine64(
		[M68kRegister(M68kRegister.D1)] BPTR lock_,
		[M68kRegister(M68kRegister.D2), M68kWritesEntireBuffer] uint fileInfoBlock,
		[M68kRegister(M68kRegister.D3)] uint tags);

	// MorphOS m68k ABI call alias.
	[AmigaLvo(-1144)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Examine64TagList(
		[M68kRegister(M68kRegister.D1)] BPTR lock_,
		[M68kRegister(M68kRegister.D2), M68kWritesEntireBuffer] uint fileInfoBlock,
		[M68kRegister(M68kRegister.D3)] uint tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1150)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ExNext64(
		[M68kRegister(M68kRegister.D1)] BPTR lock_,
		[M68kRegister(M68kRegister.D2), M68kWritesEntireBuffer] uint fileInfoBlock,
		[M68kRegister(M68kRegister.D3)] uint tags);

	// MorphOS m68k ABI call alias.
	[AmigaLvo(-1150)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ExNext64TagList(
		[M68kRegister(M68kRegister.D1)] BPTR lock_,
		[M68kRegister(M68kRegister.D2), M68kWritesEntireBuffer] uint fileInfoBlock,
		[M68kRegister(M68kRegister.D3)] uint tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1156)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ExamineFH64(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2), M68kWritesEntireBuffer] uint fileInfoBlock,
		[M68kRegister(M68kRegister.D3)] uint tags);

	// MorphOS m68k ABI call alias.
	[AmigaLvo(-1156)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ExamineFH64TagList(
		[M68kRegister(M68kRegister.D1)] BPTR file,
		[M68kRegister(M68kRegister.D2), M68kWritesEntireBuffer] uint fileInfoBlock,
		[M68kRegister(M68kRegister.D3)] uint tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1162)]
	public static extern void ReleaseCLINumber(
		[M68kRegister(M68kRegister.D1)] int cliNumber);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1168)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR QueryCLIDataTagList(
		[M68kRegister(M68kRegister.D1)] APTR tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1174)]
	public static extern void FreeCLIData(
		[M68kRegister(M68kRegister.D1)] APTR data);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1180)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetSegListAttrTagList(
		[M68kRegister(M68kRegister.D1)] BPTR segmentList,
		[M68kRegister(M68kRegister.D2)] SegmentListTag attribute,
		[M68kRegister(M68kRegister.D3)] APTR storage,
		[M68kRegister(M68kRegister.D4)] int storageSize,
		[M68kRegister(M68kRegister.D5)] APTR tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1186)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetFilePosixDate(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] APTR date,
		[M68kRegister(M68kRegister.D3)] uint tags);

	// MorphOS m68k ABI call alias.
	[AmigaLvo(-1186)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetFilePosixDateTagList(
		[M68kRegister(M68kRegister.D1)] CString name,
		[M68kRegister(M68kRegister.D2)] APTR date,
		[M68kRegister(M68kRegister.D3)] uint tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1192)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR PosixDateStamp(
		[M68kRegister(M68kRegister.D1)] APTR date);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1198)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int PosixDateStampToDateStamp(
		[M68kRegister(M68kRegister.D1)] APTR posixDate,
		[M68kRegister(M68kRegister.D2)] APTR date);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1204)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int DateStampToPosixDateStamp(
		[M68kRegister(M68kRegister.D1)] APTR date,
		[M68kRegister(M68kRegister.D2)] APTR posixDate);
}
