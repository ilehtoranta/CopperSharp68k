/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public static class KeymapConstants
{
	public const byte KeyMapTypeMask = 0x07;
	public const byte DeadPrefixModifierBit = 0;
	public const byte DeadPrefixDeadBit = 3;
	public const byte DeadPrefixModifierFlag = 1;
	public const byte DeadPrefixDeadFlag = 8;
	public const byte DeadPrefix2DIndexMask = 0x0f;
	public const byte DeadPrefix2DFactorShift = 4;
}
