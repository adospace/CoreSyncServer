using System.Security.Cryptography;
using CoreSyncServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CoreSyncServer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AgentsController(ApplicationDbContext context) : ControllerBase
{
    public record AgentDto(
        int Id,
        string Name,
        string? Description,
        bool Enabled,
        DateTime CreatedDate,
        DateTime? LastSeen,
        bool IsConnected,
        int DataStoresCount);

    public record AgentDetailDto(
        int Id,
        string Name,
        string? Description,
        bool Enabled,
        DateTime CreatedDate,
        DateTime? LastSeen,
        bool IsConnected,
        string ApiKey);

    public record AgentOption(int Id, string Name, bool Enabled, DateTime? LastSeen, bool IsConnected);

    public record CreateAgentRequest(string Name, string? Description, bool Enabled);
    public record UpdateAgentRequest(string Name, string? Description, bool Enabled);
    public record RegenerateApiKeyResponse(string ApiKey);

    public const int ConnectedThresholdSeconds = 45;

    private static bool IsConnected(DateTime? lastSeen) =>
        lastSeen is not null && (DateTime.UtcNow - lastSeen.Value).TotalSeconds <= ConnectedThresholdSeconds;

    [HttpGet]
    public async Task<ActionResult<List<AgentDto>>> GetAll([FromQuery] bool simple = false)
    {
        if (simple)
        {
            var options = await context.Agents
                .OrderBy(a => a.Name)
                .Select(a => new { a.Id, a.Name, a.Enabled, a.LastSeen })
                .ToListAsync();

            var mapped = options
                .Select(a => new AgentOption(a.Id, a.Name, a.Enabled, a.LastSeen, IsConnected(a.LastSeen)))
                .ToList();
            return Ok(mapped);
        }

        var agents = await context.Agents
            .OrderBy(a => a.Name)
            .Select(a => new { a.Id, a.Name, a.Description, a.Enabled, a.CreatedDate, a.LastSeen, Count = a.DataStores.Count })
            .ToListAsync();

        var result = agents
            .Select(a => new AgentDto(a.Id, a.Name, a.Description, a.Enabled, a.CreatedDate, a.LastSeen, IsConnected(a.LastSeen), a.Count))
            .ToList();

        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AgentDetailDto>> Get(int id)
    {
        var agent = await context.Agents.FindAsync(id);
        if (agent is null) return NotFound();

        return Ok(new AgentDetailDto(
            agent.Id,
            agent.Name,
            agent.Description,
            agent.Enabled,
            agent.CreatedDate,
            agent.LastSeen,
            IsConnected(agent.LastSeen),
            agent.ApiKey));
    }

    [HttpPost]
    public async Task<ActionResult<AgentDetailDto>> Create(CreateAgentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new[] { "Agent name is required." });
        }

        var name = request.Name.Trim();

        if (await context.Agents.AnyAsync(a => a.Name == name))
        {
            return BadRequest(new[] { "An agent with this name already exists." });
        }

        var agent = new Data.Agent
        {
            Name = name,
            Description = request.Description?.Trim(),
            Enabled = request.Enabled,
            ApiKey = GenerateApiKey(),
            CreatedDate = DateTime.UtcNow
        };

        context.Agents.Add(agent);
        await context.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = agent.Id },
            new AgentDetailDto(
                agent.Id,
                agent.Name,
                agent.Description,
                agent.Enabled,
                agent.CreatedDate,
                agent.LastSeen,
                IsConnected(agent.LastSeen),
                agent.ApiKey));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<AgentDto>> Update(int id, UpdateAgentRequest request)
    {
        var agent = await context.Agents.FindAsync(id);
        if (agent is null) return NotFound();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new[] { "Agent name is required." });
        }

        var name = request.Name.Trim();

        if (await context.Agents.AnyAsync(a => a.Name == name && a.Id != id))
        {
            return BadRequest(new[] { "An agent with this name already exists." });
        }

        agent.Name = name;
        agent.Description = request.Description?.Trim();
        agent.Enabled = request.Enabled;

        await context.SaveChangesAsync();

        var dataStoresCount = await context.DataStores.CountAsync(d => d.AgentId == id);

        return Ok(new AgentDto(
            agent.Id,
            agent.Name,
            agent.Description,
            agent.Enabled,
            agent.CreatedDate,
            agent.LastSeen,
            IsConnected(agent.LastSeen),
            dataStoresCount));
    }

    [HttpPost("{id}/regenerate-key")]
    public async Task<ActionResult<RegenerateApiKeyResponse>> RegenerateKey(int id)
    {
        var agent = await context.Agents.FindAsync(id);
        if (agent is null) return NotFound();

        agent.ApiKey = GenerateApiKey();
        await context.SaveChangesAsync();

        return Ok(new RegenerateApiKeyResponse(agent.ApiKey));
    }

    public record AgentDataStoreDto(int Id, string Name, string Type);

    [HttpGet("{id}/datastores")]
    public async Task<ActionResult<List<AgentDataStoreDto>>> GetDataStores(int id)
    {
        if (!await context.Agents.AnyAsync(a => a.Id == id))
            return NotFound();

        var stores = await context.DataStores
            .Where(d => d.AgentId == id)
            .OrderBy(d => d.Name)
            .Select(d => new AgentDataStoreDto(d.Id, d.Name, d.Type.ToString()))
            .ToListAsync();

        return Ok(stores);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var agent = await context.Agents.FindAsync(id);
        if (agent is null) return NotFound();

        context.Agents.Remove(agent);
        await context.SaveChangesAsync();

        return NoContent();
    }

    private static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return "csa_" + Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
