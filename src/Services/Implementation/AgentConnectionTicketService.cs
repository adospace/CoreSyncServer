using System.Collections.Concurrent;

namespace CoreSyncServer.Services.Implementation;

public class AgentConnectionTicketService : IAgentConnectionTicketService
{
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<Guid, Entry> _tickets = new();

    public Guid Issue(int agentId, int dataStoreId)
    {
        PruneExpired();

        var ticket = Guid.NewGuid();
        _tickets[ticket] = new Entry(agentId, dataStoreId, DateTime.UtcNow.Add(TicketLifetime));
        return ticket;
    }

    public bool TryConsume(Guid ticket, int agentId, out int dataStoreId)
    {
        dataStoreId = 0;
        if (!_tickets.TryRemove(ticket, out var entry)) return false;
        if (entry.ExpiresUtc < DateTime.UtcNow) return false;
        if (entry.AgentId != agentId) return false;
        dataStoreId = entry.DataStoreId;
        return true;
    }

    private void PruneExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var (key, entry) in _tickets)
        {
            if (entry.ExpiresUtc < now)
            {
                _tickets.TryRemove(key, out _);
            }
        }
    }

    private record Entry(int AgentId, int DataStoreId, DateTime ExpiresUtc);
}
