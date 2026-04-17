using CoreSyncServer.Agent.Contracts;

namespace CoreSyncServer.Agent.Services;

/// <summary>
/// Extension point for sync logic. The default implementation is a no-op; a future
/// implementation will react to server messages to actually perform syncs.
/// </summary>
public interface IAgentSyncHandler
{
    Task OnSyncRequestedAsync(int dataStoreId, SyncRequestMessage message, CancellationToken ct);

    Task OnConfigurationChangedAsync(int dataStoreId, ConfigurationChangedMessage message, CancellationToken ct);
}

public class DefaultAgentSyncHandler(ILogger<DefaultAgentSyncHandler> logger) : IAgentSyncHandler
{
    public Task OnSyncRequestedAsync(int dataStoreId, SyncRequestMessage message, CancellationToken ct)
    {
        logger.LogInformation("Sync requested for DataStore {DataStoreId}, session {Session}", dataStoreId, message.SessionId);
        return Task.CompletedTask;
    }

    public Task OnConfigurationChangedAsync(int dataStoreId, ConfigurationChangedMessage message, CancellationToken ct)
    {
        logger.LogInformation("Configuration {ConfigId} changed for DataStore {DataStoreId}", message.ConfigurationId, dataStoreId);
        return Task.CompletedTask;
    }
}
