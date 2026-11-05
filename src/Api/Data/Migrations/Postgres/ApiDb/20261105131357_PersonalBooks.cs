using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Api.Data.Migrations.Postgres.ApiDb
{
    /// <inheritdoc />
    public partial class PersonalBooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PersonalWorkspaces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalWorkspaces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonalWorkspaces_Company_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Company",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    WorkCompanyId = table.Column<int>(type: "integer", nullable: false),
                    PersonalCompanyId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceLinks_Company_PersonalCompanyId",
                        column: x => x.PersonalCompanyId,
                        principalTable: "Company",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkspaceLinks_Company_WorkCompanyId",
                        column: x => x.WorkCompanyId,
                        principalTable: "Company",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PersonalWorkspaces_CompanyId",
                table: "PersonalWorkspaces",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonalWorkspaces_OwnerUserId",
                table: "PersonalWorkspaces",
                column: "OwnerUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceLinks_OwnerUserId",
                table: "WorkspaceLinks",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceLinks_PersonalCompanyId",
                table: "WorkspaceLinks",
                column: "PersonalCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceLinks_WorkCompanyId",
                table: "WorkspaceLinks",
                column: "WorkCompanyId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PersonalWorkspaces");

            migrationBuilder.DropTable(
                name: "WorkspaceLinks");
        }
    }
}
