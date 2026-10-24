using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class CompanyAccountingPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BooksStartDate",
                table: "CompanySetting",
                type: "date",
                nullable: false,
                defaultValue: new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "CoaTemplateId",
                table: "CompanySetting",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "legacy-default");

            migrationBuilder.AddColumn<int>(
                name: "FiscalYearPattern",
                table: "CompanySetting",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "FiscalYearStartMonth",
                table: "CompanySetting",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IndustryTemplate",
                table: "CompanySetting",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "InventoryCostingMethod",
                table: "CompanySetting",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "OnboardingChecklistDismissedAt",
                table: "CompanySetting",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnboardingChecklistJson",
                table: "CompanySetting",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReportingFramework",
                table: "CompanySetting",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TaxRegime",
                table: "CompanySetting",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(@"
                UPDATE settings
                SET BooksStartDate = COALESCE(
                    (SELECT TOP (1) CONVERT(date, fiscalYear.StartDate)
                     FROM FinancialYear AS fiscalYear
                     WHERE fiscalYear.CompanyId = settings.CompanyId
                     ORDER BY fiscalYear.IsActive DESC, fiscalYear.StartDate),
                    CONVERT(date, company.CreatedAt),
                    CONVERT(date, '1970-01-01'))
                FROM CompanySetting AS settings
                LEFT JOIN Company AS company ON company.Id = settings.CompanyId;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BooksStartDate",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "CoaTemplateId",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "FiscalYearPattern",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "FiscalYearStartMonth",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "IndustryTemplate",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "InventoryCostingMethod",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "OnboardingChecklistDismissedAt",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "OnboardingChecklistJson",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "ReportingFramework",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "TaxRegime",
                table: "CompanySetting");
        }
    }
}
