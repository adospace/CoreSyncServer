using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CoreSyncServer.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
    {
        private const string AdminUserId = "00000000-0000-0000-0000-000000000001";
        private const string AdminRoleId = "00000000-0000-0000-0000-000000000001";

        public DbSet<Project> Projects => Set<Project>();
        public DbSet<DataStore> DataStores => Set<DataStore>();
        public DbSet<DataStoreConfiguration> DataStoreConfigurations => Set<DataStoreConfiguration>();
        public DbSet<DataStoreTableConfiguration> DataStoreTableConfigurations => Set<DataStoreTableConfiguration>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Configure TPH inheritance for DataStore using Type as discriminator
            builder.Entity<DataStore>()
                .UseTphMappingStrategy()
                .HasDiscriminator(d => d.Type)
                .HasValue<SqliteDataStore>(DataStoreType.SQLite)
                .HasValue<SqlServerDataStore>(DataStoreType.SqlServer)
                .HasValue<PostgreSqlDataStore>(DataStoreType.PostgreSQL);

            // Configure Project -> DataStore relationship
            builder.Entity<DataStore>()
                .HasOne(d => d.Project)
                .WithMany(p => p.DataStores)
                .HasForeignKey(d => d.ProjectId);

            // Configure DataStore -> DataStoreConfiguration relationship
            builder.Entity<DataStoreConfiguration>()
                .HasOne(c => c.DataStore)
                .WithMany(d => d.Configurations)
                .HasForeignKey(c => c.DataStoreId);

            // Configure DataStoreConfiguration -> DataStoreTableConfiguration relationship
            builder.Entity<DataStoreTableConfiguration>()
                .HasOne(t => t.DataStoreConfiguration)
                .WithMany(c => c.TableConfigurations)
                .HasForeignKey(t => t.DataStoreConfigurationId);

            var adminRole = new IdentityRole
            {
                Id = AdminRoleId,
                Name = "Administrator",
                NormalizedName = "ADMINISTRATOR",
                ConcurrencyStamp = AdminRoleId
            };

            var hasher = new PasswordHasher<ApplicationUser>();
            var adminUser = new ApplicationUser
            {
                Id = AdminUserId,
                UserName = "admin",
                NormalizedUserName = "ADMIN",
                Email = "admin@localhost",
                NormalizedEmail = "ADMIN@LOCALHOST",
                EmailConfirmed = true,
                SecurityStamp = AdminUserId,
                ConcurrencyStamp = AdminUserId
            };
            adminUser.PasswordHash = hasher.HashPassword(adminUser, "admin");

            builder.Entity<IdentityRole>().HasData(adminRole);
            builder.Entity<ApplicationUser>().HasData(adminUser);
            builder.Entity<IdentityUserRole<string>>().HasData(new IdentityUserRole<string>
            {
                UserId = AdminUserId,
                RoleId = AdminRoleId
            });
        }
    }
}
