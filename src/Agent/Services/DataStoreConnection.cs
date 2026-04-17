using CoreSyncServer.Agent.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;

namespace CoreSyncServer.Agent.Services;

/// <summary>
/// One HubConnection bound to a specific DataStore. Handles the ticket handshake,
/// heartbeats, and raises <see cref="OnPermanentDisconnect"/> when auto-reconnect gives up.
/// </summary>
public sealed class DataStoreConnection : IAsyncDisposable
{
    private readonly AgentOptions _options;
    private readonly IAgentSyncHandler _syncHandler;
    private readonly ILogger<DataStoreConnection> _logger;
    private readonly HubConnection _connection;
    private readonly PeriodicTimer _heartbeatTimer;
    private CancellationTokenSource? _heartbeatCts;
    private bool _disposed;

    public int DataStoreId { get; }

    public string DataStoreName { get; }

    public event Action<int>? OnPermanentDisconnect;

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

        var hubUrl = new Uri(new Uri(_options.ServerUrl), AgentAuthHeaders.HubPath);
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, opts =>
            {
                opts.Headers[AgentAuthHeaders.ApiKeyHeader] = _options.ApiKey;
                opts.AccessTokenProvider = () => Task.FromResult<string?>(_options.ApiKey);
            })
            .WithAutomaticReconnect(new[]
            {
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(30)
            })
            .Build();

        _heartbeatTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.HeartbeatIntervalMs));

        RegisterClientHandlers();

        _connection.Closed += OnClosed;
    }

    public async Task ConnectAsync(Guid ticket, CancellationToken ct)
    {
        await _connection.StartAsync(ct);

        var ok = await _connection.InvokeAsync<bool>(
            nameof(IAgentHub.AcknowledgeConnected),
            ticket,
            ct);

        if (!ok)
        {
            _logger.LogWarning("AcknowledgeConnected rejected for DataStore {Id}", DataStoreId);
            throw new InvalidOperationException($"Server rejected connection ticket for DataStore {DataStoreId}.");
        }

        _logger.LogInformation("Connected to hub for DataStore {Id} ({Name})", DataStoreId, DataStoreName);

        _heartbeatCts = new CancellationTokenSource();
        _ = RunHeartbeatLoop(_heartbeatCts.Token);
    }

    private void RegisterClientHandlers()
    {
        _connection.On<SyncRequestMessage>(nameof(IAgentHubClient.OnSyncRequested),
            async msg => await _syncHandler.OnSyncRequestedAsync(DataStoreId, msg, CancellationToken.None));

        _connection.On<ConfigurationChangedMessage>(nameof(IAgentHubClient.OnConfigurationChanged),
            async msg => await _syncHandler.OnConfigurationChangedAsync(DataStoreId, msg, CancellationToken.None));

        _connection.On(nameof(IAgentHubClient.Ping), () => Task.CompletedTask);
    }

    private async Task RunHeartbeatLoop(CancellationToken ct)
    {
        try
        {
            while (await _heartbeatTimer.WaitForNextTickAsync(ct))
            {
                if (_connection.State != HubConnectionState.Connected) continue;
                try
                {
                    await _connection.InvokeAsync(nameof(IAgentHub.Heartbeat), ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex, "Heartbeat failed for DataStore {Id}", DataStoreId);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private Task OnClosed(Exception? exception)
    {
        _logger.LogWarning(exception, "Hub connection closed for DataStore {Id} ({Name})", DataStoreId, DataStoreName);
        if (!_disposed)
        {
            OnPermanentDisconnect?.Invoke(DataStoreId);
        }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _heartbeatCts?.Cancel();
        _heartbeatTimer.Dispose();
        _heartbeatCts?.Dispose();
        await _connection.DisposeAsync();
    }
}
