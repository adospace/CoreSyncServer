using CoreSyncServer.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CoreSyncServer.Tests.Infrastructure;

public class InMemoryWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private SqliteConnection? _connection;

    public Task InitializeAsync()
    {
        // Keep-alive connection to preserve the in-memory database
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        return Task.CompletedTask;
    }

    public new async Task DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.DisposeAsync();
        }
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove ALL EF Core registrations (DbContext, options, and provider-specific services)
            var efDescriptors = services
                .Where(d =>
                    d.ServiceType.FullName?.Contains("EntityFrameworkCore") == true ||
                    d.ServiceType.FullName?.Contains("EntityFramework") == true ||
                    d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(ApplicationDbContext) ||
                    d.ImplementationType?.Name == "MigrationHostedService")
                .ToList();

            foreach (var descriptor in efDescriptors)
                services.Remove(descriptor);

            // Re-register DbContext with SQLite in-memory
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlite(_connection!);
            });

            // Create schema and seed data
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.EnsureCreated();
        });
    }
}
