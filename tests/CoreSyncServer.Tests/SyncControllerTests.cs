using CoreSync;
using CoreSync.Http;
using CoreSync.Http.Client;
using CoreSync.Sqlite;
using CoreSyncServer.Data;
using CoreSyncServer.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace CoreSyncServer.Tests;

public class SyncControllerTests : IClassFixture<InMemoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly InMemoryWebApplicationFactory _factory;
    private readonly string _localDbPath;
    private readonly string _remoteDbPath;
    private Guid _endpointId;

    public SyncControllerTests(InMemoryWebApplicationFactory factory)
    {
        _factory = factory;
        _localDbPath = Path.Combine(Path.GetTempPath(), $"sync_local_{Guid.NewGuid()}.db");
        _remoteDbPath = Path.Combine(Path.GetTempPath(), $"sync_remote_{Guid.NewGuid()}.db");
    }

    public async Task InitializeAsync()
    {
        // Create identical schemas on both local and remote SQLite databases
        CreateSqliteDatabase(_localDbPath);
        CreateSqliteDatabase(_remoteDbPath);

        // Provision the remote database for sync tracking
        var remoteProvider = CreateSqliteSyncProvider(_remoteDbPath, ProviderMode.Remote);
        await remoteProvider.ApplyProvisionAsync();

        // Provision the local database for sync tracking
        var localProvider = CreateSqliteSyncProvider(_localDbPath, ProviderMode.Local);
        await localProvider.ApplyProvisionAsync();

        // Seed test data: register the remote database as a DataStore + Configuration + Endpoint
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var project = new Project { Name = "Test Project", CreatedDate = DateTime.UtcNow, IsEnabled = true };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var dataStore = new SqliteDataStore
        {
            Name = "Test Remote DB",
            FilePath = _remoteDbPath,
            ProjectId = project.Id,
            Type = DataStoreType.SQLite
        };
        db.DataStores.Add(dataStore);
        await db.SaveChangesAsync();

        var config = new DataStoreConfiguration
        {
            Name = "Test Config",
            DataStoreId = dataStore.Id,
        };
        db.DataStoreConfigurations.Add(config);
        await db.SaveChangesAsync();

        config.TableConfigurations.Add(new DataStoreTableConfiguration
        {
            Name = "Items",
            SyncMode = DataStoreTableConfigurationSyncMode.UploadAndDownload,
            Sort = 0,
            DataStoreConfigurationId = config.Id
        });
        await db.SaveChangesAsync();

        var endpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            Name = "Test Endpoint",
            DataStoreConfigurationId = config.Id,
            IsPublished = true
        };
        db.Endpoints.Add(endpoint);
        await db.SaveChangesAsync();

        _endpointId = endpoint.Id;
    }

    public Task DisposeAsync()
    {
        // Clean up temp databases
        SqliteConnection.ClearAllPools();

        if (File.Exists(_localDbPath)) File.Delete(_localDbPath);
        if (File.Exists(_remoteDbPath)) File.Delete(_remoteDbPath);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Sync_LocalToRemote_ItemsAreSynchronized()
    {
        // Arrange: insert a row in the local database
        using (var conn = new SqliteConnection($"Data Source={_localDbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Items (Id, Name, Value) VALUES ('11111111-1111-1111-1111-111111111111', 'TestItem', 42)";
            await cmd.ExecuteNonQueryAsync();
        }

        var remoteSyncClient = CreateRemoteSyncClient();
        var localProvider = CreateSqliteSyncProvider(_localDbPath, ProviderMode.Local);

        // Act: synchronize
        var syncAgent = new SyncAgent(localProvider, remoteSyncClient);
        await syncAgent.SynchronizeAsync();

        // Assert: the row should now exist in the remote database
        using (var conn = new SqliteConnection($"Data Source={_remoteDbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Name, Value FROM Items WHERE Id = '11111111-1111-1111-1111-111111111111'";
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read().Should().BeTrue("the item should have been synced to the remote database");
            reader.GetString(0).Should().Be("TestItem");
            reader.GetInt32(1).Should().Be(42);
        }
    }

    [Fact]
    public async Task Sync_RemoteToLocal_ItemsAreSynchronized()
    {
        // Arrange: insert a row in the remote database
        using (var conn = new SqliteConnection($"Data Source={_remoteDbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Items (Id, Name, Value) VALUES ('22222222-2222-2222-2222-222222222222', 'RemoteItem', 99)";
            await cmd.ExecuteNonQueryAsync();
        }

        var remoteSyncClient = CreateRemoteSyncClient();
        var localProvider = CreateSqliteSyncProvider(_localDbPath, ProviderMode.Local);

        // Act: synchronize
        var syncAgent = new SyncAgent(localProvider, remoteSyncClient);
        await syncAgent.SynchronizeAsync();

        // Assert: the row should now exist in the local database
        using (var conn = new SqliteConnection($"Data Source={_localDbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Name, Value FROM Items WHERE Id = '22222222-2222-2222-2222-222222222222'";
            using var reader = await cmd.ExecuteReaderAsync();
            reader.Read().Should().BeTrue("the item should have been synced to the local database");
            reader.GetString(0).Should().Be("RemoteItem");
            reader.GetInt32(1).Should().Be(99);
        }
    }

    [Fact]
    public async Task Sync_BidirectionalSync_BothSidesReceiveChanges()
    {
        // Arrange: insert different rows in each database
        using (var conn = new SqliteConnection($"Data Source={_localDbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Items (Id, Name, Value) VALUES ('33333333-3333-3333-3333-333333333333', 'LocalOnly', 10)";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var conn = new SqliteConnection($"Data Source={_remoteDbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Items (Id, Name, Value) VALUES ('44444444-4444-4444-4444-444444444444', 'RemoteOnly', 20)";
            await cmd.ExecuteNonQueryAsync();
        }

        var remoteSyncClient = CreateRemoteSyncClient();
        var localProvider = CreateSqliteSyncProvider(_localDbPath, ProviderMode.Local);

        // Act
        var syncAgent = new SyncAgent(localProvider, remoteSyncClient);
        await syncAgent.SynchronizeAsync();

        // Assert: both items should exist in both databases
        using (var conn = new SqliteConnection($"Data Source={_remoteDbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Items WHERE Id IN ('33333333-3333-3333-3333-333333333333', '44444444-4444-4444-4444-444444444444')";
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            count.Should().Be(2, "both items should exist in the remote database");
        }

        using (var conn = new SqliteConnection($"Data Source={_localDbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Items WHERE Id IN ('33333333-3333-3333-3333-333333333333', '44444444-4444-4444-4444-444444444444')";
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            count.Should().Be(2, "both items should exist in the local database");
        }
    }

    [Fact]
    public async Task Sync_InvalidEndpoint_ReturnsNotFound()
    {
        var httpClient = _factory.CreateClient();
        var nonExistentEndpointId = Guid.NewGuid();

        var response = await httpClient.GetAsync($"api/sync/{nonExistentEndpointId}/store-id");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    // The sync API answers the failures it recognises with the status code it always used plus the
    // X-CoreSync-Error / X-CoreSync-Trace headers and a SyncErrorDetails body, so a current client can
    // tell the user what happened while an older one keeps seeing the status it expects.

    [Fact]
    public async Task Sync_InvalidEndpoint_CarriesEndpointNotFoundSignal()
    {
        var httpClient = _factory.CreateClient();

        var response = await httpClient.GetAsync($"api/sync/{Guid.NewGuid()}/store-id");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.GetValues(SyncHttpHeaders.ErrorCode).Should().ContainSingle().Which.Should().Be(SyncHttpErrorCodes.EndpointNotFound);
        response.Headers.GetValues(SyncHttpHeaders.TraceId).Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();

        // A JSON body, not the host's HTML not-found page the status code pages would otherwise serve
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var details = await response.Content.ReadFromJsonAsync<SyncErrorDetails>();
        details!.Code.Should().Be(SyncHttpErrorCodes.EndpointNotFound);
        details.TraceId.Should().Be(response.Headers.GetValues(SyncHttpHeaders.TraceId).Single());
    }

    [Fact]
    public async Task Sync_UnpublishedEndpoint_CarriesEndpointNotFoundSignal()
    {
        Guid unpublishedId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var config = await db.DataStoreConfigurations.FirstAsync();
            var endpoint = new Endpoint { Id = Guid.NewGuid(), Name = "Draft", DataStoreConfigurationId = config.Id, IsPublished = false };
            db.Endpoints.Add(endpoint);
            await db.SaveChangesAsync();
            unpublishedId = endpoint.Id;
        }

        var response = await _factory.CreateClient().GetAsync($"api/sync/{unpublishedId}/store-id");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.GetValues(SyncHttpHeaders.ErrorCode).Should().ContainSingle(SyncHttpErrorCodes.EndpointNotFound);
    }

    [Fact]
    public async Task Sync_UnknownDownloadSession_CarriesSessionExpiredSignal()
    {
        var response = await _factory.CreateClient().GetAsync($"api/sync/{_endpointId}/changes-bulk-item/{Guid.NewGuid()}/0/50");

        // Still a 404 - that is what an older client has always received here - but no longer
        // indistinguishable from an unknown endpoint
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.GetValues(SyncHttpHeaders.ErrorCode).Should().ContainSingle(SyncHttpErrorCodes.SessionExpired);

        var details = await response.Content.ReadFromJsonAsync<SyncErrorDetails>();
        details!.Code.Should().Be(SyncHttpErrorCodes.SessionExpired);
        details.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Sync_UnknownUploadSession_CarriesSessionExpiredSignal()
    {
        var httpClient = _factory.CreateClient();

        var complete = await httpClient.PostAsync($"api/sync/{_endpointId}/changes-bulk-complete/{Guid.NewGuid()}", null);
        complete.StatusCode.Should().Be(HttpStatusCode.NotFound);
        complete.Headers.GetValues(SyncHttpHeaders.ErrorCode).Should().ContainSingle(SyncHttpErrorCodes.SessionExpired);

        var item = await httpClient.PostAsJsonAsync($"api/sync/{_endpointId}/changes-bulk-item", new BulkChangeSetUploadItem { SessionId = Guid.NewGuid(), Items = [] });
        item.StatusCode.Should().Be(HttpStatusCode.NotFound);
        item.Headers.GetValues(SyncHttpHeaders.ErrorCode).Should().ContainSingle(SyncHttpErrorCodes.SessionExpired);
    }

    [Fact]
    public async Task Sync_UnknownEndpoint_IsReportedAsConfigurationErrorByTheAgent()
    {
        var remoteSyncClient = CreateRemoteSyncClient(Guid.NewGuid());
        var localProvider = CreateSqliteSyncProvider(_localDbPath, ProviderMode.Local);

        var syncAgent = new SyncAgent(localProvider, remoteSyncClient);
        var act = () => syncAgent.SynchronizeAsync();

        var ex = (await act.Should().ThrowAsync<SynchronizationException>()).Which;

        // What the app can now tell the user, and what it could before (an HttpRequestException)
        ex.Kind.Should().Be(SyncFailureKind.ConfigurationError);
        ex.Stage.Should().Be(SynchronizationStage.RemoteStoreId);
        ex.ErrorCode.Should().Be(SyncHttpErrorCodes.EndpointNotFound);
        ex.TraceId.Should().NotBeNullOrWhiteSpace();
        ex.ServerMessage.Should().Contain("not published");
        ex.InnerException.Should().BeAssignableTo<HttpRequestException>();
    }

    [Fact]
    public async Task Sync_ProviderFailure_TagsTheHostErrorResponse()
    {
        // An endpoint whose configuration names a table the database does not have: the provider
        // throws, the exception propagates to the host's exception handler as before (Sentry still
        // sees it), and the response it produces carries the error code and trace headers.
        Guid brokenEndpointId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dataStore = await db.DataStores.FirstAsync();

            var config = new DataStoreConfiguration { Name = "Broken Config", DataStoreId = dataStore.Id };
            db.DataStoreConfigurations.Add(config);
            await db.SaveChangesAsync();

            config.TableConfigurations.Add(new DataStoreTableConfiguration
            {
                Name = "MissingTable",
                SyncMode = DataStoreTableConfigurationSyncMode.UploadAndDownload,
                Sort = 0,
                DataStoreConfigurationId = config.Id
            });
            await db.SaveChangesAsync();

            var endpoint = new Endpoint { Id = Guid.NewGuid(), Name = "Broken Endpoint", DataStoreConfigurationId = config.Id, IsPublished = true };
            db.Endpoints.Add(endpoint);
            await db.SaveChangesAsync();
            brokenEndpointId = endpoint.Id;
        }

        var response = await _factory.CreateClient().GetAsync($"api/sync/{brokenEndpointId}/changes-bulk/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Headers.GetValues(SyncHttpHeaders.ErrorCode).Should().ContainSingle(SyncHttpErrorCodes.ServerError);
        response.Headers.GetValues(SyncHttpHeaders.TraceId).Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
    }

    private ISyncProviderHttpClient CreateRemoteSyncClient() => CreateRemoteSyncClient(_endpointId);

    private ISyncProviderHttpClient CreateRemoteSyncClient(Guid endpointId)
    {
        var httpClient = _factory.CreateClient();
        var services = new ServiceCollection();
        services.AddSingleton<IHttpClientFactory>(new TestHttpClientFactory(httpClient));
        services.AddCoreSyncHttpClient(options =>
        {
            options.SyncControllerRoute = $"api/sync/{endpointId}";
            options.BulkItemSize = 50;
        });
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<ISyncProviderHttpClient>();
    }

    private static void CreateSqliteDatabase(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE Items (
                Id TEXT PRIMARY KEY NOT NULL,
                Name TEXT NOT NULL,
                Value INTEGER NOT NULL
            )
            """;
        cmd.ExecuteNonQuery();
    }

    private static SqliteSyncProvider CreateSqliteSyncProvider(string dbPath, ProviderMode mode)
    {
        var builder = new SqliteSyncConfigurationBuilder($"Data Source={dbPath}");
        builder.Table("Items");
        return new SqliteSyncProvider(builder.Build(), mode);
    }

    private class TestHttpClientFactory(HttpClient httpClient) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => httpClient;
    }
}
