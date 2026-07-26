/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using Amiga;
using Amiga.MUI;
using CopperSharp.Compiler;

namespace MUISunflower;

public static class Program
{
	[M68kEntryPoint]
	public static uint Main()
	{
		var title = CString.FromLiteral("MUI Sunflower");
		var button = MUIButton("Grow");
		var label = MUIText("A tiny MUI window from CopperSharp.Sdk.Amiga.");

		var group = Group.New(Guest.U32Array5(
			Group.Child, label,
			Group.Child, button,
			Tag.Done));

		var window = WindowObject.New(Guest.U32Array5(
			Window.Title, title,
			Window.RootObject, group,
			Tag.Done));

		return ApplicationObject.New(Guest.U32Array13(
			Application.Author, CString.FromLiteral("Copper68k"),
			Application.Base, CString.FromLiteral("SUNFLOWER"),
			Application.Description, CString.FromLiteral("Simple MUI window and button example."),
			Application.Title, title,
			Application.Version, CString.FromLiteral("$VER: MUISunflower 1.0"),
			Application.Window, window,
			Tag.Done));
	}

	private static uint MUIText(CString contents) =>
		Text.New(Guest.U32Array3(
			Text.Contents, contents,
			Tag.Done));

	private static uint MUIButton(CString label) =>
		MUIMaster.MUI_MakeObject(MakeObject.Button, Guest.U32Array1(label));
}

public static class Guest
{
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
