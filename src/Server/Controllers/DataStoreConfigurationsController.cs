using CoreSync;
using CoreSyncServer.Data;
using CoreSyncServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CoreSyncServer.Controllers;

[ApiController]
[Route("api/datastoreconfiguration")]
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
        int EndpointCount);

    public record ConfigurationDetailDto(
        int Id,
        string Name,
        string? Description,
        int Version,
        int DataStoreId,
        string DataStoreName);

    public record TableConfigDto(int Id, string Name, string? Schema, int SyncDirection, int Sort, string? Message);
    public record EndpointDto(Guid Id, string Name, string? Tags);

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
                c.Endpoints.Count))
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

        return Ok(new ConfigurationListDto(config.Id, config.Name, config.Description, config.Version, 0, 0));
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
            .FirstOrDefaultAsync(c => c.Id == id);

        if (config is null) return NotFound();

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
            .Select(t => new TableConfigDto(t.Id, t.Name, t.Schema, (int)t.SyncDirection, t.Sort, t.Message))
            .ToListAsync();

        return Ok(tables);
    }

    public record CreateTableConfigRequest(string Name, string? Schema, int SyncDirection);

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
            SyncDirection = (SyncDirection)request.SyncDirection,
            DataStoreConfigurationId = id
        };

        context.DataStoreTableConfigurations.Add(table);
        await context.SaveChangesAsync();

        return Ok(new TableConfigDto(table.Id, table.Name, table.Schema, (int)table.SyncDirection, table.Sort, table.Message));
    }

    public record UpdateTableConfigRequest(string Name, string? Schema, int SyncDirection);

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
        table.SyncDirection = (SyncDirection)request.SyncDirection;
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
            .Select(t => new TableConfigDto(t.Id, t.Name, t.Schema, (int)t.SyncDirection, t.Sort, t.Message))
            .ToList();

        return Ok(tables);
    }

    // Endpoints

    [HttpGet("{id}/endpoints")]
    public async Task<ActionResult<List<EndpointDto>>> GetEndpoints(int id)
    {
        var endpoints = await context.Endpoints
            .Where(e => e.DataStoreConfigurationId == id)
            .OrderBy(e => e.Name)
            .Select(e => new EndpointDto(e.Id, e.Name, e.Tags))
            .ToListAsync();

        return Ok(endpoints);
    }

    public record CreateEndpointRequest(string Name, string? Tags);

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

        context.Endpoints.Add(endpoint);
        await context.SaveChangesAsync();

        return Ok(new EndpointDto(endpoint.Id, endpoint.Name, endpoint.Tags));
    }

    public record UpdateEndpointRequest(string Name, string? Tags);

    [HttpPut("{id}/endpoints/{endpointId}")]
    public async Task<ActionResult> UpdateEndpoint(int id, Guid endpointId, [FromBody] UpdateEndpointRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new[] { "Name is required." });

        var endpoint = await context.Endpoints
            .FirstOrDefaultAsync(e => e.Id == endpointId && e.DataStoreConfigurationId == id);

        if (endpoint is null) return NotFound();

        endpoint.Name = request.Name.Trim();
        endpoint.Tags = request.Tags?.Trim();
        await context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}/endpoints/{endpointId}")]
    public async Task<ActionResult> DeleteEndpoint(int id, Guid endpointId)
    {
        var endpoint = await context.Endpoints
            .FirstOrDefaultAsync(e => e.Id == endpointId && e.DataStoreConfigurationId == id);

        if (endpoint is null) return NotFound();

        context.Endpoints.Remove(endpoint);
        await context.SaveChangesAsync();

        return NoContent();
    }
}
