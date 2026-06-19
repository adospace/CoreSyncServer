using CoreSync;
using CoreSync.Http;
using CoreSyncServer.Data;
using CoreSyncServer.Filters;
using CoreSyncServer.Services;
using CoreSyncServer.Services.Implementation;
using MessagePack;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CoreSyncServer.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/sync/{endpointId:guid}")]
[ServiceFilter(typeof(SyncEndpointAuthFilter))]
[ServiceFilter(typeof(SyncConfigurationExceptionFilter))]
public class SyncController(
    ApplicationDbContext context,
    ISyncProviderFactory syncProviderFactory,
    ISyncSessionService syncSessionService,
    ISyncProviderCache syncProviderCache,
    IMemoryCache memoryCache,
    ILogger<SyncController> logger) : ControllerBase
{
    private class CachedSyncChangeSet
    {
        public required SyncChangeSet ChangeSet { get; set; }
        public List<SyncItem> BufferList { get; set; } = [];
    }

    private class CachedUploadSession
    {
        public required SyncChangeSet ChangeSet { get; set; }
        public int SyncSessionId { get; set; }
    }

    private static SyncFilterParameter[]? GetSyncFilterParameters(HttpContext httpContext)
    {
        if (httpContext.Items[SyncEndpointAuthFilter.AuthResultKey] is not SyncAuthResult authResult)
            return null;

        List<SyncFilterParameter> parameters = [];

        if (authResult.UserId is not null)
            parameters.Add(new SyncFilterParameter("@UserId", authResult.UserId));

        if (authResult.UserName is not null)
            parameters.Add(new SyncFilterParameter("@UserName", authResult.UserName));

        foreach (var claim in authResult.Claims)
        {
            var paramName = $"@Claim_{claim.Type.Replace('/', '_').Replace('.', '_')}";
            if (parameters.All(p => p.Name != paramName))
                parameters.Add(new SyncFilterParameter(paramName, claim.Value));
        }

        return parameters.Count > 0 ? parameters.ToArray() : null;
    }

    private async Task<Data.Endpoint> GetEndpointAsync(Guid endpointId, CancellationToken cancellationToken)
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

        return endpoint;
    }

    private string[]? GetClientTables()
    {
        if (!Request.Headers.TryGetValue(SyncHttpHeaders.Tables, out var tablesHeader))
            return null;

        var tables = tablesHeader.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (Request.Headers.TryGetValue(SyncHttpHeaders.TablesCount, out var countHeader)
            && int.TryParse(countHeader, out var expectedCount)
            && tables.Length != expectedCount)
        {
            throw new ArgumentException($"Expected {expectedCount} table names in {SyncHttpHeaders.Tables} header but received {tables.Length}. The header may have been truncated.");
        }

        return tables;
    }

    private async Task<(Data.Endpoint Endpoint, ISyncProvider Provider)> GetProviderAsync(
        Guid endpointId, CancellationToken cancellationToken, ISyncLogger? sessionLogger = null)
    {
        var tables = GetClientTables();
        var endpoint = await GetEndpointAsync(endpointId, cancellationToken);

        if (sessionLogger is null && tables is null)
        {
            // Include AgentId in the key so the cache invalidates when the DataStore is attached
            // to or detached from an agent — otherwise a stale proxy (or stale direct provider)
            // would keep being returned after the relationship flips.
            var agentKeyPart = endpoint.DataStoreConfiguration!.DataStore!.AgentId?.ToString() ?? "none";
            if (syncProviderCache.TryGet(endpointId, agentKeyPart, out var cachedProvider))
                return (endpoint, cachedProvider);

            var provider = syncProviderFactory.CreateSyncProvider(endpoint.DataStoreConfiguration!);
            syncProviderCache.Set(endpointId, agentKeyPart, provider);
            return (endpoint, provider);
        }

        return (endpoint, syncProviderFactory.CreateSyncProvider(endpoint.DataStoreConfiguration!, sessionLogger, tables));
    }

    [HttpGet("store-id")]
    public async Task<ActionResult<string>> GetStoreId(Guid endpointId, CancellationToken cancellationToken)
    {
        try
        {
            var (_, provider) = await GetProviderAsync(endpointId, cancellationToken);
            var storeId = await provider.GetStoreIdAsync(cancellationToken);
            return Ok(storeId.ToString());
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (AgentOfflineException ex)
        {
            return AgentOffline(ex);
        }
    }

    [HttpGet("sync-version")]
    public async Task<ActionResult<SyncVersion>> GetSyncVersion(Guid endpointId, CancellationToken cancellationToken)
    {
        try
        {
            var (_, provider) = await GetProviderAsync(endpointId, cancellationToken);
            var version = await provider.GetSyncVersionAsync(cancellationToken);
            return Ok(version);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (AgentOfflineException ex)
        {
            return AgentOffline(ex);
        }
    }

    private ObjectResult AgentOffline(AgentOfflineException ex)
    {
        logger.LogWarning(ex, "Agent offline for DataStore {DataStoreId}", ex.DataStoreId);
        return StatusCode(StatusCodes.Status503ServiceUnavailable, ex.Message);
    }

    [HttpGet("changes-bulk/{storeId:guid}")]
    public async Task<ActionResult<BulkSyncChangeSet>> GetBulkChangeSet(Guid endpointId, Guid storeId, CancellationToken cancellationToken)
    {
        try
        {
            var (endpoint, _) = await GetProviderAsync(endpointId, cancellationToken);
            var dataStoreId = endpoint.DataStoreConfiguration!.DataStoreId;

            var session = await syncSessionService.StartAsync(dataStoreId, cancellationToken);
            var sessionLogger = new SyncSessionLogger(logger, context, session.Id);

            try
            {
                var (_, provider) = await GetProviderAsync(endpointId, cancellationToken, sessionLogger);
                var filterParams = GetSyncFilterParameters(HttpContext);
                var changeSet = await provider.GetChangesAsync(storeId, syncFilterParameters: filterParams, syncDirection: SyncDirection.DownloadOnly, cancellationToken: cancellationToken);

                logger.LogInformation("GetBulkChangeSet(Endpoint={EndpointId}, StoreId={StoreId}) -> (Source={SourceAnchor} Target={TargetAnchor} Items={ItemsCount})",
                    endpointId, storeId, changeSet.SourceAnchor, changeSet.TargetAnchor, changeSet.Items.Count);

                await sessionLogger.FlushAsync(cancellationToken);
                await syncSessionService.CompleteAsync(session.Id, cancellationToken);

                var cacheSessionId = Guid.NewGuid();
                memoryCache.Set(cacheSessionId, new CachedSyncChangeSet { ChangeSet = changeSet });

                return Ok(new BulkSyncChangeSet
                {
                    SessionId = cacheSessionId,
                    TotalChanges = changeSet.Items.Count,
                    SourceAnchor = changeSet.SourceAnchor,
                    TargetAnchor = changeSet.TargetAnchor,
                    ChangesByTable = changeSet.Items
                        .GroupBy(i => i.TableName)
                        .ToDictionary(g => g.Key, g => g.Count())
                });
            }
            catch (Exception ex) when (ex is not KeyNotFoundException)
            {
                await sessionLogger.FlushAsync(cancellationToken);
                await syncSessionService.ErrorAsync(session.Id, ex.Message, cancellationToken);
                throw;
            }
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (AgentOfflineException ex)
        {
            return AgentOffline(ex);
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

            var bytes = CoreSyncMessagePackSerializer.Serialize<object>(bufferList);
            return File(bytes, "application/x-msgpack");
        }

        return NotFound();
    }

    [HttpPost("changes-bulk-begin")]
    public async Task<ActionResult> BeginApplyBulkChanges(Guid endpointId, [FromBody] BulkSyncChangeSet bulkChangeSet, CancellationToken cancellationToken)
    {
        try
        {
            var (endpoint, _) = await GetProviderAsync(endpointId, cancellationToken);

            var session = await syncSessionService.StartAsync(endpoint.DataStoreConfiguration!.DataStoreId, cancellationToken);

            var changeSet = new SyncChangeSet(bulkChangeSet.SourceAnchor, bulkChangeSet.TargetAnchor, new List<SyncItem>());
            memoryCache.Set(bulkChangeSet.SessionId, new CachedUploadSession
            {
                ChangeSet = changeSet,
                SyncSessionId = session.Id
            });

            return Ok();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("changes-bulk-item")]
    public ActionResult ApplyBulkChangesItem([FromBody] BulkChangeSetUploadItem bulkUploadItem)
    {
        if (memoryCache.TryGetValue(bulkUploadItem.SessionId, out var obj) && obj is CachedUploadSession cached)
        {
            ((List<SyncItem>)cached.ChangeSet.Items).AddRange(bulkUploadItem.Items);
            return Ok();
        }

        return NotFound();
    }

    [HttpPost("changes-bulk-item-binary")]
    public async Task<ActionResult> ApplyBulkChangesItemBinary()
    {
        var bulkUploadItem = ((BulkChangeSetUploadItem?)
            await CoreSyncMessagePackSerializer.DeserializeAsync<object>(Request.Body)) ?? throw new InvalidProgramException();

        if (bulkUploadItem is null)
            return BadRequest();

        if (memoryCache.TryGetValue(bulkUploadItem.SessionId, out var obj) && obj is CachedUploadSession cached)
        {
            ((List<SyncItem>)cached.ChangeSet.Items).AddRange(bulkUploadItem.Items);
            return Ok();
        }

        return NotFound();
    }

    [HttpPost("changes-bulk-complete/{sessionId:guid}")]
    public async Task<ActionResult<SyncAnchor>> CompleteApplyBulkChanges(Guid endpointId, Guid sessionId, CancellationToken cancellationToken)
    {
        if (memoryCache.TryGetValue(sessionId, out var obj) && obj is CachedUploadSession cached)
        {
            var changeSet = cached.ChangeSet;

            // Convert JSON elements to .NET objects
            foreach (var item in changeSet.Items)
            {
                foreach (var entry in item.Values.Where(e => e.Key != "__OP").ToList())
                {
                    item.Values[entry.Key].Value = entry.Value.Value == null ? null :
                        ConvertJsonElementToObject((JsonElement)entry.Value.Value, entry.Value.Type);
                }
            }

            var sessionLogger = new SyncSessionLogger(logger, context, cached.SyncSessionId);

            try
            {
                var (_, provider) = await GetProviderAsync(endpointId, cancellationToken, sessionLogger);
                var resAnchor = await provider.ApplyChangesAsync(changeSet, updateResultion: ConflictResolution.ForceWrite, deleteResolution: ConflictResolution.Skip);

                memoryCache.Remove(sessionId);

                logger.LogInformation("CompleteApplyBulkChanges(Endpoint={EndpointId}) => {Anchor}", endpointId, resAnchor);

                await sessionLogger.FlushAsync(cancellationToken);
                await syncSessionService.CompleteAsync(cached.SyncSessionId, cancellationToken);

                return Ok(resAnchor);
            }
            catch (KeyNotFoundException)
            {
                await sessionLogger.FlushAsync(cancellationToken);
                await syncSessionService.ErrorAsync(cached.SyncSessionId, $"Endpoint '{endpointId}' not found.", cancellationToken);
                return NotFound();
            }
            catch (AgentOfflineException ex)
            {
                await sessionLogger.FlushAsync(cancellationToken);
                await syncSessionService.ErrorAsync(cached.SyncSessionId, ex.Message, cancellationToken);
                return AgentOffline(ex);
            }
            catch (Exception ex)
            {
                await sessionLogger.FlushAsync(cancellationToken);
                await syncSessionService.ErrorAsync(cached.SyncSessionId, ex.Message, cancellationToken);
                throw;
            }
        }

        return NotFound();
    }

    [HttpPost("changes-bulk-complete-binary/{sessionId:guid}")]
    public async Task<ActionResult<SyncAnchor>> CompleteApplyBulkChangesBinary(Guid endpointId, Guid sessionId, CancellationToken cancellationToken)
    {
        if (memoryCache.TryGetValue(sessionId, out var obj) && obj is CachedUploadSession cached)
        {
            var changeSet = cached.ChangeSet;
            var sessionLogger = new SyncSessionLogger(logger, context, cached.SyncSessionId);

            try
            {
                var (_, provider) = await GetProviderAsync(endpointId, cancellationToken, sessionLogger);
                var resAnchor = await provider.ApplyChangesAsync(changeSet, updateResultion: ConflictResolution.ForceWrite, deleteResolution: ConflictResolution.Skip);

                memoryCache.Remove(sessionId);

                logger.LogInformation("CompleteApplyBulkChangesBinary(Endpoint={EndpointId}) => {Anchor}", endpointId, resAnchor);

                await sessionLogger.FlushAsync(cancellationToken);
                await syncSessionService.CompleteAsync(cached.SyncSessionId, cancellationToken);

                return Ok(resAnchor);
            }
            catch (KeyNotFoundException)
            {
                await sessionLogger.FlushAsync(cancellationToken);
                await syncSessionService.ErrorAsync(cached.SyncSessionId, $"Endpoint '{endpointId}' not found.", cancellationToken);
                return NotFound();
            }
            catch (AgentOfflineException ex)
            {
                await sessionLogger.FlushAsync(cancellationToken);
                await syncSessionService.ErrorAsync(cached.SyncSessionId, ex.Message, cancellationToken);
                return AgentOffline(ex);
            }
            catch (Exception ex)
            {
                await sessionLogger.FlushAsync(cancellationToken);
                await syncSessionService.ErrorAsync(cached.SyncSessionId, ex.Message, cancellationToken);
                throw;
            }
        }

        return NotFound();
    }

    [HttpPost("save-version/{storeId:guid}/{version:long}")]
    public async Task<ActionResult> SaveVersionForStore(Guid endpointId, Guid storeId, long version, CancellationToken cancellationToken)
    {
        try
        {
            var (_, provider) = await GetProviderAsync(endpointId, cancellationToken);

            logger.LogInformation("SaveVersionForStore(Endpoint={EndpointId}, StoreId={StoreId}, Version={Version})", endpointId, storeId, version);

            await provider.SaveVersionForStoreAsync(storeId, version, cancellationToken);
            return Ok();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (AgentOfflineException ex)
        {
            return AgentOffline(ex);
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
