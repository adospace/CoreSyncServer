using System.Diagnostics;
using CoreSync;
using CoreSyncServer.Data;
using Microsoft.Extensions.Logging;

namespace CoreSyncServer.Services.Implementation;

public class SyncSessionLogger(
    ILogger logger,
    ApplicationDbContext context,
    int syncSessionId,
    int maxVerboseTraces = 1000) : ISyncLogger
{
    private int _verboseTraces;

    public void Trace(string message)
    {
        logger.LogTrace("{Message}", message);

        // Providers trace once per applied row. Persist the first few, which are enough to see
        // what a session was doing, and drop the rest so one bulk sync cannot fill the table.
        if (_verboseTraces < maxVerboseTraces)
        {
            _verboseTraces++;
            AddTrace(message, TraceLevel.Verbose);
        }
        else if (_verboseTraces == maxVerboseTraces)
        {
            _verboseTraces++;
            AddTrace($"Verbose trace truncated after {maxVerboseTraces} entries.", TraceLevel.Info);
        }
    }

    public void Info(string message)
    {
        logger.LogInformation("{Message}", message);
        AddTrace(message, TraceLevel.Info);
    }

    public void Warning(string message)
    {
        logger.LogWarning("{Message}", message);
        AddTrace(message, TraceLevel.Warning);
    }

    public void Error(string message)
    {
        logger.LogError("{Message}", message);
        AddTrace(message, TraceLevel.Error);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await context.SaveChangesAsync(cancellationToken);
    }

    private void AddTrace(string message, TraceLevel level)
    {
        context.SyncSessionTraces.Add(new SyncSessionTrace
        {
            SyncSessionId = syncSessionId,
            Message = message,
            TimeStamp = DateTime.UtcNow,
            TraceLevel = level
        });
    }
}
