using System.Diagnostics;
using CoreSyncServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoreSyncServer.Services.Implementation;

public class SyncSessionMaintenanceTask(ILogger<SyncSessionMaintenanceTask> logger) : MaintenanceTask
{
    /// <summary>
    /// Verbose traces are deleted in batches of this many rows. A single sync session can leave
    /// hundreds of thousands of verbose rows behind, and one DELETE spanning millions of rows
    /// runs past the command timeout every time, so it never removes anything and the table only
    /// ever grows. Each batch is a handful of index seeks plus a delete by primary key.
    /// </summary>
    public const int TraceDeleteBatchSize = 5000;

    /// <summary>
    /// How long one run keeps deleting traces before handing the rest to the next run. Bounds the
    /// first run after a large backlog has built up; steady state finishes in seconds.
    /// </summary>
    public static readonly TimeSpan TraceDeleteBudget = TimeSpan.FromMinutes(10);

    public override async Task ExecuteAsync(IServiceProvider scopedProvider, CancellationToken cancellationToken)
    {
        var context = scopedProvider.GetRequiredService<ApplicationDbContext>();
        var settings = scopedProvider.GetRequiredService<IOptions<MaintenanceSettings>>().Value;

        var stopwatch = Stopwatch.StartNew();

        var completed = await RemoveExpiredVerboseTracesAsync(context, settings, stopwatch, cancellationToken)
            && await EnforceVerboseTraceCeilingAsync(context, settings, stopwatch, cancellationToken);

        if (!completed)
        {
            // Sessions still carrying verbose traces would cascade the same oversized delete
            // through the session row. Leave them for a run that has caught up.
            logger.LogInformation("Sync session maintenance: trace cleanup budget exhausted, session cleanup deferred to the next run.");
            return;
        }

        // Remove completed sync sessions older than the configured threshold
        var sessionCutoff = DateTime.UtcNow.AddDays(-settings.SyncSessionRetentionDays);

        var sessionsRemoved = await context.SyncSessions
            .Where(s => s.Status == SyncSessionStatus.Completed && s.EndTime < sessionCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (sessionsRemoved > 0)
            logger.LogInformation("Sync session maintenance: removed {Count} completed session(s) older than {Days}d.", sessionsRemoved, settings.SyncSessionRetentionDays);
    }

    /// <summary>
    /// Removes verbose traces from sessions older than the configured threshold. Targets entire
    /// sessions (by start time) rather than individual traces, so a session is never left with
    /// half its verbose trace. Returns false when the time budget ran out before the backlog was
    /// cleared.
    /// </summary>
    private async Task<bool> RemoveExpiredVerboseTracesAsync(ApplicationDbContext context, MaintenanceSettings settings, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var traceCutoff = DateTime.UtcNow.AddHours(-settings.SyncTraceRetentionHours);

        // Oldest first, so a run that hits the budget still makes the oldest sessions eligible for
        // deletion below on the next pass.
        var sessionIds = await context.SyncSessions
            .Where(s => s.StartTime < traceCutoff)
            .OrderBy(s => s.StartTime)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var tracesRemoved = 0;

        foreach (var sessionId in sessionIds)
        {
            // Walk the session's traces by primary key: the index on SyncSessionId is ordered by
            // Id within a session, so each batch is a seek that stops after BatchSize matches.
            var lastId = 0;
            while (true)
            {
                if (stopwatch.Elapsed >= TraceDeleteBudget)
                {
                    logger.LogInformation("Sync session maintenance: removed {Count} verbose trace(s) before the {Budget} budget ran out; continuing next run.", tracesRemoved, TraceDeleteBudget);
                    return false;
                }

                var batch = await context.SyncSessionTraces
                    .Where(t => t.SyncSessionId == sessionId && t.TraceLevel == TraceLevel.Verbose && t.Id > lastId)
                    .OrderBy(t => t.Id)
                    .Select(t => t.Id)
                    .Take(TraceDeleteBatchSize)
                    .ToListAsync(cancellationToken);

                if (batch.Count == 0)
                    break;

                tracesRemoved += await DeleteTracesAsync(context, batch, cancellationToken);
                lastId = batch[^1];

                if (batch.Count < TraceDeleteBatchSize)
                    break;
            }
        }

        if (tracesRemoved > 0)
            logger.LogInformation("Sync session maintenance: removed {Count} verbose trace(s) from sessions older than {Hours}h.", tracesRemoved, settings.SyncTraceRetentionHours);

        return true;
    }

    /// <summary>
    /// Backstop for when retention alone has not kept the table bounded: deletes the oldest
    /// verbose traces, whatever their session's age, until the verbose row count is back under
    /// <see cref="MaintenanceSettings.MaxVerboseTraceRows"/>. Returns false when the time budget
    /// ran out first.
    /// </summary>
    private async Task<bool> EnforceVerboseTraceCeilingAsync(ApplicationDbContext context, MaintenanceSettings settings, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        if (settings.MaxVerboseTraceRows <= 0)
            return true;

        var verboseRows = await context.SyncSessionTraces
            .CountAsync(t => t.TraceLevel == TraceLevel.Verbose, cancellationToken);

        var excess = verboseRows - settings.MaxVerboseTraceRows;
        if (excess <= 0)
            return true;

        logger.LogWarning("Sync session maintenance: {Rows} verbose trace(s) exceed the {Ceiling} ceiling; deleting the oldest {Excess}.", verboseRows, settings.MaxVerboseTraceRows, excess);

        var tracesRemoved = 0;
        while (tracesRemoved < excess)
        {
            if (stopwatch.Elapsed >= TraceDeleteBudget)
            {
                logger.LogInformation("Sync session maintenance: removed {Count} verbose trace(s) over the ceiling before the {Budget} budget ran out; continuing next run.", tracesRemoved, TraceDeleteBudget);
                return false;
            }

            var batch = await context.SyncSessionTraces
                .Where(t => t.TraceLevel == TraceLevel.Verbose)
                .OrderBy(t => t.Id)
                .Select(t => t.Id)
                .Take(Math.Min(TraceDeleteBatchSize, excess - tracesRemoved))
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
                break;

            tracesRemoved += await DeleteTracesAsync(context, batch, cancellationToken);
        }

        logger.LogInformation("Sync session maintenance: removed {Count} verbose trace(s) over the ceiling.", tracesRemoved);
        return true;
    }

    private static Task<int> DeleteTracesAsync(ApplicationDbContext context, List<int> ids, CancellationToken cancellationToken)
        => context.SyncSessionTraces
            .Where(t => ids.Contains(t.Id))
            .ExecuteDeleteAsync(cancellationToken);
}
