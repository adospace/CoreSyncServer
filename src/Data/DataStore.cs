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
    /// <summary>
    /// The default number of days SQL Server Change Tracking keeps change history for.
    /// </summary>
    /// <remarks>
    /// A client that stays offline longer than this window cannot resume an incremental sync and has
    /// to be reinitialized from a fresh snapshot, so the window has to comfortably cover how long a
    /// device is expected to go without connectivity. Longer retention grows the change tracking side
    /// tables, which is the cost being traded against here.
    /// </remarks>
    public const int DefaultChangeRetentionDays = 30;

    public required string ConnectionString { get; set; }

    public string GetResolvedConnectionString() => ResolveConnectionString(ConnectionString);

    public SqlServerDataStoreTrackingMode TrackingMode { get; set; }

    /// <summary>
    /// How many days of change history to retain, applied when
    /// <see cref="TrackingMode"/> is <see cref="SqlServerDataStoreTrackingMode.ChangeTracking"/>.
    /// Ignored for the trigger-based tracking mode, which keeps its own journal.
    /// </summary>
    public int ChangeRetentionDays { get; set; } = DefaultChangeRetentionDays;
}

public class PostgreSqlDataStore : DataStore
{
    public required string ConnectionString { get; set; }

    public string GetResolvedConnectionString() => ResolveConnectionString(ConnectionString);
}
