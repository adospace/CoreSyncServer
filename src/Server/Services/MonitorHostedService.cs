using CoreSyncServer.Data;
using CoreSyncServer.Services;
using Microsoft.Extensions.Options;

namespace CoreSyncServer.Server.Services;

public class MonitorHostedService(
    IMonitorService monitorService,
    MigrationComplete migrationComplete,
    IOptions<MonitorSettings> options,
    ILogger<MonitorHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await migrationComplete.Task.WaitAsync(stoppingToken);

        var intervalMinutes = options.Value.IntervalMinutes;
        logger.LogInformation("Monitor service started. Interval: {Interval} minute(s).", intervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await monitorService.RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Monitor service encountered an error.");
            }

            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }
}
