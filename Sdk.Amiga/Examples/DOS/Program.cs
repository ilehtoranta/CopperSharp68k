/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using Amiga;
using CopperSharp.Compiler;

namespace DOSExample;

public static class Program
{
	private const int RETURN_OK = 0;
	private const int RETURN_FAIL = 20;
	private const int SHARED_LOCK = -2;
	private const int ERROR_NO_MORE_ENTRIES = 232;

	private const int FIB_FILE_NAME = 8;
	private const int FIB_DIR_ENTRY_TYPE = 4;
	private const int FIB_SIZE = 124;
	private const int FIB_DATE_DAYS = 132;
	private const int FIB_DATE_MINUTE = 136;
	private const int FIB_DATE_TICK = 140;

	[M68kEntryPoint]
	public static unsafe int Main(int argLength, CONST_STRPTR argText)
	{
		var dosBase = Exec.OpenLibrary(CString.FromLiteral("dos.library"), 34);
		if (!dosBase.HasValue)
		{
			return RETURN_FAIL;
		}

		DOS.DOSLibraryBase = dosBase.Value;

		FileInfoBlock fib = default;
		var fibAddress = AlignLong((uint)(nuint)(&fib));
		var path = argLength > 0 && argText.IsNotNull
			? CString.FromPointer(argText.Raw)
			: CString.FromLiteral("");

		var result = ListDirectory(path, fibAddress);

		DOS.DOSLibraryBase = APTR.Null;
		Exec.CloseLibrary(dosBase.Value);
		return result;
	}

	private static int ListDirectory(CString path, uint fib)
	{
		var lock_ = DOS.Lock(path, SHARED_LOCK);
		if (!lock_.HasValue)
		{
			var error = DOS.IoErr();
			DOS.Printf(CString.FromLiteral("Cannot lock path, IoErr %ld\n"), (uint)error);
			return RETURN_FAIL;
		}

		var result = RETURN_OK;
		if (DOS.Examine(lock_.Value, fib) == 0)
		{
			var error = DOS.IoErr();
			DOS.Printf(CString.FromLiteral("Examine failed, IoErr %ld\n"), (uint)error);
			result = RETURN_FAIL;
		}
		else if (ReadLong(fib, FIB_DIR_ENTRY_TYPE) < 0)
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
			if (error != ERROR_NO_MORE_ENTRIES)
			{
				DOS.Printf(CString.FromLiteral("ExNext failed, IoErr %ld\n"), (uint)error);
				result = RETURN_FAIL;
			}
		}

		DOS.UnLock(lock_.Value);
		return result;
	}

	private static void PrintEntry(uint fib)
	{
		var name = CString.FromPointer(fib + FIB_FILE_NAME);
		var size = ReadLong(fib, FIB_SIZE);
		var days = ReadLong(fib, FIB_DATE_DAYS);
		var minute = ReadLong(fib, FIB_DATE_MINUTE);
		var tick = ReadLong(fib, FIB_DATE_TICK);
		DOS.Printf(CString.FromLiteral("%-30s "), CString.ToUInt32(name));
		DOS.Printf(CString.FromLiteral("%10ld  "), (uint)size);
		DOS.Printf(CString.FromLiteral("%ld/"), (uint)days);
		DOS.Printf(CString.FromLiteral("%ld/"), (uint)minute);
		DOS.Printf(CString.FromLiteral("%ld\n"), (uint)tick);
	}

	private static unsafe int ReadLong(uint address, int offset) =>
		*(int*)(address + (uint)offset);

	private static uint AlignLong(uint address) =>
		(address + 3u) & 0xFFFF_FFFCu;

	private struct FileInfoBlock
	{
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
}
