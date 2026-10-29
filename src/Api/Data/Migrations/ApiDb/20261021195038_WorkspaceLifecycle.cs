using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class WorkspaceLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "CompanySetting",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "PHP");

            migrationBuilder.AddColumn<int>(
                name: "ResendCount",
                table: "CompanyInvitation",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResendWindowStart",
                table: "CompanyInvitation",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Company",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Company",
                type: "datetime2",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Company",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DeletedAt", "Slug" },
                values: new object[] { null, "ph-financials" });

            migrationBuilder.CreateIndex(
                name: "IX_Company_Slug",
                table: "Company",
                column: "Slug",
                unique: true,
                filter: "[Slug] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Company_Slug",
                table: "Company");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "ResendCount",
                table: "CompanyInvitation");

            migrationBuilder.DropColumn(
                name: "ResendWindowStart",
                table: "CompanyInvitation");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Company");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Company",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "Company",
                keyColumn: "Id",
                keyValue: 1,
                column: "Slug",
                value: null);
        }
    }
}
