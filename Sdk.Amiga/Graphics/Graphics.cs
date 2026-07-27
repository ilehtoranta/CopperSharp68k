/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class Graphics
{
	public const string Name = "graphics.library";

	public static APTR GraphicsLibraryBase
	{
		get => throw new System.NotSupportedException(
			"GraphicsLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"GraphicsLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int BltBitMap(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.A1)] uint arg3,
		[M68kRegister(M68kRegister.D2)] int arg4,
		[M68kRegister(M68kRegister.D3)] int arg5,
		[M68kRegister(M68kRegister.D4)] int arg6,
		[M68kRegister(M68kRegister.D5)] int arg7,
		[M68kRegister(M68kRegister.D6)] uint arg8,
		[M68kRegister(M68kRegister.D7)] uint arg9,
		[M68kRegister(M68kRegister.A2)] uint arg10);

	[AmigaLvo(-36)]
	public static extern void BltTemplate(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.A1)] uint arg3,
		[M68kRegister(M68kRegister.D2)] int arg4,
		[M68kRegister(M68kRegister.D3)] int arg5,
		[M68kRegister(M68kRegister.D4)] int arg6,
		[M68kRegister(M68kRegister.D5)] int arg7);

	[AmigaLvo(-42)]
	public static extern void ClearEOL(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	[AmigaLvo(-48)]
	public static extern void ClearScreen(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	[AmigaLvo(-54)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern short TextLength(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.A0)] uint arg1,
		[M68kRegister(M68kRegister.D0)] uint arg2);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Text(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.A0)] uint arg1,
		[M68kRegister(M68kRegister.D0)] uint arg2);

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetFont(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.A0)] uint arg1);

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint OpenFont(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-78)]
	public static extern void CloseFont(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	[AmigaLvo(-84)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AskSoftStyle(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	[AmigaLvo(-90)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetSoftStyle(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2);

	[AmigaLvo(-96)]
	public static extern void AddBob(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-102)]
	public static extern void AddVSprite(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-108)]
	public static extern void DoCollision(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	[AmigaLvo(-114)]
	public static extern void DrawGList(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.A0)] uint arg1);

	[AmigaLvo(-120)]
	public static extern void InitGels(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2);

	[AmigaLvo(-126)]
	public static extern void InitMasks(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-132)]
	public static extern void RemIBob(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2);

	[AmigaLvo(-138)]
	public static extern void RemVSprite(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-144)]
	public static extern void SetCollision(
		[M68kRegister(M68kRegister.D0)] uint arg0,
		[M68kRegister(M68kRegister.A0)] uint arg1,
		[M68kRegister(M68kRegister.A1)] uint arg2);

	[AmigaLvo(-150)]
	public static extern void SortGList(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	[AmigaLvo(-156)]
	public static extern void AddAnimOb(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2);

	[AmigaLvo(-162)]
	public static extern void Animate(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-168)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetGBuffers(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2);

	[AmigaLvo(-174)]
	public static extern void InitGMasks(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-180)]
	public static extern void DrawEllipse(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.D2)] int arg3,
		[M68kRegister(M68kRegister.D3)] int arg4);

	[AmigaLvo(-186)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AreaEllipse(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.D2)] int arg3,
		[M68kRegister(M68kRegister.D3)] int arg4);

	[AmigaLvo(-192)]
	public static extern void LoadRGB4(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2);

	[AmigaLvo(-198)]
	public static extern void InitRastPort(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	[AmigaLvo(-204)]
	public static extern void InitVPort(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-210)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint MrgCop(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	[AmigaLvo(-216)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint MakeVPort(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-222)]
	public static extern void LoadView(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	[AmigaLvo(-228)]
	public static extern void WaitBlit();

	[AmigaLvo(-234)]
	public static extern void SetRast(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	[AmigaLvo(-240)]
	public static extern void Move(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2);

	[AmigaLvo(-246)]
	public static extern void Draw(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2);

	[AmigaLvo(-252)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AreaMove(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2);

	[AmigaLvo(-258)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AreaDraw(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2);

	[AmigaLvo(-264)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AreaEnd(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	[AmigaLvo(-270)]
	public static extern void WaitTOF();

	[AmigaLvo(-276)]
	public static extern void QBlit(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	[AmigaLvo(-282)]
	public static extern void InitArea(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2);

	[AmigaLvo(-288)]
	public static extern void SetRGB4(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2,
		[M68kRegister(M68kRegister.D2)] uint arg3,
		[M68kRegister(M68kRegister.D3)] uint arg4);

	[AmigaLvo(-294)]
	public static extern void QBSBlit(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	[AmigaLvo(-300)]
	public static extern void BltClear(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2);

	[AmigaLvo(-306)]
	public static extern void RectFill(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.D2)] int arg3,
		[M68kRegister(M68kRegister.D3)] int arg4);

	[AmigaLvo(-312)]
	public static extern void BltPattern(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.A0)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3,
		[M68kRegister(M68kRegister.D2)] int arg4,
		[M68kRegister(M68kRegister.D3)] int arg5,
		[M68kRegister(M68kRegister.D4)] uint arg6);

	[AmigaLvo(-318)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ReadPixel(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2);

	[AmigaLvo(-324)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int WritePixel(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2);

	[AmigaLvo(-330)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Flood(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D2)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3);

	[AmigaLvo(-336)]
	public static extern void PolyDraw(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.A0)] uint arg2);

	[AmigaLvo(-342)]
	public static extern void SetAPen(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	[AmigaLvo(-348)]
	public static extern void SetBPen(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	[AmigaLvo(-354)]
	public static extern void SetDrMd(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	[AmigaLvo(-360)]
	public static extern void InitView(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	[AmigaLvo(-366)]
	public static extern void CBump(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	[AmigaLvo(-372)]
	public static extern void CMove(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] int arg2);

	[AmigaLvo(-378)]
	public static extern void CWait(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2);

	[AmigaLvo(-384)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int VBeamPos();

	[AmigaLvo(-390)]
	public static extern void InitBitMap(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.D2)] int arg3);

	[AmigaLvo(-396)]
	public static extern void ScrollRaster(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.D2)] int arg3,
		[M68kRegister(M68kRegister.D3)] int arg4,
		[M68kRegister(M68kRegister.D4)] int arg5,
		[M68kRegister(M68kRegister.D5)] int arg6);

	[AmigaLvo(-402)]
	public static extern void WaitBOVP(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-408)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern short GetSprite(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1);

	[AmigaLvo(-414)]
	public static extern void FreeSprite(
		[M68kRegister(M68kRegister.D0)] int arg0);

	[AmigaLvo(-420)]
	public static extern void ChangeSprite(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2);

	[AmigaLvo(-426)]
	public static extern void MoveSprite(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3);

	[AmigaLvo(-432)]
	public static extern void LockLayerRom(
		[M68kRegister(M68kRegister.A5)] uint arg0);

	[AmigaLvo(-438)]
	public static extern void UnlockLayerRom(
		[M68kRegister(M68kRegister.A5)] uint arg0);

	[AmigaLvo(-444)]
	public static extern void SyncSBitMap(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-450)]
	public static extern void CopySBitMap(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-456)]
	public static extern void OwnBlitter();

	[AmigaLvo(-462)]
	public static extern void DisownBlitter();

	[AmigaLvo(-468)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint InitTmpRas(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2);

	[AmigaLvo(-474)]
	public static extern void AskFont(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.A0)] uint arg1);

	[AmigaLvo(-480)]
	public static extern void AddFont(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	[AmigaLvo(-486)]
	public static extern void RemFont(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	[AmigaLvo(-492)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocRaster(
		[M68kRegister(M68kRegister.D0)] uint arg0,
		[M68kRegister(M68kRegister.D1)] uint arg1);

	[AmigaLvo(-498)]
	public static extern void FreeRaster(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2);

	[AmigaLvo(-504)]
	public static extern void AndRectRegion(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-510)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int OrRectRegion(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-516)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewRegion();

	[AmigaLvo(-522)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ClearRectRegion(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-528)]
	public static extern void ClearRegion(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-534)]
	public static extern void DisposeRegion(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-540)]
	public static extern void FreeVPortCopLists(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-546)]
	public static extern void FreeCopList(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-552)]
	public static extern void ClipBlit(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.A1)] uint arg3,
		[M68kRegister(M68kRegister.D2)] int arg4,
		[M68kRegister(M68kRegister.D3)] int arg5,
		[M68kRegister(M68kRegister.D4)] int arg6,
		[M68kRegister(M68kRegister.D5)] int arg7,
		[M68kRegister(M68kRegister.D6)] uint arg8);

	[AmigaLvo(-558)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XorRectRegion(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-564)]
	public static extern void FreeCprList(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-570)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetColorMap(
		[M68kRegister(M68kRegister.D0)] int arg0);

	[AmigaLvo(-576)]
	public static extern void FreeColorMap(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-582)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetRGB4(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1);

	[AmigaLvo(-588)]
	public static extern void ScrollVPort(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-594)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint UCopperListInit(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1);

	[AmigaLvo(-600)]
	public static extern void FreeGBuffers(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2);

	[AmigaLvo(-606)]
	public static extern void BltBitMapRastPort(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.A1)] uint arg3,
		[M68kRegister(M68kRegister.D2)] int arg4,
		[M68kRegister(M68kRegister.D3)] int arg5,
		[M68kRegister(M68kRegister.D4)] int arg6,
		[M68kRegister(M68kRegister.D5)] int arg7,
		[M68kRegister(M68kRegister.D6)] uint arg8);

	[AmigaLvo(-612)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int OrRegionRegion(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-618)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int XorRegionRegion(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-624)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AndRegionRegion(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-630)]
	public static extern void SetRGB4CM(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2,
		[M68kRegister(M68kRegister.D2)] uint arg3,
		[M68kRegister(M68kRegister.D3)] uint arg4);

	[AmigaLvo(-636)]
	public static extern void BltMaskBitMapRastPort(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.A1)] uint arg3,
		[M68kRegister(M68kRegister.D2)] int arg4,
		[M68kRegister(M68kRegister.D3)] int arg5,
		[M68kRegister(M68kRegister.D4)] int arg6,
		[M68kRegister(M68kRegister.D5)] int arg7,
		[M68kRegister(M68kRegister.D6)] uint arg8,
		[M68kRegister(M68kRegister.A2)] uint arg9);

	[AmigaLvo(-654)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AttemptLockLayerRom(
		[M68kRegister(M68kRegister.A5)] uint arg0);

	[AmigaLvo(-660)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GfxNew(
		[M68kRegister(M68kRegister.D0)] uint arg0);

	[AmigaLvo(-666)]
	public static extern void GfxFree(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-672)]
	public static extern void GfxAssociate(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-678)]
	public static extern void BitMapScale(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-684)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ushort ScalerDiv(
		[M68kRegister(M68kRegister.D0)] uint arg0,
		[M68kRegister(M68kRegister.D1)] uint arg1,
		[M68kRegister(M68kRegister.D2)] uint arg2);

	[AmigaLvo(-690)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern short TextExtent(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.A0)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.A2)] uint arg3);

	[AmigaLvo(-696)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint TextFit(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.A0)] uint arg1,
		[M68kRegister(M68kRegister.D0)] uint arg2,
		[M68kRegister(M68kRegister.A2)] uint arg3,
		[M68kRegister(M68kRegister.A3)] uint arg4,
		[M68kRegister(M68kRegister.D1)] int arg5,
		[M68kRegister(M68kRegister.D2)] uint arg6,
		[M68kRegister(M68kRegister.D3)] uint arg7);

	[AmigaLvo(-702)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GfxLookUp(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-708)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int VideoControl(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-714)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint OpenMonitor(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	[AmigaLvo(-720)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int CloseMonitor(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-726)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint FindDisplayInfo(
		[M68kRegister(M68kRegister.D0)] uint arg0);

	[AmigaLvo(-732)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NextDisplayInfo(
		[M68kRegister(M68kRegister.D0)] uint arg0);

	[AmigaLvo(-756)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetDisplayInfoData(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] uint arg2,
		[M68kRegister(M68kRegister.D1)] uint arg3,
		[M68kRegister(M68kRegister.D2)] uint arg4);

	[AmigaLvo(-762)]
	public static extern void FontExtent(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-768)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ReadPixelLine8(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2,
		[M68kRegister(M68kRegister.D2)] uint arg3,
		[M68kRegister(M68kRegister.A2)] uint arg4,
		[M68kRegister(M68kRegister.A1)] uint arg5);

	[AmigaLvo(-774)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int WritePixelLine8(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2,
		[M68kRegister(M68kRegister.D2)] uint arg3,
		[M68kRegister(M68kRegister.A2)] uint arg4,
		[M68kRegister(M68kRegister.A1)] uint arg5);

	[AmigaLvo(-780)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ReadPixelArray8(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2,
		[M68kRegister(M68kRegister.D2)] uint arg3,
		[M68kRegister(M68kRegister.D3)] uint arg4,
		[M68kRegister(M68kRegister.A2)] uint arg5,
		[M68kRegister(M68kRegister.A1)] uint arg6);

	[AmigaLvo(-786)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int WritePixelArray8(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2,
		[M68kRegister(M68kRegister.D2)] uint arg3,
		[M68kRegister(M68kRegister.D3)] uint arg4,
		[M68kRegister(M68kRegister.A2)] uint arg5,
		[M68kRegister(M68kRegister.A1)] uint arg6);

	[AmigaLvo(-792)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetVPModeID(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-798)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ModeNotAvailable(
		[M68kRegister(M68kRegister.D0)] uint arg0);

	// MorphOS m68k ABI call.
	[AmigaLvo(-804)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern short WeighTAMatch(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2);

	[AmigaLvo(-810)]
	public static extern void EraseRect(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.D2)] int arg3,
		[M68kRegister(M68kRegister.D3)] int arg4);

	[AmigaLvo(-816)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ExtendFont(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-822)]
	public static extern void StripFont(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-828)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ushort CalcIVG(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-834)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AttachPalExtra(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-840)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ObtainBestPenA(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D1)] uint arg1,
		[M68kRegister(M68kRegister.D2)] uint arg2,
		[M68kRegister(M68kRegister.D3)] uint arg3,
		[M68kRegister(M68kRegister.A1)] uint arg4);

	[AmigaLvo(-852)]
	public static extern void SetRGB32(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2,
		[M68kRegister(M68kRegister.D2)] uint arg3,
		[M68kRegister(M68kRegister.D3)] uint arg4);

	[AmigaLvo(-858)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetAPen(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-864)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetBPen(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-870)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetDrMd(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-876)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetOutlinePen(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-882)]
	public static extern void LoadRGB32(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-888)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetChipRev(
		[M68kRegister(M68kRegister.D0)] uint arg0);

	[AmigaLvo(-894)]
	public static extern void SetABPenDrMd(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2,
		[M68kRegister(M68kRegister.D2)] uint arg3);

	[AmigaLvo(-900)]
	public static extern void GetRGB32(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2,
		[M68kRegister(M68kRegister.A1)] uint arg3);

	[AmigaLvo(-918)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocBitMap(
		[M68kRegister(M68kRegister.D0)] uint arg0,
		[M68kRegister(M68kRegister.D1)] uint arg1,
		[M68kRegister(M68kRegister.D2)] uint arg2,
		[M68kRegister(M68kRegister.D3)] uint arg3,
		[M68kRegister(M68kRegister.A0)] uint arg4);

	[AmigaLvo(-924)]
	public static extern void FreeBitMap(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-930)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetExtSpriteA(
		[M68kRegister(M68kRegister.A2)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-936)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CoerceMode(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2);

	[AmigaLvo(-942)]
	public static extern void ChangeVPBitMap(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2);

	[AmigaLvo(-948)]
	public static extern void ReleasePen(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	[AmigaLvo(-954)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ObtainPen(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2,
		[M68kRegister(M68kRegister.D2)] uint arg3,
		[M68kRegister(M68kRegister.D3)] uint arg4,
		[M68kRegister(M68kRegister.D4)] int arg5);

	[AmigaLvo(-960)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetBitMapAttr(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D1)] uint arg1);

	[AmigaLvo(-966)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocDBufInfo(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-972)]
	public static extern void FreeDBufInfo(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	[AmigaLvo(-978)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetOutlinePen(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	[AmigaLvo(-984)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetWriteMask(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	[AmigaLvo(-990)]
	public static extern void SetMaxPen(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	[AmigaLvo(-996)]
	public static extern void SetRGB32CM(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2,
		[M68kRegister(M68kRegister.D2)] uint arg3,
		[M68kRegister(M68kRegister.D3)] uint arg4);

	[AmigaLvo(-1002)]
	public static extern void ScrollRasterBF(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.D2)] int arg3,
		[M68kRegister(M68kRegister.D3)] int arg4,
		[M68kRegister(M68kRegister.D4)] int arg5,
		[M68kRegister(M68kRegister.D5)] int arg6);

	[AmigaLvo(-1008)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int FindColor(
		[M68kRegister(M68kRegister.A3)] uint arg0,
		[M68kRegister(M68kRegister.D1)] uint arg1,
		[M68kRegister(M68kRegister.D2)] uint arg2,
		[M68kRegister(M68kRegister.D3)] uint arg3,
		[M68kRegister(M68kRegister.D4)] int arg4);

	[AmigaLvo(-1020)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocSpriteDataA(
		[M68kRegister(M68kRegister.A2)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-1026)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ChangeExtSpriteA(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2,
		[M68kRegister(M68kRegister.A3)] uint arg3);

	[AmigaLvo(-1032)]
	public static extern void FreeSpriteData(
		[M68kRegister(M68kRegister.A2)] uint arg0);

	[AmigaLvo(-1038)]
	public static extern void SetRPAttrsA(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-1044)]
	public static extern void GetRPAttrsA(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-1050)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint BestModeIDA(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-1056)]
	public static extern void WriteChunkyPixels(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2,
		[M68kRegister(M68kRegister.D2)] uint arg3,
		[M68kRegister(M68kRegister.D3)] uint arg4,
		[M68kRegister(M68kRegister.A2)] uint arg5,
		[M68kRegister(M68kRegister.D4)] int arg6);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1062)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint OpenFontTagList(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);
}
