using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class SalesLineTax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                table: "SalesInvoiceLine",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "TaxId",
                table: "SalesInvoiceLine",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLine_TaxId",
                table: "SalesInvoiceLine",
                column: "TaxId");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceLine_Tax_TaxId",
                table: "SalesInvoiceLine",
                column: "TaxId",
                principalTable: "Tax",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceLine_Tax_TaxId",
                table: "SalesInvoiceLine");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceLine_TaxId",
                table: "SalesInvoiceLine");

            migrationBuilder.DropColumn(
                name: "TaxAmount",
                table: "SalesInvoiceLine");

            migrationBuilder.DropColumn(
                name: "TaxId",
                table: "SalesInvoiceLine");
        }
    }
}
