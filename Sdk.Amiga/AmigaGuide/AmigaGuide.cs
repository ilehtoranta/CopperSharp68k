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
		[M68kRegister(M68kRegister.A0)] uint handle)
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
	public static uint OpenAmigaGuideA(
		[M68kRegister(M68kRegister.A0)] uint newAmigaGuide,
		[M68kRegister(M68kRegister.A1)] uint tags)
	{
		return 0;
	}

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint OpenAmigaGuideAsyncA(
		[M68kRegister(M68kRegister.A0)] uint newAmigaGuide,
		[M68kRegister(M68kRegister.D0)] uint tags)
	{
		return 0;
	}

	[AmigaLvo(-66)]
	public static void CloseAmigaGuide(
		[M68kRegister(M68kRegister.A0)] uint handle)
	{
	}

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint AmigaGuideSignal(
		[M68kRegister(M68kRegister.A0)] uint handle)
	{
		return 0;
	}

	[AmigaLvo(-78)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint GetAmigaGuideMsg(
		[M68kRegister(M68kRegister.A0)] uint handle)
	{
		return 0;
	}

	[AmigaLvo(-84)]
	public static void ReplyAmigaGuideMsg(
		[M68kRegister(M68kRegister.A0)] uint message)
	{
	}

	[AmigaLvo(-90)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int SetAmigaGuideContextA(
		[M68kRegister(M68kRegister.A0)] uint handle,
		[M68kRegister(M68kRegister.D0)] uint context,
		[M68kRegister(M68kRegister.D1)] uint tags)
	{
		return 0;
	}

	[AmigaLvo(-96)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int SendAmigaGuideContextA(
		[M68kRegister(M68kRegister.A0)] uint handle,
		[M68kRegister(M68kRegister.D0)] uint tags)
	{
		return 0;
	}

	[AmigaLvo(-102)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int SendAmigaGuideCmdA(
		[M68kRegister(M68kRegister.A0)] uint handle,
		[M68kRegister(M68kRegister.D0)] CString command,
		[M68kRegister(M68kRegister.D1)] uint tags)
	{
		return 0;
	}

	[AmigaLvo(-108)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int SetAmigaGuideAttrsA(
		[M68kRegister(M68kRegister.A0)] uint handle,
		[M68kRegister(M68kRegister.A1)] uint tags)
	{
		return 0;
	}

	[AmigaLvo(-114)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int GetAmigaGuideAttr(
		[M68kRegister(M68kRegister.D0)] uint tag,
		[M68kRegister(M68kRegister.A0)] uint handle,
		[M68kRegister(M68kRegister.A1), M68kWritesBuffer] uint storage)
	{
		return 0;
	}

	[AmigaLvo(-126)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int LoadXRef(
		[M68kRegister(M68kRegister.A0)] uint lockPtr,
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
	public static uint AddAmigaGuideHostA(
		[M68kRegister(M68kRegister.A0)] uint hook,
		[M68kRegister(M68kRegister.D0)] CString name,
		[M68kRegister(M68kRegister.A1)] uint tags)
	{
		return 0;
	}

	[AmigaLvo(-144)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int RemoveAmigaGuideHostA(
		[M68kRegister(M68kRegister.A0)] uint host,
		[M68kRegister(M68kRegister.A1)] uint tags)
	{
		return 0;
	}

	[AmigaLvo(-210)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint GetAmigaGuideString(
		[M68kRegister(M68kRegister.D0)] int id)
	{
		return 0;
	}

	public static uint OpenAmigaGuide(uint newAmigaGuide, uint tags) =>
		OpenAmigaGuideA(newAmigaGuide, tags);

	public static uint OpenAmigaGuideAsync(uint newAmigaGuide, uint tags) =>
		OpenAmigaGuideAsyncA(newAmigaGuide, tags);

	public static int SetAmigaGuideContext(uint handle, uint context, uint tags) =>
		SetAmigaGuideContextA(handle, context, tags);

	public static int SendAmigaGuideContext(uint handle, uint tags) =>
		SendAmigaGuideContextA(handle, tags);

	public static int SendAmigaGuideCmd(uint handle, CString command, uint tags) =>
		SendAmigaGuideCmdA(handle, command, tags);

	public static int SetAmigaGuideAttrs(uint handle, uint tags) =>
		SetAmigaGuideAttrsA(handle, tags);

	public static uint AddAmigaGuideHost(uint hook, CString name, uint tags) =>
		AddAmigaGuideHostA(hook, name, tags);

	public static int RemoveAmigaGuideHost(uint host, uint tags) =>
		RemoveAmigaGuideHostA(host, tags);
}
