using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class WorkspaceOperatingPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RegistrationNumber",
                table: "CompanySetting",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RegistrationStatus",
                table: "CompanySetting",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AccessProfileVersion",
                table: "CompanyMemberSecurityRole",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GrantOrigin",
                table: "CompanyMemberSecurityRole",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "GrantedAtUtc",
                table: "CompanyMemberSecurityRole",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GrantedByUserId",
                table: "CompanyMemberSecurityRole",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkspaceApprovalRule",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    OperationKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RuleKind = table.Column<int>(type: "int", nullable: false),
                    Provenance = table.Column<int>(type: "int", nullable: false),
                    PolicySchemaVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    ConfiguredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConfiguredByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ConfiguredAtPolicyRevision = table.Column<long>(type: "bigint", nullable: false),
                    ReviewerSecurityRoleId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceApprovalRule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceApprovalRule_Company_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Company",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkspaceApprovalRule_SecurityRole_ReviewerSecurityRoleId",
                        column: x => x.ReviewerSecurityRoleId,
                        principalSchema: "dbo",
                        principalTable: "SecurityRole",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceOperatingSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    CollaborationIntent = table.Column<int>(type: "int", nullable: false),
                    CollaborationState = table.Column<int>(type: "int", nullable: false),
                    ConfirmedOwnerMemberId = table.Column<int>(type: "int", nullable: true),
                    InitializedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InitializedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    InitializationSource = table.Column<int>(type: "int", nullable: false),
                    EverTeamActivatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastConfirmedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    PolicyRevision = table.Column<long>(type: "bigint", nullable: false),
                    PolicySchemaVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceOperatingSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceOperatingSettings_CompanyMember_ConfirmedOwnerMemberId",
                        column: x => x.ConfirmedOwnerMemberId,
                        principalTable: "CompanyMember",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkspaceOperatingSettings_Company_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Company",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkspacePolicyCoordination",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastAcquiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspacePolicyCoordination", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspacePolicyCoordination_Company_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Company",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceApprovalRule_CompanyId_OperationKey",
                table: "WorkspaceApprovalRule",
                columns: new[] { "CompanyId", "OperationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceApprovalRule_ReviewerSecurityRoleId",
                table: "WorkspaceApprovalRule",
                column: "ReviewerSecurityRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceOperatingSettings_CompanyId",
                table: "WorkspaceOperatingSettings",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceOperatingSettings_ConfirmedOwnerMemberId",
                table: "WorkspaceOperatingSettings",
                column: "ConfirmedOwnerMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspacePolicyCoordination_CompanyId",
                table: "WorkspacePolicyCoordination",
                column: "CompanyId",
                unique: true);

            // Conservative legacy backfill: unclassified settings, coordination rows, LegacyEnforced SoD.
            // No grants and no approval relaxation. Historical team marker when second-human evidence exists.
            migrationBuilder.Sql(@"
INSERT INTO WorkspaceOperatingSettings
    (CompanyId, Purpose, CollaborationIntent, CollaborationState, ConfirmedOwnerMemberId,
     InitializedAtUtc, InitializedByUserId, InitializationSource, EverTeamActivatedAtUtc,
     LastConfirmedAtUtc, LastConfirmedByUserId, PolicyRevision, PolicySchemaVersion)
SELECT c.Id, 0, 0, 0, NULL, NULL, NULL, 0,
       CASE
           WHEN EXISTS (
               SELECT 1 FROM CompanyMember m
               WHERE m.CompanyId = c.Id AND m.Role <> 1)
             OR EXISTS (
               SELECT 1 FROM CompanyInvitation i
               WHERE i.CompanyId = c.Id AND i.AcceptedAt IS NOT NULL)
           THEN SYSUTCDATETIME()
           ELSE NULL
       END,
       NULL, NULL, 0, 1
FROM Company c
WHERE NOT EXISTS (
    SELECT 1 FROM WorkspaceOperatingSettings s WHERE s.CompanyId = c.Id);

INSERT INTO WorkspacePolicyCoordination (CompanyId, CreatedAtUtc, LastAcquiredAtUtc)
SELECT c.Id, SYSUTCDATETIME(), NULL
FROM Company c
WHERE NOT EXISTS (
    SELECT 1 FROM WorkspacePolicyCoordination x WHERE x.CompanyId = c.Id);

INSERT INTO WorkspaceApprovalRule
    (CompanyId, OperationKey, RuleKind, Provenance, PolicySchemaVersion,
     ConfiguredAtUtc, ConfiguredByUserId, ConfiguredAtPolicyRevision, ReviewerSecurityRoleId)
SELECT c.Id, N'financials.journal.post', 0, 0, 1, SYSUTCDATETIME(), NULL, 0, NULL
FROM Company c
WHERE NOT EXISTS (
    SELECT 1 FROM WorkspaceApprovalRule r
    WHERE r.CompanyId = c.Id AND r.OperationKey = N'financials.journal.post');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkspaceApprovalRule");

            migrationBuilder.DropTable(
                name: "WorkspaceOperatingSettings");

            migrationBuilder.DropTable(
                name: "WorkspacePolicyCoordination");

            migrationBuilder.DropColumn(
                name: "RegistrationNumber",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "RegistrationStatus",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "AccessProfileVersion",
                table: "CompanyMemberSecurityRole");

            migrationBuilder.DropColumn(
                name: "GrantOrigin",
                table: "CompanyMemberSecurityRole");

            migrationBuilder.DropColumn(
                name: "GrantedAtUtc",
                table: "CompanyMemberSecurityRole");

            migrationBuilder.DropColumn(
                name: "GrantedByUserId",
                table: "CompanyMemberSecurityRole");
        }
    }
}
