/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>Public dos.library vector offsets used by ROM and host compatibility gateways.</summary>
public static class DosLvo
{
	public const short Open = -30, Close = -36, Read = -42, Write = -48, Input = -54, Output = -60, Seek = -66;
	public const short DeleteFile = -72, Rename = -78, Lock = -84, UnLock = -90, DupLock = -96, Examine = -102;
	public const short CurrentDir = -126, IoErr = -132, DateStamp = -192, Delay = -198, SetIoErr = -462;
	public const short PrintFault = -474, SystemTagList = -606, NewLoadSegTagList = -768, ReadArgs = -798, FreeArgs = -858;
	public const short Seek64 = -1066, SetFileSize64 = -1072, LockRecord64 = -1078, LockRecords64 = -1084;
	public const short UnLockRecord64 = -1090, UnLockRecords64 = -1096, NewReadLink = -1114, GetFileSysAttr = -1120;
	public const short GetSegListAttr = -1126, SetDosObjectAttr = -1132, GetDosObjectAttr = -1138, Examine64 = -1144;
	public const short ExNext64 = -1150, ExamineFH64 = -1156, ReleaseCLINumber = -1162, QueryCLIData = -1168;
	public const short FreeCLIData = -1174, GetSegListAttrTagList = -1180, SetFilePosixDate = -1186;
	public const short PosixDateStamp = -1192, PosixDateStampToDateStamp = -1198, DateStampToPosixDateStamp = -1204;
}
