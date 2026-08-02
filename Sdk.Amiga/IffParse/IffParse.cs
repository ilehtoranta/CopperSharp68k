/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

/// <summary>Provides the <c>iffparse.library</c> binding surface.</summary>
/// <remarks>
/// The method bodies are ABI stubs; CopperSharp lowers each call to the
/// corresponding Amiga library-vector invocation.
/// </remarks>
[AmigaLibrary(Name)]
public static class IffParse
{
	public const string Name = "iffparse.library";
	public const int IFFF_READ = 0;
	public const int IFFF_WRITE = 1;
	public const int IFFPARSE_SCAN = 0;
	public const int IFFPARSE_STEP = 1;
	public const int IFFPARSE_RAWSTEP = 2;

	/// <summary>Gets or sets the <c>iffparse.library</c> base pointer.</summary>
	/// <remarks>
	/// This member is a compiler-lowered placeholder. The getter and setter are
	/// replaced by CopperSharp and must not execute as managed code.
	/// </remarks>
	public static APTR IffParseLibraryBase
	{
		get => throw new System.NotSupportedException(
			"IffParseLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"IffParseLibraryBase is lowered by CopperSharp.");
	}

	/// <summary>Allocates an <see cref="IFFHandle"/> for an IFF stream.</summary>
	/// <returns>A new handle, or a null pointer when the library cannot allocate one.</returns>
	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static IFFHandle AllocIFF()
	{
		return new IFFHandle(0);
	}

	/// <summary>Initializes an IFF handle for reading or writing its configured stream.</summary>
	/// <param name="iff">The handle returned by <see cref="AllocIFF"/>.</param>
	/// <param name="rwMode">Either <see cref="IFFF_READ"/> or <see cref="IFFF_WRITE"/>.</param>
	/// <returns>Zero on success; otherwise a negative <see cref="IffError"/> value.</returns>
	[AmigaLvo(-36)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int OpenIFF(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int rwMode)
	{
		return 0;
	}

	/// <summary>Parses the IFF stream associated with a handle.</summary>
	/// <param name="iff">An open IFF handle.</param>
	/// <param name="control">A parse mode: <see cref="IFFPARSE_SCAN"/>, <see cref="IFFPARSE_STEP"/>, or <see cref="IFFPARSE_RAWSTEP"/>.</param>
	/// <returns>Zero while parsing continues, or a negative result such as <see cref="IffError.Eof"/> or <see cref="IffError.Eoc"/>.</returns>
	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int ParseIFF(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int control)
	{
		return 0;
	}

	/// <summary>Ends the current IFF transaction without closing the underlying stream.</summary>
	/// <param name="iff">The handle to close.</param>
	[AmigaLvo(-48)]
	public static void CloseIFF(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff)
	{
	}

	/// <summary>Frees an IFF handle allocated by <see cref="AllocIFF"/>.</summary>
	/// <param name="iff">The handle to release.</param>
	[AmigaLvo(-54)]
	public static void FreeIFF(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff)
	{
	}

	/// <summary>Reads bytes from the current chunk into a client buffer.</summary>
	/// <param name="iff">An open IFF handle positioned inside a chunk.</param>
	/// <param name="buffer">Address of the destination buffer.</param>
	/// <param name="numBytes">Maximum number of bytes to read.</param>
	/// <returns>The number of bytes read, or a negative <see cref="IffError"/> value on failure.</returns>
	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int ReadChunkBytes(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.A1)] uint buffer,
		[M68kRegister(M68kRegister.D0)] int numBytes)
	{
		return 0;
	}

	/// <summary>Writes bytes from a client buffer to the current chunk.</summary>
	/// <param name="iff">An open IFF handle positioned inside a chunk.</param>
	/// <param name="buffer">Address of the source buffer.</param>
	/// <param name="numBytes">Number of bytes to write.</param>
	/// <returns>The number of bytes written, or a negative <see cref="IffError"/> value on failure.</returns>
	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int WriteChunkBytes(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.A1)] uint buffer,
		[M68kRegister(M68kRegister.D0)] int numBytes)
	{
		return 0;
	}

	/// <summary>Reads fixed-size records from the current chunk into a client buffer.</summary>
	/// <param name="iff">An open IFF handle positioned inside a chunk.</param>
	/// <param name="buffer">Address of the destination buffer.</param>
	/// <param name="bytesPerRecord">Size of each record in bytes.</param>
	/// <param name="numRecords">Maximum number of records to read.</param>
	/// <returns>The number of records read, or a negative <see cref="IffError"/> value on failure.</returns>
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

	/// <summary>Writes fixed-size records from a client buffer to the current chunk.</summary>
	/// <param name="iff">An open IFF handle positioned inside a chunk.</param>
	/// <param name="buffer">Address of the source buffer.</param>
	/// <param name="bytesPerRecord">Size of each record in bytes.</param>
	/// <param name="numRecords">Number of records to write.</param>
	/// <returns>The number of records written, or a negative <see cref="IffError"/> value on failure.</returns>
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

	/// <summary>Begins a new chunk while writing an IFF stream.</summary>
	/// <param name="iff">An IFF handle opened for writing.</param>
	/// <param name="type">The chunk type, or zero for a local chunk.</param>
	/// <param name="id">The four-character chunk identifier.</param>
	/// <param name="size">Chunk size in bytes, or the IFF unknown-size sentinel.</param>
	/// <returns>Zero on success; otherwise a negative <see cref="IffError"/> value.</returns>
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

	/// <summary>Completes the current chunk context while writing an IFF stream.</summary>
	[AmigaLvo(-90)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int PopChunk(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff)
	{
		return 0;
	}

	/// <summary>Installs a handler called when matching chunks are entered.</summary>
	/// <param name="iff">The IFF handle whose context receives the handler.</param>
	/// <param name="type">Chunk type to match.</param>
	/// <param name="id">Chunk identifier to match.</param>
	/// <param name="position">Context-stack position in which to install the handler.</param>
	/// <param name="handler">Address of the handler hook.</param>
	/// <param name="objectPtr">Client value passed to the handler.</param>
	/// <returns>Zero on success; otherwise a negative <see cref="IffError"/> value.</returns>
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

	/// <summary>Installs a handler called just before matching chunks are exited.</summary>
	/// <param name="iff">The IFF handle whose context receives the handler.</param>
	/// <param name="type">Chunk type to match.</param>
	/// <param name="id">Chunk identifier to match.</param>
	/// <param name="position">Context-stack position in which to install the handler.</param>
	/// <param name="handler">Address of the handler hook.</param>
	/// <param name="objectPtr">Client value passed to the handler.</param>
	/// <returns>Zero on success; otherwise a negative <see cref="IffError"/> value.</returns>
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

	/// <summary>Declares a property chunk whose data should be retained in the current scope.</summary>
	/// <param name="iff">The IFF handle being configured.</param>
	/// <param name="type">Type of the property chunk.</param>
	/// <param name="id">Identifier of the property chunk.</param>
	/// <returns>Zero on success; otherwise a negative <see cref="IffError"/> value.</returns>
	[AmigaLvo(-114)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int PropChunk(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int type,
		[M68kRegister(M68kRegister.D1)] int id)
	{
		return 0;
	}

	/// <summary>Declares multiple property chunks from an array of type and ID pairs.</summary>
	/// <param name="iff">The IFF handle being configured.</param>
	/// <param name="propArray">Address of an array containing type/ID pairs.</param>
	/// <param name="numPairs">Number of type/ID pairs in <paramref name="propArray"/>.</param>
	/// <returns>Zero on success; otherwise a negative <see cref="IffError"/> value.</returns>
	[AmigaLvo(-120)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int PropChunks(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.A1)] uint propArray,
		[M68kRegister(M68kRegister.D0)] int numPairs)
	{
		return 0;
	}

	/// <summary>Declares a chunk that causes <see cref="ParseIFF"/> to stop when entered.</summary>
	/// <param name="iff">The IFF handle being configured.</param>
	/// <param name="type">Type of the chunk to stop on.</param>
	/// <param name="id">Identifier of the chunk to stop on.</param>
	/// <returns>Zero on success; otherwise a negative <see cref="IffError"/> value.</returns>
	[AmigaLvo(-126)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int StopChunk(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int type,
		[M68kRegister(M68kRegister.D1)] int id)
	{
		return 0;
	}

	/// <summary>Declares multiple stop chunks from an array of type and ID pairs.</summary>
	/// <param name="iff">The IFF handle being configured.</param>
	/// <param name="propArray">Address of an array containing type/ID pairs.</param>
	/// <param name="numPairs">Number of type/ID pairs in <paramref name="propArray"/>.</param>
	/// <returns>Zero on success; otherwise a negative <see cref="IffError"/> value.</returns>
	[AmigaLvo(-132)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int StopChunks(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.A1)] uint propArray,
		[M68kRegister(M68kRegister.D0)] int numPairs)
	{
		return 0;
	}

	/// <summary>Declares a chunk whose every occurrence should be collected.</summary>
	/// <param name="iff">The IFF handle being configured.</param>
	/// <param name="type">Type of the collection chunk.</param>
	/// <param name="id">Identifier of the collection chunk.</param>
	/// <returns>Zero on success; otherwise a negative <see cref="IffError"/> value.</returns>
	[AmigaLvo(-138)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int CollectionChunk(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int type,
		[M68kRegister(M68kRegister.D1)] int id)
	{
		return 0;
	}

	/// <summary>Declares multiple collection chunks from an array of type and ID pairs.</summary>
	/// <param name="iff">The IFF handle being configured.</param>
	/// <param name="collectionArray">Address of an array containing type/ID pairs.</param>
	/// <param name="numPairs">Number of type/ID pairs in <paramref name="collectionArray"/>.</param>
	/// <returns>Zero on success; otherwise a negative <see cref="IffError"/> value.</returns>
	[AmigaLvo(-144)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int CollectionChunks(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.A1)] uint collectionArray,
		[M68kRegister(M68kRegister.D0)] int numPairs)
	{
		return 0;
	}

	/// <summary>Declares a chunk that causes <see cref="ParseIFF"/> to stop just before it is exited.</summary>
	/// <param name="iff">The IFF handle being configured.</param>
	/// <param name="type">Type of the chunk to stop on.</param>
	/// <param name="id">Identifier of the chunk to stop on.</param>
	/// <returns>Zero on success; otherwise a negative <see cref="IffError"/> value.</returns>
	[AmigaLvo(-150)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int StopOnExit(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int type,
		[M68kRegister(M68kRegister.D1)] int id)
	{
		return 0;
	}

	/// <summary>Finds a stored property chunk in the active context stack.</summary>
	/// <param name="iff">The IFF handle to search.</param>
	/// <param name="type">Type of the property chunk.</param>
	/// <param name="id">Identifier of the property chunk.</param>
	/// <returns>The address of the stored property, or zero if it is not in scope.</returns>
	[AmigaLvo(-156)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint FindProp(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int type,
		[M68kRegister(M68kRegister.D1)] int id)
	{
		return 0;
	}

	/// <summary>Finds the collected instances of a collection chunk in the active context stack.</summary>
	/// <param name="iff">The IFF handle to search.</param>
	/// <param name="type">Type of the collection chunk.</param>
	/// <param name="id">Identifier of the collection chunk.</param>
	/// <returns>The address of the collection, or zero if no matching chunk is in scope.</returns>
	[AmigaLvo(-162)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint FindCollection(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int type,
		[M68kRegister(M68kRegister.D1)] int id)
	{
		return 0;
	}

	/// <summary>Finds the context in which a property should be stored.</summary>
	/// <param name="iff">The IFF handle to inspect.</param>
	/// <returns>The address of the topmost applicable FORM or LIST context, or zero if none exists.</returns>
	[AmigaLvo(-168)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint FindPropContext(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff)
	{
		return 0;
	}

	/// <summary>Returns the context node for the current chunk.</summary>
	/// <param name="iff">The IFF handle to inspect.</param>
	/// <returns>The address of the current context node, or zero when no context is active.</returns>
	[AmigaLvo(-174)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint CurrentChunk(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff)
	{
		return 0;
	}

	/// <summary>Returns the parent context node of a context.</summary>
	/// <param name="contextNode">Address of the context node whose parent is requested.</param>
	/// <returns>The address of the parent context node, or zero when the node has no parent.</returns>
	[AmigaLvo(-180)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint ParentChunk(
		[M68kRegister(M68kRegister.A0)] uint contextNode)
	{
		return 0;
	}

	/// <summary>Allocates a local context item and its client data buffer.</summary>
	/// <param name="type">Type associated with the item.</param>
	/// <param name="id">Identifier associated with the item.</param>
	/// <param name="ident">Application-defined identification value.</param>
	/// <param name="dataSize">Number of client data bytes to allocate.</param>
	/// <returns>The address of the local item, or zero if allocation fails.</returns>
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

	/// <summary>Returns the client data buffer belonging to a local context item.</summary>
	/// <param name="localItem">Address of a local context item.</param>
	/// <returns>The address of the item data buffer, or zero if unavailable.</returns>
	[AmigaLvo(-192)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint LocalItemData(
		[M68kRegister(M68kRegister.A0)] uint localItem)
	{
		return 0;
	}

	/// <summary>Installs a purge hook that runs when a local context item is released.</summary>
	/// <param name="localItem">Address of the local context item.</param>
	/// <param name="purgeHook">Address of the purge hook.</param>
	[AmigaLvo(-198)]
	public static void SetLocalItemPurge(
		[M68kRegister(M68kRegister.A0)] uint localItem,
		[M68kRegister(M68kRegister.A1)] uint purgeHook)
	{
	}

	/// <summary>Frees a local context item allocated by <see cref="AllocLocalItem"/>.</summary>
	/// <param name="localItem">Address of the local context item to release.</param>
	[AmigaLvo(-204)]
	public static void FreeLocalItem(
		[M68kRegister(M68kRegister.A0)] uint localItem)
	{
	}

	/// <summary>Searches the active context stack for a matching local context item.</summary>
	/// <param name="iff">The IFF handle to search.</param>
	/// <param name="type">Type associated with the item.</param>
	/// <param name="id">Identifier associated with the item.</param>
	/// <param name="ident">Identification value associated with the item.</param>
	/// <returns>The address of the nearest matching item, or zero when none is found.</returns>
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

	/// <summary>Stores a local context item at a position in the IFF context stack.</summary>
	/// <param name="iff">The IFF handle whose context stack receives the item.</param>
	/// <param name="localItem">Address of the local context item to store.</param>
	/// <param name="position">Storage position, such as the root, current, or property context.</param>
	/// <returns>Zero on success; otherwise a negative <see cref="IffError"/> value.</returns>
	[AmigaLvo(-216)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int StoreLocalItem(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.A1)] uint localItem,
		[M68kRegister(M68kRegister.D0)] int position)
	{
		return 0;
	}

	/// <summary>Stores a local context item in a specific context node.</summary>
	/// <param name="iff">The IFF handle owning the context node.</param>
	/// <param name="localItem">Address of the local context item to store.</param>
	/// <param name="contextNode">Address of the destination context node.</param>
	[AmigaLvo(-222)]
	public static void StoreItemInContext(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.A1)] uint localItem,
		[M68kRegister(M68kRegister.A2)] uint contextNode)
	{
	}

	/// <summary>Initializes an IFF handle for an application-provided stream hook.</summary>
	/// <param name="iff">The handle whose stream has been configured by the caller.</param>
	/// <param name="flags">Stream capability flags, including forward or random seek support.</param>
	/// <param name="streamHook">Address of the stream hook used for I/O.</param>
	[AmigaLvo(-228)]
	public static void InitIFF(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff,
		[M68kRegister(M68kRegister.D0)] int flags,
		[M68kRegister(M68kRegister.A1)] uint streamHook)
	{
	}

	/// <summary>Initializes an IFF handle to use its configured AmigaDOS stream.</summary>
	/// <param name="iff">The handle whose stream is an AmigaDOS file handle.</param>
	[AmigaLvo(-234)]
	public static void InitIFFasDOS(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff)
	{
	}

	/// <summary>Initializes an IFF handle to use its configured clipboard stream.</summary>
	/// <param name="iff">The handle whose stream is a clipboard handle from <see cref="OpenClipboard"/>.</param>
	[AmigaLvo(-240)]
	public static void InitIFFasClip(
		[M68kRegister(M68kRegister.A0)] IFFHandle iff)
	{
	}

	/// <summary>Opens a clipboard.device stream for an IFF handle.</summary>
	/// <param name="unitNumber">Clipboard unit number, normally the primary clipboard unit.</param>
	/// <returns>The address of a clipboard handle, or zero if it cannot be opened.</returns>
	[AmigaLvo(-246)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint OpenClipboard(
		[M68kRegister(M68kRegister.D0)] int unitNumber)
	{
		return 0;
	}

	/// <summary>Closes a clipboard stream opened by <see cref="OpenClipboard"/>.</summary>
	/// <param name="clipboardHandle">Address of the clipboard handle to close.</param>
	[AmigaLvo(-252)]
	public static void CloseClipboard(
		[M68kRegister(M68kRegister.A0)] uint clipboardHandle)
	{
	}

	/// <summary>Checks whether a value is a valid IFF chunk identifier.</summary>
	/// <param name="id">The four-character identifier to validate.</param>
	/// <returns>Non-zero when the identifier is valid; otherwise zero.</returns>
	[AmigaLvo(-258)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int GoodID(
		[M68kRegister(M68kRegister.D0)] int id)
	{
		return 0;
	}

	/// <summary>Checks whether a value is a valid IFF chunk type.</summary>
	/// <param name="type">The four-character type to validate.</param>
	/// <returns>Non-zero when the type is valid; otherwise zero.</returns>
	[AmigaLvo(-264)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int GoodType(
		[M68kRegister(M68kRegister.D0)] int type)
	{
		return 0;
	}

	/// <summary>Converts a packed IFF identifier to its four-character string form.</summary>
	/// <param name="id">The packed four-character identifier.</param>
	/// <param name="buffer">Address of a caller-provided character buffer.</param>
	/// <returns>The address of <paramref name="buffer"/> after it has been filled.</returns>
	[AmigaLvo(-270)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint IDtoStr(
		[M68kRegister(M68kRegister.D0)] int id,
		[M68kRegister(M68kRegister.A0)] uint buffer)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	/// <summary>Seeks to a byte position within the current chunk.</summary>
	/// <param name="iff">An open IFF handle positioned inside a chunk.</param>
	/// <param name="position">Byte position or offset, interpreted according to <paramref name="mode"/>.</param>
	/// <param name="mode">Seek mode supplied by the MorphOS extension.</param>
	/// <returns>Zero on success; otherwise a negative <see cref="IffError"/> value.</returns>
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
	/// <summary>Seeks to a record position within the current chunk.</summary>
	/// <param name="iff">An open IFF handle positioned inside a chunk.</param>
	/// <param name="position">Record position or offset, interpreted according to <paramref name="mode"/>.</param>
	/// <param name="records">Number of records associated with the seek operation.</param>
	/// <param name="mode">Seek mode supplied by the MorphOS extension.</param>
	/// <returns>Zero on success; otherwise a negative <see cref="IffError"/> value.</returns>
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
