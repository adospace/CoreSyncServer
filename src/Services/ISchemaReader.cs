using CoreSyncServer.Data;
using CoreSyncServer.Services.Implementation;

namespace CoreSyncServer.Services;

/// <summary>
/// Reads table and column metadata from a data store.
/// </summary>
public interface ISchemaReader
{
    DataStoreType StoreType { get; }

    Task<IReadOnlyList<TableSchema>> GetTablesAsync(string connectionString, CancellationToken cancellationToken = default);
}
