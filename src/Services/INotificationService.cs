namespace CoreSyncServer.Services;

public interface INotificationService
{
    Task SendAsync(string subject, string message, CancellationToken cancellationToken = default);
}
