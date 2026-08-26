using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreSyncServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSqlServerDataStoreChangeRetentionDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChangeRetentionDays",
                table: "DataStores",
                type: "integer",
                nullable: true);

            // The column is nullable because it belongs to one branch of the TPH hierarchy, but the
            // property on SqlServerDataStore is not. Without this backfill every SQL Server data store
            // created before this migration would fail to materialize on a null read.
            // Type = 1 is DataStoreType.SqlServer, the discriminator value for that branch.
            migrationBuilder.Sql(
                """
                UPDATE "DataStores"
                SET "ChangeRetentionDays" = 30
                WHERE "Type" = 1 AND "ChangeRetentionDays" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChangeRetentionDays",
                table: "DataStores");
        }
    }
}
