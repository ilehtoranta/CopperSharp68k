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

	public uint DoMethod(uint message) =>
		Application.Do(Raw, message);

	public uint NewInput(uint message) =>
		Application.Do(Raw, message);

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

	public uint DoMethod(uint message) =>
		Window.Do(Raw, message);

	public uint SetAttrs(uint tags) =>
		global::Amiga.Intuition.SetAttrsA(Raw, tags);

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
