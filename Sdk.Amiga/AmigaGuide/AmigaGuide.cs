/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class AmigaGuide
{
	public const string Name = "amigaguide.library";

	public static APTR AmigaGuideLibraryBase
	{
		get => throw new System.NotSupportedException(
			"AmigaGuideLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"AmigaGuideLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(-36)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int LockAmigaGuideBase(
		[M68kRegister(M68kRegister.A0)] APTR handle)
	{
		return 0;
	}

	[AmigaLvo(-42)]
	public static void UnlockAmigaGuideBase(
		[M68kRegister(M68kRegister.D0)] int key)
	{
	}

	[AmigaLvo(-54)]
	[return: M68kRegister(M68kRegister.D0)]
	public static APTR OpenAmigaGuideA(
		[M68kRegister(M68kRegister.A0)] APTR newAmigaGuide,
		[M68kRegister(M68kRegister.A1)] APTR tags)
	{
		return APTR.Null;
	}

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static APTR OpenAmigaGuideAsyncA(
		[M68kRegister(M68kRegister.A0)] APTR newAmigaGuide,
		[M68kRegister(M68kRegister.D0)] APTR tags)
	{
		return APTR.Null;
	}

	[AmigaLvo(-66)]
	public static void CloseAmigaGuide(
		[M68kRegister(M68kRegister.A0)] APTR handle)
	{
	}

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint AmigaGuideSignal(
		[M68kRegister(M68kRegister.A0)] APTR handle)
	{
		return 0;
	}

	[AmigaLvo(-78)]
	[return: M68kRegister(M68kRegister.D0)]
	public static APTR GetAmigaGuideMsg(
		[M68kRegister(M68kRegister.A0)] APTR handle)
	{
		return APTR.Null;
	}

	[AmigaLvo(-84)]
	public static void ReplyAmigaGuideMsg(
		[M68kRegister(M68kRegister.A0)] APTR message)
	{
	}

	[AmigaLvo(-90)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int SetAmigaGuideContextA(
		[M68kRegister(M68kRegister.A0)] APTR handle,
		[M68kRegister(M68kRegister.D0)] uint context,
		[M68kRegister(M68kRegister.D1)] APTR tags)
	{
		return 0;
	}

	[AmigaLvo(-96)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int SendAmigaGuideContextA(
		[M68kRegister(M68kRegister.A0)] APTR handle,
		[M68kRegister(M68kRegister.D0)] APTR tags)
	{
		return 0;
	}

	[AmigaLvo(-102)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int SendAmigaGuideCmdA(
		[M68kRegister(M68kRegister.A0)] APTR handle,
		[M68kRegister(M68kRegister.D0)] CString command,
		[M68kRegister(M68kRegister.D1)] APTR tags)
	{
		return 0;
	}

	[AmigaLvo(-108)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int SetAmigaGuideAttrsA(
		[M68kRegister(M68kRegister.A0)] APTR handle,
		[M68kRegister(M68kRegister.A1)] APTR tags)
	{
		return 0;
	}

	[AmigaLvo(-114)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int GetAmigaGuideAttr(
		[M68kRegister(M68kRegister.D0)] uint tag,
		[M68kRegister(M68kRegister.A0)] APTR handle,
		[M68kRegister(M68kRegister.A1), M68kWritesBuffer] APTR storage)
	{
		return 0;
	}

	[AmigaLvo(-126)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int LoadXRef(
		[M68kRegister(M68kRegister.A0)] BPTR lockPtr,
		[M68kRegister(M68kRegister.A1)] CString name)
	{
		return 0;
	}

	[AmigaLvo(-132)]
	public static void ExpungeXRef()
	{
	}

	[AmigaLvo(-138)]
	[return: M68kRegister(M68kRegister.D0)]
	public static APTR AddAmigaGuideHostA(
		[M68kRegister(M68kRegister.A0)] APTR hook,
		[M68kRegister(M68kRegister.D0)] CString name,
		[M68kRegister(M68kRegister.A1)] APTR tags)
	{
		return APTR.Null;
	}

	[AmigaLvo(-144)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int RemoveAmigaGuideHostA(
		[M68kRegister(M68kRegister.A0)] APTR host,
		[M68kRegister(M68kRegister.A1)] APTR tags)
	{
		return 0;
	}

	[AmigaLvo(-210)]
	[return: M68kRegister(M68kRegister.D0)]
	public static STRPTR GetAmigaGuideString(
		[M68kRegister(M68kRegister.D0)] int id)
	{
		return STRPTR.Null;
	}

	public static APTR OpenAmigaGuide(APTR newAmigaGuide, APTR tags) =>
		OpenAmigaGuideA(newAmigaGuide, tags);

	public static APTR OpenAmigaGuideAsync(APTR newAmigaGuide, APTR tags) =>
		OpenAmigaGuideAsyncA(newAmigaGuide, tags);

	public static int SetAmigaGuideContext(APTR handle, uint context, APTR tags) =>
		SetAmigaGuideContextA(handle, context, tags);

	public static int SendAmigaGuideContext(APTR handle, APTR tags) =>
		SendAmigaGuideContextA(handle, tags);

	public static int SendAmigaGuideCmd(APTR handle, CString command, APTR tags) =>
		SendAmigaGuideCmdA(handle, command, tags);

	public static int SetAmigaGuideAttrs(APTR handle, APTR tags) =>
		SetAmigaGuideAttrsA(handle, tags);

	public static APTR AddAmigaGuideHost(APTR hook, CString name, APTR tags) =>
		AddAmigaGuideHostA(hook, name, tags);

	public static int RemoveAmigaGuideHost(APTR host, APTR tags) =>
		RemoveAmigaGuideHostA(host, tags);
}
