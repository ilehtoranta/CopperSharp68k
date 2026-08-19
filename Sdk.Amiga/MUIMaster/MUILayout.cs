/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

namespace Amiga.MUI;

public static partial class MUIConstants
{
	public const short MUI_MAXMAX = 10000;
	public const ushort MUIMASTER_VMIN = 20;
	public const ushort MUIMASTER_VLATEST = 20;
}

public static class MUIProfile
{
	public const string Name = "MUI20M68k";
	public const ushort MinimumVersion = MUIConstants.MUIMASTER_VMIN;
	public const ushort LatestVersion = MUIConstants.MUIMASTER_VLATEST;

	public static bool IsVersionAdmitted(ushort version) =>
		version >= MinimumVersion && version <= LatestVersion;
}
