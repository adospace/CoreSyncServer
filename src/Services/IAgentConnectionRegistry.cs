namespace CoreSyncServer.Services;

/// <summary>
/// Tracks the active SignalR connection id for each agent-owned DataStore so the
/// <c>AgentProxySyncProvider</c> can route RPCs to the correct single client.
/// </summary>
public interface IAgentConnectionRegistry
{
    void Register(int dataStoreId, string connectionId);

    void Unregister(string connectionId);

    bool TryGetConnectionId(int dataStoreId, out string connectionId);
}
