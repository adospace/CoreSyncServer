using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreSyncServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedAdminUserAndRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[] { "00000000-0000-0000-0000-000000000001", "00000000-0000-0000-0000-000000000001", "Administrator", "ADMINISTRATOR" });

            migrationBuilder.InsertData(
                table: "AspNetUsers",
                columns: new[] { "Id", "AccessFailedCount", "ConcurrencyStamp", "Email", "EmailConfirmed", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "SecurityStamp", "TwoFactorEnabled", "UserName" },
                values: new object[] { "00000000-0000-0000-0000-000000000001", 0, "00000000-0000-0000-0000-000000000001", "admin@localhost", true, false, null, "ADMIN@LOCALHOST", "ADMIN", "AQAAAAIAAYagAAAAEOUQXERguZsdTd8LQGnmpsbi0HWs55ecQch+nk5MVA2Hk1B2ymNQQsMk6uju7dOIWA==", null, false, "00000000-0000-0000-0000-000000000001", false, "admin" });

            migrationBuilder.InsertData(
                table: "AspNetUserRoles",
                columns: new[] { "RoleId", "UserId" },
                values: new object[] { "00000000-0000-0000-0000-000000000001", "00000000-0000-0000-0000-000000000001" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AspNetUserRoles",
                keyColumns: new[] { "RoleId", "UserId" },
                keyValues: new object[] { "00000000-0000-0000-0000-000000000001", "00000000-0000-0000-0000-000000000001" });

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "00000000-0000-0000-0000-000000000001");

            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "00000000-0000-0000-0000-000000000001");
        }
    }
}
