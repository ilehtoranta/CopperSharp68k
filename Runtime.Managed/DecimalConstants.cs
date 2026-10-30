/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Runtime;

// Keep the public Decimal constants independent of CoreLib's arithmetic caches.
public static class ShadowDecimalConstants
{
	public static readonly decimal Zero = new(0, 0, 0, false, 0);
	public static readonly decimal One = new(1, 0, 0, false, 0);
	public static readonly decimal MinusOne = new(1, 0, 0, true, 0);
}
