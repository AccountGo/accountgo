using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class VendorInvoicingPaymentEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Vendor",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Vendor",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercentage",
                table: "Vendor",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentMethod",
                table: "Vendor",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "Vendor",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Province",
                table: "Vendor",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Street1",
                table: "Vendor",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Street2",
                table: "Vendor",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "City",
                table: "Vendor");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "Vendor");

            migrationBuilder.DropColumn(
                name: "DiscountPercentage",
                table: "Vendor");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Vendor");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "Vendor");

            migrationBuilder.DropColumn(
                name: "Province",
                table: "Vendor");

            migrationBuilder.DropColumn(
                name: "Street1",
                table: "Vendor");

            migrationBuilder.DropColumn(
                name: "Street2",
                table: "Vendor");
        }
    }
}
