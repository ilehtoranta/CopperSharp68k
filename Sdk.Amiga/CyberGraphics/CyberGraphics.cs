/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class CyberGraphics
{
	public const string Name = "cybergraphics.library";

	public static APTR CyberGraphicsLibraryBase
	{
		get => throw new System.NotSupportedException(
			"CyberGraphicsLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"CyberGraphicsLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(-54)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int IsCyberModeID(
		[M68kRegister(M68kRegister.D0)] uint displayId);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint BestCModeIDTagList(
		[M68kRegister(M68kRegister.A0)] uint tags);

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CModeRequestTagList(
		[M68kRegister(M68kRegister.A0)] uint modeRequest,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocCModeListTagList(
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-78)]
	public static extern void FreeCModeList(
		[M68kRegister(M68kRegister.A0)] uint modeList);

	[AmigaLvo(-90)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ScalePixelArray(
		[M68kRegister(M68kRegister.A0)] uint source,
		[M68kRegister(M68kRegister.D0)] uint sourceWidth,
		[M68kRegister(M68kRegister.D1)] uint sourceHeight,
		[M68kRegister(M68kRegister.D2)] uint sourceStride,
		[M68kRegister(M68kRegister.A1)] uint rastPort,
		[M68kRegister(M68kRegister.D3)] uint destinationX,
		[M68kRegister(M68kRegister.D4)] uint destinationY,
		[M68kRegister(M68kRegister.D5)] uint destinationWidth,
		[M68kRegister(M68kRegister.D6)] uint destinationHeight,
		[M68kRegister(M68kRegister.D7)] uint format);

	[AmigaLvo(-96)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetCyberMapAttr(
		[M68kRegister(M68kRegister.A0)] uint bitMap,
		[M68kRegister(M68kRegister.D0)] uint attribute);

	[AmigaLvo(-102)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetCyberIDAttr(
		[M68kRegister(M68kRegister.D0)] uint displayId,
		[M68kRegister(M68kRegister.D1)] uint attribute);

	[AmigaLvo(-108)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ReadRGBPixel(
		[M68kRegister(M68kRegister.A1)] uint rastPort,
		[M68kRegister(M68kRegister.D0)] uint x,
		[M68kRegister(M68kRegister.D1)] uint y);

	[AmigaLvo(-114)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int WriteRGBPixel(
		[M68kRegister(M68kRegister.A1)] uint rastPort,
		[M68kRegister(M68kRegister.D0)] uint x,
		[M68kRegister(M68kRegister.D1)] uint y,
		[M68kRegister(M68kRegister.D2)] uint argb);

	[AmigaLvo(-120)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ReadPixelArray(
		[M68kRegister(M68kRegister.A0)] uint destination,
		[M68kRegister(M68kRegister.D0)] uint destinationX,
		[M68kRegister(M68kRegister.D1)] uint destinationY,
		[M68kRegister(M68kRegister.D2)] uint destinationStride,
		[M68kRegister(M68kRegister.A1)] uint rastPort,
		[M68kRegister(M68kRegister.D3)] uint sourceX,
		[M68kRegister(M68kRegister.D4)] uint sourceY,
		[M68kRegister(M68kRegister.D5)] uint width,
		[M68kRegister(M68kRegister.D6)] uint height,
		[M68kRegister(M68kRegister.D7)] uint format);

	[AmigaLvo(-126)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint WritePixelArray(
		[M68kRegister(M68kRegister.A0)] uint source,
		[M68kRegister(M68kRegister.D0)] uint sourceX,
		[M68kRegister(M68kRegister.D1)] uint sourceY,
		[M68kRegister(M68kRegister.D2)] uint sourceStride,
		[M68kRegister(M68kRegister.A1)] uint rastPort,
		[M68kRegister(M68kRegister.D3)] uint destinationX,
		[M68kRegister(M68kRegister.D4)] uint destinationY,
		[M68kRegister(M68kRegister.D5)] uint width,
		[M68kRegister(M68kRegister.D6)] uint height,
		[M68kRegister(M68kRegister.D7)] uint format);

	[AmigaLvo(-132)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint MovePixelArray(
		[M68kRegister(M68kRegister.D0)] uint sourceX,
		[M68kRegister(M68kRegister.D1)] uint sourceY,
		[M68kRegister(M68kRegister.A1)] uint rastPort,
		[M68kRegister(M68kRegister.D2)] uint destinationX,
		[M68kRegister(M68kRegister.D3)] uint destinationY,
		[M68kRegister(M68kRegister.D4)] uint width,
		[M68kRegister(M68kRegister.D5)] uint height);

	[AmigaLvo(-144)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint InvertPixelArray(
		[M68kRegister(M68kRegister.A1)] uint rastPort,
		[M68kRegister(M68kRegister.D0)] uint x,
		[M68kRegister(M68kRegister.D1)] uint y,
		[M68kRegister(M68kRegister.D2)] uint width,
		[M68kRegister(M68kRegister.D3)] uint height);

	[AmigaLvo(-150)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint FillPixelArray(
		[M68kRegister(M68kRegister.A1)] uint rastPort,
		[M68kRegister(M68kRegister.D0)] uint x,
		[M68kRegister(M68kRegister.D1)] uint y,
		[M68kRegister(M68kRegister.D2)] uint width,
		[M68kRegister(M68kRegister.D3)] uint height,
		[M68kRegister(M68kRegister.D4)] uint argb);

	[AmigaLvo(-156)]
	public static extern void DoCDrawMethodTagList(
		[M68kRegister(M68kRegister.A0)] uint hook,
		[M68kRegister(M68kRegister.A1)] uint rastPort,
		[M68kRegister(M68kRegister.A2)] uint tags);

	[AmigaLvo(-162)]
	public static extern void CVideoCtrlTagList(
		[M68kRegister(M68kRegister.A0)] uint viewPort,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-168)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint LockBitMapTagList(
		[M68kRegister(M68kRegister.A0)] uint bitMap,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-174)]
	public static extern void UnLockBitMap(
		[M68kRegister(M68kRegister.A0)] uint lockHandle);

	[AmigaLvo(-180)]
	public static extern void UnLockBitMapTagList(
		[M68kRegister(M68kRegister.A0)] uint lockHandle,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-186)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ExtractColor(
		[M68kRegister(M68kRegister.A0)] uint rastPort,
		[M68kRegister(M68kRegister.A1)] uint bitMap,
		[M68kRegister(M68kRegister.D0)] uint sourceX,
		[M68kRegister(M68kRegister.D1)] uint sourceY,
		[M68kRegister(M68kRegister.D2)] uint width,
		[M68kRegister(M68kRegister.D3)] uint height,
		[M68kRegister(M68kRegister.D4)] uint pen);

	[AmigaLvo(-198)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint WriteLUTPixelArray(
		[M68kRegister(M68kRegister.A0)] uint source,
		[M68kRegister(M68kRegister.D0)] uint sourceX,
		[M68kRegister(M68kRegister.D1)] uint sourceY,
		[M68kRegister(M68kRegister.D2)] uint sourceStride,
		[M68kRegister(M68kRegister.A1)] uint rastPort,
		[M68kRegister(M68kRegister.A2)] uint colorTable,
		[M68kRegister(M68kRegister.D3)] uint destinationX,
		[M68kRegister(M68kRegister.D4)] uint destinationY,
		[M68kRegister(M68kRegister.D5)] uint width,
		[M68kRegister(M68kRegister.D6)] uint height,
		[M68kRegister(M68kRegister.D7)] uint format);

	// MorphOS m68k ABI call, V43.
	[AmigaLvo(-216)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint WritePixelArrayAlpha(
		[M68kRegister(M68kRegister.A0)] uint source,
		[M68kRegister(M68kRegister.D0)] uint sourceX,
		[M68kRegister(M68kRegister.D1)] uint sourceY,
		[M68kRegister(M68kRegister.D2)] uint sourceStride,
		[M68kRegister(M68kRegister.A1)] uint rastPort,
		[M68kRegister(M68kRegister.D3)] uint destinationX,
		[M68kRegister(M68kRegister.D4)] uint destinationY,
		[M68kRegister(M68kRegister.D5)] uint width,
		[M68kRegister(M68kRegister.D6)] uint height,
		[M68kRegister(M68kRegister.D7)] uint globalAlpha);

	// MorphOS m68k ABI call, V43.
	[AmigaLvo(-222)]
	public static extern void BltTemplateAlpha(
		[M68kRegister(M68kRegister.A0)] uint source,
		[M68kRegister(M68kRegister.D0)] int sourceX,
		[M68kRegister(M68kRegister.D1)] int sourceStride,
		[M68kRegister(M68kRegister.A1)] uint rastPort,
		[M68kRegister(M68kRegister.D2)] int destinationX,
		[M68kRegister(M68kRegister.D3)] int destinationY,
		[M68kRegister(M68kRegister.D4)] int width,
		[M68kRegister(M68kRegister.D5)] int height);

	// MorphOS m68k ABI call, V43.
	[AmigaLvo(-228)]
	public static extern void ProcessPixelArray(
		[M68kRegister(M68kRegister.A1)] uint rastPort,
		[M68kRegister(M68kRegister.D0)] uint x,
		[M68kRegister(M68kRegister.D1)] uint y,
		[M68kRegister(M68kRegister.D2)] uint width,
		[M68kRegister(M68kRegister.D3)] uint height,
		[M68kRegister(M68kRegister.D4)] uint operation,
		[M68kRegister(M68kRegister.D5)] int value,
		[M68kRegister(M68kRegister.A2)] uint tags);

	// MorphOS m68k ABI call, V50.
	[AmigaLvo(-234)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint BltBitMapAlpha(
		[M68kRegister(M68kRegister.A0)] uint sourceBitMap,
		[M68kRegister(M68kRegister.D0)] int sourceX,
		[M68kRegister(M68kRegister.D1)] int sourceY,
		[M68kRegister(M68kRegister.A1)] uint destinationBitMap,
		[M68kRegister(M68kRegister.D2)] int destinationX,
		[M68kRegister(M68kRegister.D3)] int destinationY,
		[M68kRegister(M68kRegister.D4)] int width,
		[M68kRegister(M68kRegister.D5)] int height,
		[M68kRegister(M68kRegister.A2)] uint tags);

	// MorphOS m68k ABI call, V50.
	[AmigaLvo(-240)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint BltBitMapRastPortAlpha(
		[M68kRegister(M68kRegister.A0)] uint sourceBitMap,
		[M68kRegister(M68kRegister.D0)] int sourceX,
		[M68kRegister(M68kRegister.D1)] int sourceY,
		[M68kRegister(M68kRegister.A1)] uint destinationRastPort,
		[M68kRegister(M68kRegister.D2)] int destinationX,
		[M68kRegister(M68kRegister.D3)] int destinationY,
		[M68kRegister(M68kRegister.D4)] int width,
		[M68kRegister(M68kRegister.D5)] int height,
		[M68kRegister(M68kRegister.A2)] uint tags);

	// MorphOS m68k ABI call, V51.
	[AmigaLvo(-252)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ScalePixelArrayAlpha(
		[M68kRegister(M68kRegister.A0)] uint source,
		[M68kRegister(M68kRegister.D0)] uint sourceWidth,
		[M68kRegister(M68kRegister.D1)] uint sourceHeight,
		[M68kRegister(M68kRegister.D2)] uint sourceStride,
		[M68kRegister(M68kRegister.A1)] uint rastPort,
		[M68kRegister(M68kRegister.D3)] uint destinationX,
		[M68kRegister(M68kRegister.D4)] uint destinationY,
		[M68kRegister(M68kRegister.D5)] uint destinationWidth,
		[M68kRegister(M68kRegister.D6)] uint destinationHeight,
		[M68kRegister(M68kRegister.D7)] uint globalAlpha);

	// MorphOS m68k ABI call, V52.
	[AmigaLvo(-258)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ScaleMapRastPortAlpha(
		[M68kRegister(M68kRegister.A0)] uint sourceBitMap,
		[M68kRegister(M68kRegister.D0)] uint sourceX,
		[M68kRegister(M68kRegister.D1)] uint sourceY,
		[M68kRegister(M68kRegister.D2)] uint sourceWidth,
		[M68kRegister(M68kRegister.D3)] uint sourceHeight,
		[M68kRegister(M68kRegister.A1)] uint destinationRastPort,
		[M68kRegister(M68kRegister.D4)] uint destinationX,
		[M68kRegister(M68kRegister.D5)] uint destinationY,
		[M68kRegister(M68kRegister.D6)] uint destinationWidth,
		[M68kRegister(M68kRegister.D7)] uint destinationHeight,
		[M68kRegister(M68kRegister.A2)] uint tags);

	public static uint BestCModeIDTags(uint tags) =>
		BestCModeIDTagList(tags);

	public static uint CModeRequestTags(uint modeRequest, uint tags) =>
		CModeRequestTagList(modeRequest, tags);

	public static uint AllocCModeListTags(uint tags) =>
		AllocCModeListTagList(tags);

	public static void CVideoCtrlTags(uint viewPort, uint tags) =>
		CVideoCtrlTagList(viewPort, tags);

	public static void DoCDrawMethodTags(uint hook, uint rastPort, uint tags) =>
		DoCDrawMethodTagList(hook, rastPort, tags);

	public static uint LockBitMapTags(uint bitMap, uint tags) =>
		LockBitMapTagList(bitMap, tags);

	public static void UnLockBitMapTags(uint lockHandle, uint tags) =>
		UnLockBitMapTagList(lockHandle, tags);
}
