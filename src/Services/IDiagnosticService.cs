using CoreSyncServer.Data;

namespace CoreSyncServer.Services;

public interface IDiagnosticService
{
    Task CreateAsync(DiagnosticItem item, CancellationToken cancellationToken = default);

    Task<bool> IsResolvedAsync(int diagnosticItemId, CancellationToken cancellationToken = default);
}
