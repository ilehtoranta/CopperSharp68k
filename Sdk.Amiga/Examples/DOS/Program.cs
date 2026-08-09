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
		var dosBase = Exec.OpenLibrary("dos.library", 0);
		if (dosBase != null)
		{
			DOS.DOSLibraryBase = dosBase.Value;

			var fib = new FileInfoBlock();
			var fibAddress = APTR.ToUInt32(FileInfoBlock.AddressOf(ref fib));
			CString path = argLength > 0
				? argText.Raw
				: "";

			var result = ListDirectory(path, fibAddress);

			Exec.CloseLibrary(DOS.DOSLibraryBase);
			DOS.DOSLibraryBase = APTR.Null;
			return result;
		}

		return DOS.RETURN_FAIL;
	}

	private static int ListDirectory(CString path, uint fib)
	{
		var lock_ = DOS.Lock(path, DOS.LockMode.Shared);
		if (!lock_.HasValue)
		{
			var error = DOS.IoErr();
			DOS.Printf("Cannot lock path, IoErr %ld\n", (uint)error);
			return DOS.RETURN_FAIL;
		}

		var result = DOS.RETURN_OK;
		if (DOS.Examine(lock_.Value, fib) == 0)
		{
			var error = DOS.IoErr();
			DOS.Printf("Examine failed, IoErr %ld\n", (uint)error);
			result = DOS.RETURN_FAIL;
		}
		else if (FileInfoBlock.GetDirEntryType(fib) < 0)
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
			if (error != DOS.Error.NoMoreEntries)
			{
				DOS.Printf("ExNext failed, IoErr %ld\n", (uint)error);
				result = DOS.RETURN_FAIL;
			}
		}

		DOS.UnLock(lock_.Value);
		return result;
	}

	private static void PrintEntry(uint fib)
	{
		var name = FileInfoBlock.FileName(fib);
		DOS.Printf(
			"%-30s %10ld  %ld/%ld/%ld\n",
			name,
			FileInfoBlock.GetSize(fib),
			FileInfoBlock.GetDateDays(fib),
			FileInfoBlock.GetDateMinute(fib),
			FileInfoBlock.GetDateTick(fib));
	}

}
