namespace CoreSyncServer.Services;

public class ProvisionResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static ProvisionResult Ok() => new() { Success = true };
    public static ProvisionResult Fail(string error) => new() { Success = false, Error = error };
}

public interface IProvisionService
{
    Task<ProvisionResult> ApplyProvisionAsync(int dataStoreId);
    Task<ProvisionResult> RemoveProvisionAsync(int dataStoreId);
}
