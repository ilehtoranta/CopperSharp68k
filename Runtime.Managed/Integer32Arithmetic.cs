/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

/// <summary>Checked Int32 arithmetic using target-independent integer operations.</summary>
public static class Integer32Arithmetic
{
	/// <summary>Multiplies signed Int32 operands and raises the canonical overflow exception.</summary>
	public static int MultiplyCheckedSigned(int left, int right)
	{
		// Every Int32-by-Int32 product fits in Int64. Widen before multiplying,
		// then check both bounds before the unchecked narrowing conversion.
		long product = (long)left * right;
		if (product < int.MinValue || product > int.MaxValue)
			M68kRuntime.ThrowOverflowException();
		return unchecked((int)product);
	}
}
