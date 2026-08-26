/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>Stack-scoped owner of a CGXVideo layer handle.</summary>
public ref struct CgxVideoLayerHandle
{
	private APTR _raw;
	private uint _lockCount;
	private bool _attached;

	private CgxVideoLayerHandle(APTR raw) => _raw = raw;

	public APTR Raw => _raw;
	public bool IsNull => _raw.IsNull;
	public bool IsAttached => _attached;
	public uint LockCount => _lockCount;

	public static bool TryCreate(APTR screen, ref TagItem tags,
		out CgxVideoLayerHandle layer)
	{
		var raw = CgxVideo.CreateVLayerHandleTagList(screen,
			TagItem.AddressOf(ref tags));
		layer = new CgxVideoLayerHandle(raw);
		return raw.IsNotNull;
	}

	public CgxVideoError Attach(APTR window, ref TagItem tags)
	{
		var result = (CgxVideoError)CgxVideo.AttachVLayerTagList(_raw, window,
			TagItem.AddressOf(ref tags));
		if (result == CgxVideoError.Ok)
		{
			_attached = true;
		}

		return result;
	}

	public CgxVideoError Detach()
	{
		if (!_attached)
		{
			return CgxVideoError.Ok;
		}

		var result = (CgxVideoError)CgxVideo.DetachVLayer(_raw);
		if (result == CgxVideoError.Ok)
		{
			_attached = false;
		}

		return result;
	}

	public uint GetAttribute(CgxVideoTag attribute) =>
		CgxVideo.GetVLayerAttr(_raw, attribute);

	public CgxVideoError Lock()
	{
		var result = (CgxVideoError)CgxVideo.LockVLayer(_raw);
		if (result == CgxVideoError.Ok)
		{
			_lockCount++;
		}

		return result;
	}

	public CgxVideoError Unlock()
	{
		if (_lockCount == 0)
		{
			return CgxVideoError.Ok;
		}

		var result = (CgxVideoError)CgxVideo.UnlockVLayer(_raw);
		if (result == CgxVideoError.Ok)
		{
			_lockCount--;
		}

		return result;
	}

	public void SetAttributes(ref TagItem tags) =>
		CgxVideo.SetVLayerAttrTagList(_raw, TagItem.AddressOf(ref tags));

	public void SwapBuffers() => CgxVideo.SwapVLayerBuffer(_raw);

	public CgxVideoError WriteSPLine(APTR source, int x, int y, int width) =>
		(CgxVideoError)CgxVideo.WriteSPLine(_raw, source, x, y, width);

	public static uint QueryAttribute(APTR screen, CgxVideoQueryTag attribute) =>
		CgxVideo.QueryVLayerAttr(screen, attribute);

	public void Dispose()
	{
		if (_raw.IsNull)
		{
			return;
		}

		while (_lockCount != 0)
		{
			CgxVideo.UnlockVLayer(_raw);
			_lockCount--;
		}

		if (_attached)
		{
			CgxVideo.DetachVLayer(_raw);
			_attached = false;
		}

		CgxVideo.DeleteVLayerHandle(_raw);
		_raw = APTR.Null;
	}
}
