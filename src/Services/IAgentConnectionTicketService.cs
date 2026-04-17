namespace CoreSyncServer.Services;

public interface IAgentConnectionTicketService
{
    Guid Issue(int agentId, int dataStoreId);

    bool TryConsume(Guid ticket, int agentId, out int dataStoreId);
}
