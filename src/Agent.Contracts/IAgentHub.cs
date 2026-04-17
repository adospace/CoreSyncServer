namespace CoreSyncServer.Agent.Contracts;

/// <summary>
/// Methods invokable by the Agent on the server-side hub.
/// </summary>
public interface IAgentHub
{
    Task<bool> AcknowledgeConnected(Guid connectionTicket);

    Task Heartbeat();

    Task ReportSyncProgress(SyncProgressMessage message);
}

/// <summary>
/// Methods invokable by the server on the Agent-side hub client.
/// </summary>
public interface IAgentHubClient
{
    Task Ping();

    Task OnSyncRequested(SyncRequestMessage message);

    Task OnConfigurationChanged(ConfigurationChangedMessage message);
}
