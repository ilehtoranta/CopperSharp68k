/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

/// <summary>Classic asyncio.library 39.x buffered file-I/O vectors.</summary>
[AmigaLibrary(Name, AmigaLibraryBasePolicy.Manual)]
public static class AsyncIO
{
	public const string Name = "asyncio.library";
	public const short OpenAsyncLvo = -30;
	public const short OpenAsyncFromFHLvo = -36;
	public const short CloseAsyncLvo = -42;
	public const short SeekAsyncLvo = -48;
	public const short ReadAsyncLvo = -54;
	public const short WriteAsyncLvo = -60;
	public const short ReadCharAsyncLvo = -66;
	public const short WriteCharAsyncLvo = -72;
	public const short ReadLineAsyncLvo = -78;
	public const short WriteLineAsyncLvo = -84;
	public const short FGetsAsyncLvo = -90;
	public const short FGetsLenAsyncLvo = -96;
	public const short PeekAsyncLvo = -102;

	public static APTR AsyncIOLibraryBase
	{
		get => throw new System.NotSupportedException(
			"AsyncIOLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"AsyncIOLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(OpenAsyncLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR OpenAsync(
		[M68kRegister(M68kRegister.A0)] CString fileName,
		[M68kRegister(M68kRegister.D0)] AsyncOpenMode mode,
		[M68kRegister(M68kRegister.D1)] int bufferSize);

	[AmigaLvo(OpenAsyncFromFHLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR OpenAsyncFromFH(
		[M68kRegister(M68kRegister.A0)] BPTR handle,
		[M68kRegister(M68kRegister.D0)] AsyncOpenMode mode,
		[M68kRegister(M68kRegister.D1)] int bufferSize);

	[AmigaLvo(CloseAsyncLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int CloseAsync(
		[M68kRegister(M68kRegister.A0)] APTR file);

	[AmigaLvo(SeekAsyncLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SeekAsync(
		[M68kRegister(M68kRegister.A0)] APTR file,
		[M68kRegister(M68kRegister.D0)] int position,
		[M68kRegister(M68kRegister.D1)] AsyncSeekMode mode);

	[AmigaLvo(ReadAsyncLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ReadAsync(
		[M68kRegister(M68kRegister.A0)] APTR file,
		[M68kRegister(M68kRegister.A1)] APTR buffer,
		[M68kRegister(M68kRegister.D0)] int bytes);

	[AmigaLvo(WriteAsyncLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int WriteAsync(
		[M68kRegister(M68kRegister.A0)] APTR file,
		[M68kRegister(M68kRegister.A1)] APTR buffer,
		[M68kRegister(M68kRegister.D0)] int bytes);

	[AmigaLvo(ReadCharAsyncLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ReadCharAsync(
		[M68kRegister(M68kRegister.A0)] APTR file);

	[AmigaLvo(WriteCharAsyncLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int WriteCharAsync(
		[M68kRegister(M68kRegister.A0)] APTR file,
		[M68kRegister(M68kRegister.D0)] uint character);

	[AmigaLvo(ReadLineAsyncLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ReadLineAsync(
		[M68kRegister(M68kRegister.A0)] APTR file,
		[M68kRegister(M68kRegister.A1)] STRPTR buffer,
		[M68kRegister(M68kRegister.D0)] int bytes);

	[AmigaLvo(WriteLineAsyncLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int WriteLineAsync(
		[M68kRegister(M68kRegister.A0)] APTR file,
		[M68kRegister(M68kRegister.A1)] STRPTR buffer);

	[AmigaLvo(FGetsAsyncLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern STRPTR FGetsAsync(
		[M68kRegister(M68kRegister.A0)] APTR file,
		[M68kRegister(M68kRegister.A1)] STRPTR buffer,
		[M68kRegister(M68kRegister.D0)] int bytes);

	[AmigaLvo(FGetsLenAsyncLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern STRPTR FGetsLenAsync(
		[M68kRegister(M68kRegister.A0)] APTR file,
		[M68kRegister(M68kRegister.A1)] STRPTR buffer,
		[M68kRegister(M68kRegister.D0)] int bytes,
		[M68kRegister(M68kRegister.A2)] APTR length);

	[AmigaLvo(PeekAsyncLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int PeekAsync(
		[M68kRegister(M68kRegister.A0)] APTR file,
		[M68kRegister(M68kRegister.A1)] APTR buffer,
		[M68kRegister(M68kRegister.D0)] int bytes);
}
