using CoreSyncServer.Data.Schema;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CoreSyncServer.Data
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
        {
            services.AddDbContext<ApplicationDbContext>(configureDbContext);

            services.AddIdentityCore<ApplicationUser>(options =>
                {
                    options.SignIn.RequireConfirmedAccount = true;
                    options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
                })
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddSignInManager()
                .AddDefaultTokenProviders();

            services.AddSingleton<ISchemaReader, SqliteSchemaReader>();
            services.AddSingleton<ISchemaReader, SqlServerSchemaReader>();
            services.AddSingleton<ISchemaReader, PostgreSqlSchemaReader>();
            services.AddSingleton<ITableSorter, TableSorter>();

            return services;
        }
    }
}
