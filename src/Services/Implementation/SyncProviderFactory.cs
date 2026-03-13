using CoreSync;
using CoreSync.PostgreSQL;
using CoreSync.Sqlite;
using CoreSync.SqlServer;
using CoreSync.SqlServerCT;
using CoreSyncServer.Data;

namespace CoreSyncServer.Services.Implementation;

internal class SyncProviderFactory : ISyncProviderFactory
{
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

    private static ISyncProvider CreateSqliteProvider(SqliteDataStore dataStore, List<DataStoreTableConfiguration> tables)
    {
        var builder = new SqliteSyncConfigurationBuilder($"Data Source={dataStore.FilePath}");

        foreach (var table in tables)
        {
            builder.Table(table.Name, syncDirection: MapSyncDirection(table.SyncMode));
        }

        return new SqliteSyncProvider(builder.Build(), ProviderMode.Remote);
    }

    private static ISyncProvider CreateSqlServerCTProvider(SqlServerDataStore dataStore, List<DataStoreTableConfiguration> tables)
    {
        var builder = new SqlServerCTSyncConfigurationBuilder(dataStore.ConnectionString);

        foreach (var table in tables)
        {
            builder.Table(table.Name, syncDirection: MapSyncDirection(table.SyncMode), schema: table.Schema);
        }

        return new SqlServerCTProvider(builder.Build(), ProviderMode.Remote);
    }

    private static ISyncProvider CreateSqlServerProvider(SqlServerDataStore dataStore, List<DataStoreTableConfiguration> tables)
    {
        var builder = new SqlSyncConfigurationBuilder(dataStore.ConnectionString);

        foreach (var table in tables)
        {
            builder.Table(table.Name, syncDirection: MapSyncDirection(table.SyncMode), schema: table.Schema);
        }

        return new SqlSyncProvider(builder.Build(), ProviderMode.Remote);
    }

    private static ISyncProvider CreatePostgresProvider(PostgreSqlDataStore dataStore, List<DataStoreTableConfiguration> tables)
    {
        var builder = new PostgreSQLSyncConfigurationBuilder(dataStore.ConnectionString);

        foreach (var table in tables)
        {
            builder.Table(table.Name, syncDirection: MapSyncDirection(table.SyncMode));
        }

        return new PostgreSQLSyncProvider(builder.Build(), ProviderMode.Remote);
    }

    private static SyncDirection MapSyncDirection(DataStoreTableConfigurationSyncMode syncMode) => syncMode switch
    {
        DataStoreTableConfigurationSyncMode.UploadAndDownload => SyncDirection.UploadAndDownload,
        DataStoreTableConfigurationSyncMode.UploadOnly => SyncDirection.UploadOnly,
        DataStoreTableConfigurationSyncMode.DownloadOnly => SyncDirection.DownloadOnly,
        _ => throw new ArgumentOutOfRangeException(nameof(syncMode), syncMode, "Unsupported sync mode.")
    };
}
