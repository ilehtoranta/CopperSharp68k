/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler;

/// <summary>Low-level hooks implemented by the target runtime.</summary>
public static class M68kRuntime
{
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
}
