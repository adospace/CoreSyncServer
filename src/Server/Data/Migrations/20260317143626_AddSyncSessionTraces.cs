using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CoreSyncServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncSessionTraces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DiagnosticItems_SyncSession_SyncSessionId",
                table: "DiagnosticItems");

            migrationBuilder.DropForeignKey(
                name: "FK_SyncSession_DataStores_DataStoreId",
                table: "SyncSession");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SyncSession",
                table: "SyncSession");

            migrationBuilder.RenameTable(
                name: "SyncSession",
                newName: "SyncSessions");

            migrationBuilder.RenameIndex(
                name: "IX_SyncSession_DataStoreId",
                table: "SyncSessions",
                newName: "IX_SyncSessions_DataStoreId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SyncSessions",
                table: "SyncSessions",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "SyncSessionTraces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SyncSessionId = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    TimeStamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TraceLevel = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncSessionTraces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncSessionTraces_SyncSessions_SyncSessionId",
                        column: x => x.SyncSessionId,
                        principalTable: "SyncSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncSessionTraces_SyncSessionId",
                table: "SyncSessionTraces",
                column: "SyncSessionId");

            migrationBuilder.AddForeignKey(
                name: "FK_DiagnosticItems_SyncSessions_SyncSessionId",
                table: "DiagnosticItems",
                column: "SyncSessionId",
                principalTable: "SyncSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SyncSessions_DataStores_DataStoreId",
                table: "SyncSessions",
                column: "DataStoreId",
                principalTable: "DataStores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DiagnosticItems_SyncSessions_SyncSessionId",
                table: "DiagnosticItems");

            migrationBuilder.DropForeignKey(
                name: "FK_SyncSessions_DataStores_DataStoreId",
                table: "SyncSessions");

            migrationBuilder.DropTable(
                name: "SyncSessionTraces");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SyncSessions",
                table: "SyncSessions");

            migrationBuilder.RenameTable(
                name: "SyncSessions",
                newName: "SyncSession");

            migrationBuilder.RenameIndex(
                name: "IX_SyncSessions_DataStoreId",
                table: "SyncSession",
                newName: "IX_SyncSession_DataStoreId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SyncSession",
                table: "SyncSession",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_DiagnosticItems_SyncSession_SyncSessionId",
                table: "DiagnosticItems",
                column: "SyncSessionId",
                principalTable: "SyncSession",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SyncSession_DataStores_DataStoreId",
                table: "SyncSession",
                column: "DataStoreId",
                principalTable: "DataStores",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
