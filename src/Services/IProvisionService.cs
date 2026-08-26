using CoreSync.SqlServerCT;

namespace CoreSyncServer.Services;

public class ProvisionResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static ProvisionResult Ok() => new() { Success = true };
    public static ProvisionResult Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// The outcome of applying a data store's change retention setting to its database.
/// </summary>
/// <remarks>
/// Carries the settings read back from the database rather than the ones requested: an operator has to
/// be able to see that the <c>ALTER DATABASE</c> actually took, because a retention that looks saved but
/// was never applied leaves clients aging out of a window nobody believes is still in force.
/// </remarks>
public class ChangeRetentionResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    /// <summary>
    /// The retention actually in effect after the change, as reported by
    /// <c>sys.change_tracking_databases</c>. Null when it could not be read back.
    /// </summary>
    public int? EffectiveRetention { get; init; }

    /// <summary>
    /// The unit <see cref="EffectiveRetention"/> is expressed in.
    /// </summary>
    public string? EffectiveRetentionUnit { get; init; }

    public bool? EffectiveAutoCleanup { get; init; }

    public static ChangeRetentionResult Ok(ChangeTrackingDatabaseOptions? options) => new()
    {
        Success = true,
        EffectiveRetention = options?.RetentionPeriod,
        EffectiveRetentionUnit = options?.RetentionPeriodUnit.ToString(),
        EffectiveAutoCleanup = options?.AutoCleanup
    };

    public static ChangeRetentionResult Fail(string error) => new() { Success = false, Error = error };
}

public interface IProvisionService
{
    Task<ProvisionResult> ApplyProvisionAsync(int dataStoreId);
    Task<ProvisionResult> RemoveProvisionAsync(int dataStoreId);

    /// <summary>
    /// Issues the <c>ALTER DATABASE ... SET CHANGE_TRACKING</c> that brings the database in line with the
    /// data store's stored change retention, and reports what the database holds afterwards.
    /// </summary>
    Task<ChangeRetentionResult> ApplyChangeRetentionAsync(int dataStoreId);
}
