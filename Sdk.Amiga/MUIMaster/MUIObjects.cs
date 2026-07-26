/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga.MUI;

public readonly struct MUIObject
{
	public MUIObject(uint raw)
	{
		Raw = raw;
	}

	public uint Raw { get; }

	public bool IsNull => Raw == 0;

	public uint DoMethod(uint message) =>
		global::Amiga.BOOPSI.DoMethodA(Raw, message);

	public uint SetAttrs(uint tags) =>
		global::Amiga.Intuition.SetAttrsA(Raw, tags);

	public void Dispose() =>
		global::Amiga.MUIMaster.MUI_DisposeObject(Raw);

	public static implicit operator uint(MUIObject value) => value.Raw;

	public static explicit operator MUIObject(uint value) => new(value);
}

public readonly struct ApplicationObject
{
	public ApplicationObject(uint raw)
	{
		Raw = raw;
	}

	public uint Raw { get; }

	public bool IsNull => Raw == 0;

	public static ApplicationObject New(uint tags) =>
		new(Application.New(tags));

	public static uint Run(uint app) =>
		global::Amiga.BOOPSI.DoMethod(app, Application.Method.Run);

	public static void Dispose(uint app) =>
		global::Amiga.MUIMaster.MUI_DisposeObject(app);

	public static uint ConnectCloseRequest(
		uint app,
		uint window,
		uint returnId = 0xffff_ffffu) =>
		global::Amiga.BOOPSI.DoMethod(
			window,
			Notify.Method,
			Window.CloseRequest,
			(uint)Value.EveryTime,
			app,
			2,
			Application.Method.ReturnID,
			returnId);

	public uint DoMethod(uint message) =>
		Application.Do(Raw, message);

	public uint NewInput(uint message) =>
		Application.Do(Raw, message);

	public uint Run() =>
		global::Amiga.BOOPSI.DoMethod(Raw, Application.Method.Run);

	public uint ConnectCloseRequest(
		WindowObject window,
		uint returnId = 0xffff_ffffu) =>
		global::Amiga.BOOPSI.DoMethod(
			window.Raw,
			Notify.Method,
			Window.CloseRequest,
			(uint)Value.EveryTime,
			Raw,
			2,
			Application.Method.ReturnID,
			returnId);

	public uint SetAttrs(uint tags) =>
		global::Amiga.Intuition.SetAttrsA(Raw, tags);

	public void Dispose() =>
		global::Amiga.MUIMaster.MUI_DisposeObject(Raw);

	public MUIObject AsObject() => new(Raw);

	public static implicit operator uint(ApplicationObject value) => value.Raw;

	public static explicit operator ApplicationObject(uint value) => new(value);
}

public readonly struct WindowObject
{
	public WindowObject(uint raw)
	{
		Raw = raw;
	}

	public uint Raw { get; }

	public bool IsNull => Raw == 0;

	public static WindowObject New(uint tags) =>
		new(Window.New(tags));

	public static uint SetOpen(uint window, bool open) =>
		global::Amiga.BOOPSI.DoMethod(
			window,
			Method.Set,
			Window.Open,
			open ? 1u : 0u);

	public uint DoMethod(uint message) =>
		Window.Do(Raw, message);

	public uint SetAttrs(uint tags) =>
		global::Amiga.Intuition.SetAttrsA(Raw, tags);

	public uint SetOpen(bool open) =>
		global::Amiga.BOOPSI.DoMethod(
			Raw,
			Method.Set,
			Window.Open,
			open ? 1u : 0u);

	public uint ToFront(uint message) =>
		Window.Do(Raw, message);

	public uint ToBack(uint message) =>
		Window.Do(Raw, message);

	public void Dispose() =>
		global::Amiga.MUIMaster.MUI_DisposeObject(Raw);

	public MUIObject AsObject() => new(Raw);

	public static implicit operator uint(WindowObject value) => value.Raw;

	public static explicit operator WindowObject(uint value) => new(value);
}
