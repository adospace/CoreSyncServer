using System.Diagnostics;
using CoreSyncServer.Data;
using CoreSyncServer.Services.Implementation;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CoreSyncServer.Tests;

public class SyncSessionLoggerTests
{
    [Fact]
    public async Task Trace_StopsPersistingVerboseEntriesPastTheCap_AndNotesTheTruncation()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var project = new Project { Name = "Test" };
        var session = new SyncSession
        {
            DataStore = new SqliteDataStore { Name = "Store", Project = project, FilePath = "test.db" },
            StartTime = DateTime.UtcNow,
        };
        context.SyncSessions.Add(session);
        await context.SaveChangesAsync();

        var logger = new SyncSessionLogger(NullLogger.Instance, context, session.Id, maxVerboseTraces: 3);
        for (var i = 0; i < 10; i++)
            logger.Trace($"row {i}");
        logger.Info("done");
        await logger.FlushAsync();

        var traces = await context.SyncSessionTraces.OrderBy(t => t.Id).ToListAsync();
        traces.Select(t => t.TraceLevel).Should().Equal(
            TraceLevel.Verbose, TraceLevel.Verbose, TraceLevel.Verbose, TraceLevel.Info, TraceLevel.Info);
        traces[3].Message.Should().Be("Verbose trace truncated after 3 entries.");
        traces[4].Message.Should().Be("done");
    }
}
