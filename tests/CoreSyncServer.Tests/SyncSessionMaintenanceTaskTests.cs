using System.Diagnostics;
using CoreSyncServer.Data;
using CoreSyncServer.Services;
using CoreSyncServer.Services.Implementation;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CoreSyncServer.Tests;

public class SyncSessionMaintenanceTaskTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private ServiceProvider _services = null!;
    private int _maxVerboseTraceRows = 200_000;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(_connection));
        services.Configure<MaintenanceSettings>(s =>
        {
            s.SyncTraceRetentionHours = 24;
            s.SyncSessionRetentionDays = 7;
            s.MaxVerboseTraceRows = _maxVerboseTraceRows;
        });
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ExecuteAsync_RemovesVerboseTracesOfOldSessionsAcrossBatches_AndKeepsTheRest()
    {
        var oldSession = await SeedSessionAsync(
            startedAgo: TimeSpan.FromDays(2),
            verboseTraces: SyncSessionMaintenanceTask.TraceDeleteBatchSize * 2 + 17,
            infoTraces: 3);
        var recentSession = await SeedSessionAsync(
            startedAgo: TimeSpan.FromHours(1),
            verboseTraces: 40,
            infoTraces: 2);

        await RunTaskAsync();

        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var oldTraces = await context.SyncSessionTraces.Where(t => t.SyncSessionId == oldSession).ToListAsync();
        oldTraces.Should().HaveCount(3);
        oldTraces.Should().OnlyContain(t => t.TraceLevel == TraceLevel.Info);

        var recentTraces = await context.SyncSessionTraces.Where(t => t.SyncSessionId == recentSession).ToListAsync();
        recentTraces.Should().HaveCount(42);
    }

    [Fact]
    public async Task ExecuteAsync_RemovesCompletedSessionsPastRetention_WithTheirTraces()
    {
        var expired = await SeedSessionAsync(TimeSpan.FromDays(10), verboseTraces: 5, infoTraces: 5, status: SyncSessionStatus.Completed);
        var expiredButFailed = await SeedSessionAsync(TimeSpan.FromDays(10), verboseTraces: 5, infoTraces: 5, status: SyncSessionStatus.Error);
        var recent = await SeedSessionAsync(TimeSpan.FromDays(2), verboseTraces: 5, infoTraces: 5, status: SyncSessionStatus.Completed);

        await RunTaskAsync();

        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var remaining = await context.SyncSessions.Select(s => s.Id).ToListAsync();
        remaining.Should().BeEquivalentTo([expiredButFailed, recent]);

        (await context.SyncSessionTraces.CountAsync(t => t.SyncSessionId == expired)).Should().Be(0);
        (await context.SyncSessionTraces.CountAsync(t => t.SyncSessionId == expiredButFailed)).Should().Be(5);
        (await context.SyncSessionTraces.CountAsync(t => t.SyncSessionId == recent)).Should().Be(5);
    }

    [Fact]
    public async Task ExecuteAsync_DeletesOldestVerboseTracesOverTheCeiling_EvenFromRecentSessions()
    {
        _maxVerboseTraceRows = 30;
        var older = await SeedSessionAsync(TimeSpan.FromHours(3), verboseTraces: 25, infoTraces: 2);
        var newer = await SeedSessionAsync(TimeSpan.FromHours(1), verboseTraces: 25, infoTraces: 2);

        await RunTaskAsync();

        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        (await context.SyncSessionTraces.CountAsync(t => t.TraceLevel == TraceLevel.Verbose)).Should().Be(30);
        (await context.SyncSessionTraces.CountAsync(t => t.SyncSessionId == older && t.TraceLevel == TraceLevel.Verbose)).Should().Be(5);
        (await context.SyncSessionTraces.CountAsync(t => t.SyncSessionId == newer && t.TraceLevel == TraceLevel.Verbose)).Should().Be(25);
        (await context.SyncSessionTraces.CountAsync(t => t.TraceLevel == TraceLevel.Info)).Should().Be(4);
    }

    private async Task RunTaskAsync()
    {
        using var scope = _services.CreateScope();
        var task = new SyncSessionMaintenanceTask(NullLogger<SyncSessionMaintenanceTask>.Instance);
        await task.ExecuteAsync(scope.ServiceProvider, CancellationToken.None);
    }

    private async Task<int> SeedSessionAsync(TimeSpan startedAgo, int verboseTraces, int infoTraces, SyncSessionStatus status = SyncSessionStatus.Completed)
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var project = await context.Projects.FirstOrDefaultAsync();
        if (project is null)
        {
            project = new Project { Name = "Test" };
            context.Projects.Add(project);
        }

        var dataStore = new SqliteDataStore { Name = "Store", Project = project, FilePath = "test.db" };
        var start = DateTime.UtcNow - startedAgo;
        var session = new SyncSession
        {
            DataStore = dataStore,
            StartTime = start,
            EndTime = start.AddMinutes(1),
            Status = status,
        };
        context.SyncSessions.Add(session);

        for (var i = 0; i < verboseTraces + infoTraces; i++)
        {
            session.Traces.Add(new SyncSessionTrace
            {
                Message = $"trace {i}",
                TimeStamp = start,
                TraceLevel = i < verboseTraces ? TraceLevel.Verbose : TraceLevel.Info,
            });
        }

        await context.SaveChangesAsync();
        return session.Id;
    }
}
