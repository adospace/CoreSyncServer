using System.Text.Json;
using CoreSync;
using CoreSync.PostgreSQL;
using CoreSync.Sqlite;
using CoreSync.SqlServer;
using CoreSync.SqlServerCT;
using CoreSyncServer.Agent.Contracts;
using CoreSyncServer.Data;
using CoreSyncServer.Services.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace CoreSyncServer.Services.Implementation;

internal class SyncProviderFactory(
    ILogger<SyncProviderFactory> logger,
    IHubContext<AgentHub, IAgentHubClient> agentHubContext,
    IAgentConnectionRegistry agentConnectionRegistry) : ISyncProviderFactory
{
    private readonly ISyncLogger _syncLogger = new SyncLoggerAdapter(logger);

    public ISyncProvider CreateSyncProvider(DataStoreConfiguration configuration, ISyncLogger? logger = null, string[]? tables = null)
    {
        var effectiveLogger = logger ?? _syncLogger;

        var dataStore = configuration.DataStore
            ?? throw new InvalidOperationException("DataStore must be loaded on the configuration.");

        // Agent-backed DataStore: return a proxy that forwards every ISyncProvider call
        // over the SignalR hub to the agent process hosting the real provider.
        if (dataStore.AgentId is not null)
        {
            return new AgentProxySyncProvider(
                dataStore.Id,
                tables,
                agentHubContext,
                agentConnectionRegistry,
                effectiveLogger);
        }

        var configuredTables = configuration.TableConfigurations
            .Where(t => t.SyncMode != DataStoreTableConfigurationSyncMode.NotTracked)
            .ToList();

        List<DataStoreTableConfiguration> orderedTables;

        if (tables is { Length: > 0 })
        {
            var tablesByName = configuredTables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

            orderedTables = new List<DataStoreTableConfiguration>(tables.Length);
            foreach (var tableName in tables)
            {
                if (!tablesByName.TryGetValue(tableName, out var tableConfig))
                    throw new InvalidOperationException($"Table '{tableName}' requested by the client is not present in the endpoint configuration.");

                orderedTables.Add(tableConfig);
            }
        }
        else
        {
            orderedTables = configuredTables.OrderBy(t => t.Sort).ToList();
        }

        return dataStore switch
        {
            SqliteDataStore sqlite => CreateSqliteProvider(sqlite, orderedTables, effectiveLogger),
            SqlServerDataStore { TrackingMode: SqlServerDataStoreTrackingMode.ChangeTracking } sqlServer
                => CreateSqlServerCTProvider(sqlServer, orderedTables, effectiveLogger),
            SqlServerDataStore sqlServer => CreateSqlServerProvider(sqlServer, orderedTables, effectiveLogger),
            PostgreSqlDataStore postgres => CreatePostgresProvider(postgres, orderedTables, effectiveLogger),
            _ => throw new NotSupportedException($"DataStore type '{dataStore.GetType().Name}' is not supported.")
        };
    }

    private ISyncProvider CreateSqliteProvider(SqliteDataStore dataStore, List<DataStoreTableConfiguration> tables, ISyncLogger syncLogger)
    {
        var builder = new SqliteSyncConfigurationBuilder($"Data Source={dataStore.FilePath}");

        foreach (var table in tables)
        {
            builder.Table(table.Name,
                syncDirection: MapSyncDirection(table.SyncMode),
                skipInitialSnapshot: table.SkipInitialSnapshot,
                selectIncrementalQuery: table.SelectIncrementalQuery,
                customSnapshotQuery: table.CustomSnapshotQuery);
        }

        return new SqliteSyncProvider(builder.Build(), ProviderMode.Remote, syncLogger);
    }

    private ISyncProvider CreateSqlServerCTProvider(SqlServerDataStore dataStore, List<DataStoreTableConfiguration> tables, ISyncLogger syncLogger)
    {
        var builder = new SqlServerCTSyncConfigurationBuilder(dataStore.GetResolvedConnectionString());

        foreach (var table in tables)
        {
            builder.Table(table.Name,
                syncDirection: MapSyncDirection(table.SyncMode),
                schema: table.Schema,
                skipInitialSnapshot: table.SkipInitialSnapshot,
                selectIncrementalQuery: table.SelectIncrementalQuery,
                customSnapshotQuery: table.CustomSnapshotQuery);

            var skipCols = DeserializeStringArray(table.SkipColumns);
            if (skipCols.Length > 0)
                builder.SkipColumns(skipCols);

            var skipColsInsertUpdate = DeserializeStringArray(table.SkipColumnsOnInsertOrUpdate);
            if (skipColsInsertUpdate.Length > 0)
                builder.SkipColumnsOnInsertOrUpdate(skipColsInsertUpdate);

            if (table.IdentityInsert != DataStoreTableConfigurationIdentityInsertMode.Auto)
                builder.IdentityInsert((CoreSync.SqlServerCT.IdentityInsertMode)(int)table.IdentityInsert);
        }

        return new SqlServerCTProvider(builder.Build(), ProviderMode.Remote, syncLogger);
    }

    private ISyncProvider CreateSqlServerProvider(SqlServerDataStore dataStore, List<DataStoreTableConfiguration> tables, ISyncLogger syncLogger)
    {
        var builder = new SqlSyncConfigurationBuilder(dataStore.GetResolvedConnectionString());

        foreach (var table in tables)
        {
            builder.Table(table.Name,
                syncDirection: MapSyncDirection(table.SyncMode),
                schema: table.Schema,
                skipInitialSnapshot: table.SkipInitialSnapshot,
                selectIncrementalQuery: table.SelectIncrementalQuery,
                customSnapshotQuery: table.CustomSnapshotQuery);

            var skipCols = DeserializeStringArray(table.SkipColumns);
            if (skipCols.Length > 0)
                builder.SkipColumns(skipCols);

            var skipColsInsertUpdate = DeserializeStringArray(table.SkipColumnsOnInsertOrUpdate);
            if (skipColsInsertUpdate.Length > 0)
                builder.SkipColumnsOnInsertOrUpdate(skipColsInsertUpdate);

            if (table.IdentityInsert != DataStoreTableConfigurationIdentityInsertMode.Auto)
                builder.IdentityInsert((CoreSync.SqlServer.IdentityInsertMode)(int)table.IdentityInsert);

            if (table.ForceReloadInsertedRecords)
                builder.ForceReloadInsertedRecords();
        }

        return new SqlSyncProvider(builder.Build(), ProviderMode.Remote, syncLogger);
    }

    private ISyncProvider CreatePostgresProvider(PostgreSqlDataStore dataStore, List<DataStoreTableConfiguration> tables, ISyncLogger syncLogger)
    {
        var builder = new PostgreSQLSyncConfigurationBuilder(dataStore.GetResolvedConnectionString());

        foreach (var table in tables)
        {
            builder.Table(table.Name,
                syncDirection: MapSyncDirection(table.SyncMode),
                skipInitialSnapshot: table.SkipInitialSnapshot,
                selectIncrementalQuery: table.SelectIncrementalQuery,
                customSnapshotQuery: table.CustomSnapshotQuery);
        }

        return new PostgreSQLSyncProvider(builder.Build(), ProviderMode.Remote, syncLogger);
    }

    private static string[] DeserializeStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static SyncDirection MapSyncDirection(DataStoreTableConfigurationSyncMode syncMode) => syncMode switch
    {
        DataStoreTableConfigurationSyncMode.UploadAndDownload => SyncDirection.UploadAndDownload,
        DataStoreTableConfigurationSyncMode.UploadOnly => SyncDirection.UploadOnly,
        DataStoreTableConfigurationSyncMode.DownloadOnly => SyncDirection.DownloadOnly,
        _ => throw new ArgumentOutOfRangeException(nameof(syncMode), syncMode, "Unsupported sync mode.")
    };
}
