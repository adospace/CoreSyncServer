using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreSyncServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameSyncDirectionToSyncModeAndAddInError : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SyncDirection",
                table: "DataStoreTableConfigurations",
                newName: "SyncMode");

            migrationBuilder.AddColumn<bool>(
                name: "InError",
                table: "DataStoreTableConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InError",
                table: "DataStoreTableConfigurations");

            migrationBuilder.RenameColumn(
                name: "SyncMode",
                table: "DataStoreTableConfigurations",
                newName: "SyncDirection");
        }
    }
}
