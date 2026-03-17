using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CoreSyncServer.Services.Implementation;

public class MaintenanceService(
    IServiceProvider serviceProvider,
    IEnumerable<MaintenanceTask> tasks,
    ILogger<MaintenanceService> logger) : IMaintenanceService
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        foreach (var task in tasks)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                await task.ExecuteAsync(scope.ServiceProvider, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Maintenance task {Task} failed.", task.GetType().Name);
            }
        }
    }
}
