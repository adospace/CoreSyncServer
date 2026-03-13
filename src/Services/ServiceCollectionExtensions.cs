using CoreSyncServer.Data;
using CoreSyncServer.Services.Implementation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
        public static IServiceCollection AddCoreSyncData(
            this IServiceCollection services,
            Action<DbContextOptionsBuilder> configureDbContext)
            => AddCoreSyncData<ApplicationDbContext>(services, configureDbContext);

        /// <summary>
        /// Registers a derived DbContext (e.g. a multi-tenant CloudDbContext) as the
        /// ApplicationDbContext implementation, along with Identity and core services.
        /// </summary>
        public static IServiceCollection AddCoreSyncData<TContext>(
            this IServiceCollection services,
            Action<DbContextOptionsBuilder> configureDbContext)
            where TContext : ApplicationDbContext
        {
            services.AddDbContext<ApplicationDbContext, TContext>(configureDbContext);

            services.AddIdentityCore<ApplicationUser>(options =>
                {
                    options.SignIn.RequireConfirmedAccount = true;
                    options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
                })
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
            services.AddScoped<IProvisionService, ProvisionService>();
            services.AddSingleton<MonitorTask, ConnectivityMonitorTask>();
            services.AddSingleton<MonitorTask, SchemaUpdateMonitorTask>();
            services.AddSingleton<IMonitorService, MonitorService>();

            return services;
        }
    }
}
