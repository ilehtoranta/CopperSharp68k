/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

namespace Amiga;

// xpkmaster.library delegates compression to optional XPK sublibraries. It
// is therefore always supplied as a manually opened library base.
[AmigaLibrary(Name, AmigaLibraryBasePolicy.Manual)]
public static class XpkMaster
{
	public const string Name = "xpkmaster.library";

	public static APTR XpkMasterLibraryBase
	{
		get => throw new System.NotSupportedException(
			"XpkMasterLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"XpkMasterLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(-36)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XpkExamine(
		[M68kRegister(M68kRegister.A0)] uint fileInfo,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XpkPack(
		[M68kRegister(M68kRegister.A0)] uint tags);

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XpkUnpack(
		[M68kRegister(M68kRegister.A0)] uint tags);

	[AmigaLvo(-54)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XpkOpen(
		[M68kRegister(M68kRegister.A0)] uint fileInfo,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XpkRead(
		[M68kRegister(M68kRegister.A0)] uint fileInfo,
		[M68kRegister(M68kRegister.A1)] uint buffer,
		[M68kRegister(M68kRegister.D0)] uint length);

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XpkWrite(
		[M68kRegister(M68kRegister.A0)] uint fileInfo,
		[M68kRegister(M68kRegister.A1)] uint buffer,
		[M68kRegister(M68kRegister.D0)] int length);

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XpkSeek(
		[M68kRegister(M68kRegister.A0)] uint fileInfo,
		[M68kRegister(M68kRegister.D0)] int distance,
		[M68kRegister(M68kRegister.D1)] int mode);

	[AmigaLvo(-78)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XpkClose(
		[M68kRegister(M68kRegister.A0)] uint fileInfo);

	[AmigaLvo(-84)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XpkQuery(
		[M68kRegister(M68kRegister.A0)] uint tags);

	[AmigaLvo(-90)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint XpkAllocObject(
		[M68kRegister(M68kRegister.D0)] uint type,
		[M68kRegister(M68kRegister.A0)] uint tags);

	[AmigaLvo(-96)]
	public static extern void XpkFreeObject(
		[M68kRegister(M68kRegister.D0)] uint type,
		[M68kRegister(M68kRegister.A0)] uint obj);

	[AmigaLvo(-102)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XpkPrintFault(
		[M68kRegister(M68kRegister.D0)] int code,
		[M68kRegister(M68kRegister.A0)] CString header);

	[AmigaLvo(-108)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint XpkFault(
		[M68kRegister(M68kRegister.D0)] int code,
		[M68kRegister(M68kRegister.A0)] CString header,
		[M68kRegister(M68kRegister.A1)] uint buffer,
		[M68kRegister(M68kRegister.D1)] uint size);

	[AmigaLvo(-114)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XpkPassRequest(
		[M68kRegister(M68kRegister.A0)] uint tags);
}
