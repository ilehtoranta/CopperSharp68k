using Amiga;
using Copper68k.Compiler;

namespace MuiSunflower;

public static class Program
{
	[M68kEntryPoint]
	public static uint Main()
	{
		var title = Guest.CString("MUI Sunflower");
		var button = MuiButton("Grow");
		var label = MuiText("A tiny MUI window from Copper68k.AmigaSdk.");

		var group = MuiObject(MuiClass.Group, Guest.U32Array5(
			MuiTag.GroupChild, label,
			MuiTag.GroupChild, button,
			MuiTag.Done));

		var window = MuiObject(MuiClass.Window, Guest.U32Array5(
			MuiTag.WindowTitle, title,
			MuiTag.WindowRootObject, group,
			MuiTag.Done));

		return MuiObject(MuiClass.Application, Guest.U32Array13(
			MuiTag.ApplicationAuthor, Guest.CString("Copper68k"),
			MuiTag.ApplicationBase, Guest.CString("SUNFLOWER"),
			MuiTag.ApplicationDescription, Guest.CString("Simple MUI window and button example."),
			MuiTag.ApplicationTitle, title,
			MuiTag.ApplicationVersion, Guest.CString("$VER: MuiSunflower 1.0"),
			MuiTag.ApplicationWindow, window,
			MuiTag.Done));
	}

	private static uint MuiText(string contents) =>
		MuiObject(MuiClass.Text, Guest.U32Array3(
			MuiTag.TextContents, Guest.CString(contents),
			MuiTag.Done));

	private static uint MuiButton(string label) =>
		MuiMaster.MUI_MakeObject(MuiObjectType.Button, Guest.U32Array1(Guest.CString(label)));

	private static uint MuiObject(string className, uint tags) =>
		MuiMaster.MUI_NewObject(Guest.CString(className), tags);
}

public static class MuiClass
{
	public const string Application = "Application.mui";
	public const string Window = "Window.mui";
	public const string Group = "Group.mui";
	public const string Text = "Text.mui";
}

public static class MuiObjectType
{
	public const int Button = 2;
}

public static class MuiTag
{
	public const uint Done = 0;
	public const uint ApplicationAuthor = 0x80424842;
	public const uint ApplicationBase = 0x8042e07a;
	public const uint ApplicationDescription = 0x80421fc6;
	public const uint ApplicationTitle = 0x804281b8;
	public const uint ApplicationVersion = 0x8042b33f;
	public const uint ApplicationWindow = 0x8042bfe0;
	public const uint WindowTitle = 0x8042ad3d;
	public const uint WindowRootObject = 0x8042cba5;
	public const uint GroupChild = 0x804226e6;
	public const uint TextContents = 0x8042f8dc;
}

public static class Guest
{
	[M68kImport("examples.cstring")]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CString(
		[M68kRegister(M68kRegister.A0)] string value);

	[M68kImport("examples.u32array1")]
	public static extern uint U32Array1(uint value0);

	[M68kImport("examples.u32array3")]
	public static extern uint U32Array3(uint value0, uint value1, uint value2);

	[M68kImport("examples.u32array5")]
	public static extern uint U32Array5(
		uint value0,
		uint value1,
		uint value2,
		uint value3,
		uint value4);

	[M68kImport("examples.u32array13")]
	public static extern uint U32Array13(
		uint value0,
		uint value1,
		uint value2,
		uint value3,
		uint value4,
		uint value5,
		uint value6,
		uint value7,
		uint value8,
		uint value9,
		uint value10,
		uint value11,
		uint value12);
}
