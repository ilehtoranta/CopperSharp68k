/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

namespace Amiga;

/// <summary>Classic interconnection/model class methods and attributes.</summary>
public static class IntuitionInterconnectionClass
{
	public const uint ICM_Dummy = 0x0401u;
	public const uint ICM_SETLOOP = 0x0402u;
	public const uint ICM_CLEARLOOP = 0x0403u;
	public const uint ICM_CHECKLOOP = 0x0404u;
	public const uint ICA_Dummy = ExecConstants.TagUser + 0x0004_0000u;
	public const uint ICA_TARGET = ICA_Dummy + 1u;
	public const uint ICA_MAP = ICA_Dummy + 2u;
	public const uint ICSPECIAL_CODE = ICA_Dummy + 3u;
	public const uint ICTARGET_IDCMP = uint.MaxValue;
}
