using System.Diagnostics;
using CoreSyncServer.Data;
using Microsoft.EntityFrameworkCore;

namespace CoreSyncServer.Services.Implementation;

internal class SyncSessionService(
    ApplicationDbContext context,
    IDiagnosticService diagnosticService) : ISyncSessionService
{
    public async Task<SyncSession> StartAsync(int dataStoreId, CancellationToken cancellationToken = default)
    {
        var session = new SyncSession
        {
            DataStoreId = dataStoreId,
            StartTime = DateTime.UtcNow,
            Status = SyncSessionStatus.Started
        };

        context.SyncSessions.Add(session);
        await context.SaveChangesAsync(cancellationToken);

        return session;
    }

    public async Task CompleteAsync(int sessionId, CancellationToken cancellationToken = default)
    {
        await context.SyncSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SyncSessionStatus.Completed)
                .SetProperty(x => x.EndTime, DateTime.UtcNow),
                cancellationToken);
    }

    public async Task ErrorAsync(int sessionId, string errorMessage, CancellationToken cancellationToken = default)
    {
        await context.SyncSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SyncSessionStatus.Error)
                .SetProperty(x => x.EndTime, DateTime.UtcNow),
                cancellationToken);

        var session = await context.SyncSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => new { s.DataStoreId })
            .FirstAsync(cancellationToken);

        // A failed session has to carry its reason in its own traces. Some failures - an anchor that
        // has aged out of the change retention window is the usual one - are raised before the
        // per-table trace loop starts, so the session's trace list would otherwise be a single
        // "Begin GetChanges" line and Status = Error, saying nothing about what went wrong. The
        // message reaches DiagnosticItems either way, but nothing leads there from the session
        // detail view, which is where anyone investigating a failed session looks first.
        context.SyncSessionTraces.Add(new SyncSessionTrace
        {
            SyncSessionId = sessionId,
            Message = errorMessage,
            TimeStamp = DateTime.UtcNow,
            TraceLevel = TraceLevel.Error
        });

        await context.SaveChangesAsync(cancellationToken);

        await diagnosticService.CreateAsync(new DiagnosticItem
        {
            Message = errorMessage,
            Level = LogItemLevel.Error,
            Timestamp = DateTime.UtcNow,
            SyncSessionId = sessionId,
            DataStoreId = session.DataStoreId
        }, cancellationToken);
    }
}
