using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

internal interface ITrivialValueTypeConstructorMarker
{
}

public static class TrivialValueTypeConstructorFixtures
{
	public struct TrivialValue : ITrivialValueTypeConstructorMarker
	{
		public uint Value;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public TrivialValue(uint value) => Value = value;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public uint Read() => Value;
	}

	public struct NontrivialValue : ITrivialValueTypeConstructorMarker
	{
		public uint Value;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public NontrivialValue(uint value) => Value = value + 1;
	}

	public struct ThrowingValue : ITrivialValueTypeConstructorMarker
	{
		public uint Value;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public ThrowingValue(uint value) => Value = 23 / value;
	}

	public sealed class ReferenceValue
	{
		public uint Value;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public ReferenceValue(uint value) => Value = value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint EligibleEntry()
	{
		var value = new TrivialValue(42);
		return value.Read();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint RejectedEntry()
	{
		var nontrivial = new NontrivialValue(18);
		var throwing = new ThrowingValue(1);
		var reference = new ReferenceValue(0);
		return nontrivial.Value + throwing.Value + reference.Value;
	}
}
