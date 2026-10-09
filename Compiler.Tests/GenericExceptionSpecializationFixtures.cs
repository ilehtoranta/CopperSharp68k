/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private static int _genericExceptionTrace;

	public static int GenericExceptionSpecializationsEntry()
	{
		for (var fail = 0; fail < 2; fail++)
		{
			_genericExceptionTrace = 0;
			if (GenericExceptionSpecialization(42, fail != 0) != 42 || _genericExceptionTrace != (fail == 0 ? 3 : 23)) return 1;
			_genericExceptionTrace = 0;
			if (GenericExceptionSpecialization("payload", fail != 0) != "payload" || _genericExceptionTrace != (fail == 0 ? 3 : 23)) return 2;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static T GenericExceptionSpecialization<T>(T value, bool fail)
	{
		try
		{
			if (fail) throw null!;
			return value;
		}
		catch (NullReferenceException)
		{
			_genericExceptionTrace = _genericExceptionTrace * 10 + 2;
			return value;
		}
		finally { _genericExceptionTrace = _genericExceptionTrace * 10 + 3; }
	}
}
