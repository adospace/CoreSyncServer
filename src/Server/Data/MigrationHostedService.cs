using Microsoft.EntityFrameworkCore;

namespace CoreSyncServer.Data;

public class MigrationHostedService(IServiceProvider serviceProvider, MigrationComplete migrationComplete, ILogger<MigrationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Applying database migrations...");

        try
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.MigrateAsync(stoppingToken);

            logger.LogInformation("Database migrations applied successfully.");
            migrationComplete.Signal();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while applying database migrations.");
            throw;
        }
    }
}
