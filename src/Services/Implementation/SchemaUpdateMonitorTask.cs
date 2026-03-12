using CoreSyncServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CoreSyncServer.Services.Implementation;

public class SchemaUpdateMonitorTask(ILogger<SchemaUpdateMonitorTask> logger) : MonitorTask
{
    public override async Task ExecuteAsync(IServiceProvider scopedProvider, CancellationToken cancellationToken)
    {
        var context = scopedProvider.GetRequiredService<ApplicationDbContext>();
        var tableConfigService = scopedProvider.GetRequiredService<ITableConfigurationService>();

        var configurationIds = await context.DataStoreConfigurations
            .Where(c => c.DataStore!.IsMonitorEnabled && c.TableConfigurations.Any())
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        logger.LogInformation("Schema update check: {Count} configuration(s).", configurationIds.Count);

        foreach (var configId in configurationIds)
        {
            try
            {
                var result = await tableConfigService.UpdateAsync(configId, cancellationToken);
                if (!result.Success)
                    logger.LogWarning("Schema update for configuration {Id}: {Error}", configId, result.Error);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Schema update failed for configuration {Id}.", configId);
            }
        }
    }
}
