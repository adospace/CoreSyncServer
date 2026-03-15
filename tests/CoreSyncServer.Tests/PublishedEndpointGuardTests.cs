using System.Net;
using System.Net.Http.Json;
using CoreSyncServer.Controllers;
using CoreSyncServer.Data;
using CoreSyncServer.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace CoreSyncServer.Tests;

public class PublishedEndpointGuardTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PublishedEndpointGuardTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(int ConfigId, int TableId, Guid EndpointId)> SeedPublishedConfigAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var project = new Project { Name = $"Guard Test {Guid.NewGuid():N}", CreatedDate = DateTime.UtcNow, IsEnabled = true };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var dataStore = new SqliteDataStore
        {
            Name = "Guard Test DB",
            FilePath = ":memory:",
            ProjectId = project.Id,
            Type = DataStoreType.SQLite
        };
        db.DataStores.Add(dataStore);
        await db.SaveChangesAsync();

        var config = new DataStoreConfiguration
        {
            Name = "Guard Config",
            DataStoreId = dataStore.Id,
            Version = 1
        };
        db.DataStoreConfigurations.Add(config);
        await db.SaveChangesAsync();

        var table = new DataStoreTableConfiguration
        {
            Name = "TestTable",
            SyncMode = DataStoreTableConfigurationSyncMode.UploadAndDownload,
            Sort = 1,
            DataStoreConfigurationId = config.Id
        };
        db.DataStoreTableConfigurations.Add(table);
        await db.SaveChangesAsync();

        var endpoint = new Endpoint
        {
            Id = Guid.NewGuid(),
            Name = "Published EP",
            DataStoreConfigurationId = config.Id,
            IsPublished = true
        };
        db.Endpoints.Add(endpoint);
        await db.SaveChangesAsync();

        return (config.Id, table.Id, endpoint.Id);
    }

    [Fact]
    public async Task CreateTable_WhenPublished_ReturnsBadRequest()
    {
        var (configId, _, _) = await SeedPublishedConfigAsync();
        var client = _factory.CreateAuthenticatedClient();

        var request = new { Name = "NewTable", SyncMode = 0, SkipInitialSnapshot = false, IdentityInsert = 0, ForceReloadInsertedRecords = false };
        var response = await client.PostAsJsonAsync($"api/datastoreconfigurations/{configId}/tables", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = await response.Content.ReadFromJsonAsync<List<string>>();
        errors.Should().Contain(e => e.Contains("published"));
    }

    [Fact]
    public async Task UpdateTable_WhenPublished_ReturnsBadRequest()
    {
        var (configId, tableId, _) = await SeedPublishedConfigAsync();
        var client = _factory.CreateAuthenticatedClient();

        var request = new { Name = "TestTable", SyncMode = 1, SkipInitialSnapshot = false, IdentityInsert = 0, ForceReloadInsertedRecords = false };
        var response = await client.PutAsJsonAsync($"api/datastoreconfigurations/{configId}/tables/{tableId}", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = await response.Content.ReadFromJsonAsync<List<string>>();
        errors.Should().Contain(e => e.Contains("published"));
    }

    [Fact]
    public async Task DeleteTable_WhenPublished_ReturnsBadRequest()
    {
        var (configId, tableId, _) = await SeedPublishedConfigAsync();
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.DeleteAsync($"api/datastoreconfigurations/{configId}/tables/{tableId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = await response.Content.ReadFromJsonAsync<List<string>>();
        errors.Should().Contain(e => e.Contains("published"));
    }

    [Fact]
    public async Task UpdateConfigVersion_WhenPublished_ReturnsBadRequest()
    {
        var (configId, _, _) = await SeedPublishedConfigAsync();
        var client = _factory.CreateAuthenticatedClient();

        var request = new { Name = "Guard Config", Version = 2 };
        var response = await client.PutAsJsonAsync($"api/datastoreconfigurations/{configId}", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = await response.Content.ReadFromJsonAsync<List<string>>();
        errors.Should().Contain(e => e.Contains("version"));
    }

    [Fact]
    public async Task UpdateConfigNameAndDescription_WhenPublished_Succeeds()
    {
        var (configId, _, _) = await SeedPublishedConfigAsync();
        var client = _factory.CreateAuthenticatedClient();

        var request = new { Name = "Renamed Config", Description = "Updated desc", Version = 1 };
        var response = await client.PutAsJsonAsync($"api/datastoreconfigurations/{configId}", request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetConfigDetail_ReturnsPublishedState()
    {
        var (configId, _, _) = await SeedPublishedConfigAsync();
        var client = _factory.CreateAuthenticatedClient();

        var detail = await client.GetFromJsonAsync<DataStoreConfigurationsController.ConfigurationDetailDto>(
            $"api/datastoreconfigurations/{configId}");

        detail.Should().NotBeNull();
        detail!.HasPublishedEndpoint.Should().BeTrue();
        detail.HasEndpoints.Should().BeTrue();
    }
}
