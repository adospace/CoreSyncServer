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
        int SyncSessionsCount);

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
                d.SyncSessions.Count))
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
            0));
    }

    public record UpdateDataStoreRequest(string Name, string? Description);

    [HttpPut("{id}")]
    public async Task<ActionResult> Update(int id, [FromBody] UpdateDataStoreRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new[] { "Name is required." });

        var dataStore = await context.DataStores.FindAsync(id);
        if (dataStore is null) return NotFound();

        dataStore.Name = request.Name.Trim();
        dataStore.Description = request.Description?.Trim();
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
