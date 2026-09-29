using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Amiga;

namespace CopperSharp.Compiler.Tests;

public static class TransparentOutStructFixtures
{
	[StructLayout(LayoutKind.Sequential, Pack = 2)]
	public struct EntriesState
	{
		public APTR Entries;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 2)]
	public struct EntriesRecord
	{
		public uint Magic;
		public APTR Entries;
	}

	public static uint PointerEntry()
	{
		var address = APTR.FromPointer(0x5000);
		APTR.WriteUInt32(address, 0, 0x6000);
		if (!ReadEntries(out var entries)) return 1;
		if (entries.Entries.Raw != 0x5000) return 2;
		if (ReadEntriesValue(entries) != 0x5000) return 3;
		if (!ProjectEntries(out var projected) || projected != 0x5000) return 4;
		uint platform = 0;
		if (ProjectStack(ref platform, APTR.FromPointer(0x2000), APTR.FromPointer(0x2100),
			0x8042B6A1, entries, false) != 0x5000) return 5;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint ProjectStack<T>(ref T platform, APTR state, APTR obj,
		uint attribute, EntriesState value, bool notify) where T : struct
	{
		var stored = default(EntriesRecord);
		stored.Magic = 0x4D434553;
		stored.Entries = value.Entries;
		return ReadStored(ref stored);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint ReadStored(ref EntriesRecord stored) => stored.Entries.Raw;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool ProjectEntries(out uint value)
	{
		value = 0;
		if (!ReadEntries(out var entries)) return false;
		value = entries.Entries.Raw;
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool ReadEntries(out EntriesState entries)
	{
		entries = default;
		entries.Entries = ReadRaw(out var raw) ? APTR.FromPointer(raw) : APTR.Null;
		return Validate(entries.Entries);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool ReadRaw(out uint raw) { raw = 0x5000; return true; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool Validate(APTR value) => value.Raw == 0x5000;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint ReadEntriesValue(EntriesState entries) => entries.Entries.Raw;

	// Deliberately shares its short name with the multi-field nested Header in
	// StructReturnValidationRegressionFixtures. Classification must use the
	// declaring metadata identity, not whichever short name is found first.
	[StructLayout(LayoutKind.Sequential, Pack = 2)]
	public struct Header
	{
		public uint MethodId;
	}

	public static uint Entry()
	{
		if (!Read(out var header)) return 1;
		if (header.MethodId != 0x8042549A) return 2;
		if (ReadByReference(ref header) != 0x8042549A) return 3;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool Read(out Header header)
	{
		header = default;
		header.MethodId = 0x8042549A;
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint ReadByReference(ref Header header) => header.MethodId;
}
