using CoreSyncServer.Data;
using CoreSyncServer.Filters;
using CoreSyncServer.Services.Implementation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoreSyncServer.Services
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers ApplicationDbContext and ASP.NET Core Identity services.
        /// The caller provides the DbContext configuration (e.g. UseNpgsql, UseSqlServer).
        /// </summary>
        public static IServiceCollection AddCoreSyncServerServices(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<DbContextOptionsBuilder> configureDbContext)
            => AddCoreSyncServerServices<ApplicationDbContext>(services, configuration, configureDbContext);

        /// <summary>
        /// Registers a derived DbContext (e.g. a multi-tenant CloudDbContext) as the
        /// ApplicationDbContext implementation, along with Identity and core services.
        /// </summary>
        public static IServiceCollection AddCoreSyncServerServices<TContext>(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<DbContextOptionsBuilder> configureDbContext)
            where TContext : ApplicationDbContext
        {
            services.AddDbContext<ApplicationDbContext, TContext>(configureDbContext);

            services.AddIdentityCore<ApplicationUser>(options =>
                {
                    options.SignIn.RequireConfirmedAccount = true;
                    options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
                })
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddSignInManager()
                .AddDefaultTokenProviders();

            // Default tenant provider (no-op for single-tenant). SaaS layers override with TryAddScoped.
            services.TryAddScoped<ITenantProvider, NullTenantProvider>();

            services.AddSingleton<ISyncProviderFactory, SyncProviderFactory>();
            services.AddSingleton<ISchemaReader, SqliteSchemaReader>();
            services.AddSingleton<ISchemaReader, SqlServerSchemaReader>();
            services.AddSingleton<ISchemaReader, PostgreSqlSchemaReader>();
            services.AddSingleton<ITableSorter, TableSorter>();
            services.AddScoped<ITableConfigurationService, TableConfigurationService>();
            services.AddScoped<IDiagnosticService, DiagnosticService>();
            services.AddScoped<ISyncSessionService, SyncSessionService>();
            services.AddSingleton<ISyncProviderCache, SyncProviderCache>();
            services.AddScoped<IProvisionService, ProvisionService>();
            services.AddSingleton<MonitorTask, ConnectivityMonitorTask>();
            services.AddSingleton<MonitorTask, SchemaUpdateMonitorTask>();
            services.AddSingleton<IMonitorService, MonitorService>();

            services.AddSingleton<MaintenanceTask, DiagnosticMaintenanceTask>();
            services.AddSingleton<MaintenanceTask, SyncSessionMaintenanceTask>();
            services.AddSingleton<IMaintenanceService, MaintenanceService>();

            services.AddHttpClient("jwks");
            services.AddScoped<ISyncEndpointAuthService, SyncEndpointAuthService>();

            services.AddScoped<SyncEndpointAuthFilter>();
            services.AddScoped<CoreSyncServer.Filters.SyncConfigurationExceptionFilter>();

            services.AddScoped<CoreSyncServer.Filters.AgentApiKeyAuthFilter>();
            services.AddSingleton<IAgentConnectionTicketService, Implementation.AgentConnectionTicketService>();
            services.AddSingleton<IAgentConnectionRegistry, Implementation.AgentConnectionRegistry>();

            services.AddSignalR(options =>
            {
                // Sync payloads carry binary blobs (byte[] columns) that easily exceed the
                // 32 KB default. Null disables the incoming-message size check entirely.
                options.MaximumReceiveMessageSize = null;

                // Long-running server→agent RPCs (e.g. GetChanges against a large table) hold
                // the hub invocation open far longer than the default 30s client timeout, so
                // raise it and tighten keep-alive pings to keep the transport healthy.
                options.ClientTimeoutInterval = TimeSpan.FromMinutes(10);
                options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            });

            services.AddSingleton<MigrationComplete>();
            
            services.Configure<MonitorSettings>(configuration.GetRequiredSection("Monitor"));
            services.AddHostedService<MonitorHostedService>();

            services.Configure<MaintenanceSettings>(configuration.GetRequiredSection("Maintenance"));
            services.AddHostedService<MaintenanceHostedService>();


            return services;
        }
    }
}
