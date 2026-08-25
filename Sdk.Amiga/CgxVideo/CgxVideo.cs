/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

namespace Amiga;

/// <summary>MorphOS CyberGraphX video-overlay M68k vectors.</summary>
[AmigaLibrary(Name, AmigaLibraryBasePolicy.Manual)]
public static class CgxVideo
{
	public const string Name = "cgxvideo.library";
	public const ushort MinimumVersion = 41;
	public const short CreateVLayerHandleTagListLvo = -30;
	public const short DeleteVLayerHandleLvo = -36;
	public const short AttachVLayerTagListLvo = -42;
	public const short DetachVLayerLvo = -48;
	public const short GetVLayerAttrLvo = -54;
	public const short LockVLayerLvo = -60;
	public const short UnlockVLayerLvo = -66;
	public const short SetVLayerAttrTagListLvo = -72;
	public const short SwapVLayerBufferLvo = -96;
	public const short WriteSPLineLvo = -102;
	public const short QueryVLayerAttrLvo = -108;

	public static APTR CgxVideoLibraryBase
	{
		get => throw new System.NotSupportedException(
			"CgxVideoLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"CgxVideoLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(CreateVLayerHandleTagListLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR CreateVLayerHandleTagList(
		[M68kRegister(M68kRegister.A0)] APTR screen,
		[M68kRegister(M68kRegister.A1)] APTR tagList);

	[AmigaLvo(DeleteVLayerHandleLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint DeleteVLayerHandle(
		[M68kRegister(M68kRegister.A0)] APTR layerHandle);

	[AmigaLvo(AttachVLayerTagListLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AttachVLayerTagList(
		[M68kRegister(M68kRegister.A0)] APTR layerHandle,
		[M68kRegister(M68kRegister.A1)] APTR window,
		[M68kRegister(M68kRegister.A2)] APTR tagList);

	[AmigaLvo(DetachVLayerLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint DetachVLayer(
		[M68kRegister(M68kRegister.A0)] APTR layerHandle);

	[AmigaLvo(GetVLayerAttrLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetVLayerAttr(
		[M68kRegister(M68kRegister.A0)] APTR layerHandle,
		[M68kRegister(M68kRegister.D0)] CgxVideoTag attribute);

	[AmigaLvo(LockVLayerLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint LockVLayer(
		[M68kRegister(M68kRegister.A0)] APTR layerHandle);

	[AmigaLvo(UnlockVLayerLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint UnlockVLayer(
		[M68kRegister(M68kRegister.A0)] APTR layerHandle);

	[AmigaLvo(SetVLayerAttrTagListLvo)]
	public static extern void SetVLayerAttrTagList(
		[M68kRegister(M68kRegister.A0)] APTR layerHandle,
		[M68kRegister(M68kRegister.A1)] APTR tagList);

	[AmigaLvo(SwapVLayerBufferLvo)]
	public static extern void SwapVLayerBuffer(
		[M68kRegister(M68kRegister.A0)] APTR layerHandle);

	[AmigaLvo(WriteSPLineLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint WriteSPLine(
		[M68kRegister(M68kRegister.A0)] APTR layerHandle,
		[M68kRegister(M68kRegister.A1)] APTR source,
		[M68kRegister(M68kRegister.D0)] int x,
		[M68kRegister(M68kRegister.D1)] int y,
		[M68kRegister(M68kRegister.D2)] int width);

	[AmigaLvo(QueryVLayerAttrLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint QueryVLayerAttr(
		[M68kRegister(M68kRegister.A0)] APTR screen,
		[M68kRegister(M68kRegister.D0)] CgxVideoQueryTag attribute);
}
