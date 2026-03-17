using CoreSyncServer.Data;
using CoreSyncServer.Services;
using Microsoft.Extensions.Options;

namespace CoreSyncServer.Server.Services;

public class MaintenanceHostedService(
    IMaintenanceService maintenanceService,
    MigrationComplete migrationComplete,
    IOptions<MaintenanceSettings> options,
    ILogger<MaintenanceHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await migrationComplete.Task.WaitAsync(stoppingToken);

        var intervalMinutes = options.Value.IntervalMinutes;
        logger.LogInformation("Maintenance service started. Interval: {Interval} minute(s).", intervalMinutes);

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

            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }
}
