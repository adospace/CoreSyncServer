using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreSyncServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameVersionAndAddTableSortMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "VersioId",
                table: "DataStoreConfigurations",
                newName: "Version");

            migrationBuilder.AddColumn<string>(
                name: "Message",
                table: "DataStoreTableConfigurations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Sort",
                table: "DataStoreTableConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Message",
                table: "DataStoreTableConfigurations");

            migrationBuilder.DropColumn(
                name: "Sort",
                table: "DataStoreTableConfigurations");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "DataStoreConfigurations",
                newName: "VersioId");
        }
    }
}
