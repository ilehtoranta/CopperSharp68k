/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using Amiga;
using CopperSharp.Compiler;

namespace DOSExample;

public static class Program
{
	private struct FileInfoBlockStackStorage
	{
		public static APTR AddressOf(ref FileInfoBlockStackStorage storage) =>
			throw new System.NotSupportedException(
				"FileInfoBlockStackStorage.AddressOf is lowered by CopperSharp.");

		public uint Long000;
		public uint Long001;
		public uint Long002;
		public uint Long003;
		public uint Long004;
		public uint Long005;
		public uint Long006;
		public uint Long007;
		public uint Long008;
		public uint Long009;
		public uint Long010;
		public uint Long011;
		public uint Long012;
		public uint Long013;
		public uint Long014;
		public uint Long015;
		public uint Long016;
		public uint Long017;
		public uint Long018;
		public uint Long019;
		public uint Long020;
		public uint Long021;
		public uint Long022;
		public uint Long023;
		public uint Long024;
		public uint Long025;
		public uint Long026;
		public uint Long027;
		public uint Long028;
		public uint Long029;
		public uint Long030;
		public uint Long031;
		public uint Long032;
		public uint Long033;
		public uint Long034;
		public uint Long035;
		public uint Long036;
		public uint Long037;
		public uint Long038;
		public uint Long039;
		public uint Long040;
		public uint Long041;
		public uint Long042;
		public uint Long043;
		public uint Long044;
		public uint Long045;
		public uint Long046;
		public uint Long047;
		public uint Long048;
		public uint Long049;
		public uint Long050;
		public uint Long051;
		public uint Long052;
		public uint Long053;
		public uint Long054;
		public uint Long055;
		public uint Long056;
		public uint Long057;
		public uint Long058;
		public uint Long059;
		public uint Long060;
		public uint Long061;
		public uint Long062;
		public uint Long063;
		public uint Long064;
		public uint Long065;
	}

	[M68kEntryPoint]
	public static int Main(int argLength, CONST_STRPTR argText)
	{
		var dosBase = Exec.OpenLibrary(CString.FromLiteral("dos.library"), 33);
		if (dosBase != null)
		{
			DOS.DOSLibraryBase = dosBase.Value;

			var fibStorage = new FileInfoBlockStackStorage();
			var fibAddress = AlignLong(APTR.ToUInt32(FileInfoBlockStackStorage.AddressOf(ref fibStorage)));
			var path = argLength > 0
				? CString.FromPointer(argText.Raw)
				: CString.FromLiteral("");

			var result = ListDirectory(path, fibAddress);

			Exec.CloseLibrary(DOS.DOSLibraryBase);
			DOS.DOSLibraryBase = APTR.Null;
			return result;
		}

		return DOS.RETURN_FAIL;
	}

	private static int ListDirectory(CString path, uint fib)
	{
		var lock_ = DOS.Lock(path, DOS.SHARED_LOCK);
		if (!lock_.HasValue)
		{
			var error = DOS.IoErr();
			DOS.Printf(CString.FromLiteral("Cannot lock path, IoErr %ld\n"), (uint)error);
			return DOS.RETURN_FAIL;
		}

		var result = DOS.RETURN_OK;
		if (DOS.Examine(lock_.Value, fib) == 0)
		{
			var error = DOS.IoErr();
			DOS.Printf(CString.FromLiteral("Examine failed, IoErr %ld\n"), (uint)error);
			result = DOS.RETURN_FAIL;
		}
		else if ((int)APTR.ReadUInt32(APTR.FromPointer(fib), FileInfoBlock.DirEntryTypeOffset) < 0)
		{
			PrintEntry(fib);
		}
		else
		{
			while (DOS.ExNext(lock_.Value, fib) != 0)
			{
				PrintEntry(fib);
			}

			var error = DOS.IoErr();
			if (error != DOS.ERROR_NO_MORE_ENTRIES)
			{
				DOS.Printf(CString.FromLiteral("ExNext failed, IoErr %ld\n"), (uint)error);
				result = DOS.RETURN_FAIL;
			}
		}

		DOS.UnLock(lock_.Value);
		return result;
	}

	private static void PrintEntry(uint fib)
	{
		var name = FileInfoBlock.FileName(fib);
		var size = APTR.ReadUInt32(APTR.FromPointer(fib), FileInfoBlock.SizeOffset);
		var days = APTR.ReadUInt32(APTR.FromPointer(fib), FileInfoBlock.DateDaysOffset);
		var minute = APTR.ReadUInt32(APTR.FromPointer(fib), FileInfoBlock.DateMinuteOffset);
		var tick = APTR.ReadUInt32(APTR.FromPointer(fib), FileInfoBlock.DateTickOffset);
		DOS.Printf(
			CString.FromLiteral("%-30s %10ld  %ld/%ld/%ld\n"),
			name,
			size,
			days,
			minute,
			tick);
	}

	private static uint AlignLong(uint address) =>
		(address + 3u) & 0xFFFF_FFFCu;
}
