using CoreSyncServer.Agent.Services;

namespace CoreSyncServer.Agent;

public class AgentHostedService(
    AgentOrchestrator orchestrator,
    ILogger<AgentHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("CoreSyncServer.Agent starting");
        try
        {
            await orchestrator.RunAsync(stoppingToken);
        }
        finally
        {
            logger.LogInformation("CoreSyncServer.Agent stopping");
        }
    }
}
