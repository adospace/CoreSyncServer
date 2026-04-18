using CoreSync;
using Microsoft.Extensions.Logging;

namespace CoreSyncServer.Agent.Services;

internal sealed class AgentSyncLoggerAdapter(ILogger logger) : ISyncLogger
{
    public void Trace(string message) => logger.LogTrace("{Message}", message);

    public void Info(string message) => logger.LogInformation("{Message}", message);

    public void Warning(string message) => logger.LogWarning("{Message}", message);

    public void Error(string message) => logger.LogError("{Message}", message);
}
