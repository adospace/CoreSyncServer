using CoreSyncServer.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoreSyncServer.Services;

public class MaintenanceHostedService(
    IMaintenanceService maintenanceService,
    MigrationComplete migrationComplete,
    ILogger<MaintenanceHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await migrationComplete.Task.WaitAsync(stoppingToken);

        logger.LogInformation("Maintenance service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await maintenanceService.RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Maintenance service encountered an error.");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
