using CoreSync;

namespace CoreSyncServer.Services;

/// <summary>
/// Caches the (expensive to build, internally stateful) <see cref="ISyncProvider"/> instances keyed
/// by endpoint and agent assignment. Entries never expire on their own, so callers that change an
/// endpoint's configuration must invalidate it explicitly — see <see cref="Invalidate"/>.
/// </summary>
public interface ISyncProviderCache
{
    bool TryGet(Guid endpointId, string agentKey, out ISyncProvider provider);

    void Set(Guid endpointId, string agentKey, ISyncProvider provider);

    /// <summary>
    /// Evicts every cached provider for the endpoint, regardless of agent assignment.
    /// </summary>
    void Invalidate(Guid endpointId);
}
