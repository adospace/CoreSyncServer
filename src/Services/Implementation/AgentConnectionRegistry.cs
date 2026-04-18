using System.Collections.Concurrent;

namespace CoreSyncServer.Services.Implementation;

public class AgentConnectionRegistry : IAgentConnectionRegistry
{
    private readonly ConcurrentDictionary<int, string> _byDataStore = new();
    private readonly ConcurrentDictionary<string, int> _byConnection = new();

    public void Register(int dataStoreId, string connectionId)
    {
        // Latest connection wins. If a previous connection existed for the same DataStore,
        // replace its mapping so the proxy routes to the freshly-acknowledged one.
        _byDataStore[dataStoreId] = connectionId;
        _byConnection[connectionId] = dataStoreId;
    }

    public void Unregister(string connectionId)
    {
        if (!_byConnection.TryRemove(connectionId, out var dataStoreId)) return;

        // Only clear the DataStore mapping if it still points to this connection;
        // a newer connection may have already replaced it.
        _byDataStore.TryGetValue(dataStoreId, out var current);
        if (current == connectionId)
        {
            _byDataStore.TryRemove(dataStoreId, out _);
        }
    }

    public bool TryGetConnectionId(int dataStoreId, out string connectionId)
    {
        if (_byDataStore.TryGetValue(dataStoreId, out var id))
        {
            connectionId = id;
            return true;
        }
        connectionId = string.Empty;
        return false;
    }
}
