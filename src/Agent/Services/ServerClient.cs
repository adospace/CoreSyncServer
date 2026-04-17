using System.Net.Http.Json;
using CoreSyncServer.Agent.Contracts;

namespace CoreSyncServer.Agent.Services;

public class ServerClient(HttpClient http) : IServerClient
{
    public async Task<AgentInitResponse> GetInitAsync(CancellationToken ct)
    {
        var response = await http.GetAsync("api/agent/datastores", ct);
        response.EnsureSuccessStatusCode();
        var init = await response.Content.ReadFromJsonAsync<AgentInitResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Server returned an empty AgentInitResponse.");
        return init;
    }
}
