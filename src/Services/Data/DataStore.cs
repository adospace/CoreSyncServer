namespace CoreSyncServer.Data;

public enum DataStoreType
{
    SQLite,
    SqlServer,
    PostgreSQL,
}

public enum SqlServerDataStoreTrackingMode
{
    Triggers,
    ChangeTracking
}

public abstract class DataStore
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public int ProjectId { get; set; }

    public Project? Project { get; set; }

    public DataStoreType Type { get; set; }

    public IList<DataStoreConfiguration> Configurations { get; set; } = [];

    public IList<SyncSession> SyncSessions { get; set; } = [];
}

public class SqliteDataStore : DataStore
{
    public required string FilePath { get; set; }
}

public class SqlServerDataStore : DataStore
{
    public required string ConnectionString { get; set; }

    public SqlServerDataStoreTrackingMode TrackingMode { get; set; }
}

public class PostgreSqlDataStore : DataStore
{
    public required string ConnectionString { get; set; }
}
