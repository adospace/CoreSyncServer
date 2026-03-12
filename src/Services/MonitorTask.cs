using Microsoft.Extensions.DependencyInjection;

namespace CoreSyncServer.Services;

public abstract class MonitorTask
{
    public abstract Task ExecuteAsync(IServiceProvider scopedProvider, CancellationToken cancellationToken);
}
