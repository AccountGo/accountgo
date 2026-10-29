using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class SalesInvoiceSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BusinessAddress_City",
                table: "CompanySetting",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessAddress_CountryCode",
                table: "CompanySetting",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessAddress_Line1",
                table: "CompanySetting",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessAddress_Line2",
                table: "CompanySetting",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessAddress_PostalCode",
                table: "CompanySetting",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessAddress_Region",
                table: "CompanySetting",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessEmail",
                table: "CompanySetting",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessPhone",
                table: "CompanySetting",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalName",
                table: "CompanySetting",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SalesInvoiceSettingsJson",
                table: "CompanySetting",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BusinessAddress_City",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "BusinessAddress_CountryCode",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "BusinessAddress_Line1",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "BusinessAddress_Line2",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "BusinessAddress_PostalCode",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "BusinessAddress_Region",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "BusinessEmail",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "BusinessPhone",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "LegalName",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "SalesInvoiceSettingsJson",
                table: "CompanySetting");
        }
    }
}
