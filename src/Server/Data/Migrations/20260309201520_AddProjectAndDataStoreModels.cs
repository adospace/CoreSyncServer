using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CoreSyncServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectAndDataStoreModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataStores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ConnectionString = table.Column<string>(type: "text", nullable: true),
                    SqlServerDataStore_ConnectionString = table.Column<string>(type: "text", nullable: true),
                    TrackingMode = table.Column<int>(type: "integer", nullable: true),
                    FilePath = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataStores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DataStores_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DataStoreConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DataStoreId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataStoreConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DataStoreConfigurations_DataStores_DataStoreId",
                        column: x => x.DataStoreId,
                        principalTable: "DataStores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DataStoreTableConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DataStoreConfigurationId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Schema = table.Column<string>(type: "text", nullable: true),
                    SyncDirection = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataStoreTableConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DataStoreTableConfigurations_DataStoreConfigurations_DataSt~",
                        column: x => x.DataStoreConfigurationId,
                        principalTable: "DataStoreConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "00000000-0000-0000-0000-000000000001",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEF5f4IxCd062+mDp8fgp6QfFA4D5UJCjS1S9ip1CFIFLJRBDIsSOZRR49Tbii/wLqA==");

            migrationBuilder.CreateIndex(
                name: "IX_DataStoreConfigurations_DataStoreId",
                table: "DataStoreConfigurations",
                column: "DataStoreId");

            migrationBuilder.CreateIndex(
                name: "IX_DataStores_ProjectId",
                table: "DataStores",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_DataStoreTableConfigurations_DataStoreConfigurationId",
                table: "DataStoreTableConfigurations",
                column: "DataStoreConfigurationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataStoreTableConfigurations");

            migrationBuilder.DropTable(
                name: "DataStoreConfigurations");

            migrationBuilder.DropTable(
                name: "DataStores");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "00000000-0000-0000-0000-000000000001",
                column: "PasswordHash",
                value: "AQAAAAIAAYagAAAAEOUQXERguZsdTd8LQGnmpsbi0HWs55ecQch+nk5MVA2Hk1B2ymNQQsMk6uju7dOIWA==");
        }
    }
}
