using System.Diagnostics;
using CoreSyncServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoreSyncServer.Services.Implementation;

public class SyncSessionMaintenanceTask(ILogger<SyncSessionMaintenanceTask> logger) : MaintenanceTask
{
    public override async Task ExecuteAsync(IServiceProvider scopedProvider, CancellationToken cancellationToken)
    {
        var context = scopedProvider.GetRequiredService<ApplicationDbContext>();
        var settings = scopedProvider.GetRequiredService<IOptions<MaintenanceSettings>>().Value;

        // Remove verbose traces from sessions older than the configured threshold
        // Targets entire sessions to avoid partially removing traces from a session
        var traceCutoff = DateTime.UtcNow.AddHours(-settings.SyncTraceRetentionHours);

        var tracesRemoved = await context.SyncSessionTraces
            .Where(t => t.TraceLevel == TraceLevel.Verbose
                && t.SyncSession!.StartTime < traceCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (tracesRemoved > 0)
            logger.LogInformation("Sync session maintenance: removed {Count} verbose trace(s) from sessions older than {Hours}h.", tracesRemoved, settings.SyncTraceRetentionHours);

        // Remove completed sync sessions older than the configured threshold
        var sessionCutoff = DateTime.UtcNow.AddDays(-settings.SyncSessionRetentionDays);

        var sessionsRemoved = await context.SyncSessions
            .Where(s => s.Status == SyncSessionStatus.Completed && s.EndTime < sessionCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (sessionsRemoved > 0)
            logger.LogInformation("Sync session maintenance: removed {Count} completed session(s) older than {Days}d.", sessionsRemoved, settings.SyncSessionRetentionDays);
    }
}
