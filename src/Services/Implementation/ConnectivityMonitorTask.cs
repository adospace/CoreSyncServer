using System.Collections.Concurrent;
using System.Data.Common;
using CoreSyncServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CoreSyncServer.Services.Implementation;

public class ConnectivityMonitorTask(IEnumerable<ISchemaReader> schemaReaders, ILogger<ConnectivityMonitorTask> logger) : MonitorTask
{
    private readonly ConcurrentDictionary<int, bool> _downStates = new();

    public override async Task ExecuteAsync(IServiceProvider scopedProvider, CancellationToken cancellationToken)
    {
        var context = scopedProvider.GetRequiredService<ApplicationDbContext>();
        var diagnosticService = scopedProvider.GetRequiredService<IDiagnosticService>();

        var dataStores = await context.DataStores
            .Where(d => d.IsMonitorEnabled)
            .ToListAsync(cancellationToken);

        logger.LogInformation("Connectivity check: {Count} data store(s).", dataStores.Count);

        var tasks = dataStores.Select(ds => CheckConnectionAsync(ds, diagnosticService, cancellationToken));
        await Task.WhenAll(tasks);
    }

    private async Task CheckConnectionAsync(DataStore dataStore, IDiagnosticService diagnosticService, CancellationToken cancellationToken)
    {
        var connectionString = dataStore switch
        {
            SqliteDataStore sqlite => $"Data Source={sqlite.FilePath}",
            SqlServerDataStore sqlServer => sqlServer.ConnectionString,
            PostgreSqlDataStore pg => pg.ConnectionString,
            _ => null
        };

        if (connectionString is null)
        {
            logger.LogWarning("Connectivity check: unsupported type for '{Name}' (Id={Id}).", dataStore.Name, dataStore.Id);
            return;
        }

        var reader = schemaReaders.FirstOrDefault(r => r.StoreType == dataStore.Type);
        if (reader is null)
        {
            logger.LogWarning("Connectivity check: no schema reader for {Type} '{Name}' (Id={Id}).", dataStore.Type, dataStore.Name, dataStore.Id);
            return;
        }

        try
        {
            await reader.GetTablesAsync(connectionString, cancellationToken);

            if (_downStates.TryRemove(dataStore.Id, out _))
            {
                logger.LogInformation("Connectivity check: data store '{Name}' (Id={Id}) is back online.", dataStore.Name, dataStore.Id);

                await diagnosticService.CreateAsync(new DiagnosticItem
                {
                    Message = $"Data store '{dataStore.Name}' is back online.",
                    Level = LogItemLevel.Information,
                    Timestamp = DateTime.UtcNow,
                    DataStoreId = dataStore.Id,
                    ProjectId = dataStore.ProjectId
                }, cancellationToken);
            }
            else
            {
                logger.LogDebug("Connectivity check: data store '{Name}' (Id={Id}) is reachable.", dataStore.Name, dataStore.Id);
            }
        }
        catch (Exception ex) when (ex is DbException or TimeoutException)
        {
            var wasAlreadyDown = !_downStates.TryAdd(dataStore.Id, true);
            if (wasAlreadyDown)
            {
                logger.LogDebug("Connectivity check: data store '{Name}' (Id={Id}) still unreachable.", dataStore.Name, dataStore.Id);
                return;
            }

            logger.LogWarning(ex, "Connectivity check: data store '{Name}' (Id={Id}) is unreachable.", dataStore.Name, dataStore.Id);

            await diagnosticService.CreateAsync(new DiagnosticItem
            {
                Message = $"Data store '{dataStore.Name}' is unreachable: {ex.Message}",
                Level = LogItemLevel.Error,
                Timestamp = DateTime.UtcNow,
                DataStoreId = dataStore.Id,
                ProjectId = dataStore.ProjectId
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Connectivity check: unexpected error for data store '{Name}' (Id={Id}).", dataStore.Name, dataStore.Id);
        }
    }
}
