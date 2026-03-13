using CoreSyncServer.Data;
using Microsoft.EntityFrameworkCore;

namespace CoreSyncServer.Services.Implementation;

internal class ProvisionService(
    ApplicationDbContext context,
    ISyncProviderFactory syncProviderFactory) : IProvisionService
{
    public async Task<ProvisionResult> ApplyProvisionAsync(int dataStoreId)
    {
        var configuration = await BuildMergedConfigurationAsync(dataStoreId);
        if (configuration is null)
            return ProvisionResult.Fail("Data store not found.");

        if (configuration.TableConfigurations.Count == 0)
            return ProvisionResult.Fail("No tracked tables found across any configuration for this data store.");

        try
        {
            var provider = syncProviderFactory.CreateSyncProvider(configuration);
            await provider.ApplyProvisionAsync();
            return ProvisionResult.Ok();
        }
        catch (Exception ex)
        {
            return ProvisionResult.Fail($"Failed to apply provision: {ex.Message}");
        }
    }

    public async Task<ProvisionResult> RemoveProvisionAsync(int dataStoreId)
    {
        var configuration = await BuildMergedConfigurationAsync(dataStoreId);
        if (configuration is null)
            return ProvisionResult.Fail("Data store not found.");

        if (configuration.TableConfigurations.Count == 0)
            return ProvisionResult.Fail("No tracked tables found across any configuration for this data store.");

        try
        {
            var provider = syncProviderFactory.CreateSyncProvider(configuration);
            await provider.RemoveProvisionAsync();
            return ProvisionResult.Ok();
        }
        catch (Exception ex)
        {
            return ProvisionResult.Fail($"Failed to remove provision: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds a virtual DataStoreConfiguration that merges all distinct tracked tables
    /// across every configuration belonging to the given data store.
    /// </summary>
    private async Task<DataStoreConfiguration?> BuildMergedConfigurationAsync(int dataStoreId)
    {
        var dataStore = await context.DataStores
            .Include(ds => ds.Configurations)
                .ThenInclude(c => c.TableConfigurations)
            .FirstOrDefaultAsync(ds => ds.Id == dataStoreId);

        if (dataStore is null)
            return null;

        // Merge all tables from all configurations, keeping distinct by (Name, Schema).
        // When the same table appears in multiple configurations with different sync modes,
        // prefer the most permissive mode (UploadAndDownload > UploadOnly/DownloadOnly).
        var mergedTables = dataStore.Configurations
            .SelectMany(c => c.TableConfigurations)
            .Where(t => t.SyncMode != DataStoreTableConfigurationSyncMode.NotTracked)
            .GroupBy(t => new { t.Name, t.Schema })
            .Select(g => new DataStoreTableConfiguration
            {
                Name = g.Key.Name,
                Schema = g.Key.Schema,
                SyncMode = g.Min(t => t.SyncMode), // UploadAndDownload = 0 (most permissive)
                Sort = g.Min(t => t.Sort)
            })
            .OrderBy(t => t.Sort)
            .ThenBy(t => t.Name)
            .ToList();

        return new DataStoreConfiguration
        {
            Name = "__provision__",
            DataStoreId = dataStoreId,
            DataStore = dataStore,
            TableConfigurations = mergedTables
        };
    }
}
