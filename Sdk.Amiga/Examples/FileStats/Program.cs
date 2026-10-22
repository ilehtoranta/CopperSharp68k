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
		var dosBase = Exec.OpenLibrary("dos.library", 37);
		if (!dosBase.HasValue)
		{
			return DOS.RETURN_FAIL;
		}

		DOS.DOSLibraryBase = dosBase.Value;
		var result = DOS.RETURN_OK;
		// DOS command lines include their terminating newline, including an
		// otherwise empty line. Direct callers may still supply length zero.
		if (argLength == 0 ||
			(argLength == 1 && APTR.ReadUInt8(argText.Address, 0) == (byte)'\n'))
		{
			DOS.PutStr("Usage: filestats <file>\n");
			result = DOS.RETURN_ERROR;
		}
		else
		{
			// Native DOS commands receive a command line ending in LF. Direct
			// callers without LF retain the existing filename-pointer contract.
			result = APTR.ReadUInt8(argText.Address, argLength - 1) == (byte)'\n'
				? ReportCommandLine(argLength, argText)
				: Report(CString.FromPointer(argText.Raw));
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

	private static int ReportCommandLine(int length, CONST_STRPTR text)
	{
		var args = DOS.AllocDosObject((uint)DosObjectType.RdArgs, APTR.Null);
		if (args.IsNull) return DOS.RETURN_FAIL;
		var values = Exec.AllocMem(4, Exec.MemoryFlags.Public | Exec.MemoryFlags.Clear);
		if (values.IsNull)
		{
			DOS.FreeDosObject((uint)DosObjectType.RdArgs, args);
			return DOS.RETURN_FAIL;
		}
		APTR.WriteUInt32(args, DosLayout.CSource.Buffer, text.Raw);
		APTR.WriteUInt32(args, DosLayout.CSource.Length, (uint)length);
		APTR.WriteUInt32(args, DosLayout.RDArgs.Flags, (uint)DosRdArgsFlags.NoPrompt);
		var parsed = DOS.ReadArgs("FILE/A", values, args);
		var result = DOS.RETURN_ERROR;
		if (!parsed.IsNull)
		{
			result = Report(CString.FromPointer(APTR.ReadUInt32(values, 0)));
			DOS.FreeArgs(parsed);
		}
		else DOS.PutStr("Usage: filestats <file>\n");
		Exec.FreeMem(values, 4);
		DOS.FreeDosObject((uint)DosObjectType.RdArgs, args);
		return result;
	}
}
