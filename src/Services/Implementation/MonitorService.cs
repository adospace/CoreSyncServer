using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CoreSyncServer.Services.Implementation;

public class MonitorService(
    IServiceProvider serviceProvider,
    IEnumerable<MonitorTask> tasks,
    ILogger<MonitorService> logger) : IMonitorService
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
                logger.LogError(ex, "Monitor task {Task} failed.", task.GetType().Name);
            }
        }
    }
}
