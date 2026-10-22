/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.CompilerServices;

namespace CopperSharp.Runtime;

/// <summary>Temporary storage with the verified CoreLib handler field order.</summary>
public ref struct ShadowInterpolatedStringHandlerBuffer
{
#pragma warning disable CS0649, CS0414, CS0169 // Receiver fields belong to the official handler.
	private readonly IFormatProvider? _provider;
	private char[]? _arrayToReturnToPool;
	private Span<char> _chars;
	private int _pos;
	private readonly bool _hasCustomFormatter;
#pragma warning restore CS0649, CS0414, CS0169

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void GrowCore(uint requiredMinCapacity)
	{
		var chars = _chars;
		var capacity = Math.Max(requiredMinCapacity, Math.Min((uint)chars.Length * 2u, 0x3FFFFFDFu));
		var length = (int)Math.Clamp(capacity, 256u, (uint)int.MaxValue);
		// Match pool bucket sizes exposed to TryFormat callbacks, while keeping
		// the array under target GC ownership rather than host pool ownership.
		if (length <= 0x40000000)
		{
			var bucket = 256;
			while (bucket < length) bucket *= 2;
			length = bucket;
		}
		var array = new char[length];
		chars.Slice(0, _pos).CopyTo(array);
		_arrayToReturnToPool = array;
		_chars = array;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Clear()
	{
		_arrayToReturnToPool = null;
		_chars = default;
		_pos = 0;
	}
}
