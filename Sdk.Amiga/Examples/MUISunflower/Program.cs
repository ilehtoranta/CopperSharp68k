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

		var group = MUIMaster.MUI_NewObject(
			CString.FromLiteral(Group.Name),
			Group.Child, label,
			Group.Child, button,
			Tag.Done);

		var window = new WindowObject(MUIMaster.MUI_NewObject(
			CString.FromLiteral(Window.Name),
			Window.Title, title,
			Window.RootObject, group,
			Tag.Done));

		var app = new ApplicationObject(MUIMaster.MUI_NewObject(
			CString.FromLiteral(Application.Name),
			Application.Author, CString.FromLiteral("CopperSharp68k"),
			Application.Base, CString.FromLiteral("SUNFLOWER"),
			Application.Description, CString.FromLiteral("Simple MUI window and button example."),
			Application.Title, title,
			Application.Version, CString.FromLiteral("$VER: MUISunflower 1.0"),
			Application.Window, window,
			Tag.Done));

		app.ConnectCloseRequest(window);
		window.SetOpen(true);

        var result = app.Run();
		app.Dispose();
		return result;
	}

	private static uint MUIText(CString contents) =>
		MUIMaster.MUI_NewObject(
			CString.FromLiteral(Text.Name),
			Text.Contents, contents,
			Tag.Done);

	private static uint MUIButton(CString label) =>
		MUIMaster.MUI_MakeObject(MakeObject.Button, label);
}
