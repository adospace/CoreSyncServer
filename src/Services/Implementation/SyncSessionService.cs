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
