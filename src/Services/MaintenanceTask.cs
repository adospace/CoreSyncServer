namespace CoreSyncServer.Services;

public abstract class MaintenanceTask
{
    public abstract Task ExecuteAsync(IServiceProvider scopedProvider, CancellationToken cancellationToken);
}
