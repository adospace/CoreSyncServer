using CoreSyncServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CoreSyncServer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProjectsController(ApplicationDbContext context) : ControllerBase
{
    public record ProjectDto(int Id, string Name, string? Description, bool IsEnabled, string? Tags, DateTime CreatedDate);
    public record CreateProjectRequest(string Name, string? Description, bool IsEnabled, string? Tags);
    public record UpdateProjectRequest(string Name, string? Description, string? Tags);

    [HttpGet]
    public async Task<ActionResult<List<ProjectDto>>> GetAll()
    {
        var projects = await context.Projects
            .OrderBy(p => p.Name)
            .Select(p => new ProjectDto(p.Id, p.Name, p.Description, p.IsEnabled, p.Tags, p.CreatedDate))
            .ToListAsync();

        return Ok(projects);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ProjectDto>> Get(int id)
    {
        var project = await context.Projects.FindAsync(id);
        if (project is null) return NotFound();

        return Ok(new ProjectDto(project.Id, project.Name, project.Description, project.IsEnabled, project.Tags, project.CreatedDate));
    }

    [HttpPost]
    public async Task<ActionResult<ProjectDto>> Create(CreateProjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new[] { "Project name is required." });
        }

        if (await context.Projects.AnyAsync(p => p.Name == request.Name))
        {
            return BadRequest(new[] { "A project with this name already exists." });
        }

        var project = new Project
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsEnabled = request.IsEnabled,
            Tags = request.Tags?.Trim(),
            CreatedDate = DateTime.UtcNow
        };

        context.Projects.Add(project);
        await context.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = project.Id },
            new ProjectDto(project.Id, project.Name, project.Description, project.IsEnabled, project.Tags, project.CreatedDate));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ProjectDto>> Update(int id, UpdateProjectRequest request)
    {
        var project = await context.Projects.FindAsync(id);
        if (project is null) return NotFound();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new[] { "Project name is required." });
        }

        if (await context.Projects.AnyAsync(p => p.Name == request.Name && p.Id != id))
        {
            return BadRequest(new[] { "A project with this name already exists." });
        }

        project.Name = request.Name.Trim();
        project.Description = request.Description?.Trim();
        project.Tags = request.Tags?.Trim();

        await context.SaveChangesAsync();

        return Ok(new ProjectDto(project.Id, project.Name, project.Description, project.IsEnabled, project.Tags, project.CreatedDate));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var project = await context.Projects.FindAsync(id);
        if (project is null) return NotFound();

        context.Projects.Remove(project);
        await context.SaveChangesAsync();

        return NoContent();
    }
}
