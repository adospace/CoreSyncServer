namespace CoreSyncServer.Services;

public interface IMaintenanceService
{
    Task RunAsync(CancellationToken cancellationToken = default);
}
