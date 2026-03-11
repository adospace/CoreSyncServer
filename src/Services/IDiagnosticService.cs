using CoreSyncServer.Data;

namespace CoreSyncServer.Services;

public interface IDiagnosticService
{
    Task CreateAsync(DiagnosticItem item, CancellationToken cancellationToken = default);
}
