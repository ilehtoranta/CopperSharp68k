/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using Amiga;
using Amiga.MUI;
using CopperSharp.Compiler;

namespace MUITaskList;

public static class Program
{
	private const uint MEMF_PUBLIC = 0x0000_0001u;
	private const uint MEMF_CLEAR = 0x0001_0000u;
	private const uint RefreshMethod = 0xFEC8_0001u;

	private static uint _entryWorkbench;
	private static uint _entryInput;
	private static uint _entryIdle;
	private static uint _refreshCount;

	[M68kEntryPoint]
	public static unsafe uint Main()
	{
		var displayHook = Exec.AllocMem(Hook.Size, MEMF_PUBLIC | MEMF_CLEAR);
		if (displayHook == 0)
		{
			return 20;
		}

		WriteHook(displayHook, APTR.ExportAddress("muitasklist.list.display"));
		_entryWorkbench = AllocTaskEntry("Workbench", "Ready", "0");
		_entryInput = AllocTaskEntry("input.device", "Waiting", "20");
		_entryIdle = AllocTaskEntry("Idle", "Sleeping", "-128");

		if (_entryWorkbench == 0 || _entryInput == 0 || _entryIdle == 0)
		{
			FreeTaskEntries(displayHook);
			return 20;
		}

		var appClass = MUIMaster.MUI_CreateCustomClass(
			0,
			CString.FromLiteral(Application.Name),
			0,
			0,
			APTR.ExportAddress("muitasklist.app.dispatcher"));
		if (appClass == 0)
		{
			FreeTaskEntries(displayHook);
			return 20;
		}

		var listClass = CString.FromLiteral(List.Name);
		var listFormatTag = List.Format;
		var listFormat = CString.FromLiteral("BAR,WEIGHT=50,BAR,WEIGHT=30,WEIGHT=20");
		var listTitleTag = List.Title;
		var listTitle = One();
		var listDisplayHookTag = List.DisplayHook;
		var done = Tag.Done;
		var list = MUIMaster.MUI_NewObject(
			listClass,
			listFormatTag, listFormat,
			listTitleTag, listTitle,
			listDisplayHookTag, displayHook,
			done);

		var listViewClass = CString.FromLiteral(Listview.Name);
		var listViewListTag = Listview.List;
		var listView = MUIMaster.MUI_NewObject(
			listViewClass,
			listViewListTag, list,
			done);

		var refreshButton = MUIMaster.MUI_MakeObject(MakeObject.Button, CString.FromLiteral("Refresh"));
		var closeButton = MUIMaster.MUI_MakeObject(MakeObject.Button, CString.FromLiteral("Close"));

		var groupClass = CString.FromLiteral(Group.Name);
		var groupChild = Group.Child;
		var group = MUIMaster.MUI_NewObject(
			groupClass,
			groupChild, listView,
			groupChild, refreshButton,
			groupChild, closeButton,
			done);

		var windowClass = CString.FromLiteral(Window.Name);
		var windowTitleTag = Window.Title;
		var windowTitle = CString.FromLiteral("MUI Task List");
		var windowRootTag = Window.RootObject;
		var window = MUIMaster.MUI_NewObject(
			windowClass,
			windowTitleTag, windowTitle,
			windowRootTag, group,
			done);

		var appAuthor = Application.Author;
		var appAuthorValue = CString.FromLiteral("CopperSharp68k");
		var appBase = Application.Base;
		var appBaseValue = CString.FromLiteral("CSHPTASKLIST");
		var appDescription = Application.Description;
		var appDescriptionValue = CString.FromLiteral("MUI subclass and hook example.");
		var appTitle = Application.Title;
		var appTitleValue = CString.FromLiteral("MUI Task List");
		var appVersion = Application.Version;
		var appVersionValue = CString.FromLiteral("$VER: MUITaskList 1.0");
		var appWindow = Application.Window;
		var appClassPtr = ReadLong(appClass, 0);
		var app = Intuition.NewObject(
			appClassPtr,
			0,
			appAuthor, appAuthorValue,
			appBase, appBaseValue,
			appDescription, appDescriptionValue,
			appTitle, appTitleValue,
			appVersion, appVersionValue,
			appWindow, window,
			done);
		if (app == 0)
		{
			MUIMaster.MUI_DeleteCustomClass(appClass);
			FreeTaskEntries(displayHook);
			return 20;
		}

		PopulateList(list);
		ConnectCloseRequest(app, window);
		BOOPSI.DoMethod(
			closeButton,
			Notify.Method,
			global::Amiga.MUI.Attribute.Pressed,
			(uint)Value.EveryTime,
			app,
			2,
			Application.Method.ReturnID,
			0xffff_ffffu);
		BOOPSI.DoMethod(
			refreshButton,
			Notify.Method,
			global::Amiga.MUI.Attribute.Pressed,
			(uint)Value.EveryTime,
			app,
			2,
			RefreshMethod,
			list);

		BOOPSI.DoMethod(window, Method.Set, Window.Open, 1);
		var result = BOOPSI.DoMethod(app, Application.Method.Run);
		MUIMaster.MUI_DisposeObject(app);
		MUIMaster.MUI_DeleteCustomClass(appClass);
		FreeTaskEntries(displayHook);
		return result;
	}

	private static uint One() => 1;

	private static void ConnectCloseRequest(uint app, uint window) =>
		BOOPSI.DoMethod(
			window,
			Notify.Method,
			Window.CloseRequest,
			(uint)Value.EveryTime,
			app,
			2,
			Application.Method.ReturnID,
			0xffff_ffffu);

	[M68kExport("muitasklist.app.dispatcher")]
	[return: M68kRegister(M68kRegister.D0)]
	public static unsafe uint AppDispatcher(
		[M68kRegister(M68kRegister.A0)] uint cl,
		[M68kRegister(M68kRegister.A2)] uint obj,
		[M68kRegister(M68kRegister.A1)] uint message)
	{
		if (ReadLong(message, 0) == RefreshMethod)
		{
			PopulateList(ReadLong(message, 4));
			return ++_refreshCount;
		}

		return Native.DoSuperMethodA(cl, obj, message);
	}

	[M68kExport("muitasklist.list.display")]
	[return: M68kRegister(M68kRegister.D0)]
	public static unsafe uint ListDisplay(
		[M68kRegister(M68kRegister.A1)] uint entry,
		[M68kRegister(M68kRegister.A2)] uint columns)
	{
		if (entry == 0)
		{
			WriteLong(columns, 0, CString.FromLiteral("Task"));
			WriteLong(columns, 4, CString.FromLiteral("State"));
			WriteLong(columns, 8, CString.FromLiteral("Pri"));
			return 0;
		}

		WriteLong(columns, 0, ReadLong(entry, 0));
		WriteLong(columns, 4, ReadLong(entry, 4));
		WriteLong(columns, 8, ReadLong(entry, 8));
		return 0;
	}

	private static void PopulateList(uint list)
	{
		BOOPSI.DoMethod(list, List.Method.Clear);
		BOOPSI.DoMethod(list, List.Method.InsertSingle, _entryWorkbench, unchecked((uint)List.Value.Insert.Bottom));
		BOOPSI.DoMethod(list, List.Method.InsertSingle, _entryInput, unchecked((uint)List.Value.Insert.Bottom));
		BOOPSI.DoMethod(list, List.Method.InsertSingle, _entryIdle, unchecked((uint)List.Value.Insert.Bottom));
	}

	private static uint AllocTaskEntry(CString name, CString state, CString priority)
	{
		var entry = Exec.AllocMem(12, MEMF_PUBLIC | MEMF_CLEAR);
		if (entry == 0)
		{
			return 0;
		}

		WriteLong(entry, 0, name);
		WriteLong(entry, 4, state);
		WriteLong(entry, 8, priority);
		return entry;
	}

	private static void FreeTaskEntries(uint displayHook)
	{
		if (_entryWorkbench != 0)
		{
			Exec.FreeMem(_entryWorkbench, 12);
			_entryWorkbench = 0;
		}
		if (_entryInput != 0)
		{
			Exec.FreeMem(_entryInput, 12);
			_entryInput = 0;
		}
		if (_entryIdle != 0)
		{
			Exec.FreeMem(_entryIdle, 12);
			_entryIdle = 0;
		}
		if (displayHook != 0)
		{
			Exec.FreeMem(displayHook, Hook.Size);
		}
	}

	private static void WriteHook(uint hook, APTR entry)
	{
		WriteLong(hook, 0, 0);
		WriteLong(hook, 4, 0);
		WriteLong(hook, 8, entry);
		WriteLong(hook, 12, 0);
		WriteLong(hook, 16, 0);
	}

	private static unsafe uint ReadLong(uint address, int offset) =>
		*(uint*)(address + (uint)offset);

	private static unsafe void WriteLong(uint address, int offset, uint value) =>
		*(uint*)(address + (uint)offset) = value;

	private static class Native
	{
		[M68kImport("amiga.boopsi.DoSuperMethodA")]
		[return: M68kRegister(M68kRegister.D0)]
		public static extern uint DoSuperMethodA(
			[M68kRegister(M68kRegister.A0)] uint cl,
			[M68kRegister(M68kRegister.A2)] uint obj,
			[M68kRegister(M68kRegister.A1)] uint message);
	}
}
