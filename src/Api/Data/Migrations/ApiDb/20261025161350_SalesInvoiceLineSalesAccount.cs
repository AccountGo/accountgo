using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class SalesInvoiceLineSalesAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SalesAccountId",
                table: "SalesInvoiceLine",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLine_SalesAccountId",
                table: "SalesInvoiceLine",
                column: "SalesAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceLine_Account_SalesAccountId",
                table: "SalesInvoiceLine",
                column: "SalesAccountId",
                principalTable: "Account",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceLine_Account_SalesAccountId",
                table: "SalesInvoiceLine");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceLine_SalesAccountId",
                table: "SalesInvoiceLine");

            migrationBuilder.DropColumn(
                name: "SalesAccountId",
                table: "SalesInvoiceLine");
        }
    }
}
