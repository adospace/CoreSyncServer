using CoreSyncServer.Agent;
using CoreSyncServer.Agent.Contracts;
using CoreSyncServer.Agent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions<AgentOptions>()
    .Bind(builder.Configuration.GetSection(AgentOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ServerUrl), "Agent:ServerUrl is required.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Agent:ApiKey is required.")
    .ValidateOnStart();

builder.Services.AddHttpClient<IServerClient, ServerClient>((sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentOptions>>().Value;
    client.BaseAddress = new Uri(options.ServerUrl.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Add(AgentAuthHeaders.ApiKeyHeader, options.ApiKey);
});

builder.Services.AddSingleton<IAgentSyncHandler, DefaultAgentSyncHandler>();
builder.Services.AddSingleton<AgentOrchestrator>();
builder.Services.AddHostedService<AgentHostedService>();

var host = builder.Build();
await host.RunAsync();
