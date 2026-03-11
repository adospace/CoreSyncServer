using CoreSyncServer.Data;
using Microsoft.Extensions.Logging;

namespace CoreSyncServer.Services.Implementation;

public class DiagnosticService(
    ApplicationDbContext context,
    INotificationService notificationService,
    ILogger<DiagnosticService> logger) : IDiagnosticService
{
    public async Task CreateAsync(DiagnosticItem item, CancellationToken cancellationToken = default)
    {
        context.DiagnosticItems.Add(item);
        await context.SaveChangesAsync(cancellationToken);

        if (item.Level is LogItemLevel.Error or LogItemLevel.Critical)
        {
            try
            {
                var subject = $"[CoreSync] {item.Level}: {Truncate(item.Message, 80)}";
                await notificationService.SendAsync(subject, item.Message, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send notification for diagnostic item {Id}", item.Id);
            }
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "...");
}
