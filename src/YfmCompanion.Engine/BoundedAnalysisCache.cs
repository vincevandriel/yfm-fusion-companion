namespace YfmCompanion.Engine;

public sealed record AnalysisCacheDiagnostics(long AccountedBytes, long LimitBytes, long Hits, long Misses, int Entries);
internal readonly record struct AnalysisCacheKey(ulong Hand, string? Deck = null);

// Retained-object budget, not a process working-set limit. Access under the analyzer lock.
internal sealed class BoundedAnalysisCache(long limitBytes)
{
    private readonly Dictionary<AnalysisCacheKey, LinkedListNode<Entry>> _index = [];
    private readonly LinkedList<Entry> _lru = new();
    private long _bytes, _hits, _misses;
    public AnalysisCacheDiagnostics Diagnostics => new(_bytes, limitBytes, _hits, _misses, _index.Count);

    public void Clear()
    {
        _index.Clear();
        _lru.Clear();
        _bytes = 0;
    }

    public bool TryGet<T>(AnalysisCacheKey key, out T? value) where T : class
    {
        if (_index.TryGetValue(key, out var node))
        {
            _hits++;
            _lru.Remove(node);
            _lru.AddLast(node);
            value = (T)node.Value.Value;
            return true;
        }
        _misses++;
        value = null;
        return false;
    }

    public void Add(AnalysisCacheKey key, object value, long payloadBytes)
    {
        var size = checked(payloadBytes + (key.Deck?.Length ?? 0) * 2L + 256);
        if (size > limitBytes || _index.ContainsKey(key)) return;
        while (_bytes + size > limitBytes && _lru.First is { } oldest)
        {
            _index.Remove(oldest.Value.Key);
            _bytes -= oldest.Value.Size;
            _lru.RemoveFirst();
        }
        _index.Add(key, _lru.AddLast(new Entry(key, value, size)));
        _bytes += size;
    }
    private sealed record Entry(AnalysisCacheKey Key, object Value, long Size);
}
