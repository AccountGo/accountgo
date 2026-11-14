using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.Postgres.ApiDb
{
    /// <inheritdoc />
    public partial class TaxPeriodSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TaxPeriodFrequency",
                table: "CompanySetting",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "TaxQuarterCycleStartMonth",
                table: "CompanySetting",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaxPeriodFrequency",
                table: "CompanySetting");

            migrationBuilder.DropColumn(
                name: "TaxQuarterCycleStartMonth",
                table: "CompanySetting");
        }
    }
}
