using System.Diagnostics;
using CoreSync;
using CoreSyncServer.Data;
using Microsoft.Extensions.Logging;

namespace CoreSyncServer.Services.Implementation;

public class SyncSessionLogger(
    ILogger logger,
    ApplicationDbContext context,
    int syncSessionId) : ISyncLogger
{
    public void Trace(string message)
    {
        logger.LogTrace("{Message}", message);
        AddTrace(message, TraceLevel.Verbose);
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
