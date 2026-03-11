namespace CoreSyncServer.Data;

public class SyncSession
{
    public int Id { get; set; }
    public int DataStoreId { get; set; }
    public DataStore? DataStore { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public SyncSessionStatus Status { get; set; }

    public IList<DiagnosticItem> DiagnosticItems { get; set; } = [];
}

public enum SyncSessionStatus
{
    Started = 0,

    Completed = 1,

    Error = 2
}