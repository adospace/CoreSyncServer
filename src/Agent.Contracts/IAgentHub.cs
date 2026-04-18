using CoreSync;

namespace CoreSyncServer.Agent.Contracts;

/// <summary>
/// Methods invokable by the Agent on the server-side hub.
/// </summary>
public interface IAgentHub
{
    Task<bool> AcknowledgeConnected(Guid connectionTicket);

    Task Heartbeat();

    Task ReportSyncProgress(SyncProgressMessage message);
}

/// <summary>
/// Methods invokable by the server on the Agent-side hub client. The <c>Sync*</c> methods form the
/// server-to-agent half of the "dual" provider pair: the server-side <see cref="ISyncProvider"/>
/// proxy serializes calls over the hub to the agent, which applies them to the local provider.
/// </summary>
public interface IAgentHubClient
{
    Task Ping();

    Task OnSyncRequested(SyncRequestMessage message);

    Task OnConfigurationChanged(ConfigurationChangedMessage message);

    Task<Guid> SyncGetStoreId(string[]? tables);

    Task<SyncVersion> SyncGetSyncVersion(string[]? tables);

    Task<SyncChangeSet> SyncGetChanges(
        Guid otherStoreId,
        SyncFilterParameter[]? syncFilterParameters,
        SyncDirection syncDirection,
        string[]? tables);

    Task<SyncAnchor> SyncApplyChanges(
        SyncChangeSet changeSet,
        ConflictResolution updateResolution,
        ConflictResolution deleteResolution,
        string[]? tables);

    Task SyncSaveVersionForStore(Guid otherStoreId, long version, string[]? tables);
}
