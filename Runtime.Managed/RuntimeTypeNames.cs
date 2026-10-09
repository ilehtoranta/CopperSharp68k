/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.CompilerServices;

namespace CopperSharp.Runtime;

/// <summary>Field view of compiler-emitted, immutable target type objects.</summary>
public sealed class ShadowRuntimeType
{
#pragma warning disable CS0649, CS0169
	private readonly int _classification;
	private readonly string _name = null!;
	private readonly int _defaultObjectString;
#pragma warning restore CS0649, CS0169
	[MethodImpl(MethodImplOptions.NoInlining)]
	public override string ToString() => _name;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public string GetObjectFallbackName()
	{
		if (_defaultObjectString == 0) throw new NotSupportedException();
		return _name;
	}
}
