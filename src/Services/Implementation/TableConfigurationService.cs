using System.Data.Common;
using CoreSync;
using CoreSyncServer.Data;
using CoreSyncServer.Services;
using Microsoft.EntityFrameworkCore;

namespace CoreSyncServer.Services.Implementation;

public class TableConfigurationService(
    ApplicationDbContext context,
    IEnumerable<ISchemaReader> schemaReaders,
    ITableSorter tableSorter) : ITableConfigurationService
{
    private const string ChangeTrackingTablePrefix = "__CORESYNC";

    public async Task<TableConfigurationResult> ScaffoldAsync(int configurationId, CancellationToken cancellationToken = default)
    {
        var config = await context.DataStoreConfigurations
            .Include(c => c.DataStore)
            .Include(c => c.TableConfigurations)
            .FirstOrDefaultAsync(c => c.Id == configurationId, cancellationToken);

        if (config is null)
            return TableConfigurationResult.NotFound();

        var (reader, connectionString, error) = ResolveSchemaReader(config.DataStore!);
        if (error is not null)
            return TableConfigurationResult.Failure(error);

        IReadOnlyList<TableSchema> schemaTables;
        try
        {
            schemaTables = await reader!.GetTablesAsync(connectionString!, cancellationToken);
        }
        catch (DbException ex)
        {
            return TableConfigurationResult.Failure($"Unable to connect to the database: {ex.Message}");
        }

        schemaTables = FilterChangeTrackingTables(schemaTables);
        var sortResult = tableSorter.Sort(schemaTables);

        var existing = config.TableConfigurations.ToDictionary(
            t => (t.Schema?.ToLowerInvariant(), t.Name.ToLowerInvariant()));

        var sortOrder = 0;
        foreach (var schemaTable in sortResult.SortedTables)
        {
            sortOrder++;
            var key = (schemaTable.Schema?.ToLowerInvariant(), schemaTable.Name.ToLowerInvariant());
            var hasPrimaryKey = schemaTable.Columns.Any(c => c.IsPrimaryKey);

            var messages = new List<string>();
            if (!hasPrimaryKey)
            {
                messages.Add("Primary key missing (required for sync)");
                CreateDiagnostic(config, $"[{schemaTable.Name}] Primary key missing (required for sync)");
            }

            if (existing.TryGetValue(key, out var existingTable))
            {
                existingTable.Sort = sortOrder;
                existingTable.Message = messages.Count > 0 ? string.Join("; ", messages) : null;
            }
            else
            {
                var newTable = new DataStoreTableConfiguration
                {
                    Name = schemaTable.Name,
                    Schema = schemaTable.Schema,
                    SyncDirection = SyncDirection.UploadAndDownload,
                    DataStoreConfigurationId = configurationId,
                    Sort = sortOrder,
                    Message = messages.Count > 0 ? string.Join("; ", messages) : null
                };
                context.DataStoreTableConfigurations.Add(newTable);
                existing[key] = newTable;
            }
        }

        foreach (var table in config.TableConfigurations)
        {
            var found = sortResult.SortedTables.Any(s =>
                string.Equals(s.Schema, table.Schema, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(s.Name, table.Name, StringComparison.OrdinalIgnoreCase));
            if (!found)
            {
                table.Message = "Table not found in database schema";
                table.Sort = ++sortOrder;
                CreateDiagnostic(config, $"[{table.Name}] Table not found in database schema");
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        return TableConfigurationResult.Ok(await LoadTablesAsync(configurationId, cancellationToken));
    }

    public async Task<TableConfigurationResult> SortAsync(int configurationId, CancellationToken cancellationToken = default)
    {
        var config = await context.DataStoreConfigurations
            .Include(c => c.DataStore)
            .Include(c => c.TableConfigurations)
            .FirstOrDefaultAsync(c => c.Id == configurationId, cancellationToken);

        if (config is null)
            return TableConfigurationResult.NotFound();

        var (reader, connectionString, error) = ResolveSchemaReader(config.DataStore!);
        if (error is not null)
            return TableConfigurationResult.Failure(error);

        IReadOnlyList<TableSchema> schemaTables;
        try
        {
            schemaTables = await reader!.GetTablesAsync(connectionString!, cancellationToken);
        }
        catch (DbException ex)
        {
            return TableConfigurationResult.Failure($"Unable to connect to the database: {ex.Message}");
        }

        schemaTables = FilterChangeTrackingTables(schemaTables);
        var sortResult = tableSorter.Sort(schemaTables);

        var sortLookup = new Dictionary<(string?, string), int>(
            sortResult.SortedTables.Select((t, i) => KeyValuePair.Create(
                (t.Schema?.ToLowerInvariant(), t.Name.ToLowerInvariant()), i + 1)));

        var maxSort = sortLookup.Count;
        foreach (var table in config.TableConfigurations)
        {
            var key = (table.Schema?.ToLowerInvariant(), table.Name.ToLowerInvariant());
            if (sortLookup.TryGetValue(key, out var order))
            {
                table.Sort = order;
            }
            else
            {
                table.Sort = ++maxSort;
                table.Message = "Table not found in database schema";
                CreateDiagnostic(config, $"[{table.Name}] Table not found in database schema");
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        return TableConfigurationResult.Ok(await LoadTablesAsync(configurationId, cancellationToken));
    }

    private void CreateDiagnostic(DataStoreConfiguration config, string message)
    {
        context.DiagnosticItems.Add(new DiagnosticItem
        {
            Message = message,
            Level = LogItemLevel.Error,
            Timestamp = DateTime.UtcNow,
            ProjectId = config.DataStore?.ProjectId,
            DataStoreId = config.DataStoreId,
            DataStoreConfigurationId = config.Id
        });
    }

    private (ISchemaReader? reader, string? connectionString, string? error) ResolveSchemaReader(DataStore dataStore)
    {
        var connectionString = dataStore switch
        {
            SqliteDataStore sqlite => $"Data Source={sqlite.FilePath}",
            SqlServerDataStore sqlServer => sqlServer.ConnectionString,
            PostgreSqlDataStore pg => pg.ConnectionString,
            _ => null
        };

        if (connectionString is null)
            return (null, null, "Unsupported data store type.");

        var reader = schemaReaders.FirstOrDefault(r => r.StoreType == dataStore.Type);
        if (reader is null)
            return (null, null, $"No schema reader available for {dataStore.Type}.");

        return (reader, connectionString, null);
    }

    private static IReadOnlyList<TableSchema> FilterChangeTrackingTables(IReadOnlyList<TableSchema> tables) =>
        tables.Where(t => !t.Name.StartsWith(ChangeTrackingTablePrefix, StringComparison.OrdinalIgnoreCase)).ToList();

    private async Task<IReadOnlyList<DataStoreTableConfiguration>> LoadTablesAsync(int configurationId, CancellationToken cancellationToken) =>
        await context.DataStoreTableConfigurations
            .Where(t => t.DataStoreConfigurationId == configurationId)
            .OrderBy(t => t.Sort).ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
}
