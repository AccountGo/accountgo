using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.Postgres.ApiDb
{
    /// <inheritdoc />
    public partial class BoundAccessBootstrap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkspaceAccessRequest_Company_CompanyId",
                table: "WorkspaceAccessRequest");

            migrationBuilder.AddColumn<bool>(
                name: "AccessBootstrapAvailable",
                table: "Company",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkspaceAccessRequest_Company_CompanyId",
                table: "WorkspaceAccessRequest",
                column: "CompanyId",
                principalTable: "Company",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkspaceAccessRequest_Company_CompanyId",
                table: "WorkspaceAccessRequest");

            migrationBuilder.DropColumn(
                name: "AccessBootstrapAvailable",
                table: "Company");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkspaceAccessRequest_Company_CompanyId",
                table: "WorkspaceAccessRequest",
                column: "CompanyId",
                principalTable: "Company",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
