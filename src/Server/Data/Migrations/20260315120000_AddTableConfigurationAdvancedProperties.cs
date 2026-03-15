using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreSyncServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTableConfigurationAdvancedProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SkipInitialSnapshot",
                table: "DataStoreTableConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SelectIncrementalQuery",
                table: "DataStoreTableConfigurations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomSnapshotQuery",
                table: "DataStoreTableConfigurations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SkipColumns",
                table: "DataStoreTableConfigurations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SkipColumnsOnInsertOrUpdate",
                table: "DataStoreTableConfigurations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IdentityInsert",
                table: "DataStoreTableConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ForceReloadInsertedRecords",
                table: "DataStoreTableConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SkipInitialSnapshot",
                table: "DataStoreTableConfigurations");

            migrationBuilder.DropColumn(
                name: "SelectIncrementalQuery",
                table: "DataStoreTableConfigurations");

            migrationBuilder.DropColumn(
                name: "CustomSnapshotQuery",
                table: "DataStoreTableConfigurations");

            migrationBuilder.DropColumn(
                name: "SkipColumns",
                table: "DataStoreTableConfigurations");

            migrationBuilder.DropColumn(
                name: "SkipColumnsOnInsertOrUpdate",
                table: "DataStoreTableConfigurations");

            migrationBuilder.DropColumn(
                name: "IdentityInsert",
                table: "DataStoreTableConfigurations");

            migrationBuilder.DropColumn(
                name: "ForceReloadInsertedRecords",
                table: "DataStoreTableConfigurations");
        }
    }
}
