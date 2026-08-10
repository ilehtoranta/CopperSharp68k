/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public static class KeyboardDevice
{
	public const string Name = "keyboard.device";
	public const short Open = -6;
	public const short Close = -12;
	public const short Expunge = -18;
	public const short ExtFunc = -24;
	public const short BeginIO = -30;
	public const short AbortIO = -36;
}

public enum KeyboardCommand : ushort
{
	ReadEvent = 9,
	ReadMatrix = 10,
	AddResetHandler = 11,
	RemoveResetHandler = 12,
	ResetHandlerDone = 13,
}
