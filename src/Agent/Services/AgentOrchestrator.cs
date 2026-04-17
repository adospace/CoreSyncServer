using CoreSyncServer.Agent.Contracts;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace CoreSyncServer.Agent.Services;

/// <summary>
/// Drives the agent lifecycle: fetch DataStore list from the server, open one hub connection per
/// DataStore, and re-run the whole sequence on any permanent disconnect.
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
    private readonly SemaphoreSlim _reinitLock = new(1, 1);

    private readonly ResiliencePipeline _initPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<Exception>(ex => ex is not OperationCanceledException),
            MaxRetryAttempts = int.MaxValue,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(options.Value.InitPollOnFailureMs),
            MaxDelay = TimeSpan.FromMilliseconds(options.Value.ReconnectMaxDelayMs),
            OnRetry = args =>
            {
                logger.LogWarning(
                    args.Outcome.Exception,
                    "GetInitAsync failed; retrying in {Delay}ms (attempt {Attempt})",
                    args.RetryDelay.TotalMilliseconds,
                    args.AttemptNumber + 1);
                return ValueTask.CompletedTask;
            }
        })
        .Build();

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(ct);
                // RunOnceAsync returns when a permanent disconnect fires; loop re-inits.
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in orchestrator loop. Backing off before retry.");
                await BackoffAsync(_options.InitPollOnFailureMs, ct);
            }
        }

        await TearDownAllAsync();
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var reconnectSignal = new TaskCompletionSource();

        AgentInitResponse init = await _initPipeline.ExecuteAsync(
            async token => await serverClient.GetInitAsync(token),
            ct);

        logger.LogInformation(
            "Initialized as agent '{Agent}' ({Id}) with {Count} DataStore(s)",
            init.AgentName, init.AgentId, init.DataStores.Count);

        foreach (var dsDto in init.DataStores)
        {
            var conn = new DataStoreConnection(
                dsDto,
                options,
                syncHandler,
                loggerFactory.CreateLogger<DataStoreConnection>());

            conn.OnPermanentDisconnect += _ =>
            {
                // Any permanent disconnect triggers a full re-init.
                reconnectSignal.TrySetResult();
            };

            try
            {
                await conn.ConnectAsync(dsDto.ConnectionTicket, ct);
                _connections[dsDto.Id] = conn;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to connect for DataStore {Id} ({Name}); will re-init.",
                    dsDto.Id, dsDto.Name);
                await conn.DisposeAsync();
                reconnectSignal.TrySetResult();
                break;
            }
        }

        // Wait until a connection drops permanently (or until cancellation).
        using (ct.Register(() => reconnectSignal.TrySetResult()))
        {
            await reconnectSignal.Task;
        }

        if (!ct.IsCancellationRequested)
        {
            logger.LogInformation("A permanent disconnect occurred — tearing down and re-initializing.");
        }

        await TearDownAllAsync();
    }

    private async Task TearDownAllAsync()
    {
        await _reinitLock.WaitAsync();
        try
        {
            foreach (var conn in _connections.Values)
            {
                await conn.DisposeAsync();
            }
            _connections.Clear();
        }
        finally
        {
            _reinitLock.Release();
        }
    }

    private static async Task BackoffAsync(int delayMs, CancellationToken ct)
    {
        try { await Task.Delay(delayMs, ct); } catch (OperationCanceledException) { }
    }
}
