namespace CoreSyncServer.Data.Schema;

/// <summary>
/// Scaffolds and sorts table configurations for a data store configuration
/// by reading the live database schema and applying dependency-based ordering.
/// </summary>
public interface ITableConfigurationService
{
    Task<TableConfigurationResult> ScaffoldAsync(int configurationId, CancellationToken cancellationToken = default);

    Task<TableConfigurationResult> SortAsync(int configurationId, CancellationToken cancellationToken = default);
}

public class TableConfigurationResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public IReadOnlyList<DataStoreTableConfiguration> Tables { get; init; } = [];

    public static TableConfigurationResult NotFound() =>
        new() { Error = "Configuration not found." };

    public static TableConfigurationResult Failure(string error) =>
        new() { Error = error };

    public static TableConfigurationResult Ok(IReadOnlyList<DataStoreTableConfiguration> tables) =>
        new() { Success = true, Tables = tables };
}
