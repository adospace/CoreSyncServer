using CoreSync;
using CoreSyncServer.Data;
using CoreSyncServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CoreSyncServer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DataStoreConfigurationsController(
    ApplicationDbContext context,
    ITableConfigurationService tableConfigurationService) : ControllerBase
{
    public record ConfigurationListDto(
        int Id,
        string Name,
        string? Description,
        int Version,
        int TableConfigCount,
        int EndpointCount,
        bool InError);

    public record ConfigurationDetailDto(
        int Id,
        string Name,
        string? Description,
        int Version,
        int DataStoreId,
        string DataStoreName);

    public record TableConfigDto(int Id, string Name, string? Schema, int SyncMode, bool InError, int Sort, string? Message);
    public record AuthenticationDto(int Type, string? Username, string? Password, string? ApiKey, string? JwksEndpoint, string? Issuer, string? UserIdClaim, string? UserNameClaim);
    public record EndpointDto(Guid Id, string Name, string? Tags, AuthenticationDto? Authentication);

    [HttpGet]
    public async Task<ActionResult<List<ConfigurationListDto>>> GetAll([FromQuery] int dataStoreId)
    {
        var configurations = await context.DataStoreConfigurations
            .Where(c => c.DataStoreId == dataStoreId)
            .OrderBy(c => c.Name)
            .Select(c => new ConfigurationListDto(
                c.Id,
                c.Name,
                c.Description,
                c.Version,
                c.TableConfigurations.Count,
                c.Endpoints.Count,
                c.TableConfigurations.Any(t => t.InError)))
            .ToListAsync();

        return Ok(configurations);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ConfigurationDetailDto>> Get(int id)
    {
        var config = await context.DataStoreConfigurations
            .Include(c => c.DataStore)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (config is null) return NotFound();

        return Ok(new ConfigurationDetailDto(
            config.Id,
            config.Name,
            config.Description,
            config.Version,
            config.DataStoreId,
            config.DataStore!.Name));
    }

    public record CreateConfigurationRequest(string Name, int DataStoreId);

    [HttpPost]
    public async Task<ActionResult<ConfigurationListDto>> Create([FromBody] CreateConfigurationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new[] { "Name is required." });

        var dataStore = await context.DataStores.FindAsync(request.DataStoreId);
        if (dataStore is null)
            return BadRequest(new[] { "Data store not found." });

        var config = new DataStoreConfiguration
        {
            Name = request.Name.Trim(),
            DataStoreId = request.DataStoreId,
            Version = 1
        };

        context.DataStoreConfigurations.Add(config);
        await context.SaveChangesAsync();

        return Ok(new ConfigurationListDto(config.Id, config.Name, config.Description, config.Version, 0, 0, false));
    }

    public record UpdateConfigurationRequest(string Name, string? Description, int Version);

    [HttpPut("{id}")]
    public async Task<ActionResult> Update(int id, [FromBody] UpdateConfigurationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new[] { "Name is required." });

        var config = await context.DataStoreConfigurations.FindAsync(id);
        if (config is null) return NotFound();

        config.Name = request.Name.Trim();
        config.Description = request.Description?.Trim();
        config.Version = request.Version;
        await context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var config = await context.DataStoreConfigurations
            .Include(c => c.TableConfigurations)
            .Include(c => c.Endpoints)
                .ThenInclude(e => e.Authentication)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (config is null) return NotFound();

        var authRecords = config.Endpoints.Where(e => e.Authentication is not null).Select(e => e.Authentication!);
        context.EndPointAuthentications.RemoveRange(authRecords);
        context.DataStoreConfigurations.Remove(config);
        await context.SaveChangesAsync();

        return NoContent();
    }

    // Table Configurations

    [HttpGet("{id}/tables")]
    public async Task<ActionResult<List<TableConfigDto>>> GetTables(int id)
    {
        var tables = await context.DataStoreTableConfigurations
            .Where(t => t.DataStoreConfigurationId == id)
            .OrderBy(t => t.Sort).ThenBy(t => t.Name)
            .Select(t => new TableConfigDto(t.Id, t.Name, t.Schema, (int)t.SyncMode, t.InError, t.Sort, t.Message))
            .ToListAsync();

        return Ok(tables);
    }

    public record CreateTableConfigRequest(string Name, string? Schema, int SyncMode);

    [HttpPost("{id}/tables")]
    public async Task<ActionResult<TableConfigDto>> CreateTable(int id, [FromBody] CreateTableConfigRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new[] { "Name is required." });

        var config = await context.DataStoreConfigurations.FindAsync(id);
        if (config is null) return NotFound();

        var table = new DataStoreTableConfiguration
        {
            Name = request.Name.Trim(),
            Schema = request.Schema?.Trim(),
            SyncMode = (DataStoreTableConfigurationSyncMode)request.SyncMode,
            DataStoreConfigurationId = id
        };

        context.DataStoreTableConfigurations.Add(table);
        await context.SaveChangesAsync();

        return Ok(new TableConfigDto(table.Id, table.Name, table.Schema, (int)table.SyncMode, table.InError, table.Sort, table.Message));
    }

    public record UpdateTableConfigRequest(string Name, string? Schema, int SyncMode);

    [HttpPut("{id}/tables/{tableId}")]
    public async Task<ActionResult> UpdateTable(int id, int tableId, [FromBody] UpdateTableConfigRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new[] { "Name is required." });

        var table = await context.DataStoreTableConfigurations
            .FirstOrDefaultAsync(t => t.Id == tableId && t.DataStoreConfigurationId == id);

        if (table is null) return NotFound();

        table.Name = request.Name.Trim();
        table.Schema = request.Schema?.Trim();
        table.SyncMode = (DataStoreTableConfigurationSyncMode)request.SyncMode;
        await context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}/tables/{tableId}")]
    public async Task<ActionResult> DeleteTable(int id, int tableId)
    {
        var table = await context.DataStoreTableConfigurations
            .FirstOrDefaultAsync(t => t.Id == tableId && t.DataStoreConfigurationId == id);

        if (table is null) return NotFound();

        context.DataStoreTableConfigurations.Remove(table);
        await context.SaveChangesAsync();

        return NoContent();
    }

    // Scaffold & Sort

    [HttpPost("{id}/tables/scaffold")]
    public async Task<ActionResult<List<TableConfigDto>>> ScaffoldTables(int id)
    {
        var result = await tableConfigurationService.ScaffoldAsync(id);
        return ToTableResponse(result);
    }

    [HttpPost("{id}/tables/update")]
    public async Task<ActionResult<List<TableConfigDto>>> UpdateTables(int id)
    {
        var result = await tableConfigurationService.UpdateAsync(id);
        return ToTableResponse(result);
    }

    [HttpPost("{id}/tables/sort")]
    public async Task<ActionResult<List<TableConfigDto>>> SortTables(int id)
    {
        var result = await tableConfigurationService.SortAsync(id);
        return ToTableResponse(result);
    }

    private ActionResult<List<TableConfigDto>> ToTableResponse(TableConfigurationResult result)
    {
        if (!result.Success)
            return result.Error == "Configuration not found." ? NotFound() : BadRequest(new[] { result.Error });

        var tables = result.Tables
            .Select(t => new TableConfigDto(t.Id, t.Name, t.Schema, (int)t.SyncMode, t.InError, t.Sort, t.Message))
            .ToList();

        return Ok(tables);
    }

    // Endpoints

    [HttpGet("{id}/endpoints")]
    public async Task<ActionResult<List<EndpointDto>>> GetEndpoints(int id)
    {
        var endpoints = await context.Endpoints
            .Include(e => e.Authentication)
            .Where(e => e.DataStoreConfigurationId == id)
            .OrderBy(e => e.Name)
            .ToListAsync();

        return Ok(endpoints.Select(MapEndpoint).ToList());
    }

    public record CreateEndpointRequest(string Name, string? Tags, AuthenticationDto? Authentication);

    [HttpPost("{id}/endpoints")]
    public async Task<ActionResult<EndpointDto>> CreateEndpoint(int id, [FromBody] CreateEndpointRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new[] { "Name is required." });

        var config = await context.DataStoreConfigurations.FindAsync(id);
        if (config is null) return NotFound();

        var endpoint = new Data.Endpoint
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Tags = request.Tags?.Trim(),
            DataStoreConfigurationId = id
        };

        if (request.Authentication is not null)
        {
            var auth = CreateAuthentication(request.Authentication);
            if (auth is null)
                return BadRequest(new[] { "Invalid authentication configuration." });
            endpoint.Authentication = auth;
        }

        context.Endpoints.Add(endpoint);
        await context.SaveChangesAsync();

        return Ok(MapEndpoint(endpoint));
    }

    public record UpdateEndpointRequest(string Name, string? Tags, AuthenticationDto? Authentication);

    [HttpPut("{id}/endpoints/{endpointId}")]
    public async Task<ActionResult> UpdateEndpoint(int id, Guid endpointId, [FromBody] UpdateEndpointRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new[] { "Name is required." });

        var endpoint = await context.Endpoints
            .Include(e => e.Authentication)
            .FirstOrDefaultAsync(e => e.Id == endpointId && e.DataStoreConfigurationId == id);

        if (endpoint is null) return NotFound();

        endpoint.Name = request.Name.Trim();
        endpoint.Tags = request.Tags?.Trim();

        // Handle authentication changes
        if (request.Authentication is null)
        {
            // Remove existing auth
            if (endpoint.Authentication is not null)
            {
                context.EndPointAuthentications.Remove(endpoint.Authentication);
                endpoint.Authentication = null;
                endpoint.AuthenticationId = null;
            }
        }
        else
        {
            var newType = (EndPointAuthenticationType)request.Authentication.Type;

            if (endpoint.Authentication is not null && endpoint.Authentication.Type != newType)
            {
                // Type changed — must delete and recreate (TPH discriminator can't be updated)
                context.EndPointAuthentications.Remove(endpoint.Authentication);
                endpoint.Authentication = CreateAuthentication(request.Authentication);
            }
            else if (endpoint.Authentication is not null)
            {
                // Same type — update in place
                UpdateAuthentication(endpoint.Authentication, request.Authentication);
            }
            else
            {
                // No existing auth — create new
                endpoint.Authentication = CreateAuthentication(request.Authentication);
            }

            if (endpoint.Authentication is null)
                return BadRequest(new[] { "Invalid authentication configuration." });
        }

        await context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}/endpoints/{endpointId}")]
    public async Task<ActionResult> DeleteEndpoint(int id, Guid endpointId)
    {
        var endpoint = await context.Endpoints
            .Include(e => e.Authentication)
            .FirstOrDefaultAsync(e => e.Id == endpointId && e.DataStoreConfigurationId == id);

        if (endpoint is null) return NotFound();

        if (endpoint.Authentication is not null)
            context.EndPointAuthentications.Remove(endpoint.Authentication);

        context.Endpoints.Remove(endpoint);
        await context.SaveChangesAsync();

        return NoContent();
    }

    private static EndpointDto MapEndpoint(Data.Endpoint e) =>
        new(e.Id, e.Name, e.Tags, e.Authentication switch
        {
            BasicAuthentication b => new AuthenticationDto((int)EndPointAuthenticationType.Basic, b.Username, b.Password, null, null, null, null, null),
            ApiKeyAuthentication a => new AuthenticationDto((int)EndPointAuthenticationType.ApiKey, null, null, a.ApiKey, null, null, null, null),
            JwtAuthentication j => new AuthenticationDto((int)EndPointAuthenticationType.Jwt, null, null, null, j.JWKSEndpoint, j.Issuer, j.UserIdClaim, j.UserNameClaim),
            _ => null
        });

    private static EndPointAuthentication? CreateAuthentication(AuthenticationDto dto) =>
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

    private static void UpdateAuthentication(EndPointAuthentication auth, AuthenticationDto dto)
    {
        switch (auth)
        {
            case BasicAuthentication b:
                b.Username = dto.Username?.Trim() ?? b.Username;
                b.Password = dto.Password?.Trim() ?? b.Password;
                break;
            case ApiKeyAuthentication a:
                a.ApiKey = dto.ApiKey?.Trim() ?? a.ApiKey;
                break;
            case JwtAuthentication j:
                j.JWKSEndpoint = dto.JwksEndpoint?.Trim() ?? j.JWKSEndpoint;
                j.Issuer = dto.Issuer?.Trim() ?? j.Issuer;
                j.UserIdClaim = string.IsNullOrWhiteSpace(dto.UserIdClaim) ? j.UserIdClaim : dto.UserIdClaim.Trim();
                j.UserNameClaim = dto.UserNameClaim?.Trim();
                break;
        }
    }
}
