using CoreSync;
using CoreSync.PostgreSQL;
using CoreSync.Sqlite;
using CoreSync.SqlServer;
using CoreSync.SqlServerCT;
using CoreSyncServer.Agent.Contracts;

namespace CoreSyncServer.Agent.Services;

/// <summary>
/// Agent-side counterpart of the server's <c>SyncProviderFactory</c>. Builds a real
/// <see cref="ISyncProvider"/> against the local database, driven by the DTO the server
/// sent during <c>/api/agent/datastores</c> polling. <c>ENV=NAME</c> connection strings
/// are resolved against the agent's local environment.
/// </summary>
public sealed class AgentSyncProviderFactory
{
    public ISyncProvider Create(AgentDataStoreDto dataStore, string[]? tables, ISyncLogger syncLogger)
    {
        var configuration = dataStore.Configurations.FirstOrDefault()
            ?? throw new InvalidOperationException($"DataStore '{dataStore.Name}' has no configuration.");

        var configuredTables = configuration.Tables
            .Where(t => !string.Equals(t.SyncDirection, "NotTracked", StringComparison.OrdinalIgnoreCase))
            .ToList();

        List<AgentDataStoreTableDto> orderedTables;
        if (tables is { Length: > 0 })
        {
            var byName = configuredTables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
            orderedTables = new List<AgentDataStoreTableDto>(tables.Length);
            foreach (var name in tables)
            {
                if (!byName.TryGetValue(name, out var t))
                    throw new InvalidOperationException($"Table '{name}' is not present in the agent configuration for DataStore '{dataStore.Name}'.");
                orderedTables.Add(t);
            }
        }
        else
        {
            orderedTables = configuredTables.OrderBy(t => t.Sort).ToList();
        }

        return dataStore.Type switch
        {
            "SQLite" => BuildSqlite(dataStore, orderedTables, syncLogger),
            "SqlServer" when string.Equals(dataStore.TrackingMode, "ChangeTracking", StringComparison.OrdinalIgnoreCase)
                => BuildSqlServerCT(dataStore, orderedTables, syncLogger),
            "SqlServer" => BuildSqlServer(dataStore, orderedTables, syncLogger),
            "PostgreSQL" => BuildPostgres(dataStore, orderedTables, syncLogger),
            _ => throw new NotSupportedException($"DataStore type '{dataStore.Type}' is not supported by the agent.")
        };
    }

    private static string ResolveConnectionString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("DataStore connection string is required.");

        if (raw.StartsWith("ENV=", StringComparison.OrdinalIgnoreCase))
        {
            var envVarName = raw[4..];
            return Environment.GetEnvironmentVariable(envVarName)
                ?? throw new InvalidOperationException($"Environment variable '{envVarName}' is not set on the agent host.");
        }

        return raw;
    }

    private static SyncDirection MapDirection(string value) => value switch
    {
        "UploadAndDownload" => SyncDirection.UploadAndDownload,
        "UploadOnly" => SyncDirection.UploadOnly,
        "DownloadOnly" => SyncDirection.DownloadOnly,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported sync direction.")
    };

    private static string[] DeserializeStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch { return []; }
    }

    private static ISyncProvider BuildSqlite(AgentDataStoreDto ds, List<AgentDataStoreTableDto> tables, ISyncLogger syncLogger)
    {
        if (string.IsNullOrWhiteSpace(ds.FilePath))
            throw new InvalidOperationException($"SQLite DataStore '{ds.Name}' has no FilePath.");

        var builder = new SqliteSyncConfigurationBuilder($"Data Source={ds.FilePath}");
        foreach (var t in tables)
        {
            builder.Table(t.Name,
                syncDirection: MapDirection(t.SyncDirection),
                skipInitialSnapshot: t.SkipInitialSnapshot,
                selectIncrementalQuery: t.SelectIncrementalQuery,
                customSnapshotQuery: t.CustomSnapshotQuery);
        }
        return new SqliteSyncProvider(builder.Build(), ProviderMode.Remote, syncLogger);
    }

    private static ISyncProvider BuildSqlServer(AgentDataStoreDto ds, List<AgentDataStoreTableDto> tables, ISyncLogger syncLogger)
    {
        var builder = new SqlSyncConfigurationBuilder(ResolveConnectionString(ds.ConnectionString));
        foreach (var t in tables)
        {
            builder.Table(t.Name,
                syncDirection: MapDirection(t.SyncDirection),
                schema: t.Schema,
                skipInitialSnapshot: t.SkipInitialSnapshot,
                selectIncrementalQuery: t.SelectIncrementalQuery,
                customSnapshotQuery: t.CustomSnapshotQuery);

            var skip = DeserializeStringArray(t.SkipColumns);
            if (skip.Length > 0) builder.SkipColumns(skip);

            var skipIU = DeserializeStringArray(t.SkipColumnsOnInsertOrUpdate);
            if (skipIU.Length > 0) builder.SkipColumnsOnInsertOrUpdate(skipIU);

            if (!string.Equals(t.IdentityInsert, "Auto", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<CoreSync.SqlServer.IdentityInsertMode>(t.IdentityInsert, true, out var mode))
            {
                builder.IdentityInsert(mode);
            }

            if (t.ForceReloadInsertedRecords)
                builder.ForceReloadInsertedRecords();
        }
        return new SqlSyncProvider(builder.Build(), ProviderMode.Remote, syncLogger);
    }

    private static ISyncProvider BuildSqlServerCT(AgentDataStoreDto ds, List<AgentDataStoreTableDto> tables, ISyncLogger syncLogger)
    {
        var builder = new SqlServerCTSyncConfigurationBuilder(ResolveConnectionString(ds.ConnectionString));
        foreach (var t in tables)
        {
            builder.Table(t.Name,
                syncDirection: MapDirection(t.SyncDirection),
                schema: t.Schema,
                skipInitialSnapshot: t.SkipInitialSnapshot,
                selectIncrementalQuery: t.SelectIncrementalQuery,
                customSnapshotQuery: t.CustomSnapshotQuery);

            var skip = DeserializeStringArray(t.SkipColumns);
            if (skip.Length > 0) builder.SkipColumns(skip);

            var skipIU = DeserializeStringArray(t.SkipColumnsOnInsertOrUpdate);
            if (skipIU.Length > 0) builder.SkipColumnsOnInsertOrUpdate(skipIU);

            if (!string.Equals(t.IdentityInsert, "Auto", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<CoreSync.SqlServerCT.IdentityInsertMode>(t.IdentityInsert, true, out var mode))
            {
                builder.IdentityInsert(mode);
            }
        }
        return new SqlServerCTProvider(builder.Build(), ProviderMode.Remote, syncLogger);
    }

    private static ISyncProvider BuildPostgres(AgentDataStoreDto ds, List<AgentDataStoreTableDto> tables, ISyncLogger syncLogger)
    {
        var builder = new PostgreSQLSyncConfigurationBuilder(ResolveConnectionString(ds.ConnectionString));
        foreach (var t in tables)
        {
            builder.Table(t.Name,
                syncDirection: MapDirection(t.SyncDirection),
                skipInitialSnapshot: t.SkipInitialSnapshot,
                selectIncrementalQuery: t.SelectIncrementalQuery,
                customSnapshotQuery: t.CustomSnapshotQuery);
        }
        return new PostgreSQLSyncProvider(builder.Build(), ProviderMode.Remote, syncLogger);
    }
}
