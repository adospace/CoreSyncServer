using System.Collections.Concurrent;
using CoreSyncServer.Agent.Contracts;
using CoreSyncServer.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace CoreSyncServer.Filters;

/// <summary>
/// Validates the <c>X-Agent-ApiKey</c> header against a persisted <see cref="Agent"/>,
/// and stashes the authenticated agent in <see cref="HttpContext.Items"/> for the controller.
/// Also updates <c>LastSeen</c> at most once per agent per minute.
/// </summary>
public class AgentApiKeyAuthFilter(ApplicationDbContext context) : IAsyncActionFilter
{
    public const string AgentKey = "AuthenticatedAgent";
    private static readonly TimeSpan LastSeenWriteThrottle = TimeSpan.FromSeconds(30);
    private static readonly ConcurrentDictionary<int, DateTime> LastSeenWrites = new();

    public async Task OnActionExecutionAsync(ActionExecutingContext filterContext, ActionExecutionDelegate next)
    {
        var httpContext = filterContext.HttpContext;

        if (!httpContext.Request.Headers.TryGetValue(AgentAuthHeaders.ApiKeyHeader, out var values)
            || string.IsNullOrWhiteSpace(values.FirstOrDefault()))
        {
            filterContext.Result = new UnauthorizedResult();
            return;
        }

        var apiKey = values.First()!;
        var agent = await context.Agents.FirstOrDefaultAsync(
            a => a.ApiKey == apiKey,
            httpContext.RequestAborted);

        if (agent is null || !agent.Enabled)
        {
            filterContext.Result = new UnauthorizedResult();
            return;
        }

        httpContext.Items[AgentKey] = agent;

        var now = DateTime.UtcNow;
        if (!LastSeenWrites.TryGetValue(agent.Id, out var lastWrite) || now - lastWrite >= LastSeenWriteThrottle)
        {
            agent.LastSeen = now;
            await context.SaveChangesAsync(httpContext.RequestAborted);
            LastSeenWrites[agent.Id] = now;
        }

        await next();
    }
}
