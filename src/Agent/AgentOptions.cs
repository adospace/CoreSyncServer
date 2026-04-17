namespace CoreSyncServer.Agent;

public class AgentOptions
{
    public const string SectionName = "Agent";

    public required string ServerUrl { get; set; }

    public required string ApiKey { get; set; }

    /// <summary>
    /// Cadence for the orchestrator's reconcile/health-probe tick. Each tick fetches the latest
    /// DataStore assignments and heartbeats existing hub connections, so this is also the worst-case
    /// time to detect a server outage or configuration change.
    /// </summary>
    public int PollIntervalMs { get; set; } = 15000;

    /// <summary>
    /// Timeout applied to individual server probes (heartbeat invoke, init fetch). Keeps a half-open
    /// TCP connection from stalling a tick for the full HttpClient timeout.
    /// </summary>
    public int ProbeTimeoutMs { get; set; } = 5000;
}
