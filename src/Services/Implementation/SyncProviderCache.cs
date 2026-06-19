using System.Collections.Concurrent;
using CoreSync;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace CoreSyncServer.Services.Implementation;

/// <summary>
/// Backs <see cref="ISyncProviderCache"/> with the shared <see cref="IMemoryCache"/>. Because one
/// endpoint can have several entries (one per agent assignment) and <see cref="IMemoryCache"/> can
/// neither enumerate nor remove-by-prefix, every entry for an endpoint is linked to a shared
/// <see cref="CancellationTokenSource"/>; cancelling it evicts them all at once.
/// </summary>
public class SyncProviderCache(IMemoryCache cache) : ISyncProviderCache
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _evictionTokens = new();

    private static string CacheKey(Guid endpointId, string agentKey) => $"SyncProvider:{endpointId}:Agent={agentKey}";

    public bool TryGet(Guid endpointId, string agentKey, out ISyncProvider provider)
    {
        if (cache.TryGetValue(CacheKey(endpointId, agentKey), out var cached) && cached is ISyncProvider cachedProvider)
        {
            provider = cachedProvider;
            return true;
        }

        provider = null!;
        return false;
    }

    public void Set(Guid endpointId, string agentKey, ISyncProvider provider)
    {
        var cts = _evictionTokens.GetOrAdd(endpointId, _ => new CancellationTokenSource());

        var options = new MemoryCacheEntryOptions()
            .AddExpirationToken(new CancellationChangeToken(cts.Token));

        cache.Set(CacheKey(endpointId, agentKey), provider, options);
    }

    public void Invalidate(Guid endpointId)
    {
        if (_evictionTokens.TryRemove(endpointId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }
}
