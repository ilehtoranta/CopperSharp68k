/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Globalization;

namespace CopperSharp.Runtime;

/// <summary>
/// Target-owned deterministic DateOnly formatting for the compiler's exact
/// invariant ISO call-site specialization.
/// </summary>
internal static class ShadowDateOnlyFormatting
{
	public static CultureInfo? GetInvariantCulturePlaceholder() => null;
}
