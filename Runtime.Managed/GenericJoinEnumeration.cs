/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Collections;
using System.Runtime.CompilerServices;
using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

/// <summary>Exact closed-type enumeration for experimental CoreLib joins.</summary>
public static class ShadowGenericJoinEnumeration
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ShadowGenericJoinEnumerator<T> GetEnumerator<T>(IEnumerable<T> source)
	{
		if (source is T[] array) return new ShadowGenericJoinEnumerator<T>(array);
		// Native list descriptors expose the verified ShadowList field view.
		// A CLR list has a different runtime type and uses its public iterator.
		if (source is List<T> list && list.GetType() == typeof(List<T>) && (object)list is ShadowList<T> shadow)
			return new ShadowGenericJoinEnumerator<T>(shadow);
		var custom = source.GetEnumerator();
		if (custom == null) return null!;
		try { return new ShadowGenericJoinEnumerator<T>(custom); }
		catch { custom.Dispose(); throw; }
	}
}

public sealed class ShadowGenericJoinEnumerator<T> : IEnumerator<T>
{
	private T[]? _array;
	private ShadowList<T>? _list;
	private readonly int _version;
	private int _index;
	private T _current = default!;
	private IEnumerator<T>? _custom;

	public ShadowGenericJoinEnumerator(T[] array) => _array = array;
	public ShadowGenericJoinEnumerator(ShadowList<T> list) { _list = list; _version = list._version; }
	public ShadowGenericJoinEnumerator(IEnumerator<T> custom) => _custom = custom;
	public T Current => _custom != null ? _custom.Current : _current;
	object? IEnumerator.Current => Current;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public bool MoveNext()
	{
		if (_custom != null) return _custom.MoveNext();
		var list = _list;
		if (list != null)
		{
			if (_version != list._version) M68kRuntime.ThrowInvalidOperationException();
			if ((uint)_index < (uint)list._size) { _current = list._items![_index++]; return true; }
		}
		else if (_array is { } array && (uint)_index < (uint)array.Length) { _current = array[_index++]; return true; }
		_current = default!;
		return false;
	}
	public void Reset() => throw new NotSupportedException();
	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Dispose()
	{
		var custom = _custom;
		_custom = null; _array = null; _list = null; _current = default!;
		if (custom != null) custom.Dispose();
	}
}
