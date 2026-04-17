using CoreSyncServer.Agent.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;

namespace CoreSyncServer.Agent.Services;

/// <summary>
/// One HubConnection bound to a specific DataStore. Passive: no auto-reconnect and no internal
/// heartbeat loop — health and lifecycle are owned by <see cref="AgentOrchestrator"/>, which
/// probes and rebuilds this connection on every tick. Tickets are one-shot, so rebuilding
/// (instead of reconnecting in-place) is required to re-join the correct hub group.
/// </summary>
public sealed class DataStoreConnection : IAsyncDisposable
{
    private readonly AgentOptions _options;
    private readonly IAgentSyncHandler _syncHandler;
    private readonly ILogger<DataStoreConnection> _logger;
    private readonly HubConnection _connection;
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
        ILogger<DataStoreConnection> logger)
    {
        _options = options.Value;
        _syncHandler = syncHandler;
        _logger = logger;
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
    /// Invokes a short-timeout Heartbeat on the hub. Returns false if the transport is not Connected
    /// or the invoke fails, in which case the orchestrator will rebuild this connection.
    /// </summary>
    public async Task<bool> HealthProbeAsync(CancellationToken ct)
    {
        if (_connection.State != HubConnectionState.Connected) return false;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.ProbeTimeoutMs);

        try
        {
            await _connection.InvokeAsync(nameof(IAgentHub.Heartbeat), timeoutCts.Token);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Heartbeat probe failed for DataStore {Id} ({Name})", DataStoreId, DataStoreName);
            return false;
        }
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
        await _connection.DisposeAsync();
    }
}
