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
    protected static string ResolveConnectionString(string connectionString)
    {
        if (connectionString.StartsWith("ENV=", StringComparison.OrdinalIgnoreCase))
        {
            var envVarName = connectionString[4..];
            return Environment.GetEnvironmentVariable(envVarName)
                ?? throw new InvalidOperationException($"Environment variable '{envVarName}' is not set.");
        }

        return connectionString;
    }

    public int Id { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public int ProjectId { get; set; }

    public Project? Project { get; set; }

    public DataStoreType Type { get; set; }

    public bool IsMonitorEnabled { get; set; } = true;

    public int? AgentId { get; set; }

    public Agent? Agent { get; set; }

    public IList<DataStoreConfiguration> Configurations { get; set; } = [];

    public IList<SyncSession> SyncSessions { get; set; } = [];

    public IList<DiagnosticItem> DiagnosticItems { get; set; } = [];
}

public class SqliteDataStore : DataStore
{
    public required string FilePath { get; set; }
}

public class SqlServerDataStore : DataStore
{
    public required string ConnectionString { get; set; }

    public string GetResolvedConnectionString() => ResolveConnectionString(ConnectionString);

    public SqlServerDataStoreTrackingMode TrackingMode { get; set; }
}

public class PostgreSqlDataStore : DataStore
{
    public required string ConnectionString { get; set; }

    public string GetResolvedConnectionString() => ResolveConnectionString(ConnectionString);
}
