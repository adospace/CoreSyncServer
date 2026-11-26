using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreSyncServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSqlServerDataStoreDiscardOrphanedChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DiscardOrphanedChanges",
                table: "DataStores",
                type: "boolean",
                nullable: true);

            // Nullable because it belongs to one branch of the TPH hierarchy, but the property on
            // SqlServerDataStore is not: backfill existing SQL Server data stores (Type = 1) with the
            // default, off, so they still materialize.
            migrationBuilder.Sql(
                """
                UPDATE "DataStores"
                SET "DiscardOrphanedChanges" = FALSE
                WHERE "Type" = 1 AND "DiscardOrphanedChanges" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscardOrphanedChanges",
                table: "DataStores");
        }
    }
}
