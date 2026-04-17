using CoreSyncServer.Agent.Contracts;
using CoreSyncServer.Data;
using CoreSyncServer.Filters;
using CoreSyncServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CoreSyncServer.Controllers;

/// <summary>
/// Endpoints invoked by agent processes (not by the admin UI).
/// Authenticated via <see cref="AgentApiKeyAuthFilter"/> using the X-Agent-ApiKey header.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/agent")]
[ServiceFilter(typeof(AgentApiKeyAuthFilter))]
public class AgentApiController(
    ApplicationDbContext context,
    IAgentConnectionTicketService ticketService) : ControllerBase
{
    private Data.Agent CurrentAgent => (Data.Agent)HttpContext.Items[AgentApiKeyAuthFilter.AgentKey]!;

    [HttpGet("datastores")]
    public async Task<ActionResult<AgentInitResponse>> GetDataStores()
    {
        var agent = CurrentAgent;

        var dataStores = await context.DataStores
            .Include(d => d.Configurations)
                .ThenInclude(c => c.TableConfigurations)
            .Where(d => d.AgentId == agent.Id)
            .OrderBy(d => d.Name)
            .ToListAsync(HttpContext.RequestAborted);

        var dtos = dataStores.Select(d => new AgentDataStoreDto(
            d.Id,
            d.Name,
            d.Description,
            d.Type.ToString(),
            (d as SqliteDataStore)?.FilePath,
            (d as SqlServerDataStore)?.ConnectionString ?? (d as PostgreSqlDataStore)?.ConnectionString,
            (d as SqlServerDataStore)?.TrackingMode.ToString(),
            ticketService.Issue(agent.Id, d.Id),
            $"ds-{d.Id}",
            d.Configurations
                .OrderBy(c => c.Name)
                .Select(c => new AgentDataStoreConfigurationDto(
                    c.Id,
                    c.Name,
                    c.Version,
                    c.TableConfigurations
                        .OrderBy(t => t.Name)
                        .Select(t => new AgentDataStoreTableDto(
                            t.Name,
                            t.Schema,
                            t.SyncMode.ToString(),
                            t.SkipInitialSnapshot))
                        .ToList()))
                .ToList()))
            .ToList();

        return Ok(new AgentInitResponse(
            agent.Id,
            agent.Name,
            AgentAuthHeaders.HubPath,
            dtos));
    }
}
