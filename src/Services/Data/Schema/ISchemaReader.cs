namespace CoreSyncServer.Data.Schema;

/// <summary>
/// Reads table and column metadata from a data store.
/// </summary>
public interface ISchemaReader
{
    DataStoreType StoreType { get; }

    Task<IReadOnlyList<TableSchema>> GetTablesAsync(string connectionString, CancellationToken cancellationToken = default);
}
