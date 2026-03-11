using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CoreSyncServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDiagnosticItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DiagnosticItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Message = table.Column<string>(type: "text", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsResolved = table.Column<bool>(type: "boolean", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: true),
                    SyncSessionId = table.Column<int>(type: "integer", nullable: true),
                    DataStoreId = table.Column<int>(type: "integer", nullable: true),
                    DataStoreConfigurationId = table.Column<int>(type: "integer", nullable: true),
                    EndpointId = table.Column<Guid>(type: "uuid", nullable: true),
                    Metadata = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosticItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiagnosticItems_DataStoreConfigurations_DataStoreConfigurat~",
                        column: x => x.DataStoreConfigurationId,
                        principalTable: "DataStoreConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DiagnosticItems_DataStores_DataStoreId",
                        column: x => x.DataStoreId,
                        principalTable: "DataStores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DiagnosticItems_Endpoints_EndpointId",
                        column: x => x.EndpointId,
                        principalTable: "Endpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DiagnosticItems_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DiagnosticItems_SyncSession_SyncSessionId",
                        column: x => x.SyncSessionId,
                        principalTable: "SyncSession",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticItems_DataStoreConfigurationId",
                table: "DiagnosticItems",
                column: "DataStoreConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticItems_DataStoreId",
                table: "DiagnosticItems",
                column: "DataStoreId");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticItems_EndpointId",
                table: "DiagnosticItems",
                column: "EndpointId");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticItems_ProjectId",
                table: "DiagnosticItems",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticItems_SyncSessionId",
                table: "DiagnosticItems",
                column: "SyncSessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiagnosticItems");
        }
    }
}
