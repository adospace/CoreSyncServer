namespace CoreSyncServer.Services;

public interface IMonitorService
{
    Task RunAsync(CancellationToken cancellationToken = default);
}
