using CoreSync;
using CoreSyncServer.Agent.Contracts;
using CoreSyncServer.Services.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace CoreSyncServer.Services.Implementation;

/// <summary>
/// Server-side <see cref="ISyncProvider"/> that forwards every call over the SignalR
/// <see cref="AgentHub"/> to the agent process hosting the real provider for this DataStore.
/// Analogous to <c>CoreSync.Http.Client</c>'s <c>SyncProviderHttpClient</c>, but over the
/// persistent agent connection rather than HTTP. Provisioning and retention APIs are out of
/// scope for the initial iteration and throw <see cref="NotSupportedException"/>.
/// </summary>
internal sealed class AgentProxySyncProvider : ISyncProvider
{
    private readonly int _dataStoreId;
    private readonly string[]? _tables;
    private readonly IHubContext<AgentHub, IAgentHubClient> _hubContext;
    private readonly IAgentConnectionRegistry _registry;
    private readonly ISyncLogger _logger;

    public AgentProxySyncProvider(
        int dataStoreId,
        string[]? tables,
        IHubContext<AgentHub, IAgentHubClient> hubContext,
        IAgentConnectionRegistry registry,
        ISyncLogger logger)
    {
        _dataStoreId = dataStoreId;
        _tables = tables;
        _hubContext = hubContext;
        _registry = registry;
        _logger = logger;
    }

    public string[]? SyncTableNames => _tables;

    public async Task<Guid> GetStoreIdAsync(CancellationToken cancellationToken = default)
    {
        var client = ResolveClient();
        try
        {
            return await client.SyncGetStoreId(_tables);
        }
        catch (Exception ex) when (ex is not AgentOfflineException and not OperationCanceledException)
        {
            throw new AgentOfflineException(_dataStoreId, $"Agent RPC 'SyncGetStoreId' failed for DataStore {_dataStoreId}.", ex);
        }
    }

    public async Task<SyncVersion> GetSyncVersionAsync(CancellationToken cancellationToken = default)
    {
        var client = ResolveClient();
        try
        {
            return await client.SyncGetSyncVersion(_tables);
        }
        catch (Exception ex) when (ex is not AgentOfflineException and not OperationCanceledException)
        {
            throw new AgentOfflineException(_dataStoreId, $"Agent RPC 'SyncGetSyncVersion' failed for DataStore {_dataStoreId}.", ex);
        }
    }

    public async Task<SyncChangeSet> GetChangesAsync(
        Guid otherStoreId,
        SyncFilterParameter[]? syncFilterParameters = null,
        SyncDirection syncDirection = SyncDirection.UploadAndDownload,
        string[]? tables = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveTables = tables ?? _tables;
        var client = ResolveClient();
        try
        {
            var changeSet = await client.SyncGetChanges(otherStoreId, syncFilterParameters, syncDirection, effectiveTables);
            // SignalR's JSON protocol hands us JsonElement for object-typed fields; downstream
            // MessagePack serialization (used by the HTTP chunking path) can't handle those.
            SyncPayloadConverter.NormalizeChangeSet(changeSet);
            return changeSet;
        }
        catch (Exception ex) when (ex is not AgentOfflineException and not OperationCanceledException)
        {
            throw new AgentOfflineException(_dataStoreId, $"Agent RPC 'SyncGetChanges' failed for DataStore {_dataStoreId}.", ex);
        }
    }

    public async Task<SyncAnchor> ApplyChangesAsync(
        SyncChangeSet changeSet,
        Func<SyncItem, ConflictResolution>? onConflictFunc = null,
        CancellationToken cancellationToken = default)
    {
        // Delegates don't serialize. Probe the closure with dummy items of each change type to
        // extract the resolution it would return — this matches the CoreSync extension method
        // that wraps (updateResolution, deleteResolution) into a closure, and is tolerant of
        // custom closures that only inspect SyncItem.ChangeType (the common case).
        var updateResolution = ProbeResolution(onConflictFunc, ChangeType.Update);
        var deleteResolution = ProbeResolution(onConflictFunc, ChangeType.Delete);

        var client = ResolveClient();
        try
        {
            return await client.SyncApplyChanges(changeSet, updateResolution, deleteResolution, _tables);
        }
        catch (Exception ex) when (ex is not AgentOfflineException and not OperationCanceledException)
        {
            throw new AgentOfflineException(_dataStoreId, $"Agent RPC 'SyncApplyChanges' failed for DataStore {_dataStoreId}.", ex);
        }
    }

    public async Task SaveVersionForStoreAsync(Guid otherStoreId, long version, CancellationToken cancellationToken = default)
    {
        var client = ResolveClient();
        try
        {
            await client.SyncSaveVersionForStore(otherStoreId, version, _tables);
        }
        catch (Exception ex) when (ex is not AgentOfflineException and not OperationCanceledException)
        {
            throw new AgentOfflineException(_dataStoreId, $"Agent RPC 'SyncSaveVersionForStore' failed for DataStore {_dataStoreId}.", ex);
        }
    }

    public Task ApplyProvisionAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Provisioning is not proxied to agents.");

    public Task RemoveProvisionAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Provisioning is not proxied to agents.");

    public Task<SyncVersion> ApplyRetentionPolicyAsync(int minVersion, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Retention policy is not proxied to agents.");

    public Task EnableChangeTrackingForTable(string tableName, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Per-table change tracking toggle is not proxied to agents.");

    public Task DisableChangeTrackingForTable(string tableName, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Per-table change tracking toggle is not proxied to agents.");

    private IAgentHubClient ResolveClient()
    {
        if (!_registry.TryGetConnectionId(_dataStoreId, out var connectionId))
        {
            _logger.Info($"No agent connection registered for DataStore {_dataStoreId}.");
            throw new AgentOfflineException(_dataStoreId,
                $"No agent is currently connected for DataStore {_dataStoreId}.");
        }

        return _hubContext.Clients.Client(connectionId);
    }

    private static ConflictResolution ProbeResolution(Func<SyncItem, ConflictResolution>? func, ChangeType type)
    {
        if (func is null) return ConflictResolution.Skip;
        try
        {
            return func(new SyncItem("__probe__", type, new Dictionary<string, object?>()));
        }
        catch
        {
            return ConflictResolution.Skip;
        }
    }
}
