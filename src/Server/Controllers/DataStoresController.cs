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
                d.Type,
                d.Project!.Name,
                d.ProjectId,
                d.Configurations.Count,
                d.SyncSessions.Count))
            .ToListAsync();

        return Ok(dataStores);
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
