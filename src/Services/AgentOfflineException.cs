namespace CoreSyncServer.Services;

/// <summary>
/// Thrown by <c>AgentProxySyncProvider</c> when no agent connection is registered for the target DataStore,
/// or the RPC call to the connected agent fails. <c>SyncController</c> maps this to HTTP 503.
/// </summary>
public class AgentOfflineException : Exception
{
    public int DataStoreId { get; }

    public AgentOfflineException(int dataStoreId, string message) : base(message)
    {
        DataStoreId = dataStoreId;
    }

    public AgentOfflineException(int dataStoreId, string message, Exception inner) : base(message, inner)
    {
        DataStoreId = dataStoreId;
    }
}
