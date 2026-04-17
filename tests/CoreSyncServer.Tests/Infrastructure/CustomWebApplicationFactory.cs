using CoreSyncServer.Data;
using CoreSyncServer.Server.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace CoreSyncServer.Tests.Infrastructure;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // AddCoreSyncServer captures the connection string at registration time
        // from appsettings.json, so a config-level override runs too late.
        // We swap the DbContextOptions after registration to redirect the app
        // to the ephemeral Testcontainers instance, then apply migrations
        // synchronously so the server is ready before tests hit it.
        builder.ConfigureTestServices(services =>
        {
            RemoveAll(services, typeof(DbContextOptions<ApplicationDbContext>));
            RemoveAll(services, typeof(DbContextOptions<CoreSyncServerDbContext>));
            RemoveAll(services, typeof(DbContextOptions));

            services.AddDbContext<ApplicationDbContext, CoreSyncServerDbContext>(options =>
                options.UseNpgsql(
                    _postgres.GetConnectionString(),
                    b => b.MigrationsAssembly("CoreSyncServer")));

            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.Migrate();
        });
    }

    private static void RemoveAll(IServiceCollection services, Type serviceType)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == serviceType)
            {
                services.RemoveAt(i);
            }
        }
    }
}
