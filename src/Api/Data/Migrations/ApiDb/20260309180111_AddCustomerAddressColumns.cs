using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class AddCustomerAddressColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                table: "Customer",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                table: "Customer",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Customer",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Customer",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "Customer",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Province",
                table: "Customer",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingAddressLine1",
                table: "Customer",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingAddressLine2",
                table: "Customer",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingCity",
                table: "Customer",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingCountry",
                table: "Customer",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingPostalCode",
                table: "Customer",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingProvince",
                table: "Customer",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddressLine1",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "AddressLine2",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "Province",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "ShippingAddressLine1",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "ShippingAddressLine2",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "ShippingCity",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "ShippingCountry",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "ShippingPostalCode",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "ShippingProvince",
                table: "Customer");
        }
    }
}
