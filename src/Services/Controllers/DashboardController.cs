using CoreSyncServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CoreSyncServer.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController(ApplicationDbContext context) : ControllerBase
{
    public record DashboardStatsDto(
        int TotalUsers,
        int ActiveSessions,
        int SyncEventsToday,
        int ErrorCountToday);

    public record ActivityDataPointDto(DateTime Date, int Completed, int Errors);

    public record DashboardActivityDto(List<ActivityDataPointDto> DataPoints);

    [HttpGet("stats")]
    public async Task<ActionResult<DashboardStatsDto>> GetStats()
    {
        var today = DateTime.UtcNow.Date;

        var totalUsers = await context.Users.CountAsync();
        var activeSessions = await context.SyncSessions
            .CountAsync(s => s.Status == SyncSessionStatus.Started);
        var syncEventsToday = await context.SyncSessions
            .CountAsync(s => s.StartTime >= today);
        var errorCountToday = await context.SyncSessions
            .CountAsync(s => s.Status == SyncSessionStatus.Error && s.StartTime >= today);

        return Ok(new DashboardStatsDto(totalUsers, activeSessions, syncEventsToday, errorCountToday));
    }

    [HttpGet("activity")]
    public async Task<ActionResult<DashboardActivityDto>> GetActivity(int days = 30)
    {
        days = Math.Clamp(days, 7, 90);
        var since = DateTime.UtcNow.Date.AddDays(-days + 1);

        var sessions = await context.SyncSessions
            .Where(s => s.StartTime >= since)
            .GroupBy(s => new { s.StartTime.Date, s.Status })
            .Select(g => new { g.Key.Date, g.Key.Status, Count = g.Count() })
            .ToListAsync();

        var dataPoints = new List<ActivityDataPointDto>();
        for (var date = since; date <= DateTime.UtcNow.Date; date = date.AddDays(1))
        {
            var completed = sessions
                .Where(s => s.Date == date && s.Status == SyncSessionStatus.Completed)
                .Sum(s => s.Count);
            var errors = sessions
                .Where(s => s.Date == date && s.Status == SyncSessionStatus.Error)
                .Sum(s => s.Count);

            dataPoints.Add(new ActivityDataPointDto(date, completed, errors));
        }

        return Ok(new DashboardActivityDto(dataPoints));
    }
}
