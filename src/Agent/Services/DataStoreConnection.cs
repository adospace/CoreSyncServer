using CoreSync;
using CoreSyncServer.Agent.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoreSyncServer.Agent.Services;

/// <summary>
/// One HubConnection bound to a specific DataStore. Passive: no auto-reconnect and no internal
/// heartbeat loop — health and lifecycle are owned by <see cref="AgentOrchestrator"/>, which
/// probes and rebuilds this connection on every tick. Tickets are one-shot, so rebuilding
/// (instead of reconnecting in-place) is required to re-join the correct hub group.
///
/// Also hosts the agent-side of the <c>ISyncProvider</c> dual: the hub RPC methods from
/// <see cref="IAgentHubClient"/> are registered here and executed against a local
/// <see cref="ISyncProvider"/> built by <see cref="AgentSyncProviderFactory"/>.
/// </summary>
public sealed class DataStoreConnection : IAsyncDisposable
{
    private readonly AgentOptions _options;
    private readonly IAgentSyncHandler _syncHandler;
    private readonly AgentSyncProviderFactory _providerFactory;
    private readonly ISyncLogger _syncLogger;
    private readonly ILogger<DataStoreConnection> _logger;
    private readonly HubConnection _connection;
    private readonly AgentDataStoreDto _dataStoreDto;
    private ISyncProvider? _cachedFullProvider;
    private bool _disposed;

    public int DataStoreId { get; }

    public string DataStoreName { get; }

    public int ConfigurationVersion { get; }

    private readonly Guid _connectionTicket;

    public HubConnectionState State => _connection.State;

    /// <summary>Raised when the transport closes or the server signals configuration changed.
    /// Lets the orchestrator wake its reconcile loop without waiting for the next scheduled tick.</summary>
    public event Action? OnNeedsAttention;

    public DataStoreConnection(
        AgentDataStoreDto dataStore,
        IOptions<AgentOptions> options,
        IAgentSyncHandler syncHandler,
        AgentSyncProviderFactory providerFactory,
        ISyncLogger syncLogger,
        ILogger<DataStoreConnection> logger)
    {
        _options = options.Value;
        _syncHandler = syncHandler;
        _providerFactory = providerFactory;
        _syncLogger = syncLogger;
        _logger = logger;
        _dataStoreDto = dataStore;
        DataStoreId = dataStore.Id;
        DataStoreName = dataStore.Name;
        _connectionTicket = dataStore.ConnectionTicket;
        ConfigurationVersion = dataStore.Configurations.FirstOrDefault()?.Version ?? 0;

        var hubUrl = new Uri(new Uri(_options.ServerUrl), AgentAuthHeaders.HubPath);
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, opts =>
            {
                opts.Headers[AgentAuthHeaders.ApiKeyHeader] = _options.ApiKey;
                opts.AccessTokenProvider = () => Task.FromResult<string?>(_options.ApiKey);
            })
            .Build();

        // Match the server-side HubOptions so long GetChanges/ApplyChanges RPCs don't trip the
        // transport mid-call. ServerTimeout must exceed the server's KeepAliveInterval; the
        // SignalR client auto-pings at KeepAliveInterval even when no app message is flowing.
        _connection.ServerTimeout = TimeSpan.FromMinutes(10);
        _connection.KeepAliveInterval = TimeSpan.FromSeconds(15);

        RegisterClientHandlers();
        _connection.Closed += OnClosed;
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        await _connection.StartAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.ProbeTimeoutMs);

        var ok = await _connection.InvokeAsync<bool>(
            nameof(IAgentHub.AcknowledgeConnected),
            _connectionTicket,
            timeoutCts.Token);

        if (!ok)
        {
            throw new InvalidOperationException($"Server rejected connection ticket for DataStore {DataStoreId}.");
        }

        _logger.LogInformation("Connected to hub for DataStore {Id} ({Name}), config v{Version}",
            DataStoreId, DataStoreName, ConfigurationVersion);
    }

    /// <summary>
    /// Reports whether the transport is currently connected. We intentionally do NOT invoke the
    /// Heartbeat RPC here: a large server→agent call (e.g. a GetChanges returning tens of
    /// thousands of items) can monopolize the connection's write pipeline for tens of seconds,
    /// which would starve the heartbeat, trip a short timeout, and cause the orchestrator to
    /// tear down a healthy connection mid-send. SignalR's own KeepAliveInterval + ServerTimeout
    /// already detect truly-dead peers, and a hard transport close fires <c>Closed</c> which
    /// wakes the orchestrator via <c>OnNeedsAttention</c>.
    /// </summary>
    public Task<bool> HealthProbeAsync(CancellationToken ct)
        => Task.FromResult(_connection.State == HubConnectionState.Connected);

    private ISyncProvider ResolveProvider(string[]? tables)
    {
        if (tables is null || tables.Length == 0)
        {
            return _cachedFullProvider ??= _providerFactory.Create(_dataStoreDto, null, _syncLogger);
        }
        return _providerFactory.Create(_dataStoreDto, tables, _syncLogger);
    }

    private void RegisterClientHandlers()
    {
        _connection.On<SyncRequestMessage>(nameof(IAgentHubClient.OnSyncRequested),
            async msg => await _syncHandler.OnSyncRequestedAsync(DataStoreId, msg, CancellationToken.None));

        _connection.On<ConfigurationChangedMessage>(nameof(IAgentHubClient.OnConfigurationChanged),
            async msg =>
            {
                await _syncHandler.OnConfigurationChangedAsync(DataStoreId, msg, CancellationToken.None);
                OnNeedsAttention?.Invoke();
            });

        _connection.On(nameof(IAgentHubClient.Ping), () => Task.CompletedTask);

        // Server-initiated sync RPCs — the "agent-client-provider" half of the dual.
        _connection.On<string[]?, Guid>(nameof(IAgentHubClient.SyncGetStoreId), async tables =>
        {
            _logger.LogDebug("SyncGetStoreId on DataStore {Id}", DataStoreId);
            return await ResolveProvider(tables).GetStoreIdAsync();
        });

        _connection.On<string[]?, SyncVersion>(nameof(IAgentHubClient.SyncGetSyncVersion), async tables =>
        {
            _logger.LogDebug("SyncGetSyncVersion on DataStore {Id}", DataStoreId);
            return await ResolveProvider(tables).GetSyncVersionAsync();
        });

        _connection.On<Guid, SyncFilterParameter[]?, SyncDirection, string[]?, SyncChangeSet>(
            nameof(IAgentHubClient.SyncGetChanges),
            async (otherStoreId, filterParams, direction, tables) =>
            {
                _logger.LogDebug("SyncGetChanges on DataStore {Id} (dir={Dir})", DataStoreId, direction);
                var normalizedFilters = SyncPayloadConverter.NormalizeFilters(filterParams);
                var provider = ResolveProvider(tables);
                return await provider.GetChangesAsync(otherStoreId, normalizedFilters, direction, tables);
            });

        _connection.On<SyncChangeSet, ConflictResolution, ConflictResolution, string[]?, SyncAnchor>(
            nameof(IAgentHubClient.SyncApplyChanges),
            async (changeSet, updateResolution, deleteResolution, tables) =>
            {
                _logger.LogDebug("SyncApplyChanges on DataStore {Id} (items={Count})", DataStoreId, changeSet.Items.Count);
                SyncPayloadConverter.NormalizeChangeSet(changeSet);
                var provider = ResolveProvider(tables);
                return await provider.ApplyChangesAsync(changeSet, updateResolution, deleteResolution);
            });

        _connection.On<Guid, long, string[]?>(nameof(IAgentHubClient.SyncSaveVersionForStore),
            async (otherStoreId, version, tables) =>
            {
                _logger.LogDebug("SyncSaveVersionForStore on DataStore {Id} (store={Store} ver={Ver})",
                    DataStoreId, otherStoreId, version);
                await ResolveProvider(tables).SaveVersionForStoreAsync(otherStoreId, version);
            });
    }

    private Task OnClosed(Exception? exception)
    {
        if (_disposed) return Task.CompletedTask;

        _logger.LogWarning(exception, "Hub connection closed for DataStore {Id} ({Name})", DataStoreId, DataStoreName);
        OnNeedsAttention?.Invoke();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;

        // Bound the graceful-stop wait so a slow or dead server doesn't stall shutdown.
        // If the stop times out or fails, DisposeAsync still proceeds and forces the socket closed.
        try
        {
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _connection.StopAsync(stopCts.Token);
        }
        catch
        {
            // Ignore — we're tearing down regardless.
        }

        await _connection.DisposeAsync();
    }
}
