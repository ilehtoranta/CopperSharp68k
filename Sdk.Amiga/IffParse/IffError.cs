/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>Negative status values returned by <c>iffparse.library</c>.</summary>
/// <remarks>
/// The numeric values mirror the AmigaOS ABI. IFFParse methods return these
/// values as <c>int</c> because several of the same methods also return byte
/// or record counts on success.
/// </remarks>
public enum IffError : int
{
	/// <summary>The end of the input stream was reached.</summary>
	Eof = -1,
	/// <summary>The end of the current IFF context was reached.</summary>
	Eoc = -2,
	/// <summary>The operation has no active scope.</summary>
	NoScope = -3,
	/// <summary>An internal memory allocation failed.</summary>
	NoMem = -4,
	/// <summary>A stream read failed.</summary>
	Read = -5,
	/// <summary>A stream write failed.</summary>
	Write = -6,
	/// <summary>A stream seek failed.</summary>
	Seek = -7,
	/// <summary>The IFF data is malformed.</summary>
	Mangled = -8,
	/// <summary>The IFF syntax is invalid.</summary>
	Syntax = -9,
	/// <summary>The stream is not an IFF stream.</summary>
	NotIff = -10,
	/// <summary>A required hook is missing.</summary>
	NoHook = -11,
	/// <summary>A handler requested control to return to the client.</summary>
	Return2Client = -12
}
