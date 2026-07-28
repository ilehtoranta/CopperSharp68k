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
	private const uint MEMF_CLEAR = 0x0001_0000u;

	private static Hook _displayHook;
	private static uint _entryWorkbench;
	private static uint _entryInput;
	private static uint _entryIdle;

	private struct CustomClassHeader
	{
		public uint Class;

		public static ref CustomClassHeader FromAddress(APTR address) =>
			throw new System.NotSupportedException(
				"CustomClassHeader.FromAddress is lowered by CopperSharp.");
	}

	private struct TaskListEntry
	{
		public const uint Size = 12;

		public CString Name;
		public CString State;
		public CString Priority;

		public static ref TaskListEntry FromAddress(APTR address) =>
			throw new System.NotSupportedException(
				"TaskListEntry.FromAddress is lowered by CopperSharp.");
	}

	private struct ListDisplayColumns
	{
		public CString Task;
		public CString State;
		public CString Priority;

		public static ref ListDisplayColumns FromAddress(APTR address) =>
			throw new System.NotSupportedException(
				"ListDisplayColumns.FromAddress is lowered by CopperSharp.");
	}

	public static class TaskListApplication
	{
		public struct Data
		{
			public const int SizeInBytes = 8;

			public uint RefreshCount;
			public uint List;

			public static ref Data FromAddress(APTR address) =>
				throw new System.NotSupportedException(
					"TaskListApplication.Data.FromAddress is lowered by CopperSharp.");
		}

		public static class Method
		{
			public const uint Refresh = 0xFEC8_0001u;
		}

		public struct RefreshMessage
		{
			public uint MethodID;
			public uint List;

			public static ref RefreshMessage Cast(ref BOOPSI.Message message) =>
				throw new System.NotSupportedException(
					"TaskListApplication.RefreshMessage.Cast is lowered by CopperSharp.");
		}
	}

	[M68kEntryPoint]
	public static uint Main()
	{
		WriteHook(ref _displayHook, APTR.ExportAddress("muitasklist.list.display"));
		var displayHook = Hook.AddressOf(ref _displayHook);
		_entryWorkbench = AllocTaskEntry("Workbench", "Ready", "0");
		_entryInput = AllocTaskEntry("input.device", "Waiting", "20");
		_entryIdle = AllocTaskEntry("Idle", "Sleeping", "-128");

		if (_entryWorkbench == 0 || _entryInput == 0 || _entryIdle == 0)
		{
			FreeTaskEntries();
			return 20;
		}

		var appClass = MUIMaster.MUI_CreateCustomClass(
			0,
			CString.FromLiteral(Application.Name),
			0,
			TaskListApplication.Data.SizeInBytes,
			APTR.ExportAddress("muitasklist.app.dispatcher"));
		if (appClass == 0)
		{
			FreeTaskEntries();
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
		ref var appClassHeader = ref CustomClassHeader.FromAddress(APTR.FromPointer(appClass));
		var appClassPtr = appClassHeader.Class;
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
			FreeTaskEntries();
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
			TaskListApplication.Method.Refresh,
			list);

		BOOPSI.DoMethod(window, Method.Set, Window.Open, 1);
		var result = BOOPSI.DoMethod(app, Application.Method.Run);
		MUIMaster.MUI_DisposeObject(app);
		MUIMaster.MUI_DeleteCustomClass(appClass);
		FreeTaskEntries();
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

	[BOOPSI.Dispatcher("muitasklist.app.dispatcher")]
	public static uint AppDispatcher(APTR cl, APTR obj, ref BOOPSI.Message message)
	{
		var messageAddress = BOOPSI.Message.AddressOf(ref message);
		if (message.MethodID == TaskListApplication.Method.Refresh)
		{
			var dataAddress = BOOPSI.InstanceData(cl, obj);
			ref var data = ref TaskListApplication.Data.FromAddress(dataAddress);
			ref var refresh = ref TaskListApplication.RefreshMessage.Cast(ref message);
			data.List = refresh.List;
			PopulateList(data.List);
			return ++data.RefreshCount;
		}

		return Native.DoSuperMethodA(cl, obj, messageAddress);
	}

	[List.DisplayCallback("muitasklist.list.display")]
	public static uint ListDisplay(uint entry, APTR columns)
	{
		var row = new TaskListDisplayRow(entry);
		ref var output = ref ListDisplayColumns.FromAddress(columns);
		WriteTaskRow(ref output, row);
		return 0;
	}

	private static void WriteTaskRow(ref ListDisplayColumns output, TaskListDisplayRow row)
	{
		if (row.IsTitle)
		{
			output.Task = CString.FromLiteral("Task");
			output.State = CString.FromLiteral("State");
			output.Priority = CString.FromLiteral("Pri");
			return;
		}

		output.Task = row.Name;
		output.State = row.State;
		output.Priority = row.Priority;
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
		var address = APTR.FromPointer(Exec.AllocMem(TaskListEntry.Size, MEMF_CLEAR));
		if (address.IsNull)
		{
			return 0;
		}

		ref var entry = ref TaskListEntry.FromAddress(address);
		entry.Name = name;
		entry.State = state;
		entry.Priority = priority;
		return address.Raw;
	}

	private static void FreeTaskEntries()
	{
		if (_entryWorkbench != 0)
		{
			Exec.FreeMem(_entryWorkbench, TaskListEntry.Size);
			_entryWorkbench = 0;
		}
		if (_entryInput != 0)
		{
			Exec.FreeMem(_entryInput, TaskListEntry.Size);
			_entryInput = 0;
		}
		if (_entryIdle != 0)
		{
			Exec.FreeMem(_entryIdle, TaskListEntry.Size);
			_entryIdle = 0;
		}
	}

	private static void WriteHook(ref Hook hook, APTR entry)
	{
		hook.MinNode.Successor = APTR.Null;
		hook.MinNode.Predecessor = APTR.Null;
		hook.Entry = entry;
		hook.SubEntry = APTR.Null;
		hook.Data = APTR.Null;
	}

	private readonly struct TaskListDisplayRow
	{
		private readonly uint _entry;

		public TaskListDisplayRow(uint entry)
		{
			_entry = entry;
		}

		public bool IsTitle => _entry == 0;

		public CString Name => TaskListEntry.FromAddress(APTR.FromPointer(_entry)).Name;

		public CString State => TaskListEntry.FromAddress(APTR.FromPointer(_entry)).State;

		public CString Priority => TaskListEntry.FromAddress(APTR.FromPointer(_entry)).Priority;
	}

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
