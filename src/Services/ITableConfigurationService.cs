using CoreSyncServer.Data;

namespace CoreSyncServer.Services;

/// <summary>
/// Scaffolds and sorts table configurations for a data store configuration
/// by reading the live database schema and applying dependency-based ordering.
/// </summary>
public interface ITableConfigurationService
{
    Task<TableConfigurationResult> ScaffoldAsync(int configurationId, CancellationToken cancellationToken = default);

    Task<TableConfigurationResult> UpdateAsync(int configurationId, CancellationToken cancellationToken = default);

    Task<TableConfigurationResult> SortAsync(int configurationId, CancellationToken cancellationToken = default);

    Task<DiscoverTablesResult> DiscoverAsync(int configurationId, CancellationToken cancellationToken = default);
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

public class DiscoverTablesResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public IReadOnlyList<DiscoveredTable> Tables { get; init; } = [];

    public static DiscoverTablesResult NotFound() =>
        new() { Error = "Configuration not found." };

    public static DiscoverTablesResult Failure(string error) =>
        new() { Error = error };

    public static DiscoverTablesResult Ok(IReadOnlyList<DiscoveredTable> tables) =>
        new() { Success = true, Tables = tables };
}

public record DiscoveredTable(string Name, string? Schema);
