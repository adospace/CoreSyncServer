using CoreSyncServer.Data;
using CoreSyncServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace CoreSyncServer.Filters;

/// <summary>
/// Action filter that authenticates sync requests based on the endpoint's configured authentication.
/// Stores the <see cref="SyncAuthResult"/> in <see cref="HttpContext.Items"/> for use by the controller.
/// </summary>
public class SyncEndpointAuthFilter(
    ApplicationDbContext context,
    ISyncEndpointAuthService authService) : IAsyncActionFilter
{
    public const string AuthResultKey = "SyncAuthResult";

    public async Task OnActionExecutionAsync(ActionExecutingContext filterContext, ActionExecutionDelegate next)
    {
        if (!filterContext.ActionArguments.TryGetValue("endpointId", out var endpointIdObj) ||
            endpointIdObj is not Guid endpointId)
        {
            await next();
            return;
        }

        var endpoint = await context.Endpoints
            .Include(e => e.Authentication)
            .FirstOrDefaultAsync(e => e.Id == endpointId, filterContext.HttpContext.RequestAborted);

        // Same 404 as before, now carrying the endpoint-not-found code and a body: without a body the
        // host's status code pages would replace it with the HTML not-found page.
        if (endpoint is null || !endpoint.IsPublished)
        {
            filterContext.Result = SyncErrorResponses.EndpointNotFound(filterContext.HttpContext, endpointId);
            return;
        }

        var authResult = await authService.AuthenticateAsync(
            endpoint,
            filterContext.HttpContext,
            filterContext.HttpContext.RequestAborted);

        if (!authResult.IsAuthenticated)
        {
            filterContext.Result = new UnauthorizedObjectResult(new { error = authResult.ErrorMessage });
            return;
        }

        filterContext.HttpContext.Items[AuthResultKey] = authResult;

        await next();
    }
}
