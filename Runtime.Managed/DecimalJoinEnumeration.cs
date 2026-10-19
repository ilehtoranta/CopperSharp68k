/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/
using System.Collections;
using System.Runtime.CompilerServices;
using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

/// <summary>Enumeration adapters for the verified CoreLib Decimal join loop.</summary>
public static class ShadowDecimalJoinEnumeration
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ShadowDecimalJoinEnumerator GetEnumerator(IEnumerable<decimal> source)
	{
		if (source is decimal[] array) return new ShadowDecimalJoinEnumerator(array);
		// The compiler verifies the pinned CoreLib layout and descriptor for
		// this field view before admitting these two list type checks.
		if (source is List<decimal> list && list.GetType() == typeof(List<decimal>) && (object)list is ShadowList<decimal> shadow)
			return new ShadowDecimalJoinEnumerator(shadow);
		var custom = source.GetEnumerator();
		if (custom == null) return null!;
		try { return new ShadowDecimalJoinEnumerator(custom); }
		catch { custom.Dispose(); throw; }
	}
}

public sealed class ShadowDecimalJoinEnumerator : IEnumerator<decimal>
{
	private decimal[]? _array;
	private ShadowList<decimal>? _list;
	private readonly int _version;
	private int _index;
	private decimal _current;
	private IEnumerator<decimal>? _custom;

	public ShadowDecimalJoinEnumerator(IEnumerator<decimal> custom) => _custom = custom;
	public ShadowDecimalJoinEnumerator(decimal[] array) => _array = array;
	public ShadowDecimalJoinEnumerator(ShadowList<decimal> list)
	{
		_list = list;
		_version = list._version;
	}

	public decimal Current => _custom != null ? _custom.Current : _current;
	object IEnumerator.Current => Current;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public bool MoveNext()
	{
		if (_custom != null) return _custom.MoveNext();
		var list = _list;
		if (list != null)
		{
			if (_version != list._version) M68kRuntime.ThrowInvalidOperationException();
			if ((uint)_index < (uint)list._size)
			{
				_current = list._items![_index++];
				return true;
			}
		}
		else if (_array is { } array && (uint)_index < (uint)array.Length)
		{
			_current = array[_index++];
			return true;
		}
		_current = default;
		return false;
	}

	public void Reset() => throw new NotSupportedException();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Dispose()
	{
		var custom = _custom;
		_custom = null;
		_array = null;
		_list = null;
		_current = default;
		if (custom != null) custom.Dispose();
	}
}
