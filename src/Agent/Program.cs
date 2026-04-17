using CoreSyncServer.Agent;
using CoreSyncServer.Agent.Contracts;
using CoreSyncServer.Agent.Services;
using Serilog;
using Serilog.Events;

var logDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "CoreSyncServer.Agent");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore.SignalR.Client", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(logDirectory, "agent-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        fileSizeLimitBytes: 10 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        shared: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("CoreSyncServer.Agent starting up; logs → {LogDirectory}", logDirectory);

    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog();

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

    Log.Information("CoreSyncServer.Agent shut down cleanly");
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "CoreSyncServer.Agent terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
