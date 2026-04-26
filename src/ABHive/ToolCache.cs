using System.Collections.Concurrent;

namespace ABHive;

public interface IToolCache
{
    CachedToolResult? Get(string key);
    void Set(string key, ToolResult result);
    int Count { get; }
}

public class ToolCache : IToolCache
{
    private readonly ConcurrentDictionary<string, CachedToolResult> _cache;
    private const int MaxSize = 100;

    public ToolCache()
    {
        _cache = new ConcurrentDictionary<string, CachedToolResult>();
    }

    public CachedToolResult? Get(string key)
    {
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        return null;
    }

    public void Set(string key, ToolResult result)
    {
        if (_cache.Count >= MaxSize && !_cache.ContainsKey(key))
        {
            RemoveOldest();
        }
        
        _cache[key] = new CachedToolResult
        {
            CacheKey = key,
            Result = result,
            CreatedAt = DateTime.UtcNow
        };
    }

    private void RemoveOldest()
    {
        var oldestKey = _cache.MinBy(kvp => kvp.Value.CreatedAt).Key;
        if (oldestKey != null)
        {
            _cache.TryRemove(oldestKey, out _);
        }
    }

    public int Count => _cache.Count;
}
