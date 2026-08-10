using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

namespace Amiga;

[AmigaLibrary(Name)]
public static class Icon
{
	public const string Name = "icon.library";

	public static APTR IconLibraryBase
	{
		get => throw new System.NotSupportedException(
			"IconLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"IconLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(IconLvo.FreeFreeList)]
	public static extern void FreeFreeList(
		[M68kRegister(M68kRegister.A0)] uint freeList);

	[AmigaLvo(IconLvo.AddFreeList)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AddFreeList(
		[M68kRegister(M68kRegister.A0)] uint freeList,
		[M68kRegister(M68kRegister.A1)] uint memory,
		[M68kRegister(M68kRegister.A2)] uint size);

	[AmigaLvo(IconLvo.GetDiskObject)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetDiskObject(
		[M68kRegister(M68kRegister.A0)] CString name);

	[AmigaLvo(IconLvo.PutDiskObject)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int PutDiskObject(
		[M68kRegister(M68kRegister.A0)] CString name,
		[M68kRegister(M68kRegister.A1)] uint diskObject);

	[AmigaLvo(IconLvo.FreeDiskObject)]
	public static extern void FreeDiskObject(
		[M68kRegister(M68kRegister.A0)] uint diskObject);

	[AmigaLvo(IconLvo.FindToolType)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint FindToolType(
		[M68kRegister(M68kRegister.A0)] uint toolTypeArray,
		[M68kRegister(M68kRegister.A1)] CString typeName);

	[AmigaLvo(IconLvo.MatchToolValue)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MatchToolValue(
		[M68kRegister(M68kRegister.A0)] CString typeString,
		[M68kRegister(M68kRegister.A1)] CString value);

	[AmigaLvo(IconLvo.BumpRevision)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint BumpRevision(
		[M68kRegister(M68kRegister.A0)] CString newName,
		[M68kRegister(M68kRegister.A1)] CString oldName);

	[AmigaLvo(IconLvo.GetDefDiskObject)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetDefDiskObject(
		[M68kRegister(M68kRegister.D0)] int type);

	[AmigaLvo(IconLvo.PutDefDiskObject)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int PutDefDiskObject(
		[M68kRegister(M68kRegister.A0)] uint diskObject);

	[AmigaLvo(IconLvo.GetDiskObjectNew)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetDiskObjectNew(
		[M68kRegister(M68kRegister.A0)] CString name);

	[AmigaLvo(IconLvo.DeleteDiskObject)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int DeleteDiskObject(
		[M68kRegister(M68kRegister.A0)] CString name);

	// V44 tag-list vectors. MorphOS exposes these through its M68k-compatible icon.library ABI.
	[AmigaLvo(IconLvo.DupDiskObjectA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint DupDiskObjectA(
		[M68kRegister(M68kRegister.A0)] uint diskObject,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(IconLvo.IconControlA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IconControlA(
		[M68kRegister(M68kRegister.A0)] uint icon,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(IconLvo.DrawIconStateA)]
	public static extern void DrawIconStateA(
		[M68kRegister(M68kRegister.A0)] uint rastPort,
		[M68kRegister(M68kRegister.A1)] uint icon,
		[M68kRegister(M68kRegister.A2)] uint label,
		[M68kRegister(M68kRegister.D0)] int leftOffset,
		[M68kRegister(M68kRegister.D1)] int topOffset,
		[M68kRegister(M68kRegister.D2)] uint state,
		[M68kRegister(M68kRegister.A3)] uint tags);

	[AmigaLvo(IconLvo.GetIconRectangleA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetIconRectangleA(
		[M68kRegister(M68kRegister.A0)] uint rastPort,
		[M68kRegister(M68kRegister.A1)] uint icon,
		[M68kRegister(M68kRegister.A2)] uint label,
		[M68kRegister(M68kRegister.A3)] uint rectangle,
		[M68kRegister(M68kRegister.A4)] uint tags);

	[AmigaLvo(IconLvo.NewDiskObject)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewDiskObject(
		[M68kRegister(M68kRegister.D0)] int type);

	[AmigaLvo(IconLvo.GetIconTagList)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetIconTagList(
		[M68kRegister(M68kRegister.A0)] CString name,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(IconLvo.PutIconTagList)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int PutIconTagList(
		[M68kRegister(M68kRegister.A0)] CString name,
		[M68kRegister(M68kRegister.A1)] uint icon,
		[M68kRegister(M68kRegister.A2)] uint tags);

	[AmigaLvo(IconLvo.LayoutIconA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int LayoutIconA(
		[M68kRegister(M68kRegister.A0)] uint icon,
		[M68kRegister(M68kRegister.A1)] uint screen,
		[M68kRegister(M68kRegister.A2)] uint tags);

	[AmigaLvo(IconLvo.ChangeToSelectedIconColor)]
	public static extern void ChangeToSelectedIconColor(
		[M68kRegister(M68kRegister.A0)] uint colorRegister);
}
