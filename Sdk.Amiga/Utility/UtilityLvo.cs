/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public static class UtilityLvo
{
	public const short FindTagItem = -30, GetTagData = -36, PackBoolTags = -42, NextTagItem = -48;
	public const short FilterTagChanges = -54, MapTags = -60, AllocateTagItems = -66, CloneTagItems = -72;
	public const short FreeTagItems = -78, RefreshTagItemClones = -84, TagInArray = -90, FilterTagItems = -96;
	public const short CallHookPkt = -102, Amiga2Date = -120, Date2Amiga = -126, CheckDate = -132;
	public const short SMult32 = -138, UMult32 = -144, SDivMod32 = -150, UDivMod32 = -156;
	public const short Stricmp = -162, Strnicmp = -168, ToUpper = -174, ToLower = -180, ApplyTagChanges = -186;
	public const short SMult64 = -198, UMult64 = -204, PackStructureTags = -210, UnpackStructureTags = -216;
	public const short AddNamedObject = -222, AllocNamedObjectA = -228, AttemptRemNamedObject = -234;
	public const short FindNamedObject = -240, FreeNamedObject = -246, NamedObjectName = -252;
	public const short ReleaseNamedObject = -258, RemNamedObject = -264, GetUniqueID = -270;
}
