using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CoreSyncServer.Controllers;
using CoreSyncServer.Data;
using CoreSyncServer.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CoreSyncServer.Tests;

public class DataStoreTransferTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly CustomWebApplicationFactory _factory;

    public DataStoreTransferTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(int SourceId, int TargetId)> SeedAsync(bool publishSourceEndpoint = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var project = new Project { Name = $"Transfer {Guid.NewGuid():N}", CreatedDate = DateTime.UtcNow, IsEnabled = true };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var source = new SqlServerDataStore
        {
            Name = "DEV",
            Description = "dev db",
            ProjectId = project.Id,
            Type = DataStoreType.SqlServer,
            ConnectionString = "ENV=DEV_DB",
            TrackingMode = SqlServerDataStoreTrackingMode.ChangeTracking
        };
        var target = new SqlServerDataStore
        {
            Name = "PROD",
            ProjectId = project.Id,
            Type = DataStoreType.SqlServer,
            ConnectionString = "ENV=PROD_DB",
            TrackingMode = SqlServerDataStoreTrackingMode.ChangeTracking
        };
        db.DataStores.AddRange(source, target);
        await db.SaveChangesAsync();

        var config = new DataStoreConfiguration
        {
            Name = "Mobile",
            Description = "Field app",
            DataStoreId = source.Id,
            Version = 3,
            TableConfigurations =
            {
                new DataStoreTableConfiguration
                {
                    Name = "Orders",
                    Schema = "dbo",
                    SyncMode = DataStoreTableConfigurationSyncMode.UploadAndDownload,
                    Sort = 1,
                    SelectIncrementalQuery = "SELECT * FROM Orders WHERE Tenant = @tenant",
                    SkipColumns = "[\"RowVersion\"]",
                    IdentityInsert = DataStoreTableConfigurationIdentityInsertMode.On,
                    ForceReloadInsertedRecords = true,
                    InError = true,
                    Message = "runtime state, not exported"
                },
                new DataStoreTableConfiguration
                {
                    Name = "Customers",
                    Schema = "dbo",
                    SyncMode = DataStoreTableConfigurationSyncMode.DownloadOnly,
                    Sort = 0,
                    SkipInitialSnapshot = true
                }
            },
            Endpoints =
            {
                new Endpoint
                {
                    Id = Guid.NewGuid(),
                    Name = "Field devices",
                    Tags = "mobile",
                    IsPublished = publishSourceEndpoint,
                    Authentication = new ApiKeyAuthentication { ApiKey = "secret-key" }
                }
            }
        };
        db.DataStoreConfigurations.Add(config);
        await db.SaveChangesAsync();

        return (source.Id, target.Id);
    }

    private static async Task<DataStoresController.DataStoreExportDocument> ExportAsync(HttpClient client, int dataStoreId)
    {
        var response = await client.GetAsync($"api/datastores/{dataStoreId}/export");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<DataStoresController.DataStoreExportDocument>(json, JsonOptions)!;
    }

    [Fact]
    public async Task Export_ReturnsConfigurationsTablesAndEndpoints_WithoutConnectionDetails()
    {
        var (sourceId, _) = await SeedAsync();
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync($"api/datastores/{sourceId}/export");
        var json = await response.Content.ReadAsStringAsync();
        var document = JsonSerializer.Deserialize<DataStoresController.DataStoreExportDocument>(json, JsonOptions)!;

        document.Format.Should().Be(DataStoresController.ExportFormat);
        document.FormatVersion.Should().Be(DataStoresController.ExportFormatVersion);
        document.DataStore.Name.Should().Be("DEV");
        document.DataStore.Type.Should().Be(DataStoreType.SqlServer);
        json.Should().NotContain("ENV=DEV_DB", "connection details belong to the source, not the export");

        var config = document.Configurations.Should().ContainSingle().Subject;
        config.Name.Should().Be("Mobile");
        config.Version.Should().Be(3);
        config.Tables.Select(t => t.Name).Should().Equal("Customers", "Orders");

        var orders = config.Tables.Single(t => t.Name == "Orders");
        orders.Schema.Should().Be("dbo");
        orders.SyncMode.Should().Be((int)DataStoreTableConfigurationSyncMode.UploadAndDownload);
        orders.SelectIncrementalQuery.Should().Be("SELECT * FROM Orders WHERE Tenant = @tenant");
        orders.SkipColumns.Should().Be("[\"RowVersion\"]");
        orders.IdentityInsert.Should().Be((int)DataStoreTableConfigurationIdentityInsertMode.On);
        orders.ForceReloadInsertedRecords.Should().BeTrue();
        json.Should().NotContain("runtime state, not exported");

        var endpoint = config.Endpoints.Should().ContainSingle().Subject;
        endpoint.Name.Should().Be("Field devices");
        endpoint.Authentication!.Type.Should().Be((int)EndPointAuthenticationType.ApiKey);
        endpoint.Authentication.ApiKey.Should().Be("secret-key");
    }

    [Fact]
    public async Task Export_UnknownDataStore_ReturnsNotFound()
    {
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("api/datastores/999999/export");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Import_CreatesConfigurationWithTables_AndSkipsEndpointsByDefault()
    {
        var (sourceId, targetId) = await SeedAsync();
        var client = _factory.CreateAuthenticatedClient();
        var document = await ExportAsync(client, sourceId);

        var response = await client.PostAsJsonAsync($"api/datastores/{targetId}/import",
            new DataStoresController.ImportDataStoreRequest(document, IncludeEndpoints: false));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<DataStoresController.ImportDataStoreResult>())!;
        result.Should().Be(new DataStoresController.ImportDataStoreResult(1, 2, 0));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var imported = await db.DataStoreConfigurations
            .Include(c => c.TableConfigurations)
            .Include(c => c.Endpoints)
            .SingleAsync(c => c.DataStoreId == targetId);

        imported.Name.Should().Be("Mobile");
        imported.Description.Should().Be("Field app");
        imported.Version.Should().Be(3);
        imported.Endpoints.Should().BeEmpty();
        imported.TableConfigurations.OrderBy(t => t.Sort).Select(t => t.Name).Should().Equal("Customers", "Orders");

        var orders = imported.TableConfigurations.Single(t => t.Name == "Orders");
        orders.SelectIncrementalQuery.Should().Be("SELECT * FROM Orders WHERE Tenant = @tenant");
        orders.IdentityInsert.Should().Be(DataStoreTableConfigurationIdentityInsertMode.On);
        orders.ForceReloadInsertedRecords.Should().BeTrue();
        orders.InError.Should().BeFalse();
        orders.Message.Should().BeNull();
    }

    [Fact]
    public async Task Import_WithEndpoints_CreatesUnpublishedEndpointsWithFreshIds()
    {
        var (sourceId, targetId) = await SeedAsync(publishSourceEndpoint: true);
        var client = _factory.CreateAuthenticatedClient();
        var document = await ExportAsync(client, sourceId);

        var response = await client.PostAsJsonAsync($"api/datastores/{targetId}/import",
            new DataStoresController.ImportDataStoreRequest(document, IncludeEndpoints: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<DataStoresController.ImportDataStoreResult>())!;
        result.EndpointsCreated.Should().Be(1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sourceEndpoint = await db.Endpoints.SingleAsync(e => e.DataStoreConfiguration!.DataStoreId == sourceId);
        var imported = await db.Endpoints
            .Include(e => e.Authentication)
            .SingleAsync(e => e.DataStoreConfiguration!.DataStoreId == targetId);

        imported.Id.Should().NotBe(sourceEndpoint.Id);
        imported.Name.Should().Be("Field devices");
        imported.Tags.Should().Be("mobile");
        imported.IsPublished.Should().BeFalse("the target decides when to go live");
        imported.Authentication.Should().BeOfType<ApiKeyAuthentication>()
            .Which.ApiKey.Should().Be("secret-key");
    }

    [Fact]
    public async Task Import_WhenNameAlreadyExists_IsRefusedWithoutChanges()
    {
        var (sourceId, targetId) = await SeedAsync();
        var client = _factory.CreateAuthenticatedClient();
        var document = await ExportAsync(client, sourceId);

        (await client.PostAsJsonAsync($"api/datastores/{targetId}/import",
            new DataStoresController.ImportDataStoreRequest(document, IncludeEndpoints: true))).EnsureSuccessStatusCode();

        // Same name again, differing only in case, with different content.
        var clashing = document with
        {
            Configurations = [document.Configurations[0] with { Name = "MOBILE", Version = 99, Tables = [] }]
        };

        var response = await client.PostAsJsonAsync($"api/datastores/{targetId}/import",
            new DataStoresController.ImportDataStoreRequest(clashing, IncludeEndpoints: true));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = await response.Content.ReadFromJsonAsync<string[]>();
        errors.Should().ContainSingle().Which.Should().Be("Configuration 'Mobile' already exists. Choose a different name.");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var untouched = await db.DataStoreConfigurations
            .Include(c => c.TableConfigurations)
            .Include(c => c.Endpoints)
            .SingleAsync(c => c.DataStoreId == targetId);
        untouched.Version.Should().Be(3);
        untouched.TableConfigurations.Should().HaveCount(2);
        untouched.Endpoints.Should().ContainSingle();
    }

    [Fact]
    public async Task Import_UnderNewName_CreatesSecondConfigurationNextToExistingOne()
    {
        var (sourceId, targetId) = await SeedAsync();
        var client = _factory.CreateAuthenticatedClient();
        var document = await ExportAsync(client, sourceId);

        (await client.PostAsJsonAsync($"api/datastores/{targetId}/import",
            new DataStoresController.ImportDataStoreRequest(document, IncludeEndpoints: false))).EnsureSuccessStatusCode();

        // The dashboard resolves the clash by letting the user type a new name.
        var renamed = document with
        {
            Configurations = [document.Configurations[0] with { Name = "Mobile v2", Version = 4 }]
        };

        var response = await client.PostAsJsonAsync($"api/datastores/{targetId}/import",
            new DataStoresController.ImportDataStoreRequest(renamed, IncludeEndpoints: false));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<DataStoresController.ImportDataStoreResult>())!;
        result.Should().Be(new DataStoresController.ImportDataStoreResult(1, 2, 0));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var configs = await db.DataStoreConfigurations
            .Include(c => c.TableConfigurations)
            .Where(c => c.DataStoreId == targetId)
            .OrderBy(c => c.Name)
            .ToListAsync();

        configs.Select(c => (c.Name, c.Version)).Should().Equal(("Mobile", 3), ("Mobile v2", 4));
        configs.Should().OnlyContain(c => c.TableConfigurations.Count == 2);
    }

    [Fact]
    public async Task Import_WithDuplicateNamesInDocument_IsRefused()
    {
        var (sourceId, targetId) = await SeedAsync();
        var client = _factory.CreateAuthenticatedClient();
        var document = await ExportAsync(client, sourceId);

        var duplicated = document with
        {
            Configurations = [document.Configurations[0], document.Configurations[0] with { Name = " mobile " }]
        };

        var response = await client.PostAsJsonAsync($"api/datastores/{targetId}/import",
            new DataStoresController.ImportDataStoreRequest(duplicated, IncludeEndpoints: false));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = await response.Content.ReadFromJsonAsync<string[]>();
        errors.Should().ContainSingle().Which.Should().Contain("more than once");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.DataStoreConfigurations.AnyAsync(c => c.DataStoreId == targetId)).Should().BeFalse();
    }

    [Fact]
    public async Task Import_RejectsFilesThatAreNotAnExport()
    {
        var (_, targetId) = await SeedAsync();
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync($"api/datastores/{targetId}/import",
            new { Document = new { tables = new[] { new { name = "Orders" } } }, IncludeEndpoints = false });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = await response.Content.ReadFromJsonAsync<string[]>();
        errors.Should().ContainSingle().Which.Should().Contain("not a data store export");
    }

    [Fact]
    public async Task Import_RejectsInvalidTableValues_WithoutChanges()
    {
        var (sourceId, targetId) = await SeedAsync();
        var client = _factory.CreateAuthenticatedClient();
        var document = await ExportAsync(client, sourceId);

        var config = document.Configurations[0];
        var broken = document with
        {
            Configurations =
            [
                config with
                {
                    Tables =
                    [
                        config.Tables[0] with { SyncMode = 42 },
                        config.Tables[1] with { Name = " " }
                    ]
                }
            ]
        };

        var response = await client.PostAsJsonAsync($"api/datastores/{targetId}/import",
            new DataStoresController.ImportDataStoreRequest(broken, IncludeEndpoints: false));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = await response.Content.ReadFromJsonAsync<string[]>();
        errors.Should().HaveCount(2);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.DataStoreConfigurations.AnyAsync(c => c.DataStoreId == targetId)).Should().BeFalse();
    }

    [Fact]
    public async Task Import_Unauthenticated_IsRejected()
    {
        var (_, targetId) = await SeedAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"api/datastores/{targetId}/import", new { });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Unauthorized);
    }
}
