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
			Application.Name,
			0,
			TaskListApplication.Data.SizeInBytes,
			APTR.ExportAddress("muitasklist.app.dispatcher"));
		if (appClass == 0)
		{
			FreeTaskEntries();
			return 20;
		}

		var list = MUIMaster.MUI_NewObject(
			Amiga.MUI.List.Name,
			Amiga.MUI.List.Format, "BAR,WEIGHT=50,BAR,WEIGHT=30,WEIGHT=20",
			Amiga.MUI.List.Title, One(),
			Amiga.MUI.List.DisplayHook, Hook.AddressOf(ref _displayHook),
			Tag.Done);

		var listView = MUIMaster.MUI_NewObject(
			Listview.Name,
			Listview.List, list,
			Tag.Done);

		var refreshButton = MUIMaster.MUI_MakeObject(MakeObject.Button, "Refresh");
		var closeButton = MUIMaster.MUI_MakeObject(MakeObject.Button, "Close");

		var group = MUIMaster.MUI_NewObject(
			Group.Name,
			Group.Child, listView,
			Group.Child, refreshButton,
			Group.Child, closeButton,
			Tag.Done);

		var window = MUIMaster.MUI_NewObject(
			Amiga.MUI.Window.Name,
			Amiga.MUI.Window.Title, "MUI Task List",
			Amiga.MUI.Window.RootObject, group,
			Tag.Done);

		ref var appClassHeader = ref CustomClassHeader.FromAddress(APTR.FromPointer(appClass));
		var app = Intuition.NewObject(
			appClassHeader.Class,
			0,
			Application.Author, "CopperSharp68k",
			Application.Base, "CSHPTASKLIST",
			Application.Description, "MUI subclass and hook example.",
			Application.Title, "MUI Task List",
			Application.Version, "$VER: MUITaskList 1.0",
			Application.Window, window,
			Tag.Done);
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

		BOOPSI.DoMethod(window, Method.Set, Amiga.MUI.Window.Open, 1);
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
			Amiga.MUI.Window.CloseRequest,
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

		return BOOPSI.DoSuperMethodA(cl, obj, messageAddress);
	}

	[Amiga.MUI.List.DisplayCallback("muitasklist.list.display")]
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
			output.Task = "Task";
			output.State = "State";
			output.Priority = "Pri";
			return;
		}

		output.Task = row.Name;
		output.State = row.State;
		output.Priority = row.Priority;
	}

	private static void PopulateList(uint list)
	{
		BOOPSI.DoMethod(list, Amiga.MUI.List.Method.Clear);
		BOOPSI.DoMethod(list, Amiga.MUI.List.Method.InsertSingle, _entryWorkbench, unchecked((uint)Amiga.MUI.List.Value.Insert.Bottom));
		BOOPSI.DoMethod(list, Amiga.MUI.List.Method.InsertSingle, _entryInput, unchecked((uint)Amiga.MUI.List.Value.Insert.Bottom));
		BOOPSI.DoMethod(list, Amiga.MUI.List.Method.InsertSingle, _entryIdle, unchecked((uint)Amiga.MUI.List.Value.Insert.Bottom));
	}

	private static uint AllocTaskEntry(CString name, CString state, CString priority)
	{
		var address = APTR.FromPointer(Exec.AllocMem(TaskListEntry.Size, Exec.MemoryFlags.Clear));
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
}
