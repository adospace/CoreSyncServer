using CoreSync;

namespace CoreSyncServer.Data;

public class DataStoreConfiguration
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public int DataStoreId { get; set; }

    public int Version { get; set; }

    public DataStore? DataStore { get; set; }

    public IList<DataStoreTableConfiguration> TableConfigurations { get; set; } = [];

    public IList<Endpoint> Endpoints { get; set; } = [];

    public IList<DiagnosticItem> DiagnosticItems { get; set; } = [];
}

public class DataStoreTableConfiguration
{
    public int Id { get; set; }

    public int DataStoreConfigurationId { get; set; }

    public DataStoreConfiguration? DataStoreConfiguration { get; set; }

    public required string Name { get; set; }

    public string? Schema { get; set; }

    public DataStoreTableConfigurationSyncMode SyncMode { get; set; }

    public bool InError { get; set; }

    public int Sort { get; set; }

    public string? Message { get; set; }

    // Base sync properties (all providers)
    public bool SkipInitialSnapshot { get; set; }

    public string? SelectIncrementalQuery { get; set; }

    public string? CustomSnapshotQuery { get; set; }

    // SQL Server properties (Triggers + CT)
    public string? SkipColumns { get; set; }

    public string? SkipColumnsOnInsertOrUpdate { get; set; }

    public DataStoreTableConfigurationIdentityInsertMode IdentityInsert { get; set; }

    public bool ForceReloadInsertedRecords { get; set; }
}

public enum DataStoreTableConfigurationIdentityInsertMode
{
    Auto,

    On,

    Off,

    Disabled
}

public enum DataStoreTableConfigurationSyncMode
{
    UploadAndDownload,

    UploadOnly,

    DownloadOnly,

    NotTracked
}