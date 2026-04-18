using System.Security.Claims;
using CoreSyncServer.Agent.Contracts;
using CoreSyncServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace CoreSyncServer.Services.Hubs;

/// <summary>
/// Persistent connection hub for agents. Each agent opens one HubConnection per DataStore it manages;
/// every connection is placed into a per-DataStore group after a ticket handshake and registered in
/// <see cref="IAgentConnectionRegistry"/> so server-initiated RPCs can target the single client.
/// </summary>
[Authorize(AuthenticationSchemes = AgentAuthHeaders.AuthenticationScheme)]
public class AgentHub(
    ApplicationDbContext context,
    IAgentConnectionTicketService ticketService,
    IAgentConnectionRegistry connectionRegistry,
    ILogger<AgentHub> logger) : Hub<IAgentHubClient>, IAgentHub
{
    private int AgentId => int.Parse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new HubException("Missing agent identity."));

    public async Task<bool> AcknowledgeConnected(Guid connectionTicket)
    {
        if (!ticketService.TryConsume(connectionTicket, AgentId, out var dataStoreId))
        {
            logger.LogWarning("Agent {AgentId} presented invalid or expired ticket {Ticket}", AgentId, connectionTicket);
            Context.Abort();
            return false;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"ds-{dataStoreId}");
        connectionRegistry.Register(dataStoreId, Context.ConnectionId);
        await TouchLastSeen();

        logger.LogInformation("Agent {AgentId} connected for DataStore {DataStoreId}", AgentId, dataStoreId);
        return true;
    }

    public Task Heartbeat() => TouchLastSeen();

    public Task ReportSyncProgress(SyncProgressMessage message)
    {
        logger.LogDebug("Sync progress {Session}: {Processed}/{Total} ({Table})",
            message.SessionId, message.Processed, message.Total, message.CurrentTable);
        return Task.CompletedTask;
    }

    public override async Task OnConnectedAsync()
    {
        await TouchLastSeen();
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        connectionRegistry.Unregister(Context.ConnectionId);
        await TouchLastSeen();
        await base.OnDisconnectedAsync(exception);
    }

    private async Task TouchLastSeen()
    {
        var agentId = AgentId;
        var agent = await context.Agents.FindAsync(agentId);
        if (agent is null) return;
        agent.LastSeen = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }
}
