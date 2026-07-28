/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class IffParse
{
	public const string Name = "iffparse.library";
	public const int IFFF_READ = 0;
	public const int IFFF_WRITE = 1;
	public const int IFFPARSE_SCAN = 0;
	public const int IFFPARSE_STEP = 1;
	public const int IFFPARSE_RAWSTEP = 2;
	public const int IFFERR_EOF = -1;
	public const int IFFERR_EOC = -2;
	public const int IFFERR_NOSCOPE = -3;
	public const int IFFERR_NOMEM = -4;
	public const int IFFERR_READ = -5;
	public const int IFFERR_WRITE = -6;
	public const int IFFERR_SEEK = -7;
	public const int IFFERR_MANGLED = -8;
	public const int IFFERR_SYNTAX = -9;
	public const int IFFERR_NOTIFF = -10;
	public const int IFFERR_NOHOOK = -11;
	public const int IFF_RETURN2CLIENT = -12;

	public static APTR IffParseLibraryBase
	{
		get => throw new System.NotSupportedException(
			"IffParseLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"IffParseLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static IFFHandle AllocIFF()
	{
		return new IFFHandle(0);
	}

	[AmigaLvo(-36)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int OpenIFF(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int rwMode)
	{
		return 0;
	}

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int ParseIFF(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int control)
	{
		return 0;
	}

	[AmigaLvo(-48)]
	public static void CloseIFF(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff)
	{
	}

	[AmigaLvo(-54)]
	public static void FreeIFF(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff)
	{
	}

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int ReadChunkBytes(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.A1)] uint buffer,
		[M68kRegister(M68kRegister.D0)] int numBytes)
	{
		return 0;
	}

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int WriteChunkBytes(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.A1)] uint buffer,
		[M68kRegister(M68kRegister.D0)] int numBytes)
	{
		return 0;
	}

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int ReadChunkRecords(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.A1)] uint buffer,
		[M68kRegister(M68kRegister.D0)] int bytesPerRecord,
		[M68kRegister(M68kRegister.D1)] int numRecords)
	{
		return 0;
	}

	[AmigaLvo(-78)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int WriteChunkRecords(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.A1)] uint buffer,
		[M68kRegister(M68kRegister.D0)] int bytesPerRecord,
		[M68kRegister(M68kRegister.D1)] int numRecords)
	{
		return 0;
	}

	[AmigaLvo(-84)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int PushChunk(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int type,
		[M68kRegister(M68kRegister.D1)] int id,
		[M68kRegister(M68kRegister.D2)] int size)
	{
		return 0;
	}

	[AmigaLvo(-90)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int PopChunk(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff)
	{
		return 0;
	}

	[AmigaLvo(-102)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int EntryHandler(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int type,
		[M68kRegister(M68kRegister.D1)] int id,
		[M68kRegister(M68kRegister.D2)] int position,
		[M68kRegister(M68kRegister.A1)] uint handler,
		[M68kRegister(M68kRegister.A2)] uint objectPtr)
	{
		return 0;
	}

	[AmigaLvo(-108)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int ExitHandler(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int type,
		[M68kRegister(M68kRegister.D1)] int id,
		[M68kRegister(M68kRegister.D2)] int position,
		[M68kRegister(M68kRegister.A1)] uint handler,
		[M68kRegister(M68kRegister.A2)] uint objectPtr)
	{
		return 0;
	}

	[AmigaLvo(-114)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int PropChunk(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int type,
		[M68kRegister(M68kRegister.D1)] int id)
	{
		return 0;
	}

	[AmigaLvo(-120)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int PropChunks(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.A1)] uint propArray,
		[M68kRegister(M68kRegister.D0)] int numPairs)
	{
		return 0;
	}

	[AmigaLvo(-126)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int StopChunk(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int type,
		[M68kRegister(M68kRegister.D1)] int id)
	{
		return 0;
	}

	[AmigaLvo(-132)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int StopChunks(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.A1)] uint propArray,
		[M68kRegister(M68kRegister.D0)] int numPairs)
	{
		return 0;
	}

	[AmigaLvo(-138)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int CollectionChunk(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int type,
		[M68kRegister(M68kRegister.D1)] int id)
	{
		return 0;
	}

	[AmigaLvo(-144)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int CollectionChunks(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.A1)] uint collectionArray,
		[M68kRegister(M68kRegister.D0)] int numPairs)
	{
		return 0;
	}

	[AmigaLvo(-150)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int StopOnExit(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int type,
		[M68kRegister(M68kRegister.D1)] int id)
	{
		return 0;
	}

	[AmigaLvo(-156)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint FindProp(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int type,
		[M68kRegister(M68kRegister.D1)] int id)
	{
		return 0;
	}

	[AmigaLvo(-162)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint FindCollection(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int type,
		[M68kRegister(M68kRegister.D1)] int id)
	{
		return 0;
	}

	[AmigaLvo(-168)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint FindPropContext(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff)
	{
		return 0;
	}

	[AmigaLvo(-174)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint CurrentChunk(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff)
	{
		return 0;
	}

	[AmigaLvo(-180)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint ParentChunk(
		[M68kRegister(M68kRegister.A0)] uint contextNode)
	{
		return 0;
	}

	[AmigaLvo(-186)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint AllocLocalItem(
		[M68kRegister(M68kRegister.D0)] int type,
		[M68kRegister(M68kRegister.D1)] int id,
		[M68kRegister(M68kRegister.D2)] int ident,
		[M68kRegister(M68kRegister.D3)] int dataSize)
	{
		return 0;
	}

	[AmigaLvo(-192)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint LocalItemData(
		[M68kRegister(M68kRegister.A0)] uint localItem)
	{
		return 0;
	}

	[AmigaLvo(-198)]
	public static void SetLocalItemPurge(
		[M68kRegister(M68kRegister.A0)] uint localItem,
		[M68kRegister(M68kRegister.A1)] uint purgeHook)
	{
	}

	[AmigaLvo(-204)]
	public static void FreeLocalItem(
		[M68kRegister(M68kRegister.A0)] uint localItem)
	{
	}

	[AmigaLvo(-210)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint FindLocalItem(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int type,
		[M68kRegister(M68kRegister.D1)] int id,
		[M68kRegister(M68kRegister.D2)] int ident)
	{
		return 0;
	}

	[AmigaLvo(-216)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int StoreLocalItem(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.A1)] uint localItem,
		[M68kRegister(M68kRegister.D0)] int position)
	{
		return 0;
	}

	[AmigaLvo(-222)]
	public static void StoreItemInContext(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.A1)] uint localItem,
		[M68kRegister(M68kRegister.A2)] uint contextNode)
	{
	}

	[AmigaLvo(-228)]
	public static void InitIFF(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int flags,
		[M68kRegister(M68kRegister.A1)] uint streamHook)
	{
	}

	[AmigaLvo(-234)]
	public static void InitIFFasDOS(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff)
	{
	}

	[AmigaLvo(-240)]
	public static void InitIFFasClip(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff)
	{
	}

	[AmigaLvo(-246)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint OpenClipboard(
		[M68kRegister(M68kRegister.D0)] int unitNumber)
	{
		return 0;
	}

	[AmigaLvo(-252)]
	public static void CloseClipboard(
		[M68kRegister(M68kRegister.A0)] uint clipboardHandle)
	{
	}

	[AmigaLvo(-258)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int GoodID(
		[M68kRegister(M68kRegister.D0)] int id)
	{
		return 0;
	}

	[AmigaLvo(-264)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int GoodType(
		[M68kRegister(M68kRegister.D0)] int type)
	{
		return 0;
	}

	[AmigaLvo(-270)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint IDtoStr(
		[M68kRegister(M68kRegister.D0)] int id,
		[M68kRegister(M68kRegister.A0)] uint buffer)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-276)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int SeekChunkBytes(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int position,
		[M68kRegister(M68kRegister.D1)] int mode)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-282)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int SeekChunkRecords(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int position,
		[M68kRegister(M68kRegister.D1)] int records,
		[M68kRegister(M68kRegister.D2)] int mode)
	{
		return 0;
	}
}
