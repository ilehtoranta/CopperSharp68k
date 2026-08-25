/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

namespace Amiga;

/// <summary>MorphOS TrueType font engine M68k vectors.</summary>
[AmigaLibrary(Name, AmigaLibraryBasePolicy.Manual)]
public static class TTEngine
{
	public const string Name = "ttengine.library";
	public const ushort MinimumVersion = 10;
	public const short OpenFontALvo = -30;
	public const short SetFontLvo = -36;
	public const short CloseFontLvo = -42;
	public const short TextLvo = -48;
	public const short SetAttrsALvo = -54;
	public const short GetAttrsALvo = -60;
	public const short TextLengthLvo = -66;
	public const short TextExtentLvo = -72;
	public const short TextFitLvo = -78;
	public const short GetPixmapALvo = -84;
	public const short FreePixmapLvo = -90;
	public const short DoneRastPortLvo = -96;
	public const short AllocRequestLvo = -102;
	public const short RequestALvo = -108;
	public const short FreeRequestLvo = -114;
	public const short ObtainFamilyListALvo = -120;
	public const short FreeFamilyListLvo = -126;
	public const short CharPositionsLvo = -132;

	public static APTR TTEngineLibraryBase
	{
		get => throw new System.NotSupportedException(
			"TTEngineLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"TTEngineLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(OpenFontALvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR TT_OpenFontA(
		[M68kRegister(M68kRegister.A0)] APTR tagList);

	[AmigaLvo(SetFontLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int TT_SetFont(
		[M68kRegister(M68kRegister.A1)] APTR rastPort,
		[M68kRegister(M68kRegister.A0)] APTR font);

	[AmigaLvo(CloseFontLvo)]
	public static extern void TT_CloseFont(
		[M68kRegister(M68kRegister.A0)] APTR font);

	[AmigaLvo(TextLvo)]
	public static extern void TT_Text(
		[M68kRegister(M68kRegister.A1)] APTR rastPort,
		[M68kRegister(M68kRegister.A0)] APTR text,
		[M68kRegister(M68kRegister.D0)] uint count);

	[AmigaLvo(SetAttrsALvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint TT_SetAttrsA(
		[M68kRegister(M68kRegister.A1)] APTR rastPort,
		[M68kRegister(M68kRegister.A0)] APTR tagList);

	[AmigaLvo(GetAttrsALvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint TT_GetAttrsA(
		[M68kRegister(M68kRegister.A1)] APTR rastPort,
		[M68kRegister(M68kRegister.A0)] APTR tagList);

	[AmigaLvo(TextLengthLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint TT_TextLength(
		[M68kRegister(M68kRegister.A1)] APTR rastPort,
		[M68kRegister(M68kRegister.A0)] APTR text,
		[M68kRegister(M68kRegister.D0)] uint count);

	[AmigaLvo(TextExtentLvo)]
	public static extern void TT_TextExtent(
		[M68kRegister(M68kRegister.A1)] APTR rastPort,
		[M68kRegister(M68kRegister.A0)] APTR text,
		[M68kRegister(M68kRegister.D0)] short count,
		[M68kRegister(M68kRegister.A2)] APTR textExtent);

	[AmigaLvo(TextFitLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint TT_TextFit(
		[M68kRegister(M68kRegister.A1)] APTR rastPort,
		[M68kRegister(M68kRegister.A0)] APTR text,
		[M68kRegister(M68kRegister.D0)] ushort count,
		[M68kRegister(M68kRegister.A2)] APTR textExtent,
		[M68kRegister(M68kRegister.A3)] APTR constraint,
		[M68kRegister(M68kRegister.D1)] short direction,
		[M68kRegister(M68kRegister.D2)] ushort width,
		[M68kRegister(M68kRegister.D3)] ushort height);

	[AmigaLvo(GetPixmapALvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR TT_GetPixmapA(
		[M68kRegister(M68kRegister.A1)] APTR font,
		[M68kRegister(M68kRegister.A2)] APTR text,
		[M68kRegister(M68kRegister.D0)] uint count,
		[M68kRegister(M68kRegister.A0)] APTR tagList);

	[AmigaLvo(FreePixmapLvo)]
	public static extern void TT_FreePixmap(
		[M68kRegister(M68kRegister.A0)] APTR pixmap);

	[AmigaLvo(DoneRastPortLvo)]
	public static extern void TT_DoneRastPort(
		[M68kRegister(M68kRegister.A1)] APTR rastPort);

	[AmigaLvo(AllocRequestLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR TT_AllocRequest();

	[AmigaLvo(RequestALvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR TT_RequestA(
		[M68kRegister(M68kRegister.A0)] APTR request,
		[M68kRegister(M68kRegister.A1)] APTR tagList);

	[AmigaLvo(FreeRequestLvo)]
	public static extern void TT_FreeRequest(
		[M68kRegister(M68kRegister.A0)] APTR request);

	[AmigaLvo(ObtainFamilyListALvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR TT_ObtainFamilyListA(
		[M68kRegister(M68kRegister.A0)] APTR tagList);

	[AmigaLvo(FreeFamilyListLvo)]
	public static extern void TT_FreeFamilyList(
		[M68kRegister(M68kRegister.A0)] APTR familyList);

	[AmigaLvo(CharPositionsLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint TT_CharPositions(
		[M68kRegister(M68kRegister.A1)] APTR rastPort,
		[M68kRegister(M68kRegister.A0)] APTR text,
		[M68kRegister(M68kRegister.D0)] uint count,
		[M68kRegister(M68kRegister.A2)] APTR positions);
}
