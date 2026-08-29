/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

namespace Amiga;

/// <summary>Classic V40 pointerclass attributes and resolution values.</summary>
public static class IntuitionPointerClass
{
	public const uint POINTERA_Dummy = ExecConstants.TagUser + 0x0003_9000u;
	public const uint POINTERA_BitMap = POINTERA_Dummy + 1u;
	public const uint POINTERA_XOffset = POINTERA_Dummy + 2u;
	public const uint POINTERA_YOffset = POINTERA_Dummy + 3u;
	public const uint POINTERA_WordWidth = POINTERA_Dummy + 4u;
	public const uint POINTERA_XResolution = POINTERA_Dummy + 5u;
	public const uint POINTERA_YResolution = POINTERA_Dummy + 6u;

	public const uint POINTERXRESN_DEFAULT = 0u;
	public const uint POINTERXRESN_140NS = 1u;
	public const uint POINTERXRESN_70NS = 2u;
	public const uint POINTERXRESN_35NS = 3u;
	public const uint POINTERXRESN_SCREENRES = 4u;
	public const uint POINTERXRESN_LORES = 5u;
	public const uint POINTERXRESN_HIRES = 6u;

	public const uint POINTERYRESN_DEFAULT = 0u;
	public const uint POINTERYRESN_HIGH = 2u;
	public const uint POINTERYRESN_HIGHASPECT = 3u;
	public const uint POINTERYRESN_SCREENRES = 4u;
	public const uint POINTERYRESN_SCREENRESASPECT = 5u;
}
