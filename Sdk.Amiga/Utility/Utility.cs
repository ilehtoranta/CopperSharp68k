/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class Utility
{
	public const string Name = "utility.library";

	public static APTR UtilityLibraryBase
	{
		get => throw new System.NotSupportedException(
			"UtilityLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"UtilityLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(UtilityLvo.FindTagItem)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint FindTagItem(
		[M68kRegister(M68kRegister.D0)] uint tagValue,
		[M68kRegister(M68kRegister.A0)] uint tagList);

	[AmigaLvo(UtilityLvo.GetTagData)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetTagData(
		[M68kRegister(M68kRegister.D0)] uint tagValue,
		[M68kRegister(M68kRegister.D1)] uint defaultValue,
		[M68kRegister(M68kRegister.A0)] uint tagList);

	[AmigaLvo(UtilityLvo.PackBoolTags)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint PackBoolTags(
		[M68kRegister(M68kRegister.D0)] uint initialFlags,
		[M68kRegister(M68kRegister.A0)] uint tagList,
		[M68kRegister(M68kRegister.A1)] uint boolMap);

	[AmigaLvo(UtilityLvo.NextTagItem)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NextTagItem(
		[M68kRegister(M68kRegister.A0)] uint tagListPointer);

	[AmigaLvo(UtilityLvo.FilterTagChanges)]
	public static extern void FilterTagChanges(
		[M68kRegister(M68kRegister.A0)] uint changeList,
		[M68kRegister(M68kRegister.A1)] uint originalList,
		[M68kRegister(M68kRegister.D0)] uint apply);

	[AmigaLvo(UtilityLvo.MapTags)]
	public static extern void MapTags(
		[M68kRegister(M68kRegister.A0)] uint tagList,
		[M68kRegister(M68kRegister.A1)] uint mapList,
		[M68kRegister(M68kRegister.D0)] uint mapType);

	[AmigaLvo(UtilityLvo.AllocateTagItems)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocateTagItems(
		[M68kRegister(M68kRegister.D0)] uint numTags);

	[AmigaLvo(UtilityLvo.CloneTagItems)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CloneTagItems(
		[M68kRegister(M68kRegister.A0)] uint tagList);

	[AmigaLvo(UtilityLvo.FreeTagItems)]
	public static extern void FreeTagItems(
		[M68kRegister(M68kRegister.A0)] uint tagList);

	[AmigaLvo(UtilityLvo.RefreshTagItemClones)]
	public static extern void RefreshTagItemClones(
		[M68kRegister(M68kRegister.A0)] uint clone,
		[M68kRegister(M68kRegister.A1)] uint original);

	[AmigaLvo(UtilityLvo.TagInArray)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int TagInArray(
		[M68kRegister(M68kRegister.D0)] uint tagValue,
		[M68kRegister(M68kRegister.A0)] uint tagArray);

	[AmigaLvo(UtilityLvo.FilterTagItems)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint FilterTagItems(
		[M68kRegister(M68kRegister.A0)] uint tagList,
		[M68kRegister(M68kRegister.A1)] uint filterArray,
		[M68kRegister(M68kRegister.D0)] uint logic);

	[AmigaLvo(UtilityLvo.CallHookPkt)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CallHookPkt(
		[M68kRegister(M68kRegister.A0)] uint hook,
		[M68kRegister(M68kRegister.A2)] uint obj,
		[M68kRegister(M68kRegister.A1)] uint message);

	[AmigaLvo(UtilityLvo.Amiga2Date)]
	public static extern void Amiga2Date(
		[M68kRegister(M68kRegister.D0)] uint seconds,
		[M68kRegister(M68kRegister.A0)] uint clockData);

	[AmigaLvo(UtilityLvo.Date2Amiga)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint Date2Amiga(
		[M68kRegister(M68kRegister.A0)] uint clockData);

	[AmigaLvo(UtilityLvo.CheckDate)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CheckDate(
		[M68kRegister(M68kRegister.A0)] uint clockData);

	[AmigaLvo(UtilityLvo.SMult32)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SMult32(
		[M68kRegister(M68kRegister.D0)] int arg0,
		[M68kRegister(M68kRegister.D1)] int arg1);

	[AmigaLvo(UtilityLvo.UMult32)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint UMult32(
		[M68kRegister(M68kRegister.D0)] uint arg0,
		[M68kRegister(M68kRegister.D1)] uint arg1);

	[AmigaLvo(UtilityLvo.SDivMod32)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SDivMod32(
		[M68kRegister(M68kRegister.D0)] int dividend,
		[M68kRegister(M68kRegister.D1)] int divisor);

	[AmigaLvo(UtilityLvo.UDivMod32)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint UDivMod32(
		[M68kRegister(M68kRegister.D0)] uint dividend,
		[M68kRegister(M68kRegister.D1)] uint divisor);

	[AmigaLvo(UtilityLvo.Stricmp)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Stricmp(
		[M68kRegister(M68kRegister.A0)] uint string1,
		[M68kRegister(M68kRegister.A1)] uint string2);

	[AmigaLvo(UtilityLvo.Strnicmp)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Strnicmp(
		[M68kRegister(M68kRegister.A0)] uint string1,
		[M68kRegister(M68kRegister.A1)] uint string2,
		[M68kRegister(M68kRegister.D0)] int length);

	[AmigaLvo(UtilityLvo.ToUpper)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern byte ToUpper(
		[M68kRegister(M68kRegister.D0)] uint character);

	[AmigaLvo(UtilityLvo.ToLower)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern byte ToLower(
		[M68kRegister(M68kRegister.D0)] uint character);

	[AmigaLvo(UtilityLvo.ApplyTagChanges)]
	public static extern void ApplyTagChanges(
		[M68kRegister(M68kRegister.A0)] uint list,
		[M68kRegister(M68kRegister.A1)] uint changeList);

	[AmigaLvo(UtilityLvo.SMult64)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SMult64(
		[M68kRegister(M68kRegister.D0)] int arg0,
		[M68kRegister(M68kRegister.D1)] int arg1);

	[AmigaLvo(UtilityLvo.UMult64)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint UMult64(
		[M68kRegister(M68kRegister.D0)] uint arg0,
		[M68kRegister(M68kRegister.D1)] uint arg1);

	[AmigaLvo(UtilityLvo.PackStructureTags)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint PackStructureTags(
		[M68kRegister(M68kRegister.A0)] uint pack,
		[M68kRegister(M68kRegister.A1)] uint packTable,
		[M68kRegister(M68kRegister.A2)] uint tagList);

	[AmigaLvo(UtilityLvo.UnpackStructureTags)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint UnpackStructureTags(
		[M68kRegister(M68kRegister.A0)] uint pack,
		[M68kRegister(M68kRegister.A1)] uint packTable,
		[M68kRegister(M68kRegister.A2)] uint tagList);

	// MorphOS m68k ABI named-object call.
	[AmigaLvo(UtilityLvo.AddNamedObject)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AddNamedObject(
		[M68kRegister(M68kRegister.A0)] uint nameSpace,
		[M68kRegister(M68kRegister.A1)] uint namedObject);

	// MorphOS m68k ABI named-object call.
	[AmigaLvo(UtilityLvo.AllocNamedObjectA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocNamedObjectA(
		[M68kRegister(M68kRegister.A0)] CString name,
		[M68kRegister(M68kRegister.A1)] uint tags);

	// MorphOS m68k ABI named-object call.
	[AmigaLvo(UtilityLvo.AttemptRemNamedObject)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AttemptRemNamedObject(
		[M68kRegister(M68kRegister.A0)] uint namedObject);

	// MorphOS m68k ABI named-object call.
	[AmigaLvo(UtilityLvo.FindNamedObject)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint FindNamedObject(
		[M68kRegister(M68kRegister.A0)] uint nameSpace,
		[M68kRegister(M68kRegister.A1)] CString name,
		[M68kRegister(M68kRegister.A2)] uint lastObject);

	// MorphOS m68k ABI named-object call.
	[AmigaLvo(UtilityLvo.FreeNamedObject)]
	public static extern void FreeNamedObject(
		[M68kRegister(M68kRegister.A0)] uint namedObject);

	// MorphOS m68k ABI named-object call.
	[AmigaLvo(UtilityLvo.NamedObjectName)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NamedObjectName(
		[M68kRegister(M68kRegister.A0)] uint namedObject);

	// MorphOS m68k ABI named-object call.
	[AmigaLvo(UtilityLvo.ReleaseNamedObject)]
	public static extern void ReleaseNamedObject(
		[M68kRegister(M68kRegister.A0)] uint namedObject);

	// MorphOS m68k ABI named-object call.
	[AmigaLvo(UtilityLvo.RemNamedObject)]
	public static extern void RemNamedObject(
		[M68kRegister(M68kRegister.A0)] uint namedObject,
		[M68kRegister(M68kRegister.A1)] uint message);

	// MorphOS m68k ABI call.
	[AmigaLvo(UtilityLvo.GetUniqueID)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetUniqueID();

	public static uint AllocNamedObject(CString name, uint tags) =>
		AllocNamedObjectA(name, tags);
}
