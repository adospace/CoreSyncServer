namespace CoreSyncServer.Agent;

public class AgentOptions
{
    public const string SectionName = "Agent";

    public required string ServerUrl { get; set; }

    public required string ApiKey { get; set; }

    public int ReconnectInitialDelayMs { get; set; } = 2000;

    public int ReconnectMaxDelayMs { get; set; } = 30000;

    public int InitPollOnFailureMs { get; set; } = 5000;

    public int HeartbeatIntervalMs { get; set; } = 15000;
}
