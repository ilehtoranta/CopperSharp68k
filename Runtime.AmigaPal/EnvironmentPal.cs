/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Runtime.AmigaPal;

/// <summary>
/// Private target implementation for the admitted portable environment slice.
/// </summary>
public static class EnvironmentPal
{
	public static string GetNewLine() => "\n";

	public static int GetProcessorCount() => 1;
}
