using CoreSyncServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CoreSyncServer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DataStoresController(ApplicationDbContext context) : ControllerBase
{
    public record DataStoreDto(
        int Id,
        string Name,
        string? Description,
        DataStoreType Type,
        string ProjectName,
        int ProjectId,
        int ConfigurationsCount,
        int SyncSessionsCount,
        bool IsMonitorEnabled);

    [HttpGet]
    public async Task<ActionResult<List<DataStoreDto>>> GetAll([FromQuery] int? projectId)
    {
        var query = context.DataStores
            .Include(d => d.Project)
            .AsQueryable();

        if (projectId.HasValue)
        {
            query = query.Where(d => d.ProjectId == projectId.Value);
        }

        var dataStores = await query
            .OrderBy(d => d.Name)
            .Select(d => new DataStoreDto(
                d.Id,
                d.Name,
                d.Description,
                d.Type,
                d.Project!.Name,
                d.ProjectId,
                d.Configurations.Count,
                d.SyncSessions.Count,
                d.IsMonitorEnabled))
            .ToListAsync();

        return Ok(dataStores);
    }

    public record CreateDataStoreRequest(
        string Name,
        string? Description,
        DataStoreType Type,
        int ProjectId,
        string? FilePath,
        string? ConnectionString,
        SqlServerDataStoreTrackingMode? TrackingMode);

    [HttpPost]
    public async Task<ActionResult<DataStoreDto>> Create([FromBody] CreateDataStoreRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new[] { "Name is required." });

        var project = await context.Projects.FindAsync(request.ProjectId);
        if (project is null)
            return BadRequest(new[] { "Project not found." });

        DataStore dataStore = request.Type switch
        {
            DataStoreType.SQLite => new SqliteDataStore
            {
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                ProjectId = request.ProjectId,
                Type = DataStoreType.SQLite,
                FilePath = request.FilePath ?? ""
            },
            DataStoreType.SqlServer => new SqlServerDataStore
            {
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                ProjectId = request.ProjectId,
                Type = DataStoreType.SqlServer,
                ConnectionString = request.ConnectionString ?? "",
                TrackingMode = request.TrackingMode ?? SqlServerDataStoreTrackingMode.ChangeTracking
            },
            DataStoreType.PostgreSQL => new PostgreSqlDataStore
            {
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                ProjectId = request.ProjectId,
                Type = DataStoreType.PostgreSQL,
                ConnectionString = request.ConnectionString ?? ""
            },
            _ => throw new ArgumentOutOfRangeException()
        };

        context.DataStores.Add(dataStore);
        await context.SaveChangesAsync();

        return Ok(new DataStoreDto(
            dataStore.Id,
            dataStore.Name,
            dataStore.Description,
            dataStore.Type,
            project.Name,
            dataStore.ProjectId,
            0,
            0,
            dataStore.IsMonitorEnabled));
    }

    public record DataStoreDetailDto(
        int Id,
        string Name,
        string? Description,
        DataStoreType Type,
        string ProjectName,
        int ProjectId,
        bool IsMonitorEnabled,
        string? FilePath,
        string? ConnectionString,
        SqlServerDataStoreTrackingMode? TrackingMode);

    [HttpGet("{id}")]
    public async Task<ActionResult<DataStoreDetailDto>> GetById(int id)
    {
        var dataStore = await context.DataStores
            .Include(d => d.Project)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (dataStore is null) return NotFound();

        return Ok(new DataStoreDetailDto(
            dataStore.Id,
            dataStore.Name,
            dataStore.Description,
            dataStore.Type,
            dataStore.Project!.Name,
            dataStore.ProjectId,
            dataStore.IsMonitorEnabled,
            (dataStore as SqliteDataStore)?.FilePath,
            (dataStore as SqlServerDataStore)?.ConnectionString
                ?? (dataStore as PostgreSqlDataStore)?.ConnectionString,
            (dataStore as SqlServerDataStore)?.TrackingMode));
    }

    public record UpdateDataStoreRequest(
        string Name,
        string? Description,
        string? FilePath,
        string? ConnectionString,
        SqlServerDataStoreTrackingMode? TrackingMode);

    [HttpPut("{id}")]
    public async Task<ActionResult> Update(int id, [FromBody] UpdateDataStoreRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new[] { "Name is required." });

        var dataStore = await context.DataStores.FindAsync(id);
        if (dataStore is null) return NotFound();

        dataStore.Name = request.Name.Trim();
        dataStore.Description = request.Description?.Trim();

        switch (dataStore)
        {
            case SqliteDataStore sqlite:
                if (request.FilePath is not null)
                    sqlite.FilePath = request.FilePath.Trim();
                break;
            case SqlServerDataStore sqlServer:
                if (request.ConnectionString is not null)
                    sqlServer.ConnectionString = request.ConnectionString.Trim();
                if (request.TrackingMode.HasValue)
                    sqlServer.TrackingMode = request.TrackingMode.Value;
                break;
            case PostgreSqlDataStore postgres:
                if (request.ConnectionString is not null)
                    postgres.ConnectionString = request.ConnectionString.Trim();
                break;
        }

        await context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var dataStore = await context.DataStores.FindAsync(id);
        if (dataStore is null) return NotFound();

        context.DataStores.Remove(dataStore);
        await context.SaveChangesAsync();

        return NoContent();
    }
}
