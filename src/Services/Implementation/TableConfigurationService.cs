using System.Data.Common;
using CoreSync;
using CoreSyncServer.Data;
using CoreSyncServer.Services;
using Microsoft.EntityFrameworkCore;

namespace CoreSyncServer.Services.Implementation;

public class TableConfigurationService(
    ApplicationDbContext context,
    IEnumerable<ISchemaReader> schemaReaders,
    ITableSorter tableSorter,
    IDiagnosticService diagnosticService) : ITableConfigurationService
{
    private const string ChangeTrackingTablePrefix = "__CORE_SYNC_";

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

        var pendingDiagnostics = new List<DiagnosticItem>();
        var sortOrder = 0;
        foreach (var schemaTable in sortResult.SortedTables)
        {
            sortOrder++;
            var key = (schemaTable.Schema?.ToLowerInvariant(), schemaTable.Name.ToLowerInvariant());

            var messages = new List<string>();
            var pkError = ValidatePrimaryKey(schemaTable);
            if (pkError is not null)
            {
                messages.Add(pkError);
                pendingDiagnostics.Add(BuildDiagnostic(config, $"[{schemaTable.Name}] {pkError}"));
            }

            var hasError = messages.Count > 0;

            if (existing.TryGetValue(key, out var existingTable))
            {
                existingTable.Sort = sortOrder;
                existingTable.InError = hasError;
                existingTable.Message = messages.Count > 0 ? string.Join("; ", messages) : null;
            }
            else
            {
                var newTable = new DataStoreTableConfiguration
                {
                    Name = schemaTable.Name,
                    Schema = schemaTable.Schema,
                    SyncMode = DataStoreTableConfigurationSyncMode.UploadAndDownload,
                    DataStoreConfigurationId = configurationId,
                    Sort = sortOrder,
                    InError = hasError,
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
                table.InError = true;
                table.Message = "Table not found in database schema";
                table.Sort = ++sortOrder;
                pendingDiagnostics.Add(BuildDiagnostic(config, $"[{table.Name}] Table not found in database schema"));
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        foreach (var diagnostic in pendingDiagnostics)
            await diagnosticService.CreateAsync(diagnostic, cancellationToken);

        return TableConfigurationResult.Ok(await LoadTablesAsync(configurationId, cancellationToken));
    }

    public async Task<TableConfigurationResult> UpdateAsync(int configurationId, CancellationToken cancellationToken = default)
    {
        var config = await context.DataStoreConfigurations
            .Include(c => c.DataStore)
            .Include(c => c.TableConfigurations)
            .FirstOrDefaultAsync(c => c.Id == configurationId, cancellationToken);

        if (config is null)
            return TableConfigurationResult.NotFound();

        if (config.TableConfigurations.Count == 0)
            return TableConfigurationResult.Ok([]);

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

        var schemaLookup = sortResult.SortedTables.ToDictionary(
            t => (t.Schema?.ToLowerInvariant(), t.Name.ToLowerInvariant()));

        var sortLookup = sortResult.SortedTables
            .Select((t, i) => (t, i))
            .ToDictionary(
                x => (x.t.Schema?.ToLowerInvariant(), x.t.Name.ToLowerInvariant()),
                x => x.i + 1);

        var pendingDiagnostics = new List<DiagnosticItem>();
        var maxSort = sortLookup.Count;

        foreach (var table in config.TableConfigurations)
        {
            var key = (table.Schema?.ToLowerInvariant(), table.Name.ToLowerInvariant());

            if (!schemaLookup.TryGetValue(key, out var schemaTable))
            {
                // Table no longer exists in the database
                table.InError = true;
                table.Message = "Table not found in database schema";
                table.Sort = ++maxSort;
                pendingDiagnostics.Add(BuildDiagnostic(config, $"[{table.Name}] Table not found in database schema"));
                continue;
            }

            var newSort = sortLookup[key];
            var messages = new List<string>();
            var hasError = false;

            var pkError = ValidatePrimaryKey(schemaTable);
            if (pkError is not null)
            {
                hasError = true;
                messages.Add(pkError);
                pendingDiagnostics.Add(BuildDiagnostic(config, $"[{table.Name}] {pkError}"));
            }

            if (table.Sort != newSort)
            {
                hasError = true;
                messages.Add($"Sort order changed (was {table.Sort}, now {newSort})");
                pendingDiagnostics.Add(BuildDiagnostic(config, $"[{table.Name}] Sort order changed (was {table.Sort}, now {newSort})"));
            }

            table.Sort = newSort;
            table.InError = hasError;
            table.Message = messages.Count > 0 ? string.Join("; ", messages) : null;
            // SyncMode is intentionally not changed — preserve user selection
        }

        await context.SaveChangesAsync(cancellationToken);

        await CreateNewDiagnosticsAsync(configurationId, pendingDiagnostics, cancellationToken);

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

        // Build a rank lookup from the full topological sort
        var rankLookup = new Dictionary<(string?, string), int>(
            sortResult.SortedTables.Select((t, i) => KeyValuePair.Create(
                (t.Schema?.ToLowerInvariant(), t.Name.ToLowerInvariant()), i)));

        // Separate configured tables into found (sortable) and missing
        var found = new List<DataStoreTableConfiguration>();
        var missing = new List<DataStoreTableConfiguration>();
        var pendingDiagnostics = new List<DiagnosticItem>();

        foreach (var table in config.TableConfigurations)
        {
            var key = (table.Schema?.ToLowerInvariant(), table.Name.ToLowerInvariant());
            if (rankLookup.ContainsKey(key))
            {
                found.Add(table);
            }
            else
            {
                missing.Add(table);
                table.Message = "Table not found in database schema";
                pendingDiagnostics.Add(BuildDiagnostic(config, $"[{table.Name}] Table not found in database schema"));
            }
        }

        // Sort found tables by their topological rank, then assign contiguous Sort values
        found.Sort((a, b) =>
        {
            var keyA = (a.Schema?.ToLowerInvariant(), a.Name.ToLowerInvariant());
            var keyB = (b.Schema?.ToLowerInvariant(), b.Name.ToLowerInvariant());
            return rankLookup[keyA].CompareTo(rankLookup[keyB]);
        });

        var sortOrder = 0;
        foreach (var table in found)
            table.Sort = ++sortOrder;

        // Append missing tables after found ones
        foreach (var table in missing)
            table.Sort = ++sortOrder;

        await context.SaveChangesAsync(cancellationToken);

        foreach (var diagnostic in pendingDiagnostics)
            await diagnosticService.CreateAsync(diagnostic, cancellationToken);

        return TableConfigurationResult.Ok(await LoadTablesAsync(configurationId, cancellationToken));
    }

    private async Task CreateNewDiagnosticsAsync(int configurationId, List<DiagnosticItem> pendingDiagnostics, CancellationToken cancellationToken)
    {
        if (pendingDiagnostics.Count == 0)
            return;

        var unresolvedMessages = await context.DiagnosticItems
            .Where(d => d.DataStoreConfigurationId == configurationId && !d.IsResolved)
            .Select(d => d.Message)
            .ToListAsync(cancellationToken);

        var unresolvedSet = new HashSet<string>(unresolvedMessages, StringComparer.OrdinalIgnoreCase);

        foreach (var diagnostic in pendingDiagnostics)
        {
            if (!unresolvedSet.Contains(diagnostic.Message))
                await diagnosticService.CreateAsync(diagnostic, cancellationToken);
        }
    }

    // Sync requires exactly one primary key column per table; the SqlSyncProvider throws at
    // sync time otherwise. Surface both problems here so the table is flagged in the UI instead.
    private static string? ValidatePrimaryKey(TableSchema schemaTable)
    {
        var primaryKeyColumnCount = schemaTable.Columns.Count(c => c.IsPrimaryKey);
        return primaryKeyColumnCount switch
        {
            0 => "Primary key missing (required for sync)",
            > 1 => "Composite primary key not supported (a single-column primary key is required)",
            _ => null
        };
    }

    private static DiagnosticItem BuildDiagnostic(DataStoreConfiguration config, string message) => new()
    {
        Message = message,
        Level = LogItemLevel.Error,
        Timestamp = DateTime.UtcNow,
        ProjectId = config.DataStore?.ProjectId,
        DataStoreId = config.DataStoreId,
        DataStoreConfigurationId = config.Id
    };

    private (ISchemaReader? reader, string? connectionString, string? error) ResolveSchemaReader(DataStore dataStore)
    {
        var connectionString = dataStore switch
        {
            SqliteDataStore sqlite => $"Data Source={sqlite.FilePath}",
            SqlServerDataStore sqlServer => sqlServer.GetResolvedConnectionString(),
            PostgreSqlDataStore pg => pg.GetResolvedConnectionString(),
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

    public async Task<DiscoverTablesResult> DiscoverAsync(int configurationId, CancellationToken cancellationToken = default)
    {
        var config = await context.DataStoreConfigurations
            .Include(c => c.DataStore)
            .FirstOrDefaultAsync(c => c.Id == configurationId, cancellationToken);

        if (config is null)
            return DiscoverTablesResult.NotFound();

        var (reader, connectionString, error) = ResolveSchemaReader(config.DataStore!);
        if (error is not null)
            return DiscoverTablesResult.Failure(error);

        IReadOnlyList<TableSchema> schemaTables;
        try
        {
            schemaTables = await reader!.GetTablesAsync(connectionString!, cancellationToken);
        }
        catch (DbException ex)
        {
            return DiscoverTablesResult.Failure($"Unable to connect to the database: {ex.Message}");
        }

        schemaTables = FilterChangeTrackingTables(schemaTables);

        var tables = schemaTables
            .OrderBy(t => t.Schema).ThenBy(t => t.Name)
            .Select(t => new DiscoveredTable(t.Name, t.Schema))
            .ToList();

        return DiscoverTablesResult.Ok(tables);
    }

    private async Task<IReadOnlyList<DataStoreTableConfiguration>> LoadTablesAsync(int configurationId, CancellationToken cancellationToken) =>
        await context.DataStoreTableConfigurations
            .Where(t => t.DataStoreConfigurationId == configurationId)
            .OrderBy(t => t.Sort).ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
}
