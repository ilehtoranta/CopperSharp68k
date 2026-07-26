/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;

namespace Amiga;

[AmigaLibrary(Name)]
public static class Version
{
	public const string Name = "version.library";

	// version.library exposes version metadata rather than public callable
	// SDK vectors. No MorphOS ppcinline m68k register mapping is published for
	// this library.
}
