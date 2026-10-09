/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/
using System.Collections;
using System.Runtime.CompilerServices;
using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

/// <summary>Enumeration adapters for the experimental CoreLib Single join loop.</summary>
public static class ShadowSingleJoinEnumeration
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ShadowSingleJoinEnumerator GetEnumerator(IEnumerable<float> source)
	{
		if (source is float[] array) return new ShadowSingleJoinEnumerator(array);
		// The compiler verifies the pinned CoreLib layout and descriptor for
		// this field view before admitting these two list type checks.
		if (source is List<float> list && list.GetType() == typeof(List<float>) && (object)list is ShadowList<float> shadow)
			return new ShadowSingleJoinEnumerator(shadow);
		var custom = source.GetEnumerator();
		// Preserve a null producer result for CoreLib's normal receiver check.
		if (custom == null) return null!;
		try { return new ShadowSingleJoinEnumerator(custom); }
		catch { custom.Dispose(); throw; }
	}
}

public sealed class ShadowSingleJoinEnumerator : IEnumerator<float>
{
	private float[]? _array;
	private ShadowList<float>? _list;
	private readonly int _version;
	private int _index;
	private float _current;
	private IEnumerator<float>? _custom;

	public ShadowSingleJoinEnumerator(float[] array) => _array = array;
	public ShadowSingleJoinEnumerator(IEnumerator<float> custom) => _custom = custom;
	public ShadowSingleJoinEnumerator(ShadowList<float> list)
	{
		_list = list;
		_version = list._version;
	}

	public float Current => _custom != null ? _custom.Current : _current;
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


/// <summary>Enumeration adapters for the experimental CoreLib Double join loop.</summary>
public static class ShadowDoubleJoinEnumeration
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ShadowDoubleJoinEnumerator GetEnumerator(IEnumerable<double> source)
	{
		if (source is double[] array) return new ShadowDoubleJoinEnumerator(array);
		// The compiler verifies the pinned CoreLib layout and descriptor for
		// this field view before admitting these two list type checks.
		if (source is List<double> list && list.GetType() == typeof(List<double>) && (object)list is ShadowList<double> shadow)
			return new ShadowDoubleJoinEnumerator(shadow);
		var custom = source.GetEnumerator();
		if (custom == null) return null!;
		try { return new ShadowDoubleJoinEnumerator(custom); }
		catch { custom.Dispose(); throw; }
	}
}

public sealed class ShadowDoubleJoinEnumerator : IEnumerator<double>
{
	private double[]? _array;
	private ShadowList<double>? _list;
	private readonly int _version;
	private int _index;
	private double _current;
	private IEnumerator<double>? _custom;

	public ShadowDoubleJoinEnumerator(double[] array) => _array = array;
	public ShadowDoubleJoinEnumerator(IEnumerator<double> custom) => _custom = custom;
	public ShadowDoubleJoinEnumerator(ShadowList<double> list)
	{
		_list = list;
		_version = list._version;
	}

	public double Current => _custom != null ? _custom.Current : _current;
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
