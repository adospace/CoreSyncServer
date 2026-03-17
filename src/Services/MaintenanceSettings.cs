namespace CoreSyncServer.Services;

public class MaintenanceSettings
{
    public int IntervalMinutes { get; set; } = 60;
    public int DiagnosticRetentionHours { get; set; } = 24;
    public int SyncTraceRetentionHours { get; set; } = 24;
    public int SyncSessionRetentionDays { get; set; } = 7;
}
