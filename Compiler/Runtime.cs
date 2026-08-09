/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler;

/// <summary>Low-level hooks implemented by the target runtime.</summary>
public static class M68kRuntime
{
	/// <summary>
	/// Implements the default equality kernel used by private shadow collections.
	/// The host body preserves the official comparer contract for unit tests;
	/// CopperSharp lowers each admitted closed-world instantiation to its exact
	/// allocation-free target comparison.
	/// </summary>
	public static bool DefaultEquals<T>(T left, T right) =>
		EqualityComparer<T>.Default.Equals(left, right);

	/// <summary>
	/// Implements the default hash kernel used by public shadow comparers.
	/// The host body preserves the official comparer contract; admitted target
	/// instantiations lower to representation-specific integer operations.
	/// </summary>
	public static int DefaultHashCode<T>(T value) =>
		EqualityComparer<T>.Default.GetHashCode(value!);

	/// <summary>
	/// Tests the null-key rule for a closed dictionary key representation. The
	/// host body preserves the public contract; admitted target constructions
	/// lower to either a reference null test or the constant false.
	/// </summary>
	public static bool DictionaryKeyIsNull<T>(T value) => value is null;

	/// <summary>
	/// Reinterprets a compiler-proven reference-type generic value as object.
	/// The host body is an ordinary upcast; CopperSharp lowers it to an identity
	/// move so generic shadow code does not retain CIL's redundant box opcode.
	/// </summary>
	public static object? ReferenceAsObject<T>(T? value)
		where T : class => value;

	/// <summary>
	/// Reinterprets a compiler-proven exact equatable reference as its typed
	/// interface. CopperSharp lowers this upcast to an identity move.
	/// </summary>
	public static IEquatable<T> ReferenceAsEquatable<T>(T value)
		where T : class, IEquatable<T> => value;

	/// <summary>
	/// Returns the low 32-bit lane of a signed 64-bit target scalar and writes its
	/// high lane. CopperSharp lowers this to a register-pair split.
	/// </summary>
	public static uint SplitInt64(long value, out uint high)
	{
		high = unchecked((uint)((ulong)value >> 32));
		return unchecked((uint)value);
	}

	/// <summary>
	/// Returns the low 32-bit lane of an unsigned 64-bit target scalar and writes
	/// its high lane. CopperSharp lowers this to a register-pair split.
	/// </summary>
	public static uint SplitUInt64(ulong value, out uint high)
	{
		high = unchecked((uint)(value >> 32));
		return unchecked((uint)value);
	}

	/// <summary>
	/// Combines target high and low 32-bit lanes into a signed 64-bit scalar.
	/// CopperSharp lowers this to two register moves without 64-bit arithmetic.
	/// </summary>
	public static long CombineInt64(uint high, uint low) =>
		unchecked((long)(((ulong)high << 32) | low));

	/// <summary>
	/// Raises the target runtime's canonical overflow exception. On the host it
	/// throws <see cref="OverflowException"/> directly so shadow methods remain
	/// unit-testable.
	/// </summary>
	[System.Diagnostics.CodeAnalysis.DoesNotReturn]
	public static void ThrowOverflowException() => throw new OverflowException();

	/// <summary>Raises the target runtime's canonical invalid-format exception.</summary>
	[System.Diagnostics.CodeAnalysis.DoesNotReturn]
	public static void ThrowFormatException() => throw new FormatException();

	/// <summary>Raises the target runtime's canonical invalid-argument exception.</summary>
	[System.Diagnostics.CodeAnalysis.DoesNotReturn]
	public static void ThrowArgumentException() => throw new ArgumentException();

	/// <summary>Raises the target runtime's canonical null-argument exception.</summary>
	[System.Diagnostics.CodeAnalysis.DoesNotReturn]
	public static void ThrowArgumentNullException() => throw new ArgumentNullException();

	/// <summary>Raises the target runtime's canonical argument-range exception.</summary>
	[System.Diagnostics.CodeAnalysis.DoesNotReturn]
	public static void ThrowArgumentOutOfRangeException() =>
		throw new ArgumentOutOfRangeException();

	/// <summary>Raises the target runtime's canonical invalid-operation exception.</summary>
	[System.Diagnostics.CodeAnalysis.DoesNotReturn]
	public static void ThrowInvalidOperationException() =>
		throw new InvalidOperationException();

	/// <summary>Raises the target runtime's canonical I/O exception.</summary>
	[System.Diagnostics.CodeAnalysis.DoesNotReturn]
	public static void ThrowIOException() => throw new IOException();

	/// <summary>Raises the target runtime's canonical missing-directory exception.</summary>
	[System.Diagnostics.CodeAnalysis.DoesNotReturn]
	public static void ThrowDirectoryNotFoundException() =>
		throw new DirectoryNotFoundException();

	/// <summary>Raises the target runtime's canonical missing-file exception.</summary>
	[System.Diagnostics.CodeAnalysis.DoesNotReturn]
	public static void ThrowFileNotFoundException() =>
		throw new FileNotFoundException();

	/// <summary>Raises the target runtime's canonical access-denied exception.</summary>
	[System.Diagnostics.CodeAnalysis.DoesNotReturn]
	public static void ThrowUnauthorizedAccessException() =>
		throw new UnauthorizedAccessException();

	/// <summary>Raises the target runtime's canonical missing-key exception.</summary>
	[System.Diagnostics.CodeAnalysis.DoesNotReturn]
	public static void ThrowKeyNotFoundException() =>
		throw new KeyNotFoundException();

	/// <summary>Raises the target runtime's canonical out-of-memory exception.</summary>
	[System.Diagnostics.CodeAnalysis.DoesNotReturn]
	public static void ThrowOutOfMemoryException() => throw new OutOfMemoryException();

	/// <summary>
	/// Allocates a target-runtime string with the requested UTF-16 code-unit
	/// length. This primitive is meaningful only after compiler lowering.
	/// </summary>
	public static string AllocateString(int length) =>
		throw new PlatformNotSupportedException(
			"M68kRuntime.AllocateString is a CopperSharp compiler primitive.");

	/// <summary>
	/// Writes one UTF-16 code unit into a string under construction. This is a
	/// trusted private-runtime primitive; public managed strings remain immutable.
	/// </summary>
	public static void SetStringChar(string value, int index, char character) =>
		throw new PlatformNotSupportedException(
			"M68kRuntime.SetStringChar is a CopperSharp compiler primitive.");

	/// <summary>
	/// Releases the managed allocation stored in a four-byte reference slot and
	/// clears that slot. The v1 compiler does not call this automatically.
	/// </summary>
	[M68kImport(M68kRuntimeImports.Dispose)]
	public static extern void DisposeObject(ref object? value);

	/// <summary>
	/// Releases the managed int array stored in a four-byte reference slot and
	/// clears that slot. The v1 compiler does not call this automatically.
	/// </summary>
	[M68kImport(M68kRuntimeImports.Dispose)]
	public static extern void DisposeInt32Array(ref int[]? value);

	/// <summary>
	/// Releases the managed uint array stored in a four-byte reference slot and
	/// clears that slot. The v1 compiler does not call this automatically.
	/// </summary>
	[M68kImport(M68kRuntimeImports.Dispose)]
	public static extern void DisposeUInt32Array(ref uint[]? value);

	/// <summary>Runs an explicit collection cycle when a GC runtime is linked.</summary>
	[M68kImport(M68kRuntimeImports.GcCollect)]
	public static extern void Collect();

	/// <summary>Returns the runtime's approximate stale-pressure byte count.</summary>
	[M68kImport(M68kRuntimeImports.GcGetStaleBytes)]
	public static extern uint GetGcStaleBytes();

	/// <summary>Returns the runtime's approximate stale-pressure block count.</summary>
	[M68kImport(M68kRuntimeImports.GcGetStaleBlocks)]
	public static extern uint GetGcStaleBlocks();
}
