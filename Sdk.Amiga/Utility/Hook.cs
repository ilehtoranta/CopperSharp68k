/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public static class Hook
{
	public const uint Size = 20;

	public static unsafe void Initialize(APTR hook, APTR entry, APTR subEntry, APTR data)
	{
		var words = (uint*)APTR.ToUInt32(hook);
		words[0] = 0;
		words[1] = 0;
		words[2] = APTR.ToUInt32(entry);
		words[3] = APTR.ToUInt32(subEntry);
		words[4] = APTR.ToUInt32(data);
	}

	public static void Initialize(APTR hook, APTR entry) =>
		Initialize(hook, entry, APTR.Null, APTR.Null);
}
