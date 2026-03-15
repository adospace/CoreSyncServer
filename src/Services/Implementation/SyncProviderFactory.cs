using System.Text.Json;
using CoreSync;
using CoreSync.PostgreSQL;
using CoreSync.Sqlite;
using CoreSync.SqlServer;
using CoreSync.SqlServerCT;
using CoreSyncServer.Data;
using Microsoft.Extensions.Logging;

namespace CoreSyncServer.Services.Implementation;

internal class SyncProviderFactory(ILogger<SyncProviderFactory> logger) : ISyncProviderFactory
{
    private readonly ISyncLogger _syncLogger = new SyncLoggerAdapter(logger);

    public ISyncProvider CreateSyncProvider(DataStoreConfiguration configuration)
    {
        var dataStore = configuration.DataStore
            ?? throw new InvalidOperationException("DataStore must be loaded on the configuration.");

        var tables = configuration.TableConfigurations
            .Where(t => t.SyncMode != DataStoreTableConfigurationSyncMode.NotTracked)
            .OrderBy(t => t.Sort)
            .ToList();

        return dataStore switch
        {
            SqliteDataStore sqlite => CreateSqliteProvider(sqlite, tables),
            SqlServerDataStore { TrackingMode: SqlServerDataStoreTrackingMode.ChangeTracking } sqlServer
                => CreateSqlServerCTProvider(sqlServer, tables),
            SqlServerDataStore sqlServer => CreateSqlServerProvider(sqlServer, tables),
            PostgreSqlDataStore postgres => CreatePostgresProvider(postgres, tables),
            _ => throw new NotSupportedException($"DataStore type '{dataStore.GetType().Name}' is not supported.")
        };
    }

    private ISyncProvider CreateSqliteProvider(SqliteDataStore dataStore, List<DataStoreTableConfiguration> tables)
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

        return new SqliteSyncProvider(builder.Build(), ProviderMode.Remote, _syncLogger);
    }

    private ISyncProvider CreateSqlServerCTProvider(SqlServerDataStore dataStore, List<DataStoreTableConfiguration> tables)
    {
        var builder = new SqlServerCTSyncConfigurationBuilder(dataStore.ConnectionString);

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

        return new SqlServerCTProvider(builder.Build(), ProviderMode.Remote, _syncLogger);
    }

    private ISyncProvider CreateSqlServerProvider(SqlServerDataStore dataStore, List<DataStoreTableConfiguration> tables)
    {
        var builder = new SqlSyncConfigurationBuilder(dataStore.ConnectionString);

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

        return new SqlSyncProvider(builder.Build(), ProviderMode.Remote, _syncLogger);
    }

    private ISyncProvider CreatePostgresProvider(PostgreSqlDataStore dataStore, List<DataStoreTableConfiguration> tables)
    {
        var builder = new PostgreSQLSyncConfigurationBuilder(dataStore.ConnectionString);

        foreach (var table in tables)
        {
            builder.Table(table.Name,
                syncDirection: MapSyncDirection(table.SyncMode),
                skipInitialSnapshot: table.SkipInitialSnapshot,
                selectIncrementalQuery: table.SelectIncrementalQuery,
                customSnapshotQuery: table.CustomSnapshotQuery);
        }

        return new PostgreSQLSyncProvider(builder.Build(), ProviderMode.Remote, _syncLogger);
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
