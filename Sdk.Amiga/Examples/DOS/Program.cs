/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using Amiga;
using CopperSharp.Compiler;

namespace DOSExample;

public static class Program
{
	[M68kEntryPoint]
	public static int Main(int argLength, CONST_STRPTR argText)
	{
		var dosBase = Exec.OpenLibrary(CString.FromLiteral("dos.library"), 33);
		if (dosBase != null)
		{
			DOS.DOSLibraryBase = dosBase.Value;

			var fib = new FileInfoBlock();
			var fibAddress = APTR.ToUInt32(FileInfoBlock.AddressOf(ref fib));
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

}
