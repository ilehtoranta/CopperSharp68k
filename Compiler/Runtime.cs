/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler;

/// <summary>Low-level hooks implemented by the target runtime.</summary>
public static class M68kRuntime
{
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
