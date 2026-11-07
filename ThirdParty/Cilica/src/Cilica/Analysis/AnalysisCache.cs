namespace Cilica.Analysis;

/// <summary> Caches analyses and tracks dependencies established by nested queries. </summary>
internal sealed class AnalysisCache<T> where T : class
{
    readonly Dictionary<Type, T> _entries = new();
    readonly Dictionary<Type, HashSet<Type>> _dependencies = new();
    readonly Stack<Type> _constructing = new();

    public long Hits { get; private set; }

    public long Builds { get; private set; }

    public IEnumerable<T> Values => _entries.Values;

    public T Get(Type key, Func<T> create)
    {
        if (_constructing.TryPeek(out var parent)) {
            (_dependencies.GetOrAddRef(parent) ??= new()).Add(key);
        }

        if (_entries.TryGetValue(key, out var value)) {
            Hits++;

            return value;
        }

        if (_constructing.Contains(key)) {
            throw new InvalidOperationException($"Cyclic analysis dependency: {string.Join(" -> ", _constructing.Reverse().Append(key).Select(t => t.Name))}");
        }

        _constructing.Push(key);

        try {
            value = create();
            _entries.Add(key, value);
            Builds++;

            return value;
        } catch {
            _dependencies.Remove(key);
            throw;
        } finally {
            _constructing.Pop();
        }
    }

    public void Invalidate(Type key) => Invalidate((type, _) => type == key);

    public void Invalidate(Func<Type, T, bool> affected)
    {
        var invalid = _entries.Where(e => affected(e.Key, e.Value)).Select(e => e.Key).ToHashSet();
        bool changed;

        do {
            changed = false;

            foreach (var (key, dependencies) in _dependencies) {
                if (dependencies.Overlaps(invalid)) {
                    changed |= invalid.Add(key);
                }
            }
        } while (changed);

        foreach (var key in invalid) {
            _entries.Remove(key);
            _dependencies.Remove(key);
        }
    }

    public void Clear()
    {
        Ensure.That(_constructing.Count == 0, "Cannot clear analyses during construction");

        _entries.Clear();
        _dependencies.Clear();
    }
}
