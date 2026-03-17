using CoreSyncServer.Data;

namespace CoreSyncServer.Services;

public interface ISyncSessionService
{
    Task<SyncSession> StartAsync(int dataStoreId, CancellationToken cancellationToken = default);

    Task CompleteAsync(int sessionId, CancellationToken cancellationToken = default);

    Task ErrorAsync(int sessionId, string errorMessage, CancellationToken cancellationToken = default);
}
