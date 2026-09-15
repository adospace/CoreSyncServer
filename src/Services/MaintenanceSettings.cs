namespace CoreSyncServer.Services;

public class MaintenanceSettings
{
    public int IntervalMinutes { get; set; } = 60;
    public int DiagnosticRetentionHours { get; set; } = 24;
    public int SyncTraceRetentionHours { get; set; } = 24;
    public int SyncSessionRetentionDays { get; set; } = 7;

    /// <summary>
    /// Verbose traces a single sync session may persist. Sync providers trace every applied
    /// row, so a large initial sync would otherwise write one row per synced record; past the
    /// cap the session keeps only a note that its verbose trace was truncated.
    /// </summary>
    public int MaxVerboseTracesPerSession { get; set; } = 1000;

    /// <summary>
    /// Ceiling on verbose trace rows across all sessions. Retention is the normal control; this
    /// is the backstop when retention cannot keep up, and maintenance deletes the oldest verbose
    /// traces regardless of age until the table is back under it.
    /// </summary>
    public int MaxVerboseTraceRows { get; set; } = 200_000;
}
