using CoreSyncServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CoreSyncServer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DiagnosticsController(ApplicationDbContext context) : ControllerBase
{
    public record DiagnosticItemDto(
        int Id,
        string Message,
        LogItemLevel Level,
        DateTime Timestamp,
        bool IsResolved,
        int? ProjectId,
        string? ProjectName,
        string? DataStoreName,
        string? ConfigurationName,
        string? EndpointName);

    public record DiagnosticsPagedResult(List<DiagnosticItemDto> Items, int TotalCount);
    public record ResolveRequest(int[] Ids);

    [HttpGet]
    public async Task<ActionResult<DiagnosticsPagedResult>> GetAll(
        int page = 0,
        int pageSize = 10,
        string? message = null,
        LogItemLevel? level = null,
        int? projectId = null,
        bool showResolved = false)
    {
        var query = context.DiagnosticItems.AsQueryable();

        if (!showResolved)
            query = query.Where(d => !d.IsResolved);

        if (!string.IsNullOrWhiteSpace(message))
            query = query.Where(d => d.Message.Contains(message));

        if (level.HasValue)
            query = query.Where(d => d.Level == level.Value);

        if (projectId.HasValue)
            query = query.Where(d => d.ProjectId == projectId.Value);

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(d => d.Timestamp)
            .Skip(page * pageSize)
            .Take(pageSize)
            .Select(d => new DiagnosticItemDto(
                d.Id,
                d.Message,
                d.Level,
                d.Timestamp,
                d.IsResolved,
                d.ProjectId,
                d.Project != null ? d.Project.Name : null,
                d.DataStore != null ? d.DataStore.Name : null,
                d.DataStoreConfiguration != null ? d.DataStoreConfiguration.Name : null,
                d.EndPoint != null ? d.EndPoint.Name : null))
            .ToListAsync();

        return Ok(new DiagnosticsPagedResult(items, totalCount));
    }

    [HttpPost("resolve")]
    public async Task<ActionResult> Resolve(ResolveRequest request)
    {
        if (request.Ids is not { Length: > 0 })
            return BadRequest(new[] { "No items specified." });

        await context.DiagnosticItems
            .Where(d => request.Ids.Contains(d.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.IsResolved, true));

        return NoContent();
    }
}
