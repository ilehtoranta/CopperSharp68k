/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections;
using System.Runtime.CompilerServices;
using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

/// <summary>
/// Compact target implementation of the public <see cref="EqualityComparer{T}"/>
/// singleton contract. Each closed construction owns one managed instance.
/// </summary>
public interface IShadowEqualityComparer<T>
{
	bool Equals(T? first, T? second);

	int GetHashCode(T value);
}

public class ShadowEqualityComparer<T> :
	EqualityComparer<T>,
	IShadowEqualityComparer<T>
{
	private static readonly EqualityComparer<T> Instance =
		new ShadowEqualityComparer<T>();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static EqualityComparer<T> GetDefault() => Instance;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public override bool Equals(T? first, T? second) =>
		M68kRuntime.DefaultEquals(first!, second!);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public override int GetHashCode(T value) =>
		M68kRuntime.DefaultHashCode(value);
}

/// <summary>
/// Compact fallback bodies for virtual <see cref="object"/> contracts.
/// </summary>
public class ShadowObject
{
	/// <summary>
	/// Implements the default-comparer fallback for a compiler-proven sealed
	/// reference type that does not implement <see cref="IEquatable{T}"/>.
	/// </summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool DefaultEqualsObject<T>(T? first, T? second)
		where T : class
	{
		var firstObject = M68kRuntime.ReferenceAsObject(first);
		var secondObject = M68kRuntime.ReferenceAsObject(second);
		if (ReferenceEquals(firstObject, secondObject))
		{
			return true;
		}
		if (firstObject is null)
		{
			return false;
		}
		return firstObject.Equals(secondObject);
	}

	/// <summary>
	/// Implements the default-comparer path for a compiler-proven sealed type
	/// that implements the exact <see cref="IEquatable{T}"/> contract.
	/// </summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool DefaultEqualsEquatable<T>(T? first, T? second)
		where T : class, IEquatable<T>
	{
		var firstObject = M68kRuntime.ReferenceAsObject(first);
		var secondObject = M68kRuntime.ReferenceAsObject(second);
		if (ReferenceEquals(firstObject, secondObject))
		{
			return true;
		}
		if (firstObject is null)
		{
			return false;
		}
		return M68kRuntime.ReferenceAsEquatable(first!).Equals(second);
	}

	/// <summary>
	/// Implements default hashing for a compiler-proven sealed reference type.
	/// Equality-comparer hashing is null-safe and otherwise preserves the exact
	/// virtual <see cref="object.GetHashCode"/> target.
	/// </summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DefaultHashCodeObject<T>(T? value)
		where T : class
	{
		var valueObject = M68kRuntime.ReferenceAsObject(value);
		return valueObject is null ? 0 : valueObject.GetHashCode();
	}

	/// <summary>
	/// Implements default equality for a compiler-admitted nullable scalar
	/// without constructing the public comparer object graph.
	/// </summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool DefaultEqualsNullable<T>(T? first, T? second)
		where T : struct
	{
		if (first.HasValue != second.HasValue)
		{
			return false;
		}
		if (!first.HasValue)
		{
			return true;
		}
		return M68kRuntime.DefaultEquals(
			first.GetValueOrDefault(),
			second.GetValueOrDefault());
	}

	/// <summary>Implements the static <see cref="object.Equals(object?, object?)"/> contract.</summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool EqualsObjects(object? first, object? second)
	{
		if (ReferenceEquals(first, second))
		{
			return true;
		}
		if (first is null)
		{
			return false;
		}
		if (first is Delegate firstDelegate)
		{
			return firstDelegate.Equals(second);
		}

		return first.Equals(second);
	}

	// The default Object.Equals contract is reference identity. The target
	// receiver need not actually be a ShadowObject: this body is the canonical
	// fallback for the public System.Object slot and accesses no instance fields.
	[MethodImpl(MethodImplOptions.NoInlining)]
	public override bool Equals(object? other) => this == other;

	// A constant hash is contract-correct: unequal objects may collide. User
	// overrides replace this slot through ordinary virtual dispatch.
	[MethodImpl(MethodImplOptions.NoInlining)]
	public override int GetHashCode() => 0;
}

/// <summary>
/// Compact exception formatting used by the experimental official-body profile.
/// The target runtime deliberately does not materialize stack traces or reflection metadata.
/// </summary>
public class ShadowException
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public override string ToString() => "System.Exception";
}

/// <summary>
/// Date/time globalization boundary used by the experimental official-body profile.
/// Numeric formatting remains on the official CoreLib path; calendar and
/// host-locale discovery are deliberately outside the target runtime.
/// </summary>
public class ShadowCultureInfo
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public virtual System.Globalization.DateTimeFormatInfo? GetDateTimeFormat() => null;

	// Static target entry used to exercise allocation and virtual dispatch through
	// the native backend without adding this experimental boundary to app fixtures.
	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static int ProbeDateTimeFormat() =>
		new ShadowCultureInfo().GetDateTimeFormat() is null ? 42 : 0;
}

/// <summary>
/// Deterministic CoreLib error resources for the experimental official-body profile.
/// This matches .NET's resource-key mode without importing ResourceManager or reflection.
/// </summary>
public static class ShadowSystemResources
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string GetResourceString(string key) => key;

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static int ProbeResourceString() =>
		GetResourceString("resource-key").Length;
}

/// <summary>
/// Per-closed-element cache used to preserve the singleton contract of
/// <see cref="Array.Empty{T}"/> without importing the CoreLib cache graph.
/// </summary>
internal static class ShadowEmptyArray<T>
{
	internal static readonly T[] Value = new T[0];
}

/// <summary>
/// Compact target implementations of the admitted generic
/// <see cref="Array"/> algorithms.
/// </summary>
public static class ShadowArray
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static T[] Empty<T>() => ShadowEmptyArray<T>.Value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Fill<T>(T[] array, T value)
	{
		if (array is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		for (var index = 0; index < array.Length; index++)
		{
			array[index] = value;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Fill<T>(T[] array, T value, int startIndex, int count)
	{
		if (array is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		if ((uint)startIndex > (uint)array.Length ||
			(uint)count > (uint)(array.Length - startIndex))
		{
			M68kRuntime.ThrowArgumentOutOfRangeException();
		}
		var endIndex = startIndex + count;
		for (var index = startIndex; index < endIndex; index++)
		{
			array[index] = value;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int IndexOf<T>(T[] array, T value)
	{
		if (array is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		for (var index = 0; index < array.Length; index++)
		{
			if (M68kRuntime.DefaultEquals(array[index], value))
			{
				return index;
			}
		}
		return -1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int IndexOf<T>(T[] array, T value, int startIndex)
	{
		if (array is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		if ((uint)startIndex > (uint)array.Length)
		{
			M68kRuntime.ThrowArgumentOutOfRangeException();
		}
		for (var index = startIndex; index < array.Length; index++)
		{
			if (M68kRuntime.DefaultEquals(array[index], value))
			{
				return index;
			}
		}
		return -1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int IndexOf<T>(T[] array, T value, int startIndex, int count)
	{
		if (array is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		if ((uint)startIndex > (uint)array.Length ||
			(uint)count > (uint)(array.Length - startIndex))
		{
			M68kRuntime.ThrowArgumentOutOfRangeException();
		}
		var endIndex = startIndex + count;
		for (var index = startIndex; index < endIndex; index++)
		{
			if (M68kRuntime.DefaultEquals(array[index], value))
			{
				return index;
			}
		}
		return -1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LastIndexOf<T>(T[] array, T value)
	{
		if (array is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		for (var index = array.Length - 1; index >= 0; index--)
		{
			if (M68kRuntime.DefaultEquals(array[index], value))
			{
				return index;
			}
		}
		return -1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LastIndexOf<T>(T[] array, T value, int startIndex)
	{
		if (array is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		if (array.Length == 0)
		{
			if (startIndex != -1 && startIndex != 0)
			{
				M68kRuntime.ThrowArgumentOutOfRangeException();
			}
			return -1;
		}
		if ((uint)startIndex >= (uint)array.Length)
		{
			M68kRuntime.ThrowArgumentOutOfRangeException();
		}
		for (var index = startIndex; index >= 0; index--)
		{
			if (M68kRuntime.DefaultEquals(array[index], value))
			{
				return index;
			}
		}
		return -1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LastIndexOf<T>(T[] array, T value, int startIndex, int count)
	{
		if (array is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		if (array.Length == 0)
		{
			if ((startIndex != -1 && startIndex != 0) || count != 0)
			{
				M68kRuntime.ThrowArgumentOutOfRangeException();
			}
			return -1;
		}
		if ((uint)startIndex >= (uint)array.Length ||
			count < 0 ||
			startIndex - count + 1 < 0)
		{
			M68kRuntime.ThrowArgumentOutOfRangeException();
		}
		var endIndex = startIndex - count;
		for (var index = startIndex; index > endIndex; index--)
		{
			if (M68kRuntime.DefaultEquals(array[index], value))
			{
				return index;
			}
		}
		return -1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Reverse<T>(T[] array)
	{
		if (array is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		var first = 0;
		var last = array.Length - 1;
		while (first < last)
		{
			var temporary = array[first];
			array[first++] = array[last];
			array[last--] = temporary;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Reverse<T>(T[] array, int index, int length)
	{
		if (array is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		if (index < 0 || length < 0)
		{
			M68kRuntime.ThrowArgumentOutOfRangeException();
		}
		if (array.Length - index < length)
		{
			M68kRuntime.ThrowArgumentException();
		}
		var first = index;
		var last = index + length - 1;
		while (first < last)
		{
			var temporary = array[first];
			array[first++] = array[last];
			array[last--] = temporary;
		}
	}
}

/// <summary>Compact target implementations for supported <see cref="Math"/> members.</summary>
public static class ShadowMath
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static sbyte Abs(sbyte value)
	{
		if (value == sbyte.MinValue) M68kRuntime.ThrowOverflowException();
		return value < 0 ? (sbyte)-value : value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static short Abs(short value)
	{
		if (value == short.MinValue) M68kRuntime.ThrowOverflowException();
		return value < 0 ? (short)-value : value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int Abs(int value)
	{
		if (value == int.MinValue)
		{
			M68kRuntime.ThrowOverflowException();
		}

		return value < 0 ? -value : value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static long Abs(long value)
	{
		var low = M68kRuntime.SplitInt64(value, out var high);
		if ((high & 0x8000_0000u) == 0) return value;
		if (high == 0x8000_0000u && low == 0) M68kRuntime.ThrowOverflowException();
		Negate64(ref high, ref low);
		return M68kRuntime.CombineInt64(high, low);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static nint Abs(nint value)
	{
		if (value == unchecked((nint)(-2147483647 - 1))) M68kRuntime.ThrowOverflowException();
		return value < 0 ? -value : value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)] public static byte Min(byte x, byte y) => x < y ? x : y;
	[MethodImpl(MethodImplOptions.NoInlining)] public static sbyte Min(sbyte x, sbyte y) => x < y ? x : y;
	[MethodImpl(MethodImplOptions.NoInlining)] public static short Min(short x, short y) => x < y ? x : y;
	[MethodImpl(MethodImplOptions.NoInlining)] public static ushort Min(ushort x, ushort y) => x < y ? x : y;
	[MethodImpl(MethodImplOptions.NoInlining)] public static int Min(int x, int y) => x < y ? x : y;
	[MethodImpl(MethodImplOptions.NoInlining)] public static uint Min(uint x, uint y) => x < y ? x : y;
	[MethodImpl(MethodImplOptions.NoInlining)] public static long Min(long x, long y) => CompareInt64(x, y) < 0 ? x : y;
	[MethodImpl(MethodImplOptions.NoInlining)] public static ulong Min(ulong x, ulong y) => CompareUInt64(x, y) < 0 ? x : y;
	[MethodImpl(MethodImplOptions.NoInlining)] public static nint Min(nint x, nint y) => x < y ? x : y;
	[MethodImpl(MethodImplOptions.NoInlining)] public static nuint Min(nuint x, nuint y) => x < y ? x : y;

	[MethodImpl(MethodImplOptions.NoInlining)] public static byte Max(byte x, byte y) => x > y ? x : y;
	[MethodImpl(MethodImplOptions.NoInlining)] public static sbyte Max(sbyte x, sbyte y) => x > y ? x : y;
	[MethodImpl(MethodImplOptions.NoInlining)] public static short Max(short x, short y) => x > y ? x : y;
	[MethodImpl(MethodImplOptions.NoInlining)] public static ushort Max(ushort x, ushort y) => x > y ? x : y;
	[MethodImpl(MethodImplOptions.NoInlining)] public static int Max(int x, int y) => x > y ? x : y;
	[MethodImpl(MethodImplOptions.NoInlining)] public static uint Max(uint x, uint y) => x > y ? x : y;
	[MethodImpl(MethodImplOptions.NoInlining)] public static long Max(long x, long y) => CompareInt64(x, y) > 0 ? x : y;
	[MethodImpl(MethodImplOptions.NoInlining)] public static ulong Max(ulong x, ulong y) => CompareUInt64(x, y) > 0 ? x : y;
	[MethodImpl(MethodImplOptions.NoInlining)] public static nint Max(nint x, nint y) => x > y ? x : y;
	[MethodImpl(MethodImplOptions.NoInlining)] public static nuint Max(nuint x, nuint y) => x > y ? x : y;

	[MethodImpl(MethodImplOptions.NoInlining)] public static byte Clamp(byte value, byte min, byte max) => ClampCore(value, min, max);
	[MethodImpl(MethodImplOptions.NoInlining)] public static sbyte Clamp(sbyte value, sbyte min, sbyte max) => ClampCore(value, min, max);
	[MethodImpl(MethodImplOptions.NoInlining)] public static short Clamp(short value, short min, short max) => ClampCore(value, min, max);
	[MethodImpl(MethodImplOptions.NoInlining)] public static ushort Clamp(ushort value, ushort min, ushort max) => ClampCore(value, min, max);
	[MethodImpl(MethodImplOptions.NoInlining)] public static int Clamp(int value, int min, int max) => ClampCore(value, min, max);
	[MethodImpl(MethodImplOptions.NoInlining)] public static uint Clamp(uint value, uint min, uint max) => ClampCore(value, min, max);
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static long Clamp(long value, long min, long max)
	{
		if (CompareInt64(min, max) > 0) M68kRuntime.ThrowArgumentException();
		if (CompareInt64(value, min) < 0) return min;
		return CompareInt64(value, max) > 0 ? max : value;
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ulong Clamp(ulong value, ulong min, ulong max)
	{
		if (CompareUInt64(min, max) > 0) M68kRuntime.ThrowArgumentException();
		if (CompareUInt64(value, min) < 0) return min;
		return CompareUInt64(value, max) > 0 ? max : value;
	}
	[MethodImpl(MethodImplOptions.NoInlining)] public static nint Clamp(nint value, nint min, nint max) => ClampCore(value, min, max);
	[MethodImpl(MethodImplOptions.NoInlining)] public static nuint Clamp(nuint value, nuint min, nuint max) => ClampCore(value, min, max);

	[MethodImpl(MethodImplOptions.NoInlining)] public static int Sign(sbyte value) => value < 0 ? -1 : value > 0 ? 1 : 0;
	[MethodImpl(MethodImplOptions.NoInlining)] public static int Sign(short value) => value < 0 ? -1 : value > 0 ? 1 : 0;
	[MethodImpl(MethodImplOptions.NoInlining)] public static int Sign(int value) => value < 0 ? -1 : value > 0 ? 1 : 0;
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int Sign(long value)
	{
		var low = M68kRuntime.SplitInt64(value, out var high);
		if ((high & 0x8000_0000u) != 0) return -1;
		return high != 0 || low != 0 ? 1 : 0;
	}
	[MethodImpl(MethodImplOptions.NoInlining)] public static int Sign(nint value) => value < 0 ? -1 : value > 0 ? 1 : 0;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static long BigMul(int first, int second)
	{
		var negative = (first < 0) != (second < 0);
		var firstMagnitude = first < 0 ? (uint)(-(first + 1)) + 1u : (uint)first;
		var secondMagnitude = second < 0 ? (uint)(-(second + 1)) + 1u : (uint)second;
		MultiplyUInt32(firstMagnitude, secondMagnitude, out var high, out var low);
		if (negative) Negate64(ref high, ref low);
		return M68kRuntime.CombineInt64(high, low);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ulong BigMul(uint first, uint second)
	{
		MultiplyUInt32(first, second, out var high, out var low);
		return (ulong)M68kRuntime.CombineInt64(high, low);
	}

	private static byte ClampCore(byte value, byte min, byte max) { if (min > max) M68kRuntime.ThrowArgumentException(); return value < min ? min : value > max ? max : value; }
	private static sbyte ClampCore(sbyte value, sbyte min, sbyte max) { if (min > max) M68kRuntime.ThrowArgumentException(); return value < min ? min : value > max ? max : value; }
	private static short ClampCore(short value, short min, short max) { if (min > max) M68kRuntime.ThrowArgumentException(); return value < min ? min : value > max ? max : value; }
	private static ushort ClampCore(ushort value, ushort min, ushort max) { if (min > max) M68kRuntime.ThrowArgumentException(); return value < min ? min : value > max ? max : value; }
	private static int ClampCore(int value, int min, int max) { if (min > max) M68kRuntime.ThrowArgumentException(); return value < min ? min : value > max ? max : value; }
	private static uint ClampCore(uint value, uint min, uint max) { if (min > max) M68kRuntime.ThrowArgumentException(); return value < min ? min : value > max ? max : value; }
	private static nint ClampCore(nint value, nint min, nint max) { if (min > max) M68kRuntime.ThrowArgumentException(); return value < min ? min : value > max ? max : value; }
	private static nuint ClampCore(nuint value, nuint min, nuint max) { if (min > max) M68kRuntime.ThrowArgumentException(); return value < min ? min : value > max ? max : value; }

	private static int CompareInt64(long first, long second)
	{
		var firstLow = M68kRuntime.SplitInt64(first, out var firstHigh);
		var secondLow = M68kRuntime.SplitInt64(second, out var secondHigh);
		var firstSignedHigh = (int)firstHigh;
		var secondSignedHigh = (int)secondHigh;
		if (firstSignedHigh < secondSignedHigh) return -1;
		if (firstSignedHigh > secondSignedHigh) return 1;
		if (firstLow < secondLow) return -1;
		return firstLow > secondLow ? 1 : 0;
	}

	private static int CompareUInt64(ulong first, ulong second)
	{
		var firstLow = M68kRuntime.SplitUInt64(first, out var firstHigh);
		var secondLow = M68kRuntime.SplitUInt64(second, out var secondHigh);
		if (firstHigh < secondHigh) return -1;
		if (firstHigh > secondHigh) return 1;
		if (firstLow < secondLow) return -1;
		return firstLow > secondLow ? 1 : 0;
	}

	private static void MultiplyUInt32(uint first, uint second, out uint high, out uint low)
	{
		var firstLow = first & 0xffffu;
		var firstHigh = first >> 16;
		var secondLow = second & 0xffffu;
		var secondHigh = second >> 16;
		var lowProduct = firstLow * secondLow;
		var middle1 = firstHigh * secondLow;
		var middle2 = firstLow * secondHigh;
		var carry = (lowProduct >> 16) + (middle1 & 0xffffu) + (middle2 & 0xffffu);
		low = (lowProduct & 0xffffu) | (carry << 16);
		high = firstHigh * secondHigh + (middle1 >> 16) + (middle2 >> 16) + (carry >> 16);
	}

	private static void Negate64(ref uint high, ref uint low)
	{
		low = ~low + 1u;
		high = ~high + (low == 0 ? 1u : 0u);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static double Abs(double value)
	{
		var low = M68kRuntime.SplitDouble(value, out var high);
		return M68kRuntime.CombineDouble(high & 0x7fff_ffffu, low);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static float Abs(float value) =>
		M68kRuntime.UInt32BitsToSingle(
			M68kRuntime.SingleToUInt32Bits(value) & 0x7fff_ffffu);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static double CopySign(double value, double sign)
	{
		var valueLow = M68kRuntime.SplitDouble(value, out var valueHigh);
		_ = M68kRuntime.SplitDouble(sign, out var signHigh);
		return M68kRuntime.CombineDouble(
			(valueHigh & 0x7fff_ffffu) | (signHigh & 0x8000_0000u),
			valueLow);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static double Min(double first, double second)
	{
		if (ShadowDouble.IsNaN(first)) return first;
		if (ShadowDouble.IsNaN(second)) return second;
		var comparison = CompareDouble(first, second);
		if (comparison < 0) return first;
		if (comparison > 0) return second;
		return ShadowDouble.IsNegative(first) ? first : second;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static double Max(double first, double second)
	{
		if (ShadowDouble.IsNaN(first)) return first;
		if (ShadowDouble.IsNaN(second)) return second;
		var comparison = CompareDouble(first, second);
		if (comparison > 0) return first;
		if (comparison < 0) return second;
		return ShadowDouble.IsNegative(first) ? second : first;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static float Min(float first, float second)
	{
		if (ShadowSingle.IsNaN(first)) return first;
		if (ShadowSingle.IsNaN(second)) return second;
		var comparison = CompareSingle(first, second);
		if (comparison < 0) return first;
		if (comparison > 0) return second;
		return ShadowSingle.IsNegative(first) ? first : second;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static float Max(float first, float second)
	{
		if (ShadowSingle.IsNaN(first)) return first;
		if (ShadowSingle.IsNaN(second)) return second;
		var comparison = CompareSingle(first, second);
		if (comparison > 0) return first;
		if (comparison < 0) return second;
		return ShadowSingle.IsNegative(first) ? second : first;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static double Clamp(double value, double min, double max)
	{
		if (!ShadowDouble.IsNaN(min) && !ShadowDouble.IsNaN(max) && CompareDouble(min, max) > 0)
			M68kRuntime.ThrowArgumentException();
		if (!ShadowDouble.IsNaN(value) && !ShadowDouble.IsNaN(min) && CompareDouble(value, min) < 0) return min;
		if (!ShadowDouble.IsNaN(value) && !ShadowDouble.IsNaN(max) && CompareDouble(value, max) > 0) return max;
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static float Clamp(float value, float min, float max)
	{
		if (!ShadowSingle.IsNaN(min) && !ShadowSingle.IsNaN(max) && CompareSingle(min, max) > 0)
			M68kRuntime.ThrowArgumentException();
		if (!ShadowSingle.IsNaN(value) && !ShadowSingle.IsNaN(min) && CompareSingle(value, min) < 0) return min;
		if (!ShadowSingle.IsNaN(value) && !ShadowSingle.IsNaN(max) && CompareSingle(value, max) > 0) return max;
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int Sign(double value)
	{
		if (ShadowDouble.IsNaN(value)) M68kRuntime.ThrowArithmeticException();
		var low = M68kRuntime.SplitDouble(value, out var high);
		if ((high & 0x7fff_ffffu) == 0 && low == 0) return 0;
		return (high & 0x8000_0000u) != 0 ? -1 : 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int Sign(float value)
	{
		if (ShadowSingle.IsNaN(value)) M68kRuntime.ThrowArithmeticException();
		var bits = M68kRuntime.SingleToUInt32Bits(value);
		if ((bits & 0x7fff_ffffu) == 0) return 0;
		return (bits & 0x8000_0000u) != 0 ? -1 : 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static double Truncate(double value) => RoundDirected(value, 0);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static double Floor(double value) => RoundDirected(value, -1);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static double Ceiling(double value)
	{
		var low = M68kRuntime.SplitDouble(value, out var high);
		if ((high & 0x7ff0_0000u) == 0x7ff0_0000u &&
			((high & 0x000f_ffffu) != 0 || low != 0))
		{
			return M68kRuntime.CombineDouble(high | 0x0008_0000u, low);
		}
		return RoundDirected(value, 1);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static double Round(double value) => Round(value, MidpointRounding.ToEven);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static double Round(double value, MidpointRounding mode)
	{
		if (mode != MidpointRounding.ToEven &&
			mode != MidpointRounding.AwayFromZero &&
			mode != MidpointRounding.ToZero &&
			mode != MidpointRounding.ToNegativeInfinity &&
			mode != MidpointRounding.ToPositiveInfinity)
			M68kRuntime.ThrowArgumentException();

		var low = M68kRuntime.SplitDouble(value, out var high);
		var exponentBits = (high >> 20) & 0x7ffu;
		if (exponentBits == 0x7ffu)
		{
			return (high & 0x000f_ffffu) != 0 || low != 0
				? M68kRuntime.CombineDouble(high | 0x0008_0000u, low)
				: value;
		}
		if (mode == MidpointRounding.ToZero) return Truncate(value);
		if (mode == MidpointRounding.ToNegativeInfinity) return Floor(value);
		if (mode == MidpointRounding.ToPositiveInfinity) return Ceiling(value);
		var exponent = (int)exponentBits - 1023;
		if (exponent >= 52) return value;
		var negative = (high & 0x8000_0000u) != 0;
		if (exponent < -1) return M68kRuntime.CombineDouble(negative ? 0x8000_0000u : 0, 0);
		if (exponent == -1)
		{
			var magnitudeHigh = high & 0x7fff_ffffu;
			var greaterThanHalf = magnitudeHigh > 0x3fe0_0000u ||
				(magnitudeHigh == 0x3fe0_0000u && low != 0);
			var exactlyHalf = magnitudeHigh == 0x3fe0_0000u && low == 0;
			var roundUp = greaterThanHalf ||
				(exactlyHalf && mode == MidpointRounding.AwayFromZero);
			return roundUp
				? M68kRuntime.CombineDouble(negative ? 0xbff0_0000u : 0x3ff0_0000u, 0)
				: M68kRuntime.CombineDouble(negative ? 0x8000_0000u : 0, 0);
		}

		var truncatedLow = low;
		var truncatedHigh = high;
		bool halfSet;
		bool lowerSet;
		bool integerOdd;
		if (exponent < 20)
		{
			var fractionalBits = 20 - exponent;
			var unit = 1u << fractionalBits;
			var half = unit >> 1;
			halfSet = (high & half) != 0;
			lowerSet = (high & (half - 1u)) != 0 || low != 0;
			integerOdd = (high & unit) != 0;
			truncatedHigh &= ~(unit - 1u);
			truncatedLow = 0;
		}
		else if (exponent == 20)
		{
			halfSet = (low & 0x8000_0000u) != 0;
			lowerSet = (low & 0x7fff_ffffu) != 0;
			integerOdd = (high & 1u) != 0;
			truncatedLow = 0;
		}
		else
		{
			var fractionalBits = 52 - exponent;
			var unit = 1u << fractionalBits;
			var half = unit >> 1;
			halfSet = (low & half) != 0;
			lowerSet = (low & (half - 1u)) != 0;
			integerOdd = (low & unit) != 0;
			truncatedLow &= ~(unit - 1u);
		}
		if (halfSet && (lowerSet || mode == MidpointRounding.AwayFromZero || integerOdd))
			IncrementTruncatedMagnitude(ref truncatedHigh, ref truncatedLow, exponent);
		return M68kRuntime.CombineDouble(truncatedHigh, truncatedLow);
	}

	/// <summary>
	/// Correctly-rounded binary64 square root using the freely distributable
	/// fdlibm bit-by-bit integer algorithm. This is the portable reference path.
	/// </summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static double Sqrt(double value)
	{
		var ix1 = M68kRuntime.SplitDouble(value, out var ix0);
		if ((ix0 & 0x7ff0_0000u) == 0x7ff0_0000u)
		{
			if ((ix0 & 0x8000_0000u) == 0 && (ix0 & 0x000f_ffffu) == 0 && ix1 == 0)
				return value;
			// IEEE operations quiet signaling NaNs while retaining their sign and payload.
			if ((ix0 & 0x000f_ffffu) != 0 || ix1 != 0)
				return M68kRuntime.CombineDouble(ix0 | 0x0008_0000u, ix1);
			return M68kRuntime.CombineDouble(0xfff8_0000u, 0);
		}
		if ((ix0 & 0x7fff_ffffu) == 0 && ix1 == 0) return value;
		if ((ix0 & 0x8000_0000u) != 0)
			return M68kRuntime.CombineDouble(0xfff8_0000u, 0);

		var exponent = PrepareSqrtRadicand(ref ix0, ref ix1);
		var q = ExtractSqrtHigh(ref ix0, ref ix1, out var s0);
		var q1 = ExtractSqrtLow(ref ix0, ref ix1, s0);
		var resultHigh = (q >> 1) + 0x3fe0_0000u;
		var resultLow = q1 >> 1;
		if ((q & 1u) != 0) resultLow |= 0x8000_0000u;
		resultHigh = unchecked(resultHigh + ((uint)exponent << 20));
		return M68kRuntime.CombineDouble(resultHigh, resultLow);
	}

	private static int PrepareSqrtRadicand(ref uint ix0, ref uint ix1)
	{
		var exponent = (int)(ix0 >> 20);
		if (exponent == 0)
		{
			while (ix0 == 0)
			{
				exponent -= 21;
				ix0 = ix1 >> 11;
				ix1 <<= 21;
			}
			var shift = 0;
			while ((ix0 & 0x0010_0000u) == 0)
			{
				ix0 <<= 1;
				shift++;
			}
			exponent -= shift - 1;
			if (shift != 0)
			{
				ix0 |= ix1 >> (32 - shift);
				ix1 <<= shift;
			}
		}
		exponent -= 1023;
		ix0 = (ix0 & 0x000f_ffffu) | 0x0010_0000u;
		if ((exponent & 1) != 0)
		{
			ix0 = ix0 + ix0 + (ix1 >> 31);
			ix1 += ix1;
		}
		exponent >>= 1;
		ix0 = ix0 + ix0 + (ix1 >> 31);
		ix1 += ix1;
		return exponent;
	}

	private static uint ExtractSqrtHigh(ref uint ix0, ref uint ix1, out uint s0)
	{
		var q = 0u;
		s0 = 0u;
		var moving = 0x0020_0000u;
		while (moving != 0)
		{
			var trial = s0 + moving;
			if (trial <= ix0)
			{
				s0 = trial + moving;
				ix0 -= trial;
				q += moving;
			}
			ix0 = ix0 + ix0 + (ix1 >> 31);
			ix1 += ix1;
			moving >>= 1;
		}
		return q;
	}

	private static uint ExtractSqrtLow(ref uint ix0, ref uint ix1, uint s0)
	{
		var q1 = 0u;
		var s1 = 0u;
		var runningHigh = s0;
		var moving = 0x8000_0000u;
		while (moving != 0)
		{
			var trialLow = s1 + moving;
			var trialHigh = runningHigh;
			if (trialHigh < ix0 || (trialHigh == ix0 && trialLow <= ix1))
			{
				var nextS1 = trialLow + moving;
				if ((trialLow & 0x8000_0000u) != 0 && (nextS1 & 0x8000_0000u) == 0) runningHigh++;
				s1 = nextS1;
				ix0 -= trialHigh;
				if (ix1 < trialLow) ix0--;
				ix1 -= trialLow;
				q1 += moving;
			}
			ix0 = ix0 + ix0 + (ix1 >> 31);
			ix1 += ix1;
			moving >>= 1;
		}
		if ((ix0 | ix1) != 0) q1 += q1 & 1u;
		return q1;
	}

	private static double RoundDirected(double value, int direction)
	{
		var low = M68kRuntime.SplitDouble(value, out var high);
		var exponentBits = (high >> 20) & 0x7ffu;
		if (exponentBits == 0x7ffu) return value;
		var exponent = (int)exponentBits - 1023;
		if (exponent >= 52) return value;
		var negative = (high & 0x8000_0000u) != 0;
		var magnitudeNonzero = (high & 0x7fff_ffffu) != 0 || low != 0;
		if (exponent < 0)
		{
			if (!magnitudeNonzero || direction == 0 || (direction < 0 && !negative) || (direction > 0 && negative))
				return M68kRuntime.CombineDouble(negative ? 0x8000_0000u : 0, 0);
			return M68kRuntime.CombineDouble(negative ? 0xbff0_0000u : 0x3ff0_0000u, 0);
		}
		var truncatedHigh = high;
		var truncatedLow = low;
		bool hadFraction;
		if (exponent <= 20)
		{
			var unit = 1u << (20 - exponent);
			hadFraction = (high & (unit - 1u)) != 0 || low != 0;
			truncatedHigh &= ~(unit - 1u);
			truncatedLow = 0;
		}
		else
		{
			var unit = 1u << (52 - exponent);
			hadFraction = (low & (unit - 1u)) != 0;
			truncatedLow &= ~(unit - 1u);
		}
		if (hadFraction && ((direction < 0 && negative) || (direction > 0 && !negative)))
			IncrementTruncatedMagnitude(ref truncatedHigh, ref truncatedLow, exponent);
		return M68kRuntime.CombineDouble(truncatedHigh, truncatedLow);
	}

	private static void IncrementTruncatedMagnitude(ref uint high, ref uint low, int exponent)
	{
		if (exponent <= 20)
		{
			high += 1u << (20 - exponent);
			return;
		}
		var increment = 1u << (52 - exponent);
		var previous = low;
		low += increment;
		if (low < previous) high++;
	}

	private static int CompareDouble(double first, double second)
	{
		var firstLow = M68kRuntime.SplitDouble(first, out var firstHigh);
		var secondLow = M68kRuntime.SplitDouble(second, out var secondHigh);
		var firstMagnitudeHigh = firstHigh & 0x7fff_ffffu;
		var secondMagnitudeHigh = secondHigh & 0x7fff_ffffu;
		var firstZero = firstMagnitudeHigh == 0 && firstLow == 0;
		var secondZero = secondMagnitudeHigh == 0 && secondLow == 0;
		if (firstZero && secondZero) return 0;
		var firstNegative = (firstHigh & 0x8000_0000u) != 0;
		var secondNegative = (secondHigh & 0x8000_0000u) != 0;
		if (firstNegative != secondNegative) return firstNegative ? -1 : 1;
		var magnitude = CompareLanes(firstMagnitudeHigh, firstLow, secondMagnitudeHigh, secondLow);
		return firstNegative ? -magnitude : magnitude;
	}

	private static int CompareSingle(float first, float second)
	{
		var firstBits = M68kRuntime.SingleToUInt32Bits(first);
		var secondBits = M68kRuntime.SingleToUInt32Bits(second);
		var firstMagnitude = firstBits & 0x7fff_ffffu;
		var secondMagnitude = secondBits & 0x7fff_ffffu;
		if (firstMagnitude == 0 && secondMagnitude == 0) return 0;
		var firstNegative = (firstBits & 0x8000_0000u) != 0;
		var secondNegative = (secondBits & 0x8000_0000u) != 0;
		if (firstNegative != secondNegative) return firstNegative ? -1 : 1;
		var magnitude = firstMagnitude < secondMagnitude ? -1 : firstMagnitude > secondMagnitude ? 1 : 0;
		return firstNegative ? -magnitude : magnitude;
	}

	private static int CompareLanes(uint firstHigh, uint firstLow, uint secondHigh, uint secondLow)
	{
		if (firstHigh < secondHigh) return -1;
		if (firstHigh > secondHigh) return 1;
		if (firstLow < secondLow) return -1;
		return firstLow > secondLow ? 1 : 0;
	}

}

/// <summary>Allocation-free IEEE-754 binary64 classification helpers.</summary>
public static class ShadowDouble
{
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool IsFinite(double value) { var low = M68kRuntime.SplitDouble(value, out var high); return (high & 0x7ff0_0000u) != 0x7ff0_0000u; }
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool IsInfinity(double value) { var low = M68kRuntime.SplitDouble(value, out var high); return (high & 0x7fff_ffffu) == 0x7ff0_0000u && low == 0; }
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool IsNaN(double value) { var low = M68kRuntime.SplitDouble(value, out var high); return (high & 0x7fff_ffffu) > 0x7ff0_0000u || ((high & 0x7fff_ffffu) == 0x7ff0_0000u && low != 0); }
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool IsNegative(double value) { _ = M68kRuntime.SplitDouble(value, out var high); return (high & 0x8000_0000u) != 0; }
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool IsNegativeInfinity(double value) { var low = M68kRuntime.SplitDouble(value, out var high); return high == 0xfff0_0000u && low == 0; }
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool IsPositiveInfinity(double value) { var low = M68kRuntime.SplitDouble(value, out var high); return high == 0x7ff0_0000u && low == 0; }
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool IsNormal(double value) { _ = M68kRuntime.SplitDouble(value, out var high); var exponent = high & 0x7ff0_0000u; return exponent != 0 && exponent != 0x7ff0_0000u; }
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool IsSubnormal(double value) { var low = M68kRuntime.SplitDouble(value, out var high); return (high & 0x7ff0_0000u) == 0 && ((high & 0x000f_ffffu) != 0 || low != 0); }
}

/// <summary>Allocation-free IEEE-754 binary32 classification helpers.</summary>
public static class ShadowSingle
{
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool IsFinite(float value) => (M68kRuntime.SingleToUInt32Bits(value) & 0x7f80_0000u) != 0x7f80_0000u;
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool IsInfinity(float value) => (M68kRuntime.SingleToUInt32Bits(value) & 0x7fff_ffffu) == 0x7f80_0000u;
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool IsNaN(float value) => (M68kRuntime.SingleToUInt32Bits(value) & 0x7fff_ffffu) > 0x7f80_0000u;
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool IsNegative(float value) => (M68kRuntime.SingleToUInt32Bits(value) & 0x8000_0000u) != 0;
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool IsNegativeInfinity(float value) => M68kRuntime.SingleToUInt32Bits(value) == 0xff80_0000u;
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool IsPositiveInfinity(float value) => M68kRuntime.SingleToUInt32Bits(value) == 0x7f80_0000u;
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool IsNormal(float value) { var exponent = M68kRuntime.SingleToUInt32Bits(value) & 0x7f80_0000u; return exponent != 0 && exponent != 0x7f80_0000u; }
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool IsSubnormal(float value) { var bits = M68kRuntime.SingleToUInt32Bits(value); return (bits & 0x7f80_0000u) == 0 && (bits & 0x007f_ffffu) != 0; }
}

/// <summary>
/// Big-endian target implementations for supported <see cref="BitConverter"/> members.
/// </summary>
public static class ShadowBitConverter
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static byte[] GetBytes(int value)
	{
		var bytes = new byte[4];
		bytes[0] = (byte)(value >> 24);
		bytes[1] = (byte)(value >> 16);
		bytes[2] = (byte)(value >> 8);
		bytes[3] = (byte)value;
		return bytes;
	}
}

/// <summary>Target implementation for parameterless <see cref="int.ToString()"/>.</summary>
public readonly struct ShadowInt32
{
	// The public Int32 receiver and this shadow receiver are both one four-byte
	// value. Framework binding keeps the public identity while executing this body.
#pragma warning disable CS0649 // Assigned through the representation-compatible public receiver.
	private readonly int _value;
#pragma warning restore CS0649

	[MethodImpl(MethodImplOptions.NoInlining)]
	public override string ToString() =>
		ShadowIntegerFormatter.FormatInt32(_value);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public string ToString(string? format) =>
		ShadowIntegerFormatter.FormatInt32(_value, format);
}

/// <summary>Target implementation for parameterless <see cref="uint.ToString()"/>.</summary>
public readonly struct ShadowUInt32
{
#pragma warning disable CS0649 // Assigned through the representation-compatible public receiver.
	private readonly uint _value;
#pragma warning restore CS0649

	[MethodImpl(MethodImplOptions.NoInlining)]
	public override string ToString() =>
		ShadowIntegerFormatter.FormatUInt32(_value);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public string ToString(string? format) =>
		ShadowIntegerFormatter.FormatUInt32(_value, format);
}

/// <summary>Target implementation for parameterless <see cref="long.ToString()"/>.</summary>
public readonly struct ShadowInt64
{
	// The target stores 64-bit scalars as a big-endian high/low register pair.
	// Keeping the shadow as two UInt32 lanes avoids depending on unsupported
	// general Int64 arithmetic while preserving the public receiver layout.
#pragma warning disable CS0649 // Assigned through the representation-compatible public receiver.
	private readonly uint _high;
	private readonly uint _low;
#pragma warning restore CS0649

	[MethodImpl(MethodImplOptions.NoInlining)]
	public override string ToString() =>
		ShadowIntegerFormatter.FormatInt64(_high, _low);
}

/// <summary>Target implementation for parameterless <see cref="ulong.ToString()"/>.</summary>
public readonly struct ShadowUInt64
{
#pragma warning disable CS0649 // Assigned through the representation-compatible public receiver.
	private readonly uint _high;
	private readonly uint _low;
#pragma warning restore CS0649

	[MethodImpl(MethodImplOptions.NoInlining)]
	public override string ToString() =>
		ShadowIntegerFormatter.FormatUInt64(_high, _low);
}

/// <summary>Allocation-exact invariant decimal formatting shared by integer shadows.</summary>
public static class ShadowIntegerFormatter
{
	private const int MaximumPrecision = 999_999_999;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string FormatInt32(int value)
	{
		if (value >= 0)
		{
			return FormatUInt32((uint)value);
		}

		var magnitude = (uint)(-(value + 1)) + 1u;
		var digits = CountDecimalDigits(magnitude);
		var result = M68kRuntime.AllocateString(digits + 1);
		M68kRuntime.SetStringChar(result, 0, '-');
		WriteDecimalValue(result, 1, digits, magnitude);
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string FormatUInt32(uint value)
	{
		var digits = CountDecimalDigits(value);
		var result = M68kRuntime.AllocateString(digits);
		WriteDecimalValue(result, 0, digits, value);
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string FormatInt64(uint high, uint low)
	{
		if ((high & 0x8000_0000u) == 0)
		{
			return FormatUInt64(high, low);
		}

		var magnitudeLow = ~low + 1u;
		var magnitudeHigh = ~high;
		if (magnitudeLow == 0)
		{
			magnitudeHigh++;
		}
		var digits = CountDecimalDigits64(magnitudeHigh, magnitudeLow);
		var result = M68kRuntime.AllocateString(digits + 1);
		M68kRuntime.SetStringChar(result, 0, '-');
		WriteDecimalValue64(result, 1, digits, magnitudeHigh, magnitudeLow);
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string FormatUInt64(uint high, uint low)
	{
		var digits = CountDecimalDigits64(high, low);
		var result = M68kRuntime.AllocateString(digits);
		WriteDecimalValue64(result, 0, digits, high, low);
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PackInt32(
		int value,
		out uint word0,
		out uint word1,
		out uint word2)
	{
		if (value >= 0)
		{
			return PackUInt32((uint)value, out word0, out word1, out word2);
		}

		var magnitude = (uint)(-(value + 1)) + 1u;
		return PackDecimal(
			magnitude,
			negative: true,
			out word0,
			out word1,
			out word2);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PackUInt32(
		uint value,
		out uint word0,
		out uint word1,
		out uint word2) =>
		PackDecimal(
			value,
			negative: false,
			out word0,
			out word1,
			out word2);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PackInt64(
		uint high,
		uint low,
		out uint word0,
		out uint word1,
		out uint word2,
		out uint word3,
		out uint word4)
	{
		var negative = (high & 0x8000_0000u) != 0;
		var magnitudeHigh = high;
		var magnitudeLow = low;
		if (negative)
		{
			magnitudeLow = ~low + 1u;
			magnitudeHigh = ~high;
			if (magnitudeLow == 0)
			{
				magnitudeHigh++;
			}
		}
		return PackDecimal64(
			magnitudeHigh,
			magnitudeLow,
			negative,
			out word0,
			out word1,
			out word2,
			out word3,
			out word4);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PackUInt64(
		uint high,
		uint low,
		out uint word0,
		out uint word1,
		out uint word2,
		out uint word3,
		out uint word4) =>
		PackDecimal64(
			high,
			low,
			negative: false,
			out word0,
			out word1,
			out word2,
			out word3,
			out word4);

	private static int PackDecimal64(
		uint high,
		uint low,
		bool negative,
		out uint word0,
		out uint word1,
		out uint word2,
		out uint word3,
		out uint word4)
	{
		var digitCount = CountDecimalDigits64(high, low);
		var length = digitCount + (negative ? 1 : 0);
		word0 = 0;
		word1 = 0;
		word2 = 0;
		word3 = 0;
		word4 = 0;
		var index = length - 1;
		var currentHigh = high;
		var currentLow = low;
		var remainingDigits = digitCount;
		do
		{
			var chunk = DivideUInt64By10000(
				currentHigh,
				currentLow,
				out currentHigh,
				out currentLow);
			var packedChunk = PackFourDecimalDigits(chunk);
			var chunkDigits = remainingDigits >= 4 ? 4 : remainingDigits;
			var outputIndex = index - chunkDigits + 1;
			if (chunkDigits == 4)
			{
				PackDecimalByte(
					outputIndex++, packedChunk >> 24,
					ref word0, ref word1, ref word2, ref word3, ref word4);
			}
			if (chunkDigits >= 3)
			{
				PackDecimalByte(
					outputIndex++, (packedChunk >> 16) & 0xffu,
					ref word0, ref word1, ref word2, ref word3, ref word4);
			}
			if (chunkDigits >= 2)
			{
				PackDecimalByte(
					outputIndex++, (packedChunk >> 8) & 0xffu,
					ref word0, ref word1, ref word2, ref word3, ref word4);
			}
			PackDecimalByte(
				outputIndex, packedChunk & 0xffu,
				ref word0, ref word1, ref word2, ref word3, ref word4);
			index -= chunkDigits;
			remainingDigits -= chunkDigits;
		}
		while (currentHigh != 0 || currentLow != 0);

		if (negative)
		{
			word0 |= (uint)'-' << 24;
		}
		return length;
	}

	private static void PackDecimalByte(
		int index,
		uint encoded,
		ref uint word0,
		ref uint word1,
		ref uint word2,
		ref uint word3,
		ref uint word4)
	{
		var shift = (3 - (index & 3)) * 8;
		if (index < 4) word0 |= encoded << shift;
		else if (index < 8) word1 |= encoded << shift;
		else if (index < 12) word2 |= encoded << shift;
		else if (index < 16) word3 |= encoded << shift;
		else word4 |= encoded << shift;
	}

	private static uint PackFourDecimalDigits(uint value)
	{
		var remainder = value;
		var thousands = 0u;
		while (remainder >= 1_000u)
		{
			remainder -= 1_000u;
			thousands++;
		}
		var hundreds = 0u;
		while (remainder >= 100u)
		{
			remainder -= 100u;
			hundreds++;
		}
		var tens = 0u;
		while (remainder >= 10u)
		{
			remainder -= 10u;
			tens++;
		}
		return 0x3030_3030u |
			(thousands << 24) |
			(hundreds << 16) |
			(tens << 8) |
			remainder;
	}

	private static int PackDecimal(
		uint value,
		bool negative,
		out uint word0,
		out uint word1,
		out uint word2)
	{
		var digitCount = CountDecimalDigits(value);
		var length = digitCount + (negative ? 1 : 0);
		word0 = 0;
		word1 = 0;
		word2 = 0;
		var index = negative ? 1 : 0;
		var remainder = value;
		var place = 1u;
		if (digitCount == 10) place = 1_000_000_000u;
		else if (digitCount == 9) place = 100_000_000u;
		else if (digitCount == 8) place = 10_000_000u;
		else if (digitCount == 7) place = 1_000_000u;
		else if (digitCount == 6) place = 100_000u;
		else if (digitCount == 5) place = 10_000u;
		else if (digitCount == 4) place = 1_000u;
		else if (digitCount == 3) place = 100u;
		else if (digitCount == 2) place = 10u;
		while (place != 0)
		{
			var digit = 0u;
			while (remainder >= place)
			{
				remainder -= place;
				digit++;
			}
			var encoded = '0' + digit;
			var shift = (3 - (index & 3)) * 8;
			if (index < 4)
			{
				word0 |= encoded << shift;
			}
			else if (index < 8)
			{
				word1 |= encoded << shift;
			}
			else
			{
				word2 |= encoded << shift;
			}
			index++;
			if (place == 1_000_000_000u) place = 100_000_000u;
			else if (place == 100_000_000u) place = 10_000_000u;
			else if (place == 10_000_000u) place = 1_000_000u;
			else if (place == 1_000_000u) place = 100_000u;
			else if (place == 100_000u) place = 10_000u;
			else if (place == 10_000u) place = 1_000u;
			else if (place == 1_000u) place = 100u;
			else if (place == 100u) place = 10u;
			else if (place == 10u) place = 1u;
			else place = 0;
		}

		if (negative)
		{
			word0 |= (uint)'-' << 24;
		}
		return length;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string FormatInt32(int value, string? format)
	{
		if (format is null || format.Length == 0)
		{
			return FormatInt32(value);
		}

		var specifier = format[0];
		var precision = ParsePrecision(format);
		if (specifier is 'G' or 'g')
		{
			if (format.Length == 1 || precision == 0)
			{
				return FormatInt32(value);
			}
			M68kRuntime.ThrowFormatException();
		}
		if (specifier is 'D' or 'd')
		{
			return FormatDecimalInt32(value, precision);
		}
		if (specifier is 'X' or 'x')
		{
			return FormatHexUInt32((uint)value, precision, specifier == 'X');
		}

		M68kRuntime.ThrowFormatException();
		return "";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string FormatUInt32(uint value, string? format)
	{
		if (format is null || format.Length == 0)
		{
			return FormatUInt32(value);
		}

		var specifier = format[0];
		var precision = ParsePrecision(format);
		if (specifier is 'G' or 'g')
		{
			if (format.Length == 1 || precision == 0)
			{
				return FormatUInt32(value);
			}
			M68kRuntime.ThrowFormatException();
		}
		if (specifier is 'D' or 'd')
		{
			return FormatDecimalUInt32(value, precision);
		}
		if (specifier is 'X' or 'x')
		{
			return FormatHexUInt32(value, precision, specifier == 'X');
		}

		M68kRuntime.ThrowFormatException();
		return "";
	}

	private static int ParsePrecision(string format)
	{
		var precision = 0;
		for (var index = 1; index < format.Length; index++)
		{
			var character = format[index];
			if (character < '0' || character > '9' ||
				precision > MaximumPrecision / 10)
			{
				M68kRuntime.ThrowFormatException();
			}
			precision = precision * 10 + character - '0';
			if (precision > MaximumPrecision)
			{
				M68kRuntime.ThrowFormatException();
			}
		}
		return precision;
	}

	private static string FormatDecimalInt32(int value, int precision)
	{
		if (value >= 0)
		{
			return FormatDecimalUInt32((uint)value, precision);
		}

		var magnitude = (uint)(-(value + 1)) + 1u;
		var digits = CountDecimalDigits(magnitude);
		var paddedDigits = precision > digits ? precision : digits;
		var result = M68kRuntime.AllocateString(paddedDigits + 1);
		M68kRuntime.SetStringChar(result, 0, '-');
		WriteZeroes(result, 1, paddedDigits - digits);
		WriteDecimalValue(result, 1 + paddedDigits - digits, digits, magnitude);
		return result;
	}

	private static string FormatDecimalUInt32(uint value, int precision)
	{
		var digits = CountDecimalDigits(value);
		var paddedDigits = precision > digits ? precision : digits;
		var result = M68kRuntime.AllocateString(paddedDigits);
		WriteZeroes(result, 0, paddedDigits - digits);
		WriteDecimalValue(result, paddedDigits - digits, digits, value);
		return result;
	}

	private static string FormatHexUInt32(uint value, int precision, bool upperCase)
	{
		var digits = CountHexDigits(value);
		var paddedDigits = precision > digits ? precision : digits;
		var result = M68kRuntime.AllocateString(paddedDigits);
		WriteZeroes(result, 0, paddedDigits - digits);
		var index = paddedDigits - 1;
		var remainder = value;
		do
		{
			var digit = remainder & 15u;
			var character = digit < 10u
				? (char)('0' + digit)
				: (char)((upperCase ? 'A' : 'a') + digit - 10u);
			M68kRuntime.SetStringChar(result, index, character);
			index--;
			remainder >>= 4;
		}
		while (remainder != 0);
		return result;
	}

	private static int CountHexDigits(uint value)
	{
		if (value >= 0x1000_0000) return 8;
		if (value >= 0x0100_0000) return 7;
		if (value >= 0x0010_0000) return 6;
		if (value >= 0x0001_0000) return 5;
		if (value >= 0x0000_1000) return 4;
		if (value >= 0x0000_0100) return 3;
		if (value >= 0x0000_0010) return 2;
		return 1;
	}

	private static void WriteZeroes(string result, int start, int count)
	{
		for (var index = 0; index < count; index++)
		{
			M68kRuntime.SetStringChar(result, start + index, '0');
		}
	}

	private static void WriteDecimalValue(
		string result,
		int start,
		int digits,
		uint value)
	{
		if (value >= 100_000_000)
		{
			var high = 0u;
			var remainder = value;
			while (remainder >= 100_000_000)
			{
				remainder -= 100_000_000;
				high++;
			}
			WriteDecimalChunk(result, start, digits - 8, high);
			var middle = remainder / 10_000;
			WriteDecimalChunk(result, start + digits - 8, 4, middle);
			WriteDecimalChunk(
				result,
				start + digits - 4,
				4,
				remainder - middle * 10_000);
		}
		else if (value >= 10_000)
		{
			var high = value / 10_000;
			WriteDecimalChunk(result, start, digits - 4, high);
			WriteDecimalChunk(
				result,
				start + digits - 4,
				4,
				value - high * 10_000);
		}
		else
		{
			WriteDecimalChunk(result, start, digits, value);
		}
	}

	private static int CountDecimalDigits(uint value)
	{
		if (value >= 1_000_000_000) return 10;
		if (value >= 100_000_000) return 9;
		if (value >= 10_000_000) return 8;
		if (value >= 1_000_000) return 7;
		if (value >= 100_000) return 6;
		if (value >= 10_000) return 5;
		if (value >= 1_000) return 4;
		if (value >= 100) return 3;
		if (value >= 10) return 2;
		return 1;
	}

	private static int CountDecimalDigits64(uint high, uint low)
	{
		if (high == 0)
		{
			return CountDecimalDigits(low);
		}
		if (high < 0x0000_0002u ||
			(high == 0x0000_0002u && low < 0x540b_e400u)) return 10;
		if (high < 0x0000_0017u ||
			(high == 0x0000_0017u && low < 0x4876_e800u)) return 11;
		if (high < 0x0000_00e8u ||
			(high == 0x0000_00e8u && low < 0xd4a5_1000u)) return 12;
		if (high < 0x0000_0918u ||
			(high == 0x0000_0918u && low < 0x4e72_a000u)) return 13;
		if (high < 0x0000_5af3u ||
			(high == 0x0000_5af3u && low < 0x107a_4000u)) return 14;
		if (high < 0x0003_8d7eu ||
			(high == 0x0003_8d7eu && low < 0xa4c6_8000u)) return 15;
		if (high < 0x0023_86f2u ||
			(high == 0x0023_86f2u && low < 0x6fc1_0000u)) return 16;
		if (high < 0x0163_4578u ||
			(high == 0x0163_4578u && low < 0x5d8a_0000u)) return 17;
		if (high < 0x0de0_b6b3u ||
			(high == 0x0de0_b6b3u && low < 0xa764_0000u)) return 18;
		if (high < 0x8ac7_2304u ||
			(high == 0x8ac7_2304u && low < 0x89e8_0000u)) return 19;
		return 20;
	}

	private static void WriteDecimalValue64(
		string result,
		int start,
		int digits,
		uint high,
		uint low)
	{
		var currentHigh = high;
		var currentLow = low;
		var index = start + digits - 1;
		do
		{
			var digit = DivideUInt64By10(
				currentHigh,
				currentLow,
				out currentHigh,
				out currentLow);
			M68kRuntime.SetStringChar(result, index, (char)('0' + digit));
			index--;
		}
		while (currentHigh != 0 || currentLow != 0);
	}

	private static uint DivideUInt64By10(
		uint high,
		uint low,
		out uint quotientHigh,
		out uint quotientLow)
	{
		var carry = 0u;
		var part = high >> 16;
		var quotientPart = part / 10u;
		carry = part - quotientPart * 10u;
		quotientHigh = quotientPart << 16;

		part = (carry << 16) | (high & 0xffffu);
		quotientPart = part / 10u;
		carry = part - quotientPart * 10u;
		quotientHigh |= quotientPart;

		part = (carry << 16) | (low >> 16);
		quotientPart = part / 10u;
		carry = part - quotientPart * 10u;
		quotientLow = quotientPart << 16;

		part = (carry << 16) | (low & 0xffffu);
		quotientPart = part / 10u;
		carry = part - quotientPart * 10u;
		quotientLow |= quotientPart;
		return carry;
	}

	private static uint DivideUInt64By10000(
		uint high,
		uint low,
		out uint quotientHigh,
		out uint quotientLow)
	{
		var carry = 0u;
		var part = high >> 16;
		var quotientPart = part / 10_000u;
		carry = part - quotientPart * 10_000u;
		quotientHigh = quotientPart << 16;

		part = (carry << 16) | (high & 0xffffu);
		quotientPart = part / 10_000u;
		carry = part - quotientPart * 10_000u;
		quotientHigh |= quotientPart;

		part = (carry << 16) | (low >> 16);
		quotientPart = part / 10_000u;
		carry = part - quotientPart * 10_000u;
		quotientLow = quotientPart << 16;

		part = (carry << 16) | (low & 0xffffu);
		quotientPart = part / 10_000u;
		carry = part - quotientPart * 10_000u;
		quotientLow |= quotientPart;
		return carry;
	}

	private static void WriteDecimalChunk(
		string result,
		int start,
		int digits,
		uint value)
	{
		var index = start;
		var remainder = value;
		if (digits >= 4)
		{
			remainder = WriteDecimalPlace(result, index, remainder, 1_000);
			index++;
		}
		if (digits >= 3)
		{
			remainder = WriteDecimalPlace(result, index, remainder, 100);
			index++;
		}
		if (digits >= 2)
		{
			remainder = WriteDecimalPlace(result, index, remainder, 10);
			index++;
		}
		M68kRuntime.SetStringChar(result, index, (char)('0' + remainder));
	}

	private static uint WriteDecimalPlace(
		string result,
		int index,
		uint value,
		uint place)
	{
		var digit = 0u;
		var remainder = value;
		while (remainder >= place)
		{
			remainder -= place;
			digit++;
		}
		M68kRuntime.SetStringChar(result, index, (char)('0' + digit));
		return remainder;
	}

}

/// <summary>
/// Allocation-conscious target for the admitted composite-formatting slice.
/// The compiler rewrites a fresh one-to-eight-element params array into a
/// typed Int32 call, so neither the object array nor its boxes survive.
/// </summary>
public static class ShadowStringFormat
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Format1(string format, int argument0) =>
		FormatCore(format, 1, argument0.ToString()!, "", "", "", "", "", "", "");

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Format2(string format, int argument0, int argument1) =>
		FormatCore(format, 2, argument0.ToString()!, argument1.ToString()!, "", "", "", "", "", "");

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Format3(string format, int argument0, int argument1, int argument2) =>
		FormatCore(format, 3, argument0.ToString()!, argument1.ToString()!, argument2.ToString()!, "", "", "", "", "");

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Format4(string format, int argument0, int argument1, int argument2, int argument3) =>
		FormatCore(format, 4, argument0.ToString()!, argument1.ToString()!, argument2.ToString()!, argument3.ToString()!, "", "", "", "");

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Format5(string format, int argument0, int argument1, int argument2, int argument3, int argument4) =>
		FormatCore(format, 5, argument0.ToString()!, argument1.ToString()!, argument2.ToString()!, argument3.ToString()!, argument4.ToString()!, "", "", "");

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Format6(string format, int argument0, int argument1, int argument2, int argument3, int argument4, int argument5) =>
		FormatCore(format, 6, argument0.ToString()!, argument1.ToString()!, argument2.ToString()!, argument3.ToString()!, argument4.ToString()!, argument5.ToString()!, "", "");

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Format7(string format, int argument0, int argument1, int argument2, int argument3, int argument4, int argument5, int argument6) =>
		FormatCore(format, 7, argument0.ToString()!, argument1.ToString()!, argument2.ToString()!, argument3.ToString()!, argument4.ToString()!, argument5.ToString()!, argument6.ToString()!, "");

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Format8(string format, int argument0, int argument1, int argument2, int argument3, int argument4, int argument5, int argument6, int argument7) =>
		FormatCore(format, 8, argument0.ToString()!, argument1.ToString()!, argument2.ToString()!, argument3.ToString()!, argument4.ToString()!, argument5.ToString()!, argument6.ToString()!, argument7.ToString()!);

	private static string FormatCore(
		string format,
		int argumentCount,
		string argument0,
		string argument1,
		string argument2,
		string argument3,
		string argument4,
		string argument5,
		string argument6,
		string argument7)
	{
		var writer = new CompositeFormatWriter(format, argumentCount);
		while (writer.TryGetNextArgument(out var argumentIndex))
		{
			if (argumentIndex < 0 || argumentIndex >= argumentCount)
			{
				M68kRuntime.ThrowFormatException();
			}
			switch (argumentIndex)
			{
				case 0: writer.AppendString(argument0); break;
				case 1: writer.AppendString(argument1); break;
				case 2: writer.AppendString(argument2); break;
				case 3: writer.AppendString(argument3); break;
				case 4: writer.AppendString(argument4); break;
				case 5: writer.AppendString(argument5); break;
				case 6: writer.AppendString(argument6); break;
				case 7: writer.AppendString(argument7); break;
				default:
					M68kRuntime.ThrowFormatException();
					break;
			}
		}
		return writer.Complete();
	}

	private ref struct CompositeFormatWriter
	{
		private readonly string _format;
		private char[] _buffer;
		private int _scan;
		private int _position;

		public CompositeFormatWriter(string format, int argumentCount)
		{
			if (format is null)
			{
				M68kRuntime.ThrowArgumentNullException();
			}
			_format = format;
			_buffer = new char[format.Length + argumentCount * 11];
			_scan = 0;
			_position = 0;
		}

		public bool TryGetNextArgument(out int argumentIndex)
		{
			var format = _format;
			while (_scan < format.Length)
			{
				var character = format[_scan++];
				if (character == '{')
				{
					if (_scan < format.Length && format[_scan] == '{')
					{
						_scan++;
						AppendCharacter('{');
						continue;
					}

					if (_scan >= format.Length ||
						format[_scan] < '0' || format[_scan] > '9')
					{
						M68kRuntime.ThrowFormatException();
					}
					var index = 0;
					while (_scan < format.Length &&
						format[_scan] >= '0' && format[_scan] <= '9')
					{
						var digit = (int)format[_scan] - 48;
						if (index > 214_748_364 ||
							index == 214_748_364 && digit > 7)
						{
							M68kRuntime.ThrowFormatException();
						}
						index = index * 10 + digit;
						_scan++;
					}
					if (_scan >= format.Length || format[_scan++] != '}')
					{
						// Alignment, custom format strings, and malformed items are
						// deliberately outside this first allocation-free slice.
						M68kRuntime.ThrowFormatException();
					}
					argumentIndex = index;
					return true;
				}

				if (character == '}')
				{
					if (_scan >= format.Length || format[_scan] != '}')
					{
						M68kRuntime.ThrowFormatException();
					}
					_scan++;
				}
				AppendCharacter(character);
			}

			argumentIndex = 0;
			return false;
		}

		public void AppendString(string value)
		{
			EnsureCapacity(_position + value.Length);
			for (var index = 0; index < value.Length; index++)
			{
				_buffer[_position++] = value[index];
			}
		}

		public string Complete()
		{
			var result = M68kRuntime.AllocateString(_position);
			for (var index = 0; index < _position; index++)
			{
				M68kRuntime.SetStringChar(result, index, _buffer[index]);
			}
			return result;
		}

		private void AppendCharacter(char value)
		{
			EnsureCapacity(_position + 1);
			_buffer[_position++] = value;
		}

		private void EnsureCapacity(int required)
		{
			if (required <= _buffer.Length)
			{
				return;
			}
			var replacement = new char[required];
			for (var index = 0; index < _position; index++)
			{
				replacement[index] = _buffer[index];
			}
			_buffer = replacement;
		}
	}
}

/// <summary>
/// Private target body for the pinned .NET 10 interpolated-string handler.
/// Field order mirrors the public handler so its framework identity and stack
/// layout stay public while target behavior remains compact and pay-for-play.
/// </summary>
public ref struct ShadowDefaultInterpolatedStringHandler
{
#pragma warning disable CS0414 // Layout-compatibility fields for the public .NET 10 handler.
	private object? _provider;
	private char[]? _buffer;
	private uint _spanData;
	private uint _spanLength;
	private uint _spanOwner;
	private int _position;
	private bool _hasCustomFormatter;
#pragma warning restore CS0414

	[MethodImpl(MethodImplOptions.NoInlining)]
	public ShadowDefaultInterpolatedStringHandler(int literalLength, int formattedCount)
	{
		_provider = null;
		_buffer = new char[literalLength + formattedCount * 11];
		_spanData = 0;
		_spanLength = 0;
		_spanOwner = 0;
		_position = 0;
		_hasCustomFormatter = false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void AppendLiteral(string value) => AppendString(value);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void AppendFormattedInt32(int value)
	{
		if (value >= 0)
		{
			AppendDecimalUInt32((uint)value, 0);
			return;
		}

		var magnitude = (uint)(-(value + 1)) + 1u;
		var digits = CountDecimalDigits(magnitude);
		EnsureCapacity(_position + digits + 1);
		_buffer![_position++] = '-';
		WriteDecimalBackwards(magnitude, digits);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void AppendFormattedUInt32(uint value, string? format)
	{
		if (format is null || format.Length == 0)
		{
			AppendDecimalUInt32(value, 0);
			return;
		}

		var specifier = format[0];
		var precision = ParsePrecision(format);
		if (specifier is 'G' or 'g')
		{
			if (format.Length == 1 || precision == 0)
			{
				AppendDecimalUInt32(value, 0);
				return;
			}
			M68kRuntime.ThrowFormatException();
			return;
		}
		if (specifier is 'D' or 'd')
		{
			AppendDecimalUInt32(value, precision);
			return;
		}
		if (specifier is 'X' or 'x')
		{
			AppendHexUInt32(value, precision, specifier == 'X');
			return;
		}

		M68kRuntime.ThrowFormatException();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public string ToStringAndClear()
	{
		var result = M68kRuntime.AllocateString(_position);
		var buffer = _buffer!;
		for (var index = 0; index < _position; index++)
		{
			M68kRuntime.SetStringChar(result, index, buffer[index]);
		}
		_buffer = null;
		_position = 0;
		return result;
	}

	private void AppendString(string value)
	{
		EnsureCapacity(_position + value.Length);
		var buffer = _buffer!;
		for (var index = 0; index < value.Length; index++)
		{
			buffer[_position + index] = value[index];
		}
		_position += value.Length;
	}

	private void AppendDecimalUInt32(uint value, int precision)
	{
		var digits = CountDecimalDigits(value);
		var paddedDigits = precision > digits ? precision : digits;
		EnsureCapacity(_position + paddedDigits);
		var zeroes = paddedDigits - digits;
		for (var index = 0; index < zeroes; index++)
		{
			_buffer![_position++] = '0';
		}
		WriteDecimalBackwards(value, digits);
	}

	private void WriteDecimalBackwards(uint value, int digits)
	{
		var end = _position + digits;
		var remainder = value;
		for (var index = end - 1; index >= _position; index--)
		{
			var quotient = remainder / 10u;
			_buffer![index] = (char)('0' + remainder - quotient * 10u);
			remainder = quotient;
		}
		_position = end;
	}

	private void AppendHexUInt32(uint value, int precision, bool upperCase)
	{
		var digits = CountHexDigits(value);
		var paddedDigits = precision > digits ? precision : digits;
		EnsureCapacity(_position + paddedDigits);
		var zeroes = paddedDigits - digits;
		for (var index = 0; index < zeroes; index++)
		{
			_buffer![_position++] = '0';
		}
		var end = _position + digits;
		var remainder = value;
		for (var index = end - 1; index >= _position; index--)
		{
			var digit = remainder & 15u;
			_buffer![index] = digit < 10u
				? (char)('0' + digit)
				: (char)((upperCase ? 'A' : 'a') + digit - 10u);
			remainder >>= 4;
		}
		_position = end;
	}

	private static int ParsePrecision(string format)
	{
		const int maximumPrecision = 999_999_999;
		var precision = 0;
		for (var index = 1; index < format.Length; index++)
		{
			var character = format[index];
			if (character < '0' || character > '9' ||
				precision > maximumPrecision / 10)
			{
				M68kRuntime.ThrowFormatException();
			}
			precision = precision * 10 + character - '0';
			if (precision > maximumPrecision)
			{
				M68kRuntime.ThrowFormatException();
			}
		}
		return precision;
	}

	private static int CountDecimalDigits(uint value)
	{
		if (value >= 1_000_000_000) return 10;
		if (value >= 100_000_000) return 9;
		if (value >= 10_000_000) return 8;
		if (value >= 1_000_000) return 7;
		if (value >= 100_000) return 6;
		if (value >= 10_000) return 5;
		if (value >= 1_000) return 4;
		if (value >= 100) return 3;
		if (value >= 10) return 2;
		return 1;
	}

	private static int CountHexDigits(uint value)
	{
		if (value >= 0x1000_0000) return 8;
		if (value >= 0x0100_0000) return 7;
		if (value >= 0x0010_0000) return 6;
		if (value >= 0x0001_0000) return 5;
		if (value >= 0x0000_1000) return 4;
		if (value >= 0x0000_0100) return 3;
		if (value >= 0x0000_0010) return 2;
		return 1;
	}

	private void EnsureCapacity(int required)
	{
		var buffer = _buffer!;
		if (required <= buffer.Length)
		{
			return;
		}

		var replacement = new char[required];
		for (var index = 0; index < _position; index++)
		{
			replacement[index] = buffer[index];
		}
		_buffer = replacement;
	}
}

/// <summary>
/// Compact growable backing implementation for the admitted
/// <see cref="System.Collections.Generic.List{T}"/> contract.
/// </summary>
public sealed class ShadowList<T>
{
	internal T[]? _items;
	internal int _size;
	internal int _version;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public ShadowList()
	{
		_items = null;
		_size = 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public ShadowList(int capacity)
	{
		if (capacity < 0)
		{
			M68kRuntime.ThrowArgumentOutOfRangeException();
		}
		_items = capacity == 0 ? null : new T[capacity];
		_size = 0;
	}

	public int Capacity
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get => _items is null ? 0 : _items.Length;
		[MethodImpl(MethodImplOptions.NoInlining)]
		set
		{
			if (value < _size)
			{
				M68kRuntime.ThrowArgumentOutOfRangeException();
			}
			var items = _items;
			var currentCapacity = items is null ? 0 : items.Length;
			if (value == currentCapacity)
			{
				return;
			}
			if (value == 0)
			{
				_items = null;
				return;
			}
			SetCapacity(items, value);
		}
	}

	public int Count
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get => _size;
	}

	public T this[int index]
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			if ((uint)index >= (uint)_size)
			{
				M68kRuntime.ThrowArgumentOutOfRangeException();
			}
			return _items![index];
		}
		[MethodImpl(MethodImplOptions.NoInlining)]
		set
		{
			if ((uint)index >= (uint)_size)
			{
				M68kRuntime.ThrowArgumentOutOfRangeException();
			}
			_items![index] = value;
			_version++;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Add(T item)
	{
		_version++;
		var items = _items;
		if (items is null || _size == items.Length)
		{
			Grow(items);
		}
		_items![_size] = item;
		_size++;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Clear()
	{
		_version++;
		var items = _items;
		for (var index = 0; index < _size; index++)
		{
			items![index] = default!;
		}
		_size = 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public bool Contains(T item) => _size != 0 && IndexOf(item) >= 0;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public int IndexOf(T item)
	{
		var items = _items;
		for (var index = 0; index < _size; index++)
		{
			if (M68kRuntime.DefaultEquals(items![index], item))
			{
				return index;
			}
		}
		return -1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public bool Remove(T item)
	{
		var index = IndexOf(item);
		if (index < 0)
		{
			return false;
		}
		RemoveAt(index);
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void RemoveAt(int index)
	{
		if ((uint)index >= (uint)_size)
		{
			M68kRuntime.ThrowArgumentOutOfRangeException();
		}
		var items = _items!;
		var last = _size - 1;
		for (var source = index + 1; source <= last; source++)
		{
			items[source - 1] = items[source];
		}
		items[last] = default!;
		_size = last;
		_version++;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public ShadowListEnumerator<T> GetEnumerator()
	{
		var result = default(ShadowListEnumerator<T>);
		result._list = this;
		result._version = _version;
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public T[] ToArray()
	{
		var result = new T[_size];
		var items = _items;
		for (var index = 0; index < _size; index++)
		{
			result[index] = items![index];
		}
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void Grow(T[]? items)
	{
		var capacity = items is null ? 4 : items.Length * 2;
		if (capacity < 0)
		{
			M68kRuntime.ThrowOutOfMemoryException();
		}
		var replacement = new T[capacity];
		for (var index = 0; index < _size; index++)
		{
			replacement[index] = items![index];
		}
		_items = replacement;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void SetCapacity(T[]? items, int capacity)
	{
		var replacement = new T[capacity];
		for (var index = 0; index < _size; index++)
		{
			replacement[index] = items![index];
		}
		_items = replacement;
	}
}

/// <summary>
/// Compact hash-table backing implementation for the admitted
/// <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/> contract.
/// </summary>
public sealed class ShadowDictionary<TKey, TValue>
{
	private ShadowDictionaryStorage<TKey, TValue>? _storage;
	private ShadowDictionaryValueCollection<TKey, TValue>? _values;
	private int _count;
	private int _version;

	internal int ValueCount => _count;

	internal int Version => _version;

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal TValue GetValueAt(int index, int version)
	{
		if (version != _version || (uint)index >= (uint)_count)
		{
			M68kRuntime.ThrowInvalidOperationException();
		}
		return _storage!._values![index];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public ShadowDictionary()
	{
	}

	public int Count
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get => _count;
	}

	public ShadowDictionaryValueCollection<TKey, TValue> Values
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			var values = _values;
			if (values is null)
			{
				values = new ShadowDictionaryValueCollection<TKey, TValue>(this);
				_values = values;
			}
			return values;
		}
	}

	public TValue this[TKey key]
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			var index = FindEntry(key);
			if (index < 0)
			{
				M68kRuntime.ThrowKeyNotFoundException();
			}
			return _storage!._values![index];
		}
		[MethodImpl(MethodImplOptions.NoInlining)]
		set => Insert(key, value, false);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Add(TKey key, TValue value) => Insert(key, value, true);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public bool TryGetValue(TKey key, out TValue value)
	{
		var index = FindEntry(key);
		if (index >= 0)
		{
			value = _storage!._values![index];
			return true;
		}
		value = default!;
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private int FindEntry(TKey key)
	{
		if (M68kRuntime.DictionaryKeyIsNull(key))
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		if (_storage is null)
		{
			return -1;
		}
		var hash = M68kRuntime.DefaultHashCode(key) & 0x7fffffff;
		var storage = _storage;
		var buckets = storage!._buckets;
		if (buckets is null)
		{
			return -1;
		}
		var bucket = hash & (buckets.Length - 1);
		for (var probes = 0; probes < buckets.Length; probes++)
		{
			var entry = buckets[bucket];
			if (entry == 0)
			{
				return -1;
			}
			var index = entry - 1;
			if (storage._hashes![index] == hash)
			{
				if (M68kRuntime.DefaultEquals(storage._keys![index], key))
				{
					return index;
				}
			}
			bucket = (bucket + 1) & (buckets.Length - 1);
		}
		return -1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void Insert(TKey key, TValue value, bool throwOnExisting)
	{
		var existing = FindEntry(key);
		if (existing >= 0)
		{
			if (throwOnExisting)
			{
				M68kRuntime.ThrowArgumentException();
			}
			_storage!._values![existing] = value;
			_version++;
			return;
		}

		var storage = _storage;
		if (storage is null || storage._buckets is null ||
			(_count + 1) * 4 > storage._buckets.Length * 3)
		{
			Grow();
			storage = _storage;
		}
		var hash = M68kRuntime.DefaultHashCode(key) & 0x7fffffff;
		var bucket = hash & (storage!._buckets!.Length - 1);
		while (storage._buckets[bucket] != 0)
		{
			bucket = (bucket + 1) & (storage._buckets.Length - 1);
		}
		var index = _count;
		storage._hashes![index] = hash;
		storage._keys![index] = key;
		storage._values![index] = value;
		storage._buckets[bucket] = index + 1;
		_count = index + 1;
		_version++;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void Grow()
	{
		var storage = _storage;
		if (storage is null)
		{
			storage = new ShadowDictionaryStorage<TKey, TValue>();
			_storage = storage;
		}
		var oldCapacity = storage._buckets is null ? 0 : storage._buckets.Length;
		var capacity = oldCapacity == 0 ? 4 : oldCapacity * 2;
		if (capacity < 0)
		{
			M68kRuntime.ThrowOutOfMemoryException();
		}
		GrowHashes(storage, capacity);
		GrowKeys(storage, capacity);
		GrowValues(storage, capacity);
		storage._buckets = new int[capacity];
		for (var index = 0; index < _count; index++)
		{
			var bucket = storage._hashes![index] & (capacity - 1);
			while (storage._buckets[bucket] != 0)
			{
				bucket = (bucket + 1) & (capacity - 1);
			}
			storage._buckets[bucket] = index + 1;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void GrowHashes(ShadowDictionaryStorage<TKey, TValue> storage, int capacity)
	{
		var replacement = new int[capacity];
		for (var index = 0; index < _count; index++)
		{
			replacement[index] = storage._hashes![index];
		}
		storage._hashes = replacement;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void GrowKeys(ShadowDictionaryStorage<TKey, TValue> storage, int capacity)
	{
		var replacement = new TKey[capacity];
		for (var index = 0; index < _count; index++)
		{
			replacement[index] = storage._keys![index];
		}
		storage._keys = replacement;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void GrowValues(ShadowDictionaryStorage<TKey, TValue> storage, int capacity)
	{
		var replacement = new TValue[capacity];
		for (var index = 0; index < _count; index++)
		{
			replacement[index] = storage._values![index];
		}
		storage._values = replacement;
	}
}

public sealed class ShadowDictionaryValueCollection<TKey, TValue> : IEnumerable<TValue>
{
	internal readonly ShadowDictionary<TKey, TValue> Dictionary;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public ShadowDictionaryValueCollection(ShadowDictionary<TKey, TValue> dictionary)
	{
		Dictionary = dictionary;
	}

	public IEnumerator<TValue> GetEnumerator() =>
		new ShadowDictionaryValueEnumerator<TKey, TValue>(Dictionary);

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class ShadowDictionaryValueEnumerator<TKey, TValue> : IEnumerator<TValue>
{
	private readonly ShadowDictionary<TKey, TValue> _dictionary;
	private readonly int _version;
	private int _index;
	private TValue _current;

	public ShadowDictionaryValueEnumerator(ShadowDictionary<TKey, TValue> dictionary)
	{
		_dictionary = dictionary;
		_version = dictionary.Version;
		_index = -1;
		_current = default!;
	}

	public bool MoveNext()
	{
		var next = _index + 1;
		if (next < _dictionary.ValueCount)
		{
			_current = _dictionary.GetValueAt(next, _version);
			_index = next;
			return true;
		}
		if (_version != _dictionary.Version)
		{
			M68kRuntime.ThrowInvalidOperationException();
		}
		_index = _dictionary.ValueCount;
		_current = default!;
		return false;
	}

	public TValue Current => _current;

	object IEnumerator.Current => Current!;

	public void Reset()
	{
		if (_version != _dictionary.Version)
		{
			M68kRuntime.ThrowInvalidOperationException();
		}
		_index = -1;
		_current = default!;
	}

	public void Dispose()
	{
	}
}

public sealed class ShadowDictionaryStorage<TKey, TValue>
{
	internal int[]? _buckets;
	internal int[]? _hashes;
	internal TKey[]? _keys;
	internal TValue[]? _values;
}

/// <summary>
/// Layout-compatible private implementation of the admitted
/// <see cref="System.Collections.Generic.List{T}.Enumerator"/> contract.
/// </summary>
public struct ShadowListEnumerator<T>
{
	internal ShadowList<T> _list;
	internal int _version;
	internal int _index;
	internal T _current;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public bool MoveNext()
	{
		var list = _list;
		if (_version != list._version)
		{
			M68kRuntime.ThrowInvalidOperationException();
		}
		if ((uint)_index < (uint)list._size)
		{
			_current = list._items![_index];
			_index++;
			return true;
		}
		_current = default!;
		_index = -1;
		return false;
	}

	public T Current
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get => _current;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Dispose()
	{
	}
}

/// <summary>
/// Private implementation of the first selected <see cref="Enumerable"/>
/// factories and materializer. Public programs retain the official LINQ
/// identities; these types are target-runtime details.
/// </summary>
public static class ShadowEnumerable
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static int CheckedAdd(int left, int right)
	{
		if (right > 0)
		{
			if (left > int.MaxValue - right)
			{
				M68kRuntime.ThrowOverflowException();
			}
		}
		else if (right < 0)
		{
			if (left < int.MinValue - right)
			{
				M68kRuntime.ThrowOverflowException();
			}
		}
		return left + right;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static IEnumerable<int> Range(int start, int count)
	{
		if (count < 0 ||
			(count != 0 && start > int.MaxValue - count + 1))
		{
			M68kRuntime.ThrowArgumentOutOfRangeException();
		}
		return new ShadowRangeIterator(start, count);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static IEnumerable<TResult> Repeat<TResult>(TResult element, int count)
	{
		if (count < 0)
		{
			M68kRuntime.ThrowArgumentOutOfRangeException();
		}
		return new ShadowRepeatIterator<TResult>(element, count);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static IOrderedEnumerable<TSource> DictionaryUInt32ValuesOrderBy<TSource, TKey>(
		IEnumerable<TSource> source,
		Func<TSource, int> keySelector)
	{
		if (source is null || keySelector is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return new ShadowPrimaryOrderedEnumerable<TSource>(
			(ShadowDictionaryValueCollection<uint, TSource>)source,
			keySelector);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static IOrderedEnumerable<TSource> DictionaryUInt32ValuesThenBy<TSource, TKey>(
		IOrderedEnumerable<TSource> source,
		Func<TSource, int> keySelector)
	{
		if (source is null || keySelector is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return new ShadowOrderedEnumerable<TSource>(
			(ShadowPrimaryOrderedEnumerable<TSource>)source,
			keySelector);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static IEnumerable<int> SelectInt32(
		IEnumerable<int> source,
		Func<int, int> selector)
	{
		if (source is null || selector is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return new ShadowInt32SelectIterator(
			(ShadowRangeIterator)source,
			selector);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static IEnumerable<int> RangeWhereInt32(
		IEnumerable<int> source,
		Func<int, bool> predicate)
	{
		if (source is null || predicate is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return new ShadowRangeWhereIterator(
			(ShadowRangeIterator)source,
			predicate);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static IEnumerable<int> SelectWhereInt32(
		IEnumerable<int> source,
		Func<int, bool> predicate)
	{
		if (source is null || predicate is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return new ShadowSelectWhereIterator(
			(ShadowInt32SelectIterator)source,
			predicate);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static IEnumerable<int> RangeTakeInt32(
		IEnumerable<int> source,
		int count)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowRangeIterator)source).Take(count);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static IEnumerable<int> RepeatInt32TakeInt32(
		IEnumerable<int> source,
		int count)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowRepeatIterator<int>)source).Take(count);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static IEnumerable<int> SelectInt32TakeInt32(
		IEnumerable<int> source,
		int count)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowInt32SelectIterator)source).Take(count);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static IEnumerable<int> RangeWhereInt32TakeInt32(
		IEnumerable<int> source,
		int count)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowRangeWhereIterator)source).Take(count);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static IEnumerable<int> SelectWhereInt32TakeInt32(
		IEnumerable<int> source,
		int count)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowSelectWhereIterator)source).Take(count);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static TSource[] ToArray<TSource>(IEnumerable<TSource> source)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		if (source is ShadowEnumerableIterator<TSource> iterator)
		{
			return iterator.Materialize();
		}
		M68kRuntime.ThrowArgumentException();
		return null!;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int[] RangeToArray(IEnumerable<int> source)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowRangeIterator)source).MaterializeRange();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static TSource[] RepeatToArray<TSource>(IEnumerable<TSource> source)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowRepeatIterator<TSource>)source).MaterializeRepeat();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int[] SelectInt32ToArray(IEnumerable<int> source)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowInt32SelectIterator)source).MaterializeSelect();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int[] RangeWhereInt32ToArray(IEnumerable<int> source)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowRangeWhereIterator)source).MaterializeWhere();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int[] SelectWhereInt32ToArray(IEnumerable<int> source)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowSelectWhereIterator)source).MaterializeWhere();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool RangeAny(IEnumerable<int> source)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowRangeIterator)source).Any();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool RangeAnyPredicate(
		IEnumerable<int> source,
		Func<int, bool> predicate)
	{
		if (source is null || predicate is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowRangeIterator)source).Any(predicate);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool RepeatInt32Any(IEnumerable<int> source)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowRepeatIterator<int>)source)._count != 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool RepeatInt32AnyPredicate(
		IEnumerable<int> source,
		Func<int, bool> predicate)
	{
		if (source is null || predicate is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		var repeat = (ShadowRepeatIterator<int>)source;
		var found = 0;
		for (var index = 0; index < repeat._count; index++)
		{
			if (predicate(repeat._element))
			{
				found = 1;
				break;
			}
		}
		return found != 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool SelectInt32Any(IEnumerable<int> source)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowInt32SelectIterator)source).Any();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool SelectInt32AnyPredicate(
		IEnumerable<int> source,
		Func<int, bool> predicate)
	{
		if (source is null || predicate is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowInt32SelectIterator)source).Any(predicate);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool RangeWhereInt32Any(IEnumerable<int> source)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowRangeWhereIterator)source).Any();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool RangeWhereInt32AnyPredicate(
		IEnumerable<int> source,
		Func<int, bool> predicate)
	{
		if (source is null || predicate is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowRangeWhereIterator)source).Any(predicate);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool RangeWhereInt32TakeAnyPredicate(
		IEnumerable<int> source,
		Func<int, bool> predicate)
	{
		if (source is null || predicate is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowRangeWhereIterator)source).AnyTaken(predicate);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool SelectWhereInt32Any(IEnumerable<int> source)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowSelectWhereIterator)source).Any();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool SelectWhereInt32AnyPredicate(
		IEnumerable<int> source,
		Func<int, bool> predicate)
	{
		if (source is null || predicate is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowSelectWhereIterator)source).Any(predicate);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool SelectWhereInt32TakeAnyPredicate(
		IEnumerable<int> source,
		Func<int, bool> predicate)
	{
		if (source is null || predicate is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowSelectWhereIterator)source).AnyTaken(predicate);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int RangeSum(IEnumerable<int> source)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowRangeIterator)source).Sum();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int RangeSumSelector(
		IEnumerable<int> source,
		Func<int, int> selector)
	{
		if (source is null || selector is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowRangeIterator)source).Sum(selector);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ArraySumSelector<TSource>(
		IEnumerable<TSource> source,
		Func<TSource, int> selector)
		where TSource : struct
	{
		if (source is null || selector is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		var array = (TSource[])source;
		var sum = 0;
		for (var index = 0; index < array.Length; index++)
		{
			sum = CheckedAdd(sum, selector(array[index]));
		}
		return sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int RepeatInt32Sum(IEnumerable<int> source)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		var repeat = (ShadowRepeatIterator<int>)source;
		var sum = 0;
		for (var index = 0; index < repeat._count; index++)
		{
			sum = CheckedAdd(sum, repeat._element);
		}
		return sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int RepeatInt32SumSelector(
		IEnumerable<int> source,
		Func<int, int> selector)
	{
		if (source is null || selector is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		var repeat = (ShadowRepeatIterator<int>)source;
		var sum = 0;
		for (var index = 0; index < repeat._count; index++)
		{
			sum = CheckedAdd(sum, selector(repeat._element));
		}
		return sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SelectInt32Sum(IEnumerable<int> source)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowInt32SelectIterator)source).Sum();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SelectInt32SumSelector(
		IEnumerable<int> source,
		Func<int, int> selector)
	{
		if (source is null || selector is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowInt32SelectIterator)source).Sum(selector);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int RangeWhereInt32Sum(IEnumerable<int> source)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowRangeWhereIterator)source).Sum();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int RangeWhereInt32SumSelector(
		IEnumerable<int> source,
		Func<int, int> selector)
	{
		if (source is null || selector is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowRangeWhereIterator)source).Sum(selector);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int RangeWhereInt32TakeSum(IEnumerable<int> source)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowRangeWhereIterator)source).SumTaken();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int RangeWhereInt32TakeSumSelector(
		IEnumerable<int> source,
		Func<int, int> selector)
	{
		if (source is null || selector is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowRangeWhereIterator)source).SumTaken(selector);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SelectWhereInt32Sum(IEnumerable<int> source)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowSelectWhereIterator)source).Sum();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SelectWhereInt32SumSelector(
		IEnumerable<int> source,
		Func<int, int> selector)
	{
		if (source is null || selector is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowSelectWhereIterator)source).Sum(selector);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SelectWhereInt32TakeSum(IEnumerable<int> source)
	{
		if (source is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowSelectWhereIterator)source).SumTaken();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SelectWhereInt32TakeSumSelector(
		IEnumerable<int> source,
		Func<int, int> selector)
	{
		if (source is null || selector is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}
		return ((ShadowSelectWhereIterator)source).SumTaken(selector);
	}

}

/// <summary>
/// Private deferred primary-key state for the first stable ordering slice.
/// </summary>
public sealed class ShadowPrimaryOrderedEnumerable<T> : IOrderedEnumerable<T>
{
	internal readonly ShadowDictionaryValueCollection<uint, T> Source;
	internal readonly Func<T, int> Selector;

	public ShadowPrimaryOrderedEnumerable(
		ShadowDictionaryValueCollection<uint, T> source,
		Func<T, int> selector)
	{
		Source = source;
		Selector = selector;
	}

	public IEnumerator<T> GetEnumerator() =>
		throw new NotSupportedException(
			"The selected profile requires one exact ThenBy stage before enumeration.");

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	IOrderedEnumerable<T> IOrderedEnumerable<T>.CreateOrderedEnumerable<TKey>(
		Func<T, TKey> keySelector,
		IComparer<TKey>? comparer,
		bool descending) =>
		throw new NotSupportedException(
			"Only the compiler-bound ascending Int32 ThenBy slice is supported.");
}

/// <summary>
/// Private deferred two-key state tied to Dictionary&lt;uint,T&gt;.Values and two
/// ascending Int32 selectors. Each ordering stage has only two reference fields.
/// </summary>
public sealed class ShadowOrderedEnumerable<T> : IOrderedEnumerable<T>
{
	private readonly ShadowPrimaryOrderedEnumerable<T> _primary;
	private readonly Func<T, int> _secondarySelector;

	public ShadowOrderedEnumerable(
		ShadowPrimaryOrderedEnumerable<T> primary,
		Func<T, int> secondarySelector)
	{
		_primary = primary;
		_secondarySelector = secondarySelector;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public IEnumerator<T> GetEnumerator()
	{
		var dictionary = _primary.Source.Dictionary;
		var count = dictionary.ValueCount;
		var version = dictionary.Version;
		var elements = new T[count];
		var permutation = new int[count];

		for (var index = 0; index < count; index++)
		{
			elements[index] = dictionary.GetValueAt(index, version);
			permutation[index] = index;
		}
		var primaryKeys = MaterializeKeys(elements, _primary.Selector);
		var secondaryKeys = MaterializeKeys(elements, _secondarySelector);

		ShadowInt32StablePermutationSort.Sort(
			permutation,
			primaryKeys,
			secondaryKeys);
		return new ShadowOrderedEnumerator<T>(elements, permutation);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int[] MaterializeKeys(T[] elements, Func<T, int> selector)
	{
		var keys = new int[elements.Length];
		for (var index = 0; index < elements.Length; index++)
		{
			keys[index] = selector(elements[index]);
		}
		return keys;
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	IOrderedEnumerable<T> IOrderedEnumerable<T>.CreateOrderedEnumerable<TKey>(
		Func<T, TKey> keySelector,
		IComparer<TKey>? comparer,
		bool descending) =>
		throw new NotSupportedException(
			"Only the compiler-bound ascending Int32 ThenBy slice is supported.");
}

public static class ShadowInt32StablePermutationSort
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Sort(
		int[] permutation,
		int[] primaryKeys,
		int[] secondaryKeys)
	{
		var count = permutation.Length;
		for (var root = count / 2 - 1; root >= 0; root--)
		{
			SiftDown(permutation, primaryKeys, secondaryKeys, root, count);
		}
		for (var end = count - 1; end > 0; end--)
		{
			var first = permutation[0];
			permutation[0] = permutation[end];
			permutation[end] = first;
			SiftDown(permutation, primaryKeys, secondaryKeys, 0, end);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void SiftDown(
		int[] permutation,
		int[] primaryKeys,
		int[] secondaryKeys,
		int root,
		int count)
	{
		var currentRoot = root;
		while (true)
		{
			var child = currentRoot * 2 + 1;
			if (child >= count)
			{
				return;
			}
			if (child + 1 < count)
			{
				if (IsAfter(
					permutation[child + 1],
					permutation[child],
					primaryKeys,
					secondaryKeys))
				{
					child++;
				}
			}
			if (!IsAfter(
					permutation[child],
					permutation[currentRoot],
					primaryKeys,
					secondaryKeys))
			{
				return;
			}
			var value = permutation[currentRoot];
			permutation[currentRoot] = permutation[child];
			permutation[child] = value;
			currentRoot = child;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsAfter(
		int left,
		int right,
		int[] primaryKeys,
		int[] secondaryKeys)
	{
		var leftPrimary = primaryKeys[left];
		var rightPrimary = primaryKeys[right];
		if (leftPrimary != rightPrimary)
		{
			return leftPrimary > rightPrimary;
		}
		var leftSecondary = secondaryKeys[left];
		var rightSecondary = secondaryKeys[right];
		if (leftSecondary != rightSecondary)
		{
			return leftSecondary > rightSecondary;
		}
		return left > right;
	}
}

public class ShadowOrderedEnumeratorBase
{
	protected int Index = -1;
	protected readonly int Count;

	protected ShadowOrderedEnumeratorBase(int count)
	{
		Count = count;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public bool MoveNext()
	{
		if (Index + 1 < Count)
		{
			Index++;
			return true;
		}
		Index = Count;
		return false;
	}

	public void Reset() => Index = -1;

	public void Dispose()
	{
	}
}

public sealed class ShadowOrderedEnumerator<T> :
	ShadowOrderedEnumeratorBase,
	IEnumerator<T>
{
	private readonly T[] _elements;
	private readonly int[] _permutation;

	public ShadowOrderedEnumerator(T[] elements, int[] permutation)
		: base(permutation.Length)
	{
		_elements = elements;
		_permutation = permutation;
	}

	public T Current
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			if ((uint)Index >= (uint)Count)
			{
				M68kRuntime.ThrowInvalidOperationException();
			}
			return _elements[_permutation[Index]];
		}
	}

	object IEnumerator.Current => Current!;
}

/// <summary>
/// Private immutable sequence state shared by selected LINQ iterator shadows.
/// </summary>
public abstract class ShadowEnumerableIterator<T> : IEnumerable<T>
{
	internal abstract int Count { get; }

	internal abstract T GetElement(int index);

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal T[] Materialize()
	{
		var result = new T[Count];
		for (var index = 0; index < result.Length; index++)
		{
			result[index] = GetElement(index);
		}
		return result;
	}

	public IEnumerator<T> GetEnumerator() =>
		new ShadowEnumerableEnumerator<T>(this);

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Private immutable state for <see cref="Enumerable.Range(int, int)"/>.
/// </summary>
public sealed class ShadowRangeIterator : ShadowEnumerableIterator<int>
{
	private readonly int _start;
	private readonly int _count;

	public ShadowRangeIterator(int start, int count)
	{
		_start = start;
		_count = count;
	}

	internal override int Count => _count;

	internal override int GetElement(int index) => _start + index;

	internal ShadowRangeIterator Take(int count)
	{
		var takeCount = 0;
		if (count > 0)
		{
			takeCount = count;
			if (takeCount > _count)
			{
				takeCount = _count;
			}
		}
		if (takeCount == _count)
		{
			return this;
		}
		return new ShadowRangeIterator(_start, takeCount);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal int[] MaterializeRange()
	{
		var result = new int[_count];
		for (var index = 0; index < result.Length; index++)
		{
			result[index] = _start + index;
		}
		return result;
	}

	internal bool Any() => _count != 0;

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal bool Any(Func<int, bool> predicate)
	{
		var found = 0;
		for (var index = 0; index < _count; index++)
		{
			if (predicate(_start + index))
			{
				found = 1;
				break;
			}
		}
		return found != 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal int Sum()
	{
		var sum = 0;
		for (var index = 0; index < _count; index++)
		{
			var value = _start + index;
			sum = ShadowEnumerable.CheckedAdd(sum, value);
		}
		return sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal int Sum(Func<int, int> selector)
	{
		var sum = 0;
		for (var index = 0; index < _count; index++)
		{
			sum = ShadowEnumerable.CheckedAdd(sum, selector(_start + index));
		}
		return sum;
	}
}

/// <summary>
/// Private deferred projection over a selected exact iterator source.
/// </summary>
public sealed class ShadowInt32SelectIterator : ShadowEnumerableIterator<int>
{
	private readonly ShadowRangeIterator _source;
	private readonly Func<int, int> _selector;
	private readonly int _count;

	public ShadowInt32SelectIterator(
		ShadowRangeIterator source,
		Func<int, int> selector)
	{
		_source = source;
		_selector = selector;
		_count = source.Count;
	}

	private ShadowInt32SelectIterator(
		ShadowRangeIterator source,
		Func<int, int> selector,
		int count)
	{
		_source = source;
		_selector = selector;
		_count = count;
	}

	internal override int Count => _count;

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal override int GetElement(int index) =>
		_selector(_source.GetElement(index));

	internal ShadowInt32SelectIterator Take(int count)
	{
		var takeCount = 0;
		if (count > 0)
		{
			takeCount = count;
			if (takeCount > _count)
			{
				takeCount = _count;
			}
		}
		if (takeCount == _count)
		{
			return this;
		}
		return new ShadowInt32SelectIterator(_source, _selector, takeCount);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal int[] MaterializeSelect()
	{
		var result = new int[_count];
		for (var index = 0; index < result.Length; index++)
		{
			result[index] = _selector(_source.GetElement(index));
		}
		return result;
	}

	internal bool Any() => _count != 0;

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal bool Any(Func<int, bool> predicate)
	{
		var found = 0;
		for (var index = 0; index < _count; index++)
		{
			if (predicate(_selector(_source.GetElement(index))))
			{
				found = 1;
				break;
			}
		}
		return found != 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal int Sum()
	{
		var sum = 0;
		for (var index = 0; index < _count; index++)
		{
			sum = ShadowEnumerable.CheckedAdd(
				sum,
				_selector(_source.GetElement(index)));
		}
		return sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal int Sum(Func<int, int> selector)
	{
		var sum = 0;
		for (var index = 0; index < _count; index++)
		{
			var value = _selector(_source.GetElement(index));
			sum = ShadowEnumerable.CheckedAdd(sum, selector(value));
		}
		return sum;
	}
}

/// <summary>
/// Private deferred predicate over an exact Range source.
/// </summary>
public sealed class ShadowRangeWhereIterator : IEnumerable<int>
{
	private readonly ShadowRangeIterator _source;
	private readonly Func<int, bool> _predicate;
	private readonly int _takeCount;

	public ShadowRangeWhereIterator(
		ShadowRangeIterator source,
		Func<int, bool> predicate)
	{
		_source = source;
		_predicate = predicate;
		_takeCount = source.Count;
	}

	private ShadowRangeWhereIterator(
		ShadowRangeIterator source,
		Func<int, bool> predicate,
		int takeCount)
	{
		_source = source;
		_predicate = predicate;
		_takeCount = takeCount;
	}

	internal ShadowRangeWhereIterator Take(int count)
	{
		var takeCount = 0;
		if (count > 0)
		{
			takeCount = count;
		}
		if (takeCount >= _takeCount)
		{
			return this;
		}
		return new ShadowRangeWhereIterator(_source, _predicate, takeCount);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal int[] MaterializeWhere()
	{
		var buffer = new int[_takeCount];
		var count = 0;
		for (var index = 0;
			index < _source.Count && count < _takeCount;
			index++)
		{
			var value = _source.GetElement(index);
			if (_predicate(value))
			{
				buffer[count++] = value;
			}
		}
		if (count == buffer.Length)
		{
			return buffer;
		}
		var result = new int[count];
		for (var index = 0; index < count; index++)
		{
			result[index] = buffer[index];
		}
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal bool Any()
	{
		var found = 0;
		for (var index = 0; index < _source.Count && _takeCount != 0; index++)
		{
			if (_predicate(_source.GetElement(index)))
			{
				found = 1;
				break;
			}
		}
		return found != 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal bool Any(Func<int, bool> predicate)
	{
		var found = 0;
		for (var index = 0; index < _source.Count; index++)
		{
			var value = _source.GetElement(index);
			if (_predicate(value))
			{
				if (predicate(value))
				{
					found = 1;
					break;
				}
			}
		}
		return found != 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal bool AnyTaken(Func<int, bool> predicate)
	{
		var found = 0;
		var accepted = 0;
		for (var index = 0;
			index < _source.Count && accepted < _takeCount;
			index++)
		{
			var value = _source.GetElement(index);
			if (_predicate(value))
			{
				accepted++;
				if (predicate(value))
				{
					found = 1;
					break;
				}
			}
		}
		return found != 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal int Sum()
	{
		var sum = 0;
		for (var index = 0; index < _source.Count; index++)
		{
			var value = _source.GetElement(index);
			if (_predicate(value))
			{
				sum = ShadowEnumerable.CheckedAdd(sum, value);
			}
		}
		return sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal int Sum(Func<int, int> selector)
	{
		var sum = 0;
		for (var index = 0; index < _source.Count; index++)
		{
			var value = _source.GetElement(index);
			if (_predicate(value))
			{
				sum = ShadowEnumerable.CheckedAdd(sum, selector(value));
			}
		}
		return sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal int SumTaken()
	{
		var sum = 0;
		var accepted = 0;
		for (var index = 0;
			index < _source.Count && accepted < _takeCount;
			index++)
		{
			var value = _source.GetElement(index);
			if (_predicate(value))
			{
				accepted++;
				sum = ShadowEnumerable.CheckedAdd(sum, value);
			}
		}
		return sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal int SumTaken(Func<int, int> selector)
	{
		var sum = 0;
		var accepted = 0;
		for (var index = 0;
			index < _source.Count && accepted < _takeCount;
			index++)
		{
			var value = _source.GetElement(index);
			if (_predicate(value))
			{
				accepted++;
				sum = ShadowEnumerable.CheckedAdd(sum, selector(value));
			}
		}
		return sum;
	}

	public IEnumerator<int> GetEnumerator()
	{
		var filtered = new ShadowWhereEnumerator<int>(
			_source.GetEnumerator(),
			_predicate);
		return _takeCount < _source.Count
			? new ShadowTakeEnumerator<int>(filtered, _takeCount)
			: filtered;
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Private deferred predicate over an exact Range-to-Select source.
/// </summary>
public sealed class ShadowSelectWhereIterator : IEnumerable<int>
{
	private readonly ShadowInt32SelectIterator _source;
	private readonly Func<int, bool> _predicate;
	private readonly int _takeCount;

	public ShadowSelectWhereIterator(
		ShadowInt32SelectIterator source,
		Func<int, bool> predicate)
	{
		_source = source;
		_predicate = predicate;
		_takeCount = source.Count;
	}

	private ShadowSelectWhereIterator(
		ShadowInt32SelectIterator source,
		Func<int, bool> predicate,
		int takeCount)
	{
		_source = source;
		_predicate = predicate;
		_takeCount = takeCount;
	}

	internal ShadowSelectWhereIterator Take(int count)
	{
		var takeCount = 0;
		if (count > 0)
		{
			takeCount = count;
		}
		if (takeCount >= _takeCount)
		{
			return this;
		}
		return new ShadowSelectWhereIterator(_source, _predicate, takeCount);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal int[] MaterializeWhere()
	{
		var buffer = new int[_takeCount];
		var count = 0;
		for (var index = 0;
			index < _source.Count && count < _takeCount;
			index++)
		{
			var value = _source.GetElement(index);
			if (_predicate(value))
			{
				buffer[count++] = value;
			}
		}
		if (count == buffer.Length)
		{
			return buffer;
		}
		var result = new int[count];
		for (var index = 0; index < count; index++)
		{
			result[index] = buffer[index];
		}
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal bool Any()
	{
		var found = 0;
		for (var index = 0; index < _source.Count && _takeCount != 0; index++)
		{
			if (_predicate(_source.GetElement(index)))
			{
				found = 1;
				break;
			}
		}
		return found != 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal bool Any(Func<int, bool> predicate)
	{
		var found = 0;
		for (var index = 0; index < _source.Count; index++)
		{
			var value = _source.GetElement(index);
			if (_predicate(value))
			{
				if (predicate(value))
				{
					found = 1;
					break;
				}
			}
		}
		return found != 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal bool AnyTaken(Func<int, bool> predicate)
	{
		var found = 0;
		var accepted = 0;
		for (var index = 0;
			index < _source.Count && accepted < _takeCount;
			index++)
		{
			var value = _source.GetElement(index);
			if (_predicate(value))
			{
				accepted++;
				if (predicate(value))
				{
					found = 1;
					break;
				}
			}
		}
		return found != 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal int Sum()
	{
		var sum = 0;
		for (var index = 0; index < _source.Count; index++)
		{
			var value = _source.GetElement(index);
			if (_predicate(value))
			{
				sum = ShadowEnumerable.CheckedAdd(sum, value);
			}
		}
		return sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal int Sum(Func<int, int> selector)
	{
		var sum = 0;
		for (var index = 0; index < _source.Count; index++)
		{
			var value = _source.GetElement(index);
			if (_predicate(value))
			{
				sum = ShadowEnumerable.CheckedAdd(sum, selector(value));
			}
		}
		return sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal int SumTaken()
	{
		var sum = 0;
		var accepted = 0;
		for (var index = 0;
			index < _source.Count && accepted < _takeCount;
			index++)
		{
			var value = _source.GetElement(index);
			if (_predicate(value))
			{
				accepted++;
				sum = ShadowEnumerable.CheckedAdd(sum, value);
			}
		}
		return sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal int SumTaken(Func<int, int> selector)
	{
		var sum = 0;
		var accepted = 0;
		for (var index = 0;
			index < _source.Count && accepted < _takeCount;
			index++)
		{
			var value = _source.GetElement(index);
			if (_predicate(value))
			{
				accepted++;
				sum = ShadowEnumerable.CheckedAdd(sum, selector(value));
			}
		}
		return sum;
	}

	public IEnumerator<int> GetEnumerator()
	{
		var filtered = new ShadowWhereEnumerator<int>(
			_source.GetEnumerator(),
			_predicate);
		return _takeCount < _source.Count
			? new ShadowTakeEnumerator<int>(filtered, _takeCount)
			: filtered;
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Private immutable state for <see cref="Enumerable.Repeat{TResult}(TResult, int)"/>.
/// </summary>
public sealed class ShadowRepeatIterator<T> : ShadowEnumerableIterator<T>
{
	internal readonly T _element;
	internal readonly int _count;

	public ShadowRepeatIterator(T element, int count)
	{
		_element = element;
		_count = count;
	}

	internal override int Count => _count;

	internal override T GetElement(int index) => _element;

	internal ShadowRepeatIterator<T> Take(int count)
	{
		var takeCount = 0;
		if (count > 0)
		{
			takeCount = count;
			if (takeCount > _count)
			{
				takeCount = _count;
			}
		}
		if (takeCount == _count)
		{
			return this;
		}
		return new ShadowRepeatIterator<T>(_element, takeCount);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal T[] MaterializeRepeat()
	{
		var result = new T[_count];
		for (var index = 0; index < result.Length; index++)
		{
			result[index] = _element;
		}
		return result;
	}

}

/// <summary>
/// Host-correct enumerator for private iterator shadows. Selected target LINQ
/// terminals materialize through the private iterator base without interface
/// dispatch.
/// </summary>
public sealed class ShadowEnumerableEnumerator<T> : IEnumerator<T>
{
	private readonly ShadowEnumerableIterator<T> _iterator;
	private int _index;

	public ShadowEnumerableEnumerator(ShadowEnumerableIterator<T> iterator)
	{
		_iterator = iterator;
		_index = -1;
	}

	public bool MoveNext()
	{
		if (_index + 1 < _iterator.Count)
		{
			_index++;
			return true;
		}
		_index = _iterator.Count;
		return false;
	}

	public void Reset() => _index = -1;

	public T Current
	{
		get
		{
			if ((uint)_index >= (uint)_iterator.Count)
			{
				M68kRuntime.ThrowInvalidOperationException();
			}
			return _iterator.GetElement(_index);
		}
	}

	object IEnumerator.Current => Current!;

	public void Dispose()
	{
	}
}

/// <summary>
/// Host-correct enumerator for selected private Where shadows. Exact target
/// materializers do not link this public-interface path.
/// </summary>
public sealed class ShadowWhereEnumerator<T> : IEnumerator<T>
{
	private readonly IEnumerator<T> _source;
	private readonly Func<T, bool> _predicate;
	private T _current = default!;
	private bool _hasCurrent;

	public ShadowWhereEnumerator(IEnumerator<T> source, Func<T, bool> predicate)
	{
		_source = source;
		_predicate = predicate;
	}

	public bool MoveNext()
	{
		while (_source.MoveNext())
		{
			var value = _source.Current;
			if (_predicate(value))
			{
				_current = value;
				_hasCurrent = true;
				return true;
			}
		}
		_current = default!;
		_hasCurrent = false;
		return false;
	}

	public void Reset()
	{
		_source.Reset();
		_current = default!;
		_hasCurrent = false;
	}

	public T Current
	{
		get
		{
			if (!_hasCurrent)
			{
				M68kRuntime.ThrowInvalidOperationException();
			}
			return _current;
		}
	}

	object IEnumerator.Current => Current!;

	public void Dispose() => _source.Dispose();
}

/// <summary>
/// Host-correct limit wrapper used only when enumerating a private filtered
/// shadow through public interfaces. Exact target terminals do not link it.
/// </summary>
public sealed class ShadowTakeEnumerator<T> : IEnumerator<T>
{
	private readonly IEnumerator<T> _source;
	private readonly int _count;
	private int _taken;
	private bool _hasCurrent;

	public ShadowTakeEnumerator(IEnumerator<T> source, int count)
	{
		_source = source;
		_count = count;
	}

	public bool MoveNext()
	{
		if (_taken >= _count || !_source.MoveNext())
		{
			_hasCurrent = false;
			return false;
		}
		_taken++;
		_hasCurrent = true;
		return true;
	}

	public void Reset()
	{
		_source.Reset();
		_taken = 0;
		_hasCurrent = false;
	}

	public T Current
	{
		get
		{
			if (!_hasCurrent)
			{
				M68kRuntime.ThrowInvalidOperationException();
			}
			return _source.Current;
		}
	}

	object IEnumerator.Current => Current!;

	public void Dispose() => _source.Dispose();
}
