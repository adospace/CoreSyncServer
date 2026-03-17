using CoreSyncServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoreSyncServer.Services.Implementation;

public class DiagnosticMaintenanceTask(ILogger<DiagnosticMaintenanceTask> logger) : MaintenanceTask
{
    public override async Task ExecuteAsync(IServiceProvider scopedProvider, CancellationToken cancellationToken)
    {
        var context = scopedProvider.GetRequiredService<ApplicationDbContext>();
        var settings = scopedProvider.GetRequiredService<IOptions<MaintenanceSettings>>().Value;

        var cutoff = DateTime.UtcNow.AddHours(-settings.DiagnosticRetentionHours);

        var count = await context.DiagnosticItems
            .Where(d => d.IsResolved && d.Timestamp < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (count > 0)
            logger.LogInformation("Diagnostic maintenance: removed {Count} resolved item(s) older than {Hours}h.", count, settings.DiagnosticRetentionHours);
    }
}
