using System.Text.Json;
using CoreSyncServer.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CoreSyncServer.Controllers;

/// <summary>
/// Export and import of a data store's configurations, so the table rules and endpoints
/// built against one data store (typically a development database) can be carried over
/// to another (typically production), on the same server or on a different one.
/// </summary>
public partial class DataStoresController
{
    /// <summary>
    /// Identifies the export file format so a file produced by an unrelated tool is rejected
    /// instead of being half-imported.
    /// </summary>
    public const string ExportFormat = "coresync-datastore-export";

    public const int ExportFormatVersion = 1;

    private static readonly JsonSerializerOptions ExportJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public record ExportedTable(
        string Name,
        string? Schema,
        int SyncMode,
        bool SkipInitialSnapshot,
        string? SelectIncrementalQuery,
        string? CustomSnapshotQuery,
        string? SkipColumns,
        string? SkipColumnsOnInsertOrUpdate,
        int IdentityInsert,
        bool ForceReloadInsertedRecords);

    public record ExportedEndpoint(string Name, string? Tags, EndpointAuthDto? Authentication);

    public record ExportedConfiguration(
        string Name,
        string? Description,
        int Version,
        ExportedTable[] Tables,
        ExportedEndpoint[] Endpoints);

    public record ExportedDataStore(string Name, string? Description, DataStoreType Type);

    public record DataStoreExportDocument(
        string Format,
        int FormatVersion,
        DateTime ExportedAt,
        ExportedDataStore DataStore,
        ExportedConfiguration[] Configurations);

    /// <summary>
    /// Exports every configuration of the data store, with its tables and endpoints, as a JSON
    /// document. Connection details of the data store itself are deliberately left out: the file
    /// is meant to be imported into a different data store, and the target already has its own.
    /// </summary>
    /// <remarks>
    /// Endpoint credentials (Basic passwords, API keys) are included verbatim, exactly as the
    /// dashboard already shows them, so an endpoint can be recreated as-is on the target. Treat
    /// the file accordingly.
    /// </remarks>
    [HttpGet("{id}/export")]
    public async Task<IActionResult> Export(int id)
    {
        var dataStore = await context.DataStores
            .Include(d => d.Configurations)
                .ThenInclude(c => c.TableConfigurations)
            .Include(d => d.Configurations)
                .ThenInclude(c => c.Endpoints)
                    .ThenInclude(e => e.Authentication)
            .AsSplitQuery()
            .FirstOrDefaultAsync(d => d.Id == id);

        if (dataStore is null) return NotFound();

        var configurations = dataStore.Configurations
            .OrderBy(c => c.Name)
            .Select(c => new ExportedConfiguration(
                c.Name,
                c.Description,
                c.Version,
                c.TableConfigurations
                    .OrderBy(t => t.Sort).ThenBy(t => t.Name)
                    .Select(t => new ExportedTable(
                        t.Name,
                        t.Schema,
                        (int)t.SyncMode,
                        t.SkipInitialSnapshot,
                        t.SelectIncrementalQuery,
                        t.CustomSnapshotQuery,
                        t.SkipColumns,
                        t.SkipColumnsOnInsertOrUpdate,
                        (int)t.IdentityInsert,
                        t.ForceReloadInsertedRecords))
                    .ToArray(),
                c.Endpoints
                    .OrderBy(e => e.Name)
                    .Select(e => new ExportedEndpoint(e.Name, e.Tags, MapAuthentication(e.Authentication)))
                    .ToArray()))
            .ToArray();

        var document = new DataStoreExportDocument(
            ExportFormat,
            ExportFormatVersion,
            DateTime.UtcNow,
            new ExportedDataStore(dataStore.Name, dataStore.Description, dataStore.Type),
            configurations);

        // Serialized by hand so the file is indented: it is meant to be read and diffed by people,
        // not only fed back into the import endpoint.
        return Content(JsonSerializer.Serialize(document, ExportJsonOptions), "application/json");
    }

    public record ImportDataStoreRequest(DataStoreExportDocument? Document, bool IncludeEndpoints);

    public record ImportDataStoreResult(
        int ConfigurationsCreated,
        int TablesImported,
        int EndpointsCreated);

    /// <summary>
    /// Imports the configurations of an export document into this data store.
    /// </summary>
    /// <remarks>
    /// Every configuration in the document is created as a new configuration on the target.
    /// Existing configurations are never touched: a name that is already in use (case-insensitive)
    /// is refused, and the dashboard asks for a different name before sending the document. The
    /// whole import is validated before anything is written, so the target is never left
    /// half-updated.
    ///
    /// Endpoints are only imported when asked for. They always get a fresh id and start
    /// unpublished: an endpoint id is the URL clients sync against, so the target must hand out
    /// its own rather than inherit the source's.
    /// </remarks>
    [HttpPost("{id}/import")]
    public async Task<ActionResult<ImportDataStoreResult>> Import(int id, [FromBody] ImportDataStoreRequest request)
    {
        var document = request.Document;
        if (document is null || document.Format != ExportFormat)
            return BadRequest(new[] { "The file is not a data store export." });

        if (document.FormatVersion > ExportFormatVersion)
            return BadRequest(new[] { $"The export was produced by a newer server (format version {document.FormatVersion}); this server supports version {ExportFormatVersion}." });

        if (document.Configurations is null || document.Configurations.Length == 0)
            return BadRequest(new[] { "The export contains no configurations." });

        var dataStore = await context.DataStores
            .Include(d => d.Configurations)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (dataStore is null) return NotFound();

        // Validate everything up front: the import is all-or-nothing.
        var errors = new List<string>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var exported in document.Configurations)
        {
            if (string.IsNullOrWhiteSpace(exported.Name))
            {
                errors.Add("Each configuration must have a name.");
                continue;
            }

            if (!seenNames.Add(exported.Name.Trim()))
                errors.Add($"Configuration '{exported.Name.Trim()}' appears more than once in the export.");

            if (exported.Version < 1)
                errors.Add($"Configuration '{exported.Name.Trim()}' has an invalid version.");

            foreach (var table in exported.Tables ?? [])
            {
                if (string.IsNullOrWhiteSpace(table.Name))
                    errors.Add($"Configuration '{exported.Name.Trim()}' has a table without a name.");
                if (!Enum.IsDefined(typeof(DataStoreTableConfigurationSyncMode), table.SyncMode))
                    errors.Add($"Table '{table.Name}' in configuration '{exported.Name.Trim()}' has an invalid sync mode.");
                if (!Enum.IsDefined(typeof(DataStoreTableConfigurationIdentityInsertMode), table.IdentityInsert))
                    errors.Add($"Table '{table.Name}' in configuration '{exported.Name.Trim()}' has an invalid identity insert mode.");
            }

            if (request.IncludeEndpoints)
            {
                foreach (var endpoint in exported.Endpoints ?? [])
                {
                    if (string.IsNullOrWhiteSpace(endpoint.Name))
                        errors.Add($"Configuration '{exported.Name.Trim()}' has an endpoint without a name.");
                    else if (endpoint.Authentication is not null && CreateAuthentication(endpoint.Authentication) is null)
                        errors.Add($"Endpoint '{endpoint.Name.Trim()}' in configuration '{exported.Name.Trim()}' has an invalid authentication configuration.");
                }
            }

            if (FindConfiguration(dataStore, exported.Name) is { } existing)
                errors.Add($"Configuration '{existing.Name}' already exists. Choose a different name.");
        }

        if (errors.Count > 0)
            return BadRequest(errors.ToArray());

        var result = new ImportDataStoreResult(0, 0, 0);

        foreach (var exported in document.Configurations)
        {
            var target = new DataStoreConfiguration
            {
                Name = exported.Name.Trim(),
                Description = exported.Description?.Trim(),
                DataStoreId = dataStore.Id,
                Version = exported.Version
            };
            dataStore.Configurations.Add(target);
            context.DataStoreConfigurations.Add(target);
            result = result with { ConfigurationsCreated = result.ConfigurationsCreated + 1 };

            var sort = 0;
            foreach (var table in exported.Tables ?? [])
            {
                target.TableConfigurations.Add(new DataStoreTableConfiguration
                {
                    Name = table.Name.Trim(),
                    Schema = table.Schema?.Trim(),
                    SyncMode = (DataStoreTableConfigurationSyncMode)table.SyncMode,
                    Sort = sort++,
                    SkipInitialSnapshot = table.SkipInitialSnapshot,
                    SelectIncrementalQuery = table.SelectIncrementalQuery?.Trim(),
                    CustomSnapshotQuery = table.CustomSnapshotQuery?.Trim(),
                    SkipColumns = table.SkipColumns?.Trim(),
                    SkipColumnsOnInsertOrUpdate = table.SkipColumnsOnInsertOrUpdate?.Trim(),
                    IdentityInsert = (DataStoreTableConfigurationIdentityInsertMode)table.IdentityInsert,
                    ForceReloadInsertedRecords = table.ForceReloadInsertedRecords
                });
                result = result with { TablesImported = result.TablesImported + 1 };
            }

            if (!request.IncludeEndpoints)
                continue;

            foreach (var endpoint in exported.Endpoints ?? [])
            {
                var created = new Data.Endpoint
                {
                    Id = Guid.NewGuid(),
                    Name = endpoint.Name.Trim(),
                    Tags = endpoint.Tags?.Trim(),
                    IsPublished = false,
                    DataStoreConfiguration = target,
                    Authentication = endpoint.Authentication is null ? null : CreateAuthentication(endpoint.Authentication)
                };

                // Added through the DbSet on purpose: the id is assigned here rather than generated,
                // and an entity with a set key that change tracking discovers through a navigation
                // is treated as an existing row to update, not a new one to insert.
                context.Endpoints.Add(created);
                target.Endpoints.Add(created);
                result = result with { EndpointsCreated = result.EndpointsCreated + 1 };
            }
        }

        await context.SaveChangesAsync();

        return Ok(result);
    }

    private static DataStoreConfiguration? FindConfiguration(DataStore dataStore, string name)
    {
        var trimmed = name.Trim();
        return dataStore.Configurations.FirstOrDefault(c => string.Equals(c.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static EndpointAuthDto? MapAuthentication(EndPointAuthentication? authentication) => authentication switch
    {
        BasicAuthentication b => new EndpointAuthDto((int)EndPointAuthenticationType.Basic, b.Username, b.Password, null, null, null, null, null),
        ApiKeyAuthentication a => new EndpointAuthDto((int)EndPointAuthenticationType.ApiKey, null, null, a.ApiKey, null, null, null, null),
        JwtAuthentication j => new EndpointAuthDto((int)EndPointAuthenticationType.Jwt, null, null, null, j.JWKSEndpoint, j.Issuer, j.UserIdClaim, j.UserNameClaim),
        _ => null
    };

    private static EndPointAuthentication? CreateAuthentication(EndpointAuthDto dto) =>
        (EndPointAuthenticationType)dto.Type switch
        {
            EndPointAuthenticationType.Basic when !string.IsNullOrWhiteSpace(dto.Username) && !string.IsNullOrWhiteSpace(dto.Password) =>
                new BasicAuthentication { Username = dto.Username.Trim(), Password = dto.Password.Trim() },
            EndPointAuthenticationType.ApiKey when !string.IsNullOrWhiteSpace(dto.ApiKey) =>
                new ApiKeyAuthentication { ApiKey = dto.ApiKey.Trim() },
            EndPointAuthenticationType.Jwt when !string.IsNullOrWhiteSpace(dto.JwksEndpoint) && !string.IsNullOrWhiteSpace(dto.Issuer) =>
                new JwtAuthentication
                {
                    JWKSEndpoint = dto.JwksEndpoint.Trim(),
                    Issuer = dto.Issuer.Trim(),
                    UserIdClaim = string.IsNullOrWhiteSpace(dto.UserIdClaim) ? "sub" : dto.UserIdClaim.Trim(),
                    UserNameClaim = dto.UserNameClaim?.Trim()
                },
            _ => null
        };
}
