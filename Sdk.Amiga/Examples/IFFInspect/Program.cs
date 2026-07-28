/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using Amiga;
using CopperSharp.Compiler;

namespace IFFInspect;

public sealed class IFFInspectException : Exception
{
	public IFFInspectException(int operation, int error)
	{
		Operation = operation;
		Error = error;
	}

	public int Operation { get; }

	public int Error { get; }
}

public static class Program
{
	private const int OperationArguments = 1;
	private const int OperationOpenFile = 2;
	private const int OperationAllocateIFF = 3;
	private const int OperationOpenIFF = 4;
	private const int OperationParseIFF = 5;

	[M68kEntryPoint]
	public static int Main(int argLength, CONST_STRPTR argText)
	{
		var dosBase = OpenLibrary("dos.library");
		if (dosBase.IsNull)
		{
			return DOS.RETURN_FAIL;
		}

		DOS.DOSLibraryBase = dosBase;
		try
		{
			var iffParseBase = OpenLibrary("iffparse.library");
			if (iffParseBase.IsNull)
			{
				DOS.PutStr("Cannot open iffparse.library\n");
				return DOS.RETURN_FAIL;
			}

			IffParse.IffParseLibraryBase = iffParseBase;
			try
			{
				try
				{
					if (argLength == 0)
					{
						throw new IFFInspectException(OperationArguments, 0);
					}

					Inspect(CString.FromPointer(argText.Raw));
					DOS.PutStr("IFF stream is valid\n");
					return DOS.RETURN_OK;
				}
				catch (IFFInspectException exception)
				{
					DOS.PutStr("IFF inspection failed\n");
					return exception.Operation == OperationAllocateIFF
						? DOS.RETURN_FAIL
						: DOS.RETURN_ERROR;
				}
			}
			finally
			{
				Exec.CloseLibrary(IffParse.IffParseLibraryBase);
				IffParse.IffParseLibraryBase = APTR.Null;
			}
		}
		finally
		{
			Exec.CloseLibrary(DOS.DOSLibraryBase);
			DOS.DOSLibraryBase = APTR.Null;
		}
	}

	private static void Inspect(CString path)
	{
		var file = OpenFile(path);

		try
		{
			var iff = IffParse.AllocIFF();
			if (iff.IsNull)
			{
				throw new IFFInspectException(OperationAllocateIFF, IffParse.IFFERR_NOMEM);
			}

			try
			{
				iff.SetStream(file);
				IffParse.InitIFFasDOS(iff);

				var error = IffParse.OpenIFF(iff, IffParse.IFFF_READ);
				if (error != 0)
				{
					throw new IFFInspectException(OperationOpenIFF, error);
				}

				try
				{
					error = IffParse.ParseIFF(iff, IffParse.IFFPARSE_SCAN);
					if (error != IffParse.IFFERR_EOF)
					{
						throw new IFFInspectException(OperationParseIFF, error);
					}
				}
				finally
				{
					IffParse.CloseIFF(iff);
				}
			}
			finally
			{
				IffParse.FreeIFF(iff);
			}
		}
		finally
		{
			DOS.Close(file);
		}
	}

	private static APTR OpenLibrary(CString name)
	{
		var library = Exec.OpenLibrary(name, 33);
		return library.HasValue
			? library.Value
			: APTR.Null;
	}

	private static BPTR OpenFile(CString path)
	{
		var file = DOS.Open(path, DOS.FileMode.OldFile);
		if (!file.HasValue)
		{
			throw new IFFInspectException(OperationOpenFile, (int)DOS.IoErr());
		}

		return file.Value;
	}
}
