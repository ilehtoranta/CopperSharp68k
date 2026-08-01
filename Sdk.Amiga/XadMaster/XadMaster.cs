/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

namespace Amiga;

// xadmaster.library and its archive-format clients are optional system
// components. Open the library explicitly and assign the returned base.
[AmigaLibrary(Name, AmigaLibraryBasePolicy.Manual)]
public static class XadMaster
{
	public const string Name = "xadmaster.library";

	public static APTR XadMasterLibraryBase
	{
		get => throw new System.NotSupportedException(
			"XadMasterLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"XadMasterLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint XadAllocObjectA(
		[M68kRegister(M68kRegister.D0)] uint type,
		[M68kRegister(M68kRegister.A0)] uint tags);

	[AmigaLvo(-36)]
	public static extern void XadFreeObjectA(
		[M68kRegister(M68kRegister.A0)] uint obj,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint XadRecogFileA(
		[M68kRegister(M68kRegister.D0)] uint size,
		[M68kRegister(M68kRegister.A0)] uint memory,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XadGetInfoA(
		[M68kRegister(M68kRegister.A0)] uint archiveInfo,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-54)]
	public static extern void XadFreeInfo(
		[M68kRegister(M68kRegister.A0)] uint archiveInfo);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XadFileUnArcA(
		[M68kRegister(M68kRegister.A0)] uint archiveInfo,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XadDiskUnArcA(
		[M68kRegister(M68kRegister.A0)] uint archiveInfo,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern CString XadGetErrorText(
		[M68kRegister(M68kRegister.D0)] int errorNumber);

	[AmigaLvo(-78)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint XadGetClientInfo();

	[AmigaLvo(-84)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XadHookAccess(
		[M68kRegister(M68kRegister.D0)] uint command,
		[M68kRegister(M68kRegister.D1)] int data,
		[M68kRegister(M68kRegister.A0)] uint buffer,
		[M68kRegister(M68kRegister.A1)] uint archiveInfo);

	[AmigaLvo(-90)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XadConvertDatesA(
		[M68kRegister(M68kRegister.A0)] uint tags);

	[AmigaLvo(-96)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ushort XadCalcCRC16(
		[M68kRegister(M68kRegister.D0)] uint id,
		[M68kRegister(M68kRegister.D1)] uint initial,
		[M68kRegister(M68kRegister.D2)] uint size,
		[M68kRegister(M68kRegister.A0)] uint buffer);

	[AmigaLvo(-102)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint XadCalcCRC32(
		[M68kRegister(M68kRegister.D0)] uint id,
		[M68kRegister(M68kRegister.D1)] uint initial,
		[M68kRegister(M68kRegister.D2)] uint size,
		[M68kRegister(M68kRegister.A0)] uint buffer);

	[AmigaLvo(-108)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint XadAllocVec(
		[M68kRegister(M68kRegister.D0)] uint size,
		[M68kRegister(M68kRegister.D1)] uint flags);

	[AmigaLvo(-114)]
	public static extern void XadCopyMem(
		[M68kRegister(M68kRegister.A0)] uint source,
		[M68kRegister(M68kRegister.A1)] uint destination,
		[M68kRegister(M68kRegister.D0)] uint size);

	[AmigaLvo(-120)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XadHookTagAccessA(
		[M68kRegister(M68kRegister.D0)] uint command,
		[M68kRegister(M68kRegister.D1)] int data,
		[M68kRegister(M68kRegister.A0)] uint buffer,
		[M68kRegister(M68kRegister.A1)] uint archiveInfo,
		[M68kRegister(M68kRegister.A2)] uint tags);

	[AmigaLvo(-126)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XadConvertProtectionA(
		[M68kRegister(M68kRegister.A0)] uint tags);

	[AmigaLvo(-132)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XadGetDiskInfoA(
		[M68kRegister(M68kRegister.A0)] uint archiveInfo,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-144)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XadGetHookAccessA(
		[M68kRegister(M68kRegister.A0)] uint archiveInfo,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-150)]
	public static extern void XadFreeHookAccessA(
		[M68kRegister(M68kRegister.A0)] uint archiveInfo,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-156)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XadAddFileEntryA(
		[M68kRegister(M68kRegister.A0)] uint fileInfo,
		[M68kRegister(M68kRegister.A1)] uint archiveInfo,
		[M68kRegister(M68kRegister.A2)] uint tags);

	[AmigaLvo(-162)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XadAddDiskEntryA(
		[M68kRegister(M68kRegister.A0)] uint diskInfo,
		[M68kRegister(M68kRegister.A1)] uint archiveInfo,
		[M68kRegister(M68kRegister.A2)] uint tags);

	[AmigaLvo(-168)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XadGetFilenameA(
		[M68kRegister(M68kRegister.D0)] uint bufferSize,
		[M68kRegister(M68kRegister.A0)] uint buffer,
		[M68kRegister(M68kRegister.A1)] uint path,
		[M68kRegister(M68kRegister.A2)] uint name,
		[M68kRegister(M68kRegister.A3)] uint tags);

	[AmigaLvo(-174)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint XadConvertNameA(
		[M68kRegister(M68kRegister.D0)] uint charset,
		[M68kRegister(M68kRegister.A0)] uint tags);

	[AmigaLvo(-180)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint XadGetDefaultNameA(
		[M68kRegister(M68kRegister.A0)] uint tags);

	[AmigaLvo(-186)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint XadGetSystemInfo();
}
