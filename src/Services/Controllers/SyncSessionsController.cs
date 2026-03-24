using CoreSyncServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CoreSyncServer.Controllers;

[ApiController]
[Route("api/sync-sessions")]
[Authorize]
public class SyncSessionsController(ApplicationDbContext context) : ControllerBase
{
    public record SyncSessionDto(
        int Id,
        DateTime StartTime,
        DateTime? EndTime,
        SyncSessionStatus Status,
        int DataStoreId,
        string DataStoreName,
        int? ProjectId,
        string? ProjectName,
        int DiagnosticCount,
        int TraceCount);

    public record SyncSessionsPagedResult(List<SyncSessionDto> Items, int TotalCount);

    public record SyncSessionTraceDto(
        int Id,
        string Message,
        DateTime TimeStamp,
        int TraceLevel);

    public record SyncSessionTracesPagedResult(List<SyncSessionTraceDto> Items, int TotalCount);

    public record SyncSessionDetailDto(
        int Id,
        DateTime StartTime,
        DateTime? EndTime,
        SyncSessionStatus Status,
        string DataStoreName,
        string? ProjectName,
        int TraceCount,
        int DiagnosticCount);

    [HttpGet]
    public async Task<ActionResult<SyncSessionsPagedResult>> GetAll(
        int page = 0,
        int pageSize = 15,
        int? projectId = null,
        int? dataStoreId = null,
        SyncSessionStatus? status = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        string? search = null)
    {
        var query = context.SyncSessions.AsQueryable();

        if (projectId.HasValue)
            query = query.Where(s => s.DataStore!.ProjectId == projectId.Value);

        if (dataStoreId.HasValue)
            query = query.Where(s => s.DataStoreId == dataStoreId.Value);

        if (status.HasValue)
            query = query.Where(s => s.Status == status.Value);

        if (dateFrom.HasValue)
            query = query.Where(s => s.StartTime >= dateFrom.Value);

        if (dateTo.HasValue)
            query = query.Where(s => s.StartTime <= dateTo.Value);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(s => s.DataStore!.Name.Contains(search) ||
                                     s.DataStore!.Project!.Name.Contains(search));

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(s => s.StartTime)
            .Skip(page * pageSize)
            .Take(pageSize)
            .Select(s => new SyncSessionDto(
                s.Id,
                s.StartTime,
                s.EndTime,
                s.Status,
                s.DataStoreId,
                s.DataStore!.Name,
                s.DataStore.ProjectId,
                s.DataStore.Project != null ? s.DataStore.Project.Name : null,
                s.DiagnosticItems.Count,
                s.Traces.Count))
            .ToListAsync();

        return Ok(new SyncSessionsPagedResult(items, totalCount));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SyncSessionDetailDto>> GetById(int id)
    {
        var session = await context.SyncSessions
            .Where(s => s.Id == id)
            .Select(s => new SyncSessionDetailDto(
                s.Id,
                s.StartTime,
                s.EndTime,
                s.Status,
                s.DataStore!.Name,
                s.DataStore.Project != null ? s.DataStore.Project.Name : null,
                s.Traces.Count,
                s.DiagnosticItems.Count))
            .FirstOrDefaultAsync();

        if (session is null)
            return NotFound();

        return Ok(session);
    }

    [HttpGet("{id:int}/traces")]
    public async Task<ActionResult<SyncSessionTracesPagedResult>> GetTraces(
        int id,
        int page = 0,
        int pageSize = 25,
        int? traceLevel = null,
        string? search = null)
    {
        var exists = await context.SyncSessions.AnyAsync(s => s.Id == id);
        if (!exists)
            return NotFound();

        var query = context.SyncSessionTraces
            .Where(t => t.SyncSessionId == id);

        if (traceLevel.HasValue)
            query = query.Where(t => (int)t.TraceLevel <= traceLevel.Value && t.TraceLevel != System.Diagnostics.TraceLevel.Off);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(t => t.Message.Contains(search));

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(t => t.TimeStamp)
            .Skip(page * pageSize)
            .Take(pageSize)
            .Select(t => new SyncSessionTraceDto(t.Id, t.Message, t.TimeStamp, (int)t.TraceLevel))
            .ToListAsync();

        return Ok(new SyncSessionTracesPagedResult(items, totalCount));
    }
}
