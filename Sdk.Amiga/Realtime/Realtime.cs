/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class Realtime
{
	public const string Name = "realtime.library";

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint LockRealTime(
		[M68kRegister(M68kRegister.D0)] uint lockType);

	[AmigaLvo(-36)]
	public static extern void UnlockRealTime(
		[M68kRegister(M68kRegister.A0)] uint lock_);

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CreatePlayerA(
		[M68kRegister(M68kRegister.A0)] uint tags);

	[AmigaLvo(-48)]
	public static extern void DeletePlayer(
		[M68kRegister(M68kRegister.A0)] uint player);

	[AmigaLvo(-54)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetPlayerAttrsA(
		[M68kRegister(M68kRegister.A0)] uint player,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetConductorState(
		[M68kRegister(M68kRegister.A0)] uint player,
		[M68kRegister(M68kRegister.D0)] uint state,
		[M68kRegister(M68kRegister.D1)] int time);

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ExternalSync(
		[M68kRegister(M68kRegister.A0)] uint player,
		[M68kRegister(M68kRegister.D0)] int minTime,
		[M68kRegister(M68kRegister.D1)] int maxTime);

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NextConductor(
		[M68kRegister(M68kRegister.A0)] uint conductor);

	[AmigaLvo(-78)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint FindConductor(
		[M68kRegister(M68kRegister.A0)] CString name);

	[AmigaLvo(-84)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetPlayerAttrsA(
		[M68kRegister(M68kRegister.A0)] uint player,
		[M68kRegister(M68kRegister.A1)] uint tags);

	public static uint CreatePlayer(uint tags) =>
		CreatePlayerA(tags);

	public static int SetPlayerAttrs(uint player, uint tags) =>
		SetPlayerAttrsA(player, tags);

	public static uint GetPlayerAttrs(uint player, uint tags) =>
		GetPlayerAttrsA(player, tags);
}
