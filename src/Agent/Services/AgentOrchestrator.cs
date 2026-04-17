using System.Threading.Channels;
using CoreSyncServer.Agent.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;

namespace CoreSyncServer.Agent.Services;

/// <summary>
/// Single reconcile loop that keeps the agent aligned with the server. On each tick it:
///   1. Polls <c>/api/agent/datastores</c> for the latest assignments (this is also the server
///      reachability probe — if it fails, existing connections are left alone and we retry next tick).
///   2. Adds/removes/rebuilds <see cref="DataStoreConnection"/>s so they match the server's response.
///      A connection is rebuilt when its state is not Connected, or when the server issued a new
///      configuration version or a new one-shot ticket.
///   3. Heartbeats each still-healthy connection; a failed probe flags it for rebuild next tick.
/// Lost transports and server-pushed <see cref="ConfigurationChangedMessage"/>s wake the loop
/// early via <see cref="DataStoreConnection.OnNeedsAttention"/>.
/// </summary>
public class AgentOrchestrator(
    IServerClient serverClient,
    IOptions<AgentOptions> options,
    IAgentSyncHandler syncHandler,
    ILoggerFactory loggerFactory,
    ILogger<AgentOrchestrator> logger)
{
    private readonly AgentOptions _options = options.Value;
    private readonly Dictionary<int, DataStoreConnection> _connections = new();

    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public async Task RunAsync(CancellationToken ct)
    {
        logger.LogInformation(
            "Orchestrator starting: PollIntervalMs={Poll} ProbeTimeoutMs={Probe}",
            _options.PollIntervalMs, _options.ProbeTimeoutMs);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await TickAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unexpected error in orchestrator tick; will retry.");
                }

                await WaitForNextTickAsync(ct);
            }
        }
        finally
        {
            await TearDownAllAsync();
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        AgentInitResponse init;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_options.ProbeTimeoutMs * 2);
            init = await serverClient.GetInitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "GetInitAsync failed; keeping {Count} existing connection(s) and retrying in {Ms}ms.",
                _connections.Count, _options.PollIntervalMs);
            return;
        }

        logger.LogDebug(
            "Tick: agent '{Agent}' ({Id}), server reports {Count} DataStore(s)",
            init.AgentName, init.AgentId, init.DataStores.Count);

        var incomingIds = new HashSet<int>(init.DataStores.Select(d => d.Id));

        foreach (var removedId in _connections.Keys.Except(incomingIds).ToList())
        {
            logger.LogInformation("DataStore {Id} no longer assigned; disposing connection.", removedId);
            var removed = _connections[removedId];
            _connections.Remove(removedId);
            await removed.DisposeAsync();
        }

        foreach (var ds in init.DataStores)
        {
            await EnsureConnectionAsync(ds, ct);
        }
    }

    private async Task EnsureConnectionAsync(AgentDataStoreDto ds, CancellationToken ct)
    {
        var incomingVersion = ds.Configurations.FirstOrDefault()?.Version ?? 0;

        if (_connections.TryGetValue(ds.Id, out var existing))
        {
            // Tickets are one-shot and regenerated every poll, so they're not a change signal —
            // only configuration version bumps and bad connection state are.
            var versionChanged = existing.ConfigurationVersion != incomingVersion;
            var stateBad = existing.State != HubConnectionState.Connected;

            if (!versionChanged && !stateBad)
            {
                if (await existing.HealthProbeAsync(ct)) return;

                logger.LogWarning("Heartbeat probe failed for DataStore {Id} ({Name}); rebuilding.",
                    ds.Id, ds.Name);
            }
            else if (versionChanged)
            {
                logger.LogInformation(
                    "DataStore {Id} ({Name}) configuration v{Old} → v{New}; rebuilding.",
                    ds.Id, ds.Name, existing.ConfigurationVersion, incomingVersion);
            }
            else
            {
                logger.LogInformation(
                    "DataStore {Id} ({Name}) connection state is {State}; rebuilding.",
                    ds.Id, ds.Name, existing.State);
            }

            _connections.Remove(ds.Id);
            await existing.DisposeAsync();
        }

        var conn = new DataStoreConnection(
            ds,
            options,
            syncHandler,
            loggerFactory.CreateLogger<DataStoreConnection>());

        conn.OnNeedsAttention += TriggerWake;

        try
        {
            await conn.ConnectAsync(ct);
            _connections[ds.Id] = conn;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await conn.DisposeAsync();
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to connect DataStore {Id} ({Name}); will retry next tick.",
                ds.Id, ds.Name);
            await conn.DisposeAsync();
        }
    }

    private void TriggerWake() => _wake.Writer.TryWrite(true);

    private async Task WaitForNextTickAsync(CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.PollIntervalMs);
        try
        {
            await _wake.Reader.ReadAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Poll interval elapsed — proceed to next tick.
        }
    }

    private async Task TearDownAllAsync()
    {
        foreach (var conn in _connections.Values)
        {
            try { await conn.DisposeAsync(); }
            catch (Exception ex) { logger.LogDebug(ex, "Error disposing connection for DataStore {Id}", conn.DataStoreId); }
        }
        _connections.Clear();
    }
}
