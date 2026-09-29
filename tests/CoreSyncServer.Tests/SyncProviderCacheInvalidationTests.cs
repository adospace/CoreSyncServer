using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using CoreSync;
using CoreSyncServer.Controllers;
using CoreSyncServer.Data;
using CoreSyncServer.Services;
using CoreSyncServer.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CoreSyncServer.Tests;

/// <summary>
/// The sync provider cache never expires on its own, so every action that changes what a cached
/// provider was built from has to evict it. These cover the two that do not require unpublishing:
/// repointing an endpoint at another configuration, and editing the data store.
/// </summary>
public class SyncProviderCacheInvalidationTests : IClassFixture<CustomWebApplicationFactory>
{
    // SyncController keys an endpoint with no agent as "none".
    private const string NoAgent = "none";

    private readonly CustomWebApplicationFactory _factory;

    public SyncProviderCacheInvalidationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _host = factory.CreateAuthenticatedFactory();
    }

    private async Task<(int DataStoreId, int TargetConfigId, Guid EndpointId)> SeedPublishedEndpointAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var project = new Project { Name = $"Cache Test {Guid.NewGuid():N}", CreatedDate = DateTime.UtcNow, IsEnabled = true };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var dataStore = new SqliteDataStore
        {
            Name = "Cache Test DB",
            FilePath = ":memory:",
            ProjectId = project.Id,
            Type = DataStoreType.SQLite
        };
        db.DataStores.Add(dataStore);
        await db.SaveChangesAsync();

        var endpointId = Guid.NewGuid();
        var current = new DataStoreConfiguration
        {
            Name = "Current",
            DataStoreId = dataStore.Id,
            Endpoints =
            {
                new Endpoint
                {
                    Id = endpointId,
                    Name = "Field devices",
                    IsPublished = true,
                    Authentication = new ApiKeyAuthentication { ApiKey = "key" }
                }
            }
        };
        var next = new DataStoreConfiguration { Name = "Next", DataStoreId = dataStore.Id };
        db.DataStoreConfigurations.AddRange(current, next);
        await db.SaveChangesAsync();

        return (dataStore.Id, next.Id, endpointId);
    }

    // The authenticated client runs against its own host, so the cache it invalidates is that
    // host's singleton - not the one on the fixture.
    private readonly WebApplicationFactory<Program> _host;

    private ISyncProviderCache Cache => _host.Services.GetRequiredService<ISyncProviderCache>();

    private void SeedCachedProvider(Guid endpointId) =>
        Cache.Set(endpointId, NoAgent, SentinelProvider.Create());

    [Fact]
    public async Task RepointingEndpoint_EvictsCachedProvider()
    {
        var (dataStoreId, targetConfigId, endpointId) = await SeedPublishedEndpointAsync();
        SeedCachedProvider(endpointId);
        var client = _host.CreateAuthenticatedClient();

        var response = await client.PutAsJsonAsync(
            $"api/datastores/{dataStoreId}/endpoints/{endpointId}/configuration",
            new DataStoresController.UpdateEndpointConfigurationRequest(targetConfigId));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        Cache.TryGet(endpointId, NoAgent, out _).Should().BeFalse(
            "the cached provider was built from the configuration the endpoint no longer points at");
    }

    [Fact]
    public async Task UpdatingDataStore_EvictsCachedProvidersOfItsEndpoints()
    {
        var (dataStoreId, _, endpointId) = await SeedPublishedEndpointAsync();
        SeedCachedProvider(endpointId);
        var client = _host.CreateAuthenticatedClient();

        var response = await client.PutAsJsonAsync(
            $"api/datastores/{dataStoreId}",
            new DataStoresController.UpdateDataStoreRequest("Cache Test DB", null, "other.db", null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        Cache.TryGet(endpointId, NoAgent, out _).Should().BeFalse(
            "the cached provider still holds the previous connection settings");
    }

    [Fact]
    public async Task RepointingEndpoint_LeavesOtherEndpointsCached()
    {
        var (dataStoreId, targetConfigId, endpointId) = await SeedPublishedEndpointAsync();
        var (_, _, otherEndpointId) = await SeedPublishedEndpointAsync();
        SeedCachedProvider(endpointId);
        SeedCachedProvider(otherEndpointId);
        var client = _host.CreateAuthenticatedClient();

        await client.PutAsJsonAsync(
            $"api/datastores/{dataStoreId}/endpoints/{endpointId}/configuration",
            new DataStoresController.UpdateEndpointConfigurationRequest(targetConfigId));

        Cache.TryGet(otherEndpointId, NoAgent, out _).Should().BeTrue();
    }

    [Fact]
    public void Invalidate_EvictsTheEndpointsEntry()
    {
        var endpointId = Guid.NewGuid();
        SeedCachedProvider(endpointId);

        Cache.Invalidate(endpointId);

        Cache.TryGet(endpointId, NoAgent, out _).Should().BeFalse();
    }

    /// <summary>Stands in for a real provider; the cache only stores the reference.</summary>
    public class SentinelProvider : DispatchProxy
    {
        public static ISyncProvider Create() => Create<ISyncProvider, SentinelProvider>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException("Sentinel provider is never used for syncing.");
    }
}
