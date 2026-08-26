/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>Stack-scoped owner of a TTengine font handle.</summary>
public ref struct TTFontHandle
{
	private APTR _raw;

	private TTFontHandle(APTR raw) => _raw = raw;

	public APTR Raw => _raw;
	public bool IsNull => _raw.IsNull;

	public static bool TryCreate(ref TagItem tags, out TTFontHandle font)
	{
		var raw = TTEngine.TT_OpenFontA(TagItem.AddressOf(ref tags));
		font = new TTFontHandle(raw);
		return raw.IsNotNull;
	}

	public int Set(APTR rastPort) => TTEngine.TT_SetFont(rastPort, _raw);

	public uint SetAttributes(APTR rastPort, ref TagItem tags) =>
		TTEngine.TT_SetAttrsA(rastPort, TagItem.AddressOf(ref tags));

	public uint GetAttributes(APTR rastPort, ref TagItem tags) =>
		TTEngine.TT_GetAttrsA(rastPort, TagItem.AddressOf(ref tags));

	public bool TryGetPixmap(APTR text, uint count, ref TagItem tags,
		out TTPixmapHandle pixmap)
	{
		var raw = TTEngine.TT_GetPixmapA(_raw, text, count,
			TagItem.AddressOf(ref tags));
		pixmap = new TTPixmapHandle(raw);
		return raw.IsNotNull;
	}

	public void Dispose()
	{
		if (_raw.IsNull)
		{
			return;
		}

		TTEngine.TT_CloseFont(_raw);
		_raw = APTR.Null;
	}
}

/// <summary>Stack-scoped owner of a TTengine pixmap handle.</summary>
public ref struct TTPixmapHandle
{
	private APTR _raw;

	internal TTPixmapHandle(APTR raw) => _raw = raw;

	public APTR Raw => _raw;
	public bool IsNull => _raw.IsNull;

	public void Dispose()
	{
		if (_raw.IsNull)
		{
			return;
		}

		TTEngine.TT_FreePixmap(_raw);
		_raw = APTR.Null;
	}
}

/// <summary>Stack-scoped owner of a TTengine font requester.</summary>
public ref struct TTRequesterHandle
{
	private APTR _raw;

	private TTRequesterHandle(APTR raw) => _raw = raw;

	public APTR Raw => _raw;
	public bool IsNull => _raw.IsNull;

	public static bool TryCreate(out TTRequesterHandle requester)
	{
		var raw = TTEngine.TT_AllocRequest();
		requester = new TTRequesterHandle(raw);
		return raw.IsNotNull;
	}

	public APTR Request(ref TagItem tags) =>
		TTEngine.TT_RequestA(_raw, TagItem.AddressOf(ref tags));

	public void Dispose()
	{
		if (_raw.IsNull)
		{
			return;
		}

		TTEngine.TT_FreeRequest(_raw);
		_raw = APTR.Null;
	}
}

/// <summary>Stack-scoped owner of a TTengine family-list handle.</summary>
public ref struct TTFamilyListHandle
{
	private APTR _raw;

	private TTFamilyListHandle(APTR raw) => _raw = raw;

	public APTR Raw => _raw;
	public bool IsNull => _raw.IsNull;

	public static bool TryObtain(ref TagItem tags, out TTFamilyListHandle familyList)
	{
		var raw = TTEngine.TT_ObtainFamilyListA(TagItem.AddressOf(ref tags));
		familyList = new TTFamilyListHandle(raw);
		return raw.IsNotNull;
	}

	public void Dispose()
	{
		if (_raw.IsNull)
		{
			return;
		}

		TTEngine.TT_FreeFamilyList(_raw);
		_raw = APTR.Null;
	}
}
