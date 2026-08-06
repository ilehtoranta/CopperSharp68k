/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

public sealed class ManagedRuntimeShadowTests
{
	[Theory]
	[InlineData(0, 0)]
	[InlineData(42, 42)]
	[InlineData(-42, 42)]
	public void ShadowMathAbsMatchesNetContract(int value, int expected) =>
		Assert.Equal(expected, ShadowMath.Abs(value));

	[Fact]
	public void ShadowMathAbsThrowsForMinimumValue() =>
		Assert.Throws<OverflowException>(() => ShadowMath.Abs(int.MinValue));

	[Fact]
	public void ShadowBitConverterUsesTargetBigEndianByteOrder() =>
		Assert.Equal(
			new byte[] { 0x01, 0x02, 0x03, 0x04 },
			ShadowBitConverter.GetBytes(0x01020304));

	[Fact]
	public void CompilerOnlyPrimitiveFailsClearlyOnHost()
	{
		var exception = Assert.Throws<PlatformNotSupportedException>(
			() => M68kRuntime.AllocateString(4));
		Assert.Contains("compiler primitive", exception.Message, StringComparison.Ordinal);
	}
}
