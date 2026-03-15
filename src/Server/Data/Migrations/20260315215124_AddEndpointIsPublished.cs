using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreSyncServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEndpointIsPublished : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublished",
                table: "Endpoints",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPublished",
                table: "Endpoints");
        }
    }
}
