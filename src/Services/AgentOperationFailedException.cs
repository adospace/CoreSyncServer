namespace CoreSyncServer.Services;

/// <summary>
/// Thrown by <c>AgentProxySyncProvider</c> when the agent was reached, executed the requested
/// operation and reported that it failed - typically its sync provider rejecting the changes against
/// the on-premise database.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="AgentOfflineException"/> on purpose: that one means "nobody answered",
/// which is transient and answered with 503, while this one is a failure of the data store behind the
/// agent and is treated like any other provider failure - it propagates (500, reported to the host's
/// error reporting) and reaches the client tagged with an error code instead of as "agent offline".
/// </remarks>
public class AgentOperationFailedException : Exception
{
    public int DataStoreId { get; }

    public string Operation { get; }

    public AgentOperationFailedException(int dataStoreId, string operation, Exception inner)
        : base($"Agent for DataStore {dataStoreId} reported a failure executing '{operation}': {inner.Message}", inner)
    {
        DataStoreId = dataStoreId;
        Operation = operation;
    }
}
