using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Api.Data.Migrations.Postgres.ApiDb
{
    /// <inheritdoc />
    public partial class WorkspaceAccessRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkspaceAccessRequest",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    RequestId = table.Column<string>(type: "text", nullable: false),
                    RequesterUserId = table.Column<string>(type: "text", nullable: false),
                    TargetUserId = table.Column<string>(type: "text", nullable: false),
                    TargetMemberId = table.Column<int>(type: "integer", nullable: false),
                    RequestedRoleIdsJson = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ApproverUserId = table.Column<string>(type: "text", nullable: true),
                    DecisionReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    AppliedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    PolicyVersion = table.Column<string>(type: "text", nullable: false),
                    TargetMembershipRevisionAtRequest = table.Column<long>(type: "bigint", nullable: false),
                    BoundApprovalId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceAccessRequest", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceAccessRequest_CompanyMember_TargetMemberId",
                        column: x => x.TargetMemberId,
                        principalTable: "CompanyMember",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkspaceAccessRequest_Company_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Company",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceAccessRequest_CompanyId_RequestId",
                table: "WorkspaceAccessRequest",
                columns: new[] { "CompanyId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceAccessRequest_CompanyId_Status",
                table: "WorkspaceAccessRequest",
                columns: new[] { "CompanyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceAccessRequest_TargetMemberId",
                table: "WorkspaceAccessRequest",
                column: "TargetMemberId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkspaceAccessRequest");
        }
    }
}
