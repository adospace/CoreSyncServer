using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CoreSyncServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AgentId",
                table: "DataStores",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Agents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ApiKey = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeen = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DataStores_AgentId",
                table: "DataStores",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_Agents_ApiKey",
                table: "Agents",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Agents_Name",
                table: "Agents",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DataStores_Agents_AgentId",
                table: "DataStores",
                column: "AgentId",
                principalTable: "Agents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DataStores_Agents_AgentId",
                table: "DataStores");

            migrationBuilder.DropTable(
                name: "Agents");

            migrationBuilder.DropIndex(
                name: "IX_DataStores_AgentId",
                table: "DataStores");

            migrationBuilder.DropColumn(
                name: "AgentId",
                table: "DataStores");
        }
    }
}
