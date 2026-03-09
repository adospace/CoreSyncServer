using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreSyncServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixAdminPasswordHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "00000000-0000-0000-0000-000000000001",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEG1yor+ewRplvj33lrT+XzGAg5S0+b8567EtIg7WbPLQwBO1E4xGeSXFO7AwLnylXg==");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "00000000-0000-0000-0000-000000000001",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEG1yor+ewRplvj33lrT+XzGAg5S0+b8567EtIg7WbPLQwBO1E4xGeSXFO7AwLnylXg==");
        }
    }
}
