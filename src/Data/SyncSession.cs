using System.Diagnostics;

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
    public IList<SyncSessionTrace> Traces { get; set; } = [];
}

public enum SyncSessionStatus
{
    Started = 0,

    Completed = 1,

    Error = 2
}

public class SyncSessionTrace
{
    public int Id {get;set;}

    public int SyncSessionId{get;set;}

    public SyncSession? SyncSession{get;set;}

    public required string Message{get;set;}


    public DateTime TimeStamp{get;set;}

    public TraceLevel TraceLevel{get;set;}

}

