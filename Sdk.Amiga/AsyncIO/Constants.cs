/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>Modes accepted by <see cref="AsyncIO.OpenAsync"/>.</summary>
public enum AsyncOpenMode : uint
{
	Read = 0,
	Write = 1,
	Append = 2,
}

/// <summary>Origins accepted by <see cref="AsyncIO.SeekAsync"/>.</summary>
public enum AsyncSeekMode : int
{
	Start = -1,
	Current = 0,
	End = 1,
}
