/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using Amiga;
using CopperSharp.Compiler;

namespace FileStatsExample;

public static class Program
{
	[M68kEntryPoint]
	public static int Main(int argLength, CONST_STRPTR argText)
	{
		var dosBase = Exec.OpenLibrary("dos.library", 0);
		if (!dosBase.HasValue)
		{
			return DOS.RETURN_FAIL;
		}

		DOS.DOSLibraryBase = dosBase.Value;
		var result = DOS.RETURN_OK;
		if (argLength == 0)
		{
			DOS.PutStr("Usage: filestats <file>\n");
			result = DOS.RETURN_ERROR;
		}
		else
		{
			result = Report(CString.FromPointer(argText.Raw));
		}

		Exec.CloseLibrary(DOS.DOSLibraryBase);
		DOS.DOSLibraryBase = APTR.Null;
		return result;
	}

	private static int Report(CString path)
	{
		var file = DOS.Open(path, DOS.FileMode.OldFile);
		if (!file.HasValue)
		{
			DOS.PutStr("Cannot open file\n");
			return DOS.RETURN_FAIL;
		}

		byte byteChecksum = 0;
		ushort lineCount = 0;
		ushort wordChecksum = 0;
		uint byteCount = 0;
		uint byteSum = 0;
		uint rollingHash = 2166136261;

		while (true)
		{
			var character = DOS.FGetC(file.Value);
			if (character < 0)
			{
				break;
			}

			var value = (byte)character;
			byteChecksum = (byte)(byteChecksum + value);
			wordChecksum = (ushort)(((wordChecksum << 5) | (wordChecksum >> 11)) + value);
			byteCount = byteCount + 1;
			byteSum = byteSum + value;
			rollingHash = (rollingHash ^ value) * 16777619;
			if (value == 10)
			{
				lineCount = (ushort)(lineCount + 1);
			}
		}

		DOS.Close(file.Value);
		var averageByte = byteCount == 0 ? 0 : byteSum / byteCount;
		PrintReport(
			path,
			byteCount,
			(uint)lineCount,
			(uint)byteChecksum,
			(uint)wordChecksum,
			averageByte,
			rollingHash);
		return DOS.RETURN_OK;
	}

	private static void PrintReport(
		CString path,
		uint byteCount,
		uint lineCount,
		uint byteChecksum,
		uint wordChecksum,
		uint averageByte,
		uint rollingHash)
	{
		DOS.Printf(
			"%s: %ld bytes, %ld lines, byte checksum %ld, word checksum %ld, average byte %ld, hash %ld\n",
			path,
			byteCount,
			lineCount,
			byteChecksum,
			wordChecksum,
			averageByte,
			rollingHash);
	}
}
