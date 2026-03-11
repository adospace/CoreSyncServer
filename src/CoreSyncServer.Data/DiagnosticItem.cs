namespace CoreSyncServer.Data;

public class DiagnosticItem
{
    public int Id { get; set; }

    public required string Message { get; set; }

    public LogItemLevel Level { get; set; }

    public DateTime Timestamp { get; set; }

    public bool IsResolved { get; set; }

    public int? ProjectId { get; set; }

    public Project? Project { get; set; }

    public int? SyncSessionId { get; set; }

    public SyncSession? SyncSession { get; set; }   

    public int? DataStoreId { get; set; }

    public DataStore? DataStore { get; set; }

    public int? DataStoreConfigurationId { get; set; }

    public DataStoreConfiguration? DataStoreConfiguration { get; set; }

    public Guid? EndpointId { get; set; }

    public Endpoint? EndPoint { get; set; }

    public string? Metadata { get; set; }
}


public enum LogItemLevel
{
    Critical,
    Error,
    Warning,
    Information,
    Debug,
    Trace
}