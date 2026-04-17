using System.Security.Claims;
using System.Text.Encodings.Web;
using CoreSyncServer.Agent.Contracts;
using CoreSyncServer.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoreSyncServer.Services.Authentication;

public class AgentApiKeyAuthenticationOptions : AuthenticationSchemeOptions { }

/// <summary>
/// Authenticates SignalR (and any other) requests by matching the X-Agent-ApiKey header
/// (or an <c>access_token</c> query parameter for WebSocket upgrades) to an enabled Agent.
/// </summary>
public class AgentApiKeyAuthenticationHandler(
    IOptionsMonitor<AgentApiKeyAuthenticationOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    ApplicationDbContext context)
    : AuthenticationHandler<AgentApiKeyAuthenticationOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var apiKey = ExtractApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            return AuthenticateResult.NoResult();

        var agent = await context.Agents
            .FirstOrDefaultAsync(a => a.ApiKey == apiKey, Context.RequestAborted);

        if (agent is null || !agent.Enabled)
            return AuthenticateResult.Fail("Invalid API key.");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, agent.Id.ToString()),
            new Claim(ClaimTypes.Name, agent.Name)
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    private string? ExtractApiKey()
    {
        if (Context.Request.Headers.TryGetValue(AgentAuthHeaders.ApiKeyHeader, out var header)
            && !string.IsNullOrWhiteSpace(header.FirstOrDefault()))
        {
            return header.First();
        }

        // SignalR WebSocket transport cannot send arbitrary headers; fall back to the access_token query string.
        var path = Context.Request.Path;
        if (path.StartsWithSegments(AgentAuthHeaders.HubPath)
            && Context.Request.Query.TryGetValue("access_token", out var token)
            && !string.IsNullOrWhiteSpace(token.FirstOrDefault()))
        {
            return token.First();
        }

        return null;
    }
}
