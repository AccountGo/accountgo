using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class AddPurchaseOrderNumberToPurchaseInvoiceHeader : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PurchaseOrderNumber",
                table: "PurchaseInvoiceHeader",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PurchaseOrderNumber",
                table: "PurchaseInvoiceHeader");
        }
    }
}
