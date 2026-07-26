/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class Locale
{
	public const string Name = "locale.library";

	[AmigaLvo(-36)]
	public static void CloseCatalog(
		[M68kRegister(M68kRegister.A0)] uint catalog)
	{
	}

	[AmigaLvo(-42)]
	public static void CloseLocale(
		[M68kRegister(M68kRegister.A0)] uint locale)
	{
	}

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint ConvToLower(
		[M68kRegister(M68kRegister.A0)] uint locale,
		[M68kRegister(M68kRegister.D0)] uint character)
	{
		return 0;
	}

	[AmigaLvo(-54)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint ConvToUpper(
		[M68kRegister(M68kRegister.A0)] uint locale,
		[M68kRegister(M68kRegister.D0)] uint character)
	{
		return 0;
	}

	[AmigaLvo(-60)]
	public static void FormatDate(
		[M68kRegister(M68kRegister.A0)] uint locale,
		[M68kRegister(M68kRegister.A1)] CString formatString,
		[M68kRegister(M68kRegister.A2)] uint dateStamp,
		[M68kRegister(M68kRegister.A3)] uint hook)
	{
	}

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint FormatString(
		[M68kRegister(M68kRegister.A0)] uint locale,
		[M68kRegister(M68kRegister.A1)] CString formatString,
		[M68kRegister(M68kRegister.A2)] uint dataStream,
		[M68kRegister(M68kRegister.A3)] uint hook)
	{
		return 0;
	}

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint GetCatalogStr(
		[M68kRegister(M68kRegister.A0)] uint catalog,
		[M68kRegister(M68kRegister.D0)] int stringNum,
		[M68kRegister(M68kRegister.A1)] CString defaultString)
	{
		return 0;
	}

	[AmigaLvo(-78)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint GetLocaleStr(
		[M68kRegister(M68kRegister.A0)] uint locale,
		[M68kRegister(M68kRegister.D0)] uint stringNum)
	{
		return 0;
	}

	[AmigaLvo(-84)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int IsAlNum(
		[M68kRegister(M68kRegister.A0)] uint locale,
		[M68kRegister(M68kRegister.D0)] uint character)
	{
		return 0;
	}

	[AmigaLvo(-90)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int IsAlpha(
		[M68kRegister(M68kRegister.A0)] uint locale,
		[M68kRegister(M68kRegister.D0)] uint character)
	{
		return 0;
	}

	[AmigaLvo(-96)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int IsCntrl(
		[M68kRegister(M68kRegister.A0)] uint locale,
		[M68kRegister(M68kRegister.D0)] uint character)
	{
		return 0;
	}

	[AmigaLvo(-102)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int IsDigit(
		[M68kRegister(M68kRegister.A0)] uint locale,
		[M68kRegister(M68kRegister.D0)] uint character)
	{
		return 0;
	}

	[AmigaLvo(-108)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int IsGraph(
		[M68kRegister(M68kRegister.A0)] uint locale,
		[M68kRegister(M68kRegister.D0)] uint character)
	{
		return 0;
	}

	[AmigaLvo(-114)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int IsLower(
		[M68kRegister(M68kRegister.A0)] uint locale,
		[M68kRegister(M68kRegister.D0)] uint character)
	{
		return 0;
	}

	[AmigaLvo(-120)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int IsPrint(
		[M68kRegister(M68kRegister.A0)] uint locale,
		[M68kRegister(M68kRegister.D0)] uint character)
	{
		return 0;
	}

	[AmigaLvo(-126)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int IsPunct(
		[M68kRegister(M68kRegister.A0)] uint locale,
		[M68kRegister(M68kRegister.D0)] uint character)
	{
		return 0;
	}

	[AmigaLvo(-132)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int IsSpace(
		[M68kRegister(M68kRegister.A0)] uint locale,
		[M68kRegister(M68kRegister.D0)] uint character)
	{
		return 0;
	}

	[AmigaLvo(-138)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int IsUpper(
		[M68kRegister(M68kRegister.A0)] uint locale,
		[M68kRegister(M68kRegister.D0)] uint character)
	{
		return 0;
	}

	[AmigaLvo(-144)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int IsXDigit(
		[M68kRegister(M68kRegister.A0)] uint locale,
		[M68kRegister(M68kRegister.D0)] uint character)
	{
		return 0;
	}

	[AmigaLvo(-150)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint OpenCatalogA(
		[M68kRegister(M68kRegister.A0)] uint locale,
		[M68kRegister(M68kRegister.A1)] CString name,
		[M68kRegister(M68kRegister.A2)] uint tags)
	{
		return 0;
	}

	[AmigaLvo(-156)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint OpenLocale(
		[M68kRegister(M68kRegister.A0)] CString name)
	{
		return 0;
	}

	[AmigaLvo(-162)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int ParseDate(
		[M68kRegister(M68kRegister.A0)] uint locale,
		[M68kRegister(M68kRegister.A1)] uint dateStamp,
		[M68kRegister(M68kRegister.A2)] CString formatString,
		[M68kRegister(M68kRegister.A3)] uint hook)
	{
		return 0;
	}

	[AmigaLvo(-174)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint StrConvert(
		[M68kRegister(M68kRegister.A0)] uint locale,
		[M68kRegister(M68kRegister.A1)] CString stringPtr,
		[M68kRegister(M68kRegister.A2)] uint buffer,
		[M68kRegister(M68kRegister.D0)] uint bufferSize,
		[M68kRegister(M68kRegister.D1)] uint type)
	{
		return 0;
	}

	[AmigaLvo(-180)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int StrnCmp(
		[M68kRegister(M68kRegister.A0)] uint locale,
		[M68kRegister(M68kRegister.A1)] CString string1,
		[M68kRegister(M68kRegister.A2)] CString string2,
		[M68kRegister(M68kRegister.D0)] int length,
		[M68kRegister(M68kRegister.D1)] uint type)
	{
		return 0;
	}

	public static uint OpenCatalog(uint locale, CString name, uint tags) =>
		OpenCatalogA(locale, name, tags);
}
