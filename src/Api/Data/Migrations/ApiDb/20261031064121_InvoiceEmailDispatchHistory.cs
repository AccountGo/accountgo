using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class InvoiceEmailDispatchHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DispatchId",
                table: "SalesInvoiceEmail",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FromEmail",
                table: "SalesInvoiceEmail",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceEmail_DispatchId",
                table: "SalesInvoiceEmail",
                column: "DispatchId",
                unique: true,
                filter: "[DispatchId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceEmail_DispatchId",
                table: "SalesInvoiceEmail");

            migrationBuilder.DropColumn(
                name: "DispatchId",
                table: "SalesInvoiceEmail");

            migrationBuilder.DropColumn(
                name: "FromEmail",
                table: "SalesInvoiceEmail");
        }
    }
}
