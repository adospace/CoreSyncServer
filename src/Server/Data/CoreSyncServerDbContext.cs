using CoreSyncServer.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CoreSyncServer.Server.Data;

/// <summary>
/// DbContext for CoreSyncServer standalone deployments.
/// Seeds a default admin user and role for local authentication.
/// </summary>
public class CoreSyncServerDbContext : ApplicationDbContext
{
    private const string AdminUserId = "00000000-0000-0000-0000-000000000001";
    private const string AdminRoleId = "00000000-0000-0000-0000-000000000001";

    public CoreSyncServerDbContext(DbContextOptions<CoreSyncServerDbContext> options)
        : base(options) { }

    protected override void ConfigureAdditionalModel(ModelBuilder builder)
    {
        base.ConfigureAdditionalModel(builder);

        var adminRole = new IdentityRole
        {
            Id = AdminRoleId,
            Name = "Administrator",
            NormalizedName = "ADMINISTRATOR",
            ConcurrencyStamp = AdminRoleId
        };

        // Pre-computed hash for "admin" password - do not use PasswordHasher dynamically in HasData
        // To regenerate: new PasswordHasher<ApplicationUser>().HashPassword(adminUser, "admin")
        const string adminPasswordHash = "AQAAAAIAAYagAAAAEG1yor+ewRplvj33lrT+XzGAg5S0+b8567EtIg7WbPLQwBO1E4xGeSXFO7AwLnylXg==";

        var adminUser = new ApplicationUser
        {
            Id = AdminUserId,
            UserName = "admin",
            NormalizedUserName = "ADMIN",
            Email = "admin@localhost",
            NormalizedEmail = "ADMIN@LOCALHOST",
            EmailConfirmed = true,
            SecurityStamp = AdminUserId,
            ConcurrencyStamp = AdminUserId,
            PasswordHash = adminPasswordHash
        };

        builder.Entity<IdentityRole>().HasData(adminRole);
        builder.Entity<ApplicationUser>().HasData(adminUser);
        builder.Entity<IdentityUserRole<string>>().HasData(new IdentityUserRole<string>
        {
            UserId = AdminUserId,
            RoleId = AdminRoleId
        });
    }
}
