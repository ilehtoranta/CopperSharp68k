/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections;

namespace CopperSharp.Compiler.Backend;

/// <summary>
/// Named offsets with dictionary ordering and an independent, lazy offset index.
/// Range edits touch tree paths and interior entries, not every named position.
/// </summary>
internal sealed class M68kOffsetMap : IDictionary<string, int>, IReadOnlyDictionary<string, int>
{
	private sealed class Entry(int offset, ulong identity)
	{
		internal readonly ulong Identity = identity;
		// A deterministic mix of insertion identity avoids a skewed tree when
		// emitted offsets are increasing. Priorities never affect enumeration.
		internal readonly ulong Priority = Mix(identity);
		internal int Offset = offset;
		internal int Lazy;
		internal Entry? Left, Right, Parent;
		internal long CacheVersion = -1;
		internal int InheritedDelta;
		internal long PositionVersion = -1;
		internal int CachedPosition;

		private static ulong Mix(ulong value)
		{
			value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
			value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
			return value ^ (value >> 31);
		}
	}

	private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
	private HashSet<string>? _methodEndLabelNames;
	private List<Entry>? _methodEndOffsets;
	private bool _methodEndOffsetsOrdered;
	private Entry? _root;
	private ulong _identity;
	private long _version;
	private long _positionVersion;
	private readonly ValueCollection _values;

	internal M68kOffsetMap() => _values = new(this);

	public int Count => _entries.Count;
	public bool IsReadOnly => false;
	public ICollection<string> Keys => _entries.Keys;
	public ValueCollection Values => _values;
	ICollection<int> IDictionary<string, int>.Values => Values;
	IEnumerable<string> IReadOnlyDictionary<string, int>.Keys => Keys;
	IEnumerable<int> IReadOnlyDictionary<string, int>.Values => Values;

	// Keep names across position edits, resolving their current offsets only
	// when a round needs them. Most labels are IL positions, not method ends.
	internal IReadOnlyCollection<string> MethodEndLabelNames =>
		_methodEndLabelNames ??= _entries.Keys
			.Where(static name => name.EndsWith(":end", StringComparison.Ordinal))
			.ToHashSet(StringComparer.Ordinal);

	// Index existing entries so ordinary suffix rebasing updates their offsets
	// once, in the main tree. Positive insertions and clamped deletions preserve
	// boundary order; only edits that can reorder method ends invalidate it.
	private List<Entry> MethodEndOffsets()
	{
		if (_methodEndOffsets is null)
		{
			_methodEndOffsets = new(MethodEndLabelNames.Count);
			_methodEndOffsetsOrdered = false;
			foreach (var name in MethodEndLabelNames) _methodEndOffsets.Add(_entries[name]);
		}
		if (!_methodEndOffsetsOrdered)
		{
			_methodEndOffsets.Sort((left, right) => Position(left).CompareTo(Position(right)));
			_methodEndOffsetsOrdered = true;
		}
		return _methodEndOffsets;
	}

	internal int? MethodEndAfter(int offset)
	{
		var entries = MethodEndOffsets();
		var index = MethodEndBound(entries, offset, includeEqual: false);
		return index < entries.Count ? Position(entries[index]) : null;
	}

	internal int? MethodEndAtOrBefore(int offset)
	{
		var entries = MethodEndOffsets();
		var index = MethodEndBound(entries, offset, includeEqual: false) - 1;
		return index >= 0 ? Position(entries[index]) : null;
	}

	private int MethodEndBound(List<Entry> entries, int offset, bool includeEqual)
	{
		var low = 0;
		var high = entries.Count;
		while (low < high)
		{
			var middle = low + (high - low) / 2;
			var position = Position(entries[middle]);
			if (position < offset || !includeEqual && position == offset) low = middle + 1;
			else high = middle;
		}
		return low;
	}

	private void InvalidateMethodEndOrderInRange(int start, int end, bool includeStart, bool includeEnd)
	{
		if (_methodEndOffsets is null || !_methodEndOffsetsOrdered) return;
		var index = MethodEndBound(_methodEndOffsets, start, includeStart);
		if (index == _methodEndOffsets.Count) return;
		var position = Position(_methodEndOffsets[index]);
		if (position < end || includeEnd && position == end) _methodEndOffsetsOrdered = false;
	}

	public int this[string key]
	{
		get => Position(_entries[key]);
		set
		{
			if (!_entries.TryGetValue(key, out var entry)) { Add(key, value); return; }
			var old = Position(entry);
			if (old == value) return;
			if (_methodEndOffsets is not null && key.EndsWith(":end", StringComparison.Ordinal))
				_methodEndOffsetsOrdered = false;
			_version++;
			_root = Remove(_root!, old, entry.Identity);
			entry.Offset = value;
			_root = Insert(_root, entry);
			entry.CachedPosition = value;
			entry.PositionVersion = _positionVersion;
		}
	}

	public void Add(string key, int value)
	{
		var entry = new Entry(value, ++_identity);
		// Check dictionary errors before changing the index. Keep the original
		// dictionary's key slots, including its remove/reinsert ordering.
		_entries.Add(key, entry);
		if (_methodEndOffsets is not null && key.EndsWith(":end", StringComparison.Ordinal))
		{
			_methodEndOffsets.Add(entry);
			_methodEndOffsetsOrdered = false;
		}
		if (_methodEndLabelNames is not null && key.EndsWith(":end", StringComparison.Ordinal))
			_methodEndLabelNames.Add(key);
		_version++;
		_root = Insert(_root, entry);
		entry.CachedPosition = value;
		entry.PositionVersion = _positionVersion;
	}

	public bool TryAdd(string key, int value)
	{
		if (_entries.ContainsKey(key)) return false;
		Add(key, value);
		return true;
	}

	public bool ContainsKey(string key) => _entries.ContainsKey(key);
	public bool TryGetValue(string key, out int value)
	{
		if (_entries.TryGetValue(key, out var entry)) { value = Position(entry); return true; }
		value = default;
		return false;
	}

	public bool Remove(string key)
	{
		if (!_entries.TryGetValue(key, out var entry)) return false;
		var offset = Position(entry);
		if (_methodEndOffsets is not null && key.EndsWith(":end", StringComparison.Ordinal))
			_methodEndOffsets.Remove(entry);
		_version++;
		_root = Remove(_root!, offset, entry.Identity);
		_methodEndLabelNames?.Remove(key);
		return _entries.Remove(key);
	}

	public void Clear()
	{
		_methodEndOffsets?.Clear();
		_methodEndOffsetsOrdered = true;
		_entries.Clear();
		_methodEndLabelNames?.Clear();
		_root = null;
		_version++;
	}

	public Enumerator GetEnumerator() => new(this);
	IEnumerator<KeyValuePair<string, int>> IEnumerable<KeyValuePair<string, int>>.GetEnumerator() => GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	public void Add(KeyValuePair<string, int> item) => Add(item.Key, item.Value);
	public bool Contains(KeyValuePair<string, int> item) =>
		TryGetValue(item.Key, out var offset) && offset == item.Value;
	public bool Remove(KeyValuePair<string, int> item) => Contains(item) && Remove(item.Key);
	public void CopyTo(KeyValuePair<string, int>[] array, int arrayIndex)
	{
		ArgumentNullException.ThrowIfNull(array);
		if (arrayIndex < 0 || arrayIndex > array.Length) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
		if (array.Length - arrayIndex < Count) throw new ArgumentException("Insufficient array space.", nameof(array));
		foreach (var item in this) array[arrayIndex++] = item;
	}

	internal bool HasOffset(int offset)
	{
		var entry = _root;
		while (entry is not null)
		{
			var position = Position(entry);
			if (position == offset) return true;
			entry = offset < position ? entry.Left : entry.Right;
		}
		return false;
	}

	internal void InsertBytes(int offset, int count, bool shiftBoundary)
	{
		if (count == 0 || _root is null) return;
		if (count < 0) _methodEndOffsetsOrdered = false;
		_version++;
		_positionVersion++;
		Split(_root, offset, includeEqual: !shiftBoundary, out var left, out var right);
		Shift(right, count);
		_root = Merge(left, right);
	}

	/// <summary>Retarget the original entries in an offset interval, including aliases.</summary>
	internal void MoveOffsets(int start, int end, int destination,
		bool includeStart = true, bool includeEnd = false)
	{
		if (_root is null || start > end || start == end && !(includeStart && includeEnd)) return;
		InvalidateMethodEndOrderInRange(start, end, includeStart, includeEnd);
		_version++;
		Split(_root, start, includeEqual: !includeStart, out var left, out var tail);
		Split(tail, end, includeEqual: includeEnd, out var middle, out var right);
		_root = Merge(left, right);
		// Detach the whole selection before retargeting: destinations may fall
		// inside it or cross either boundary. Dictionary slots remain untouched.
		ReinsertInterior(middle, destination);
	}

	internal void MoveOffsetsAt(int source, int destination) =>
		MoveOffsets(source, source, destination, includeEnd: true);

	internal void RemoveBytes(int offset, int count, bool clampInterior)
	{
		if (count == 0 || _root is null) return;
		if (!clampInterior)
			InvalidateMethodEndOrderInRange(offset, checked(offset + count), includeStart: true, includeEnd: false);
		_version++;
		_positionVersion++;
		Split(_root, offset, includeEqual: false, out var left, out var tail);
		Split(tail, checked(offset + count), includeEqual: false, out var middle, out var right);
		Shift(right, -count);
		_root = Merge(left, right);
		// Kept labels inside a deletion can overlap the shifted suffix. Anchors
		// collapse onto the cut boundary. Reinsert only these interior entries;
		// merging the original middle tree would violate the offset ordering.
		ReinsertInterior(middle, clampInterior ? offset : null);
	}

	private void ReinsertInterior(Entry? entry, int? clampedOffset)
	{
		if (entry is null) return;
		Push(entry);
		var left = entry.Left;
		var right = entry.Right;
		entry.Left = entry.Right = entry.Parent = null;
		if (clampedOffset.HasValue)
		{
			entry.Offset = clampedOffset.Value;
			entry.CachedPosition = clampedOffset.Value;
			entry.PositionVersion = _positionVersion;
		}
		_root = Insert(_root, entry);
		ReinsertInterior(left, clampedOffset);
		ReinsertInterior(right, clampedOffset);
	}

	private int Position(Entry entry)
	{
		if (entry.PositionVersion == _positionVersion) return entry.CachedPosition;
		entry.CachedPosition = unchecked(entry.Offset + InheritedDelta(entry));
		entry.PositionVersion = _positionVersion;
		return entry.CachedPosition;
	}

	private int InheritedDelta(Entry entry)
	{
		if (entry.CacheVersion == _version) return entry.InheritedDelta;
		entry.InheritedDelta = entry.Parent is { } parent
			? unchecked(InheritedDelta(parent) + parent.Lazy) : 0;
		entry.CacheVersion = _version;
		return entry.InheritedDelta;
	}

	private static void Shift(Entry? entry, int delta)
	{
		if (entry is null) return;
		entry.Offset = unchecked(entry.Offset + delta);
		entry.Lazy = unchecked(entry.Lazy + delta);
	}

	private static void Push(Entry entry)
	{
		if (entry.Lazy == 0) return;
		Shift(entry.Left, entry.Lazy);
		Shift(entry.Right, entry.Lazy);
		entry.Lazy = 0;
	}

	private static void Left(Entry entry, Entry? child)
	{
		entry.Left = child;
		if (child is not null) child.Parent = entry;
	}
	private static void Right(Entry entry, Entry? child)
	{
		entry.Right = child;
		if (child is not null) child.Parent = entry;
	}

	private static void Split(Entry? entry, int offset, bool includeEqual,
		out Entry? left, out Entry? right)
	{
		if (entry is null) { left = right = null; return; }
		Push(entry);
		if (entry.Offset < offset || includeEqual && entry.Offset == offset)
		{
			Split(entry.Right, offset, includeEqual, out var child, out right);
			Right(entry, child);
			left = entry;
		}
		else
		{
			Split(entry.Left, offset, includeEqual, out left, out var child);
			Left(entry, child);
			right = entry;
		}
		if (left is not null) left.Parent = null;
		if (right is not null) right.Parent = null;
	}

	private static Entry? Merge(Entry? left, Entry? right)
	{
		if (left is null) { if (right is not null) right.Parent = null; return right; }
		if (right is null) { left.Parent = null; return left; }
		Push(left);
		Push(right);
		if (left.Priority > right.Priority)
		{
			Right(left, Merge(left.Right, right));
			left.Parent = null;
			return left;
		}
		Left(right, Merge(left, right.Left));
		right.Parent = null;
		return right;
	}

	private static Entry Insert(Entry? root, Entry entry)
	{
		if (root is null) return entry;
		Push(root);
		if (entry.Offset < root.Offset || entry.Offset == root.Offset && entry.Identity < root.Identity)
		{
			Left(root, Insert(root.Left, entry));
			if (root.Left!.Priority > root.Priority)
			{
				var pivot = root.Left;
				Push(pivot);
				Left(root, pivot.Right);
				Right(pivot, root);
				root = pivot;
			}
		}
		else
		{
			Right(root, Insert(root.Right, entry));
			if (root.Right!.Priority > root.Priority)
			{
				var pivot = root.Right;
				Push(pivot);
				Right(root, pivot.Left);
				Left(pivot, root);
				root = pivot;
			}
		}
		root.Parent = null;
		return root;
	}

	private static Entry? Remove(Entry root, int offset, ulong identity)
	{
		Push(root);
		if (offset == root.Offset && identity == root.Identity)
		{
			var merged = Merge(root.Left, root.Right);
			root.Left = root.Right = root.Parent = null;
			return merged;
		}
		if (offset < root.Offset || offset == root.Offset && identity < root.Identity)
			Left(root, Remove(root.Left!, offset, identity));
		else Right(root, Remove(root.Right!, offset, identity));
		root.Parent = null;
		return root;
	}

	public struct Enumerator : IEnumerator<KeyValuePair<string, int>>
	{
		private readonly M68kOffsetMap _owner;
		private Dictionary<string, Entry>.Enumerator _inner;
		private KeyValuePair<string, int> _current;
		internal Enumerator(M68kOffsetMap owner)
		{
			_owner = owner;
			_inner = owner._entries.GetEnumerator();
		}
		public KeyValuePair<string, int> Current => _current;
		object IEnumerator.Current => Current;
		public bool MoveNext()
		{
			if (!_inner.MoveNext()) { _current = default; return false; }
			var item = _inner.Current;
			_current = new(item.Key, _owner.Position(item.Value));
			return true;
		}
		public void Dispose() => _inner.Dispose();
		void IEnumerator.Reset() => throw new NotSupportedException();
	}

	public struct ValueEnumerator : IEnumerator<int>
	{
		private readonly M68kOffsetMap _owner;
		private Dictionary<string, Entry>.ValueCollection.Enumerator _inner;
		private int _current;
		internal ValueEnumerator(M68kOffsetMap owner)
		{
			_owner = owner;
			_inner = owner._entries.Values.GetEnumerator();
		}
		public int Current => _current;
		object IEnumerator.Current => Current;
		public bool MoveNext()
		{
			if (!_inner.MoveNext()) { _current = default; return false; }
			_current = _owner.Position(_inner.Current);
			return true;
		}
		public void Dispose() => _inner.Dispose();
		void IEnumerator.Reset() => throw new NotSupportedException();
	}

	public sealed class ValueCollection(M68kOffsetMap owner) : ICollection<int>
	{
		public int Count => owner.Count;
		public bool IsReadOnly => true;
		public bool Contains(int item) => owner.HasOffset(item);
		public ValueEnumerator GetEnumerator() => new(owner);
		IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
		public void CopyTo(int[] array, int arrayIndex)
		{
			ArgumentNullException.ThrowIfNull(array);
			if (arrayIndex < 0 || arrayIndex > array.Length) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
			if (array.Length - arrayIndex < Count) throw new ArgumentException("Insufficient array space.", nameof(array));
			foreach (var value in this) array[arrayIndex++] = value;
		}
		public void Add(int item) => throw new NotSupportedException();
		public void Clear() => throw new NotSupportedException();
		public bool Remove(int item) => throw new NotSupportedException();
	}
}
