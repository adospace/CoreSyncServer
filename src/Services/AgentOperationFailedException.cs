namespace CoreSyncServer.Services;

/// <summary>
/// Thrown by <c>AgentProxySyncProvider</c> when the agent was reached, executed the requested
/// operation and reported that it failed - typically its sync provider rejecting the changes against
/// the on-premise database.
/// </summary>
/// <remarks>
/// Derives from <see cref="AgentOfflineException"/> and carries the very message that type carried
/// for this case, so <c>SyncController</c> keeps answering HTTP 503 with the same body and logging
/// it the same way: a server deployed ahead of its clients must look identical to them. What it adds
/// is the distinction itself, which the controller turns into the <c>agent-error</c> code header so
/// that a current client no longer reads an agent-side failure as "agent offline".
/// </remarks>
public class AgentOperationFailedException : AgentOfflineException
{
    public string Operation { get; }

    public AgentOperationFailedException(int dataStoreId, string operation, Exception inner)
        : base(dataStoreId, $"Agent RPC '{operation}' failed for DataStore {dataStoreId}.", inner)
    {
        Operation = operation;
    }
}
