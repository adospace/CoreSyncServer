using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CoreSyncServer.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
    {
        private const string AdminUserId = "00000000-0000-0000-0000-000000000001";
        private const string AdminRoleId = "00000000-0000-0000-0000-000000000001";

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

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
