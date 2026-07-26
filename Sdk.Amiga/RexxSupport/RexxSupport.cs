/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;

namespace Amiga;

[AmigaLibrary(Name)]
public static class RexxSupport
{
	public const string Name = "rexxsupport.library";

	// rexxsupport.library is an ARexx external function library loaded with
	// ADDLIB/rxlib at entry -30, not a public C-style library vector surface.
	// No MorphOS ppcinline m68k register mapping is published for it.
}
