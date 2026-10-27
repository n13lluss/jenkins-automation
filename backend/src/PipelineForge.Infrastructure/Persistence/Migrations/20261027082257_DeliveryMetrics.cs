using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PipelineForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeploymentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Digest = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: true),
                    Commit = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CommitTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    BuildUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentEvents_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IngestTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngestTokens_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentEvents_ProjectId_Environment_FinishedAt",
                table: "DeploymentEvents",
                columns: new[] { "ProjectId", "Environment", "FinishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IngestTokens_ProjectId",
                table: "IngestTokens",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_IngestTokens_Sha256",
                table: "IngestTokens",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IngestTokens_WorkspaceId_ProjectId",
                table: "IngestTokens",
                columns: new[] { "WorkspaceId", "ProjectId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeploymentEvents");

            migrationBuilder.DropTable(
                name: "IngestTokens");
        }
    }
}
