using CoreSyncServer.Agent.Contracts;

namespace CoreSyncServer.Agent.Services;

public interface IServerClient
{
    Task<AgentInitResponse> GetInitAsync(CancellationToken ct);
}
