namespace CoreSyncServer.Services;

/// <summary>
/// Thrown by <c>AgentProxySyncProvider</c> when no agent connection is registered for the target DataStore,
/// or the RPC call to the connected agent does not complete (timeout, dropped connection).
/// <c>SyncController</c> maps this to HTTP 503 with the <c>agent-unavailable</c> code.
/// An agent that answers with an error raises <see cref="AgentOperationFailedException"/> instead.
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
