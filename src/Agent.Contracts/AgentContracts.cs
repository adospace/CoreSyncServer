namespace CoreSyncServer.Agent.Contracts;

/// <summary>
/// Header name used by the agent to authenticate HTTP and SignalR requests.
/// </summary>
public static class AgentAuthHeaders
{
    public const string ApiKeyHeader = "X-Agent-ApiKey";
    public const string AuthenticationScheme = "AgentApiKey";
    public const string HubPath = "/hubs/agent";
}

public record AgentInitResponse(
    int AgentId,
    string AgentName,
    string HubUrl,
    List<AgentDataStoreDto> DataStores);

public record AgentDataStoreDto(
    int Id,
    string Name,
    string? Description,
    string Type,
    string? FilePath,
    string? ConnectionString,
    string? TrackingMode,
    Guid ConnectionTicket,
    string HubGroupName,
    List<AgentDataStoreConfigurationDto> Configurations);

public record AgentDataStoreConfigurationDto(
    int Id,
    string Name,
    int Version,
    List<AgentDataStoreTableDto> Tables);

public record AgentDataStoreTableDto(
    string Name,
    string? Schema,
    string SyncDirection,
    int Sort,
    bool SkipInitialSnapshot,
    string? SelectIncrementalQuery,
    string? CustomSnapshotQuery,
    string? SkipColumns,
    string? SkipColumnsOnInsertOrUpdate,
    string IdentityInsert,
    bool ForceReloadInsertedRecords);

public record SyncRequestMessage(Guid SessionId, int ConfigurationId);
public record SyncProgressMessage(Guid SessionId, int Processed, int Total, string? CurrentTable);
public record ConfigurationChangedMessage(int DataStoreId, int ConfigurationId);
