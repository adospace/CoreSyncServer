using CoreSync;
using CoreSync.Http;
using CoreSyncServer.Data;
using CoreSyncServer.Services;
using MessagePack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace CoreSyncServer.Controllers;

[ApiController]
[Route("api/sync/{endpointId:guid}")]
public class SyncController(
    ApplicationDbContext context,
    ISyncProviderFactory syncProviderFactory,
    IMemoryCache memoryCache,
    ILogger<SyncController> logger) : ControllerBase
{
    private class CachedSyncChangeSet
    {
        public required SyncChangeSet ChangeSet { get; set; }
        public List<SyncItem> BufferList { get; set; } = [];
    }

    private async Task<ISyncProvider> GetSyncProviderAsync(Guid endpointId, CancellationToken cancellationToken)
    {
        var endpoint = await context.Endpoints
            .Include(e => e.DataStoreConfiguration)
                .ThenInclude(c => c!.DataStore)
            .Include(e => e.DataStoreConfiguration)
                .ThenInclude(c => c!.TableConfigurations)
            .FirstOrDefaultAsync(e => e.Id == endpointId, cancellationToken)
            ?? throw new KeyNotFoundException($"Endpoint '{endpointId}' not found.");

        if (endpoint.DataStoreConfiguration is null)
            throw new InvalidOperationException($"Endpoint '{endpointId}' has no associated DataStoreConfiguration.");

        return syncProviderFactory.CreateSyncProvider(endpoint.DataStoreConfiguration);
    }

    [HttpGet("store-id")]
    public async Task<ActionResult<string>> GetStoreId(Guid endpointId, CancellationToken cancellationToken)
    {
        try
        {
            var provider = await GetSyncProviderAsync(endpointId, cancellationToken);
            var storeId = await provider.GetStoreIdAsync(cancellationToken);
            return Ok(storeId.ToString());
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("sync-version")]
    public async Task<ActionResult<SyncVersion>> GetSyncVersion(Guid endpointId, CancellationToken cancellationToken)
    {
        try
        {
            var provider = await GetSyncProviderAsync(endpointId, cancellationToken);
            var version = await provider.GetSyncVersionAsync(cancellationToken);
            return Ok(version);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("changes-bulk/{storeId:guid}")]
    public async Task<ActionResult<BulkSyncChangeSet>> GetBulkChangeSet(Guid endpointId, Guid storeId, CancellationToken cancellationToken)
    {
        try
        {
            var provider = await GetSyncProviderAsync(endpointId, cancellationToken);
            var changeSet = await provider.GetChangesAsync(storeId, syncDirection: SyncDirection.DownloadOnly, cancellationToken: cancellationToken);

            logger.LogInformation("GetBulkChangeSet(Endpoint={EndpointId}, StoreId={StoreId}) -> (Source={SourceAnchor} Target={TargetAnchor} Items={ItemsCount})",
                endpointId, storeId, changeSet.SourceAnchor, changeSet.TargetAnchor, changeSet.Items.Count);

            var sessionId = Guid.NewGuid();
            memoryCache.Set(sessionId, new CachedSyncChangeSet { ChangeSet = changeSet });

            return Ok(new BulkSyncChangeSet
            {
                SessionId = sessionId,
                TotalChanges = changeSet.Items.Count,
                SourceAnchor = changeSet.SourceAnchor,
                TargetAnchor = changeSet.TargetAnchor,
                ChangesByTable = changeSet.Items
                    .GroupBy(i => i.TableName)
                    .ToDictionary(g => g.Key, g => g.Count())
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("changes-bulk-item/{sessionId:guid}/{skip:int}/{take:int}")]
    public ActionResult<IReadOnlyList<SyncItem>> GetBulkChangeSetItem(Guid sessionId, int skip, int take)
    {
        if (memoryCache.TryGetValue(sessionId, out var obj) && obj is CachedSyncChangeSet cached)
        {
            var bufferList = cached.BufferList;
            bufferList.Clear();

            for (int i = skip; i < skip + take && i < cached.ChangeSet.Items.Count; i++)
            {
                bufferList.Add(cached.ChangeSet.Items[i]);
            }

            if (skip + take >= cached.ChangeSet.Items.Count)
                memoryCache.Remove(sessionId);

            return Ok(bufferList);
        }

        return NotFound();
    }

    [HttpGet("changes-bulk-item-binary/{sessionId:guid}/{skip:int}/{take:int}")]
    public ActionResult GetBulkChangeSetItemBinary(Guid sessionId, int skip, int take)
    {
        if (memoryCache.TryGetValue(sessionId, out var obj) && obj is CachedSyncChangeSet cached)
        {
            var bufferList = cached.BufferList;
            bufferList.Clear();

            for (int i = skip; i < skip + take && i < cached.ChangeSet.Items.Count; i++)
            {
                bufferList.Add(cached.ChangeSet.Items[i]);
            }

            if (skip + take >= cached.ChangeSet.Items.Count)
                memoryCache.Remove(sessionId);

            var bytes = MessagePackSerializer.Typeless.Serialize(bufferList);
            return File(bytes, "application/x-msgpack");
        }

        return NotFound();
    }

    [HttpPost("changes-bulk-begin")]
    public async Task<ActionResult> BeginApplyBulkChanges(Guid endpointId, [FromBody] BulkSyncChangeSet bulkChangeSet, CancellationToken cancellationToken)
    {
        // Validate that the endpoint exists
        var exists = await context.Endpoints.AnyAsync(e => e.Id == endpointId, cancellationToken);
        if (!exists)
            return NotFound();

        var changeSet = new SyncChangeSet(bulkChangeSet.SourceAnchor, bulkChangeSet.TargetAnchor, new List<SyncItem>());
        memoryCache.Set(bulkChangeSet.SessionId, changeSet);

        return Ok();
    }

    [HttpPost("changes-bulk-item")]
    public ActionResult ApplyBulkChangesItem([FromBody] BulkChangeSetUploadItem bulkUploadItem)
    {
        if (memoryCache.TryGetValue(bulkUploadItem.SessionId, out var obj) && obj is SyncChangeSet changeSet)
        {
            ((List<SyncItem>)changeSet.Items).AddRange(bulkUploadItem.Items);
            return Ok();
        }

        return NotFound();
    }

    [HttpPost("changes-bulk-item-binary")]
    public async Task<ActionResult> ApplyBulkChangesItemBinary()
    {
        var bulkUploadItem = (BulkChangeSetUploadItem?)
            await MessagePackSerializer.Typeless.DeserializeAsync(Request.Body);

        if (bulkUploadItem is null)
            return BadRequest();

        if (memoryCache.TryGetValue(bulkUploadItem.SessionId, out var obj) && obj is SyncChangeSet changeSet)
        {
            ((List<SyncItem>)changeSet.Items).AddRange(bulkUploadItem.Items);
            return Ok();
        }

        return NotFound();
    }

    [HttpPost("changes-bulk-complete/{sessionId:guid}")]
    public async Task<ActionResult<SyncAnchor>> CompleteApplyBulkChanges(Guid endpointId, Guid sessionId, CancellationToken cancellationToken)
    {
        if (memoryCache.TryGetValue(sessionId, out var obj) && obj is SyncChangeSet changeSet)
        {
            // Convert JSON elements to .NET objects
            foreach (var item in changeSet.Items)
            {
                foreach (var entry in item.Values.Where(e => e.Key != "__OP").ToList())
                {
                    item.Values[entry.Key].Value = entry.Value.Value == null ? null :
                        ConvertJsonElementToObject((JsonElement)entry.Value.Value, entry.Value.Type);
                }
            }

            try
            {
                var provider = await GetSyncProviderAsync(endpointId, cancellationToken);
                var resAnchor = await provider.ApplyChangesAsync(changeSet, updateResultion: ConflictResolution.ForceWrite, deleteResolution: ConflictResolution.Skip);

                memoryCache.Remove(sessionId);

                logger.LogInformation("CompleteApplyBulkChanges(Endpoint={EndpointId}) => {Anchor}", endpointId, resAnchor);

                return Ok(resAnchor);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        return NotFound();
    }

    [HttpPost("changes-bulk-complete-binary/{sessionId:guid}")]
    public async Task<ActionResult<SyncAnchor>> CompleteApplyBulkChangesBinary(Guid endpointId, Guid sessionId, CancellationToken cancellationToken)
    {
        if (memoryCache.TryGetValue(sessionId, out var obj) && obj is SyncChangeSet changeSet)
        {
            try
            {
                var provider = await GetSyncProviderAsync(endpointId, cancellationToken);
                var resAnchor = await provider.ApplyChangesAsync(changeSet, updateResultion: ConflictResolution.ForceWrite, deleteResolution: ConflictResolution.Skip);

                memoryCache.Remove(sessionId);

                logger.LogInformation("CompleteApplyBulkChangesBinary(Endpoint={EndpointId}) => {Anchor}", endpointId, resAnchor);

                return Ok(resAnchor);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        return NotFound();
    }

    [HttpPost("save-version/{storeId:guid}/{version:long}")]
    public async Task<ActionResult> SaveVersionForStore(Guid endpointId, Guid storeId, long version, CancellationToken cancellationToken)
    {
        try
        {
            var provider = await GetSyncProviderAsync(endpointId, cancellationToken);

            logger.LogInformation("SaveVersionForStore(Endpoint={EndpointId}, StoreId={StoreId}, Version={Version})", endpointId, storeId, version);

            await provider.SaveVersionForStoreAsync(storeId, version, cancellationToken);
            return Ok();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private static object? ConvertJsonElementToObject(JsonElement value, SyncItemValueType targetType)
    {
        return targetType switch
        {
            SyncItemValueType.Null => null,
            SyncItemValueType.String => value.GetString(),
            SyncItemValueType.Int32 => value.GetInt32(),
            SyncItemValueType.Float => value.GetSingle(),
            SyncItemValueType.Double => value.GetDouble(),
            SyncItemValueType.DateTime => value.GetDateTime(),
            SyncItemValueType.Boolean => value.GetBoolean(),
            SyncItemValueType.ByteArray => value.GetBytesFromBase64(),
            SyncItemValueType.Guid => value.GetGuid(),
            SyncItemValueType.Int64 => value.GetInt64(),
            SyncItemValueType.Decimal => value.GetDecimal(),
            _ => throw new NotSupportedException(),
        };
    }
}
