using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class AddCustomerPartyAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompanyCountry",
                table: "Party",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanyPostalCode",
                table: "Party",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanyProvince",
                table: "Party",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanyStreet",
                table: "Party",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingCountry",
                table: "Party",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingPostalCode",
                table: "Party",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingProvince",
                table: "Party",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingStreet",
                table: "Party",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompanyCountry",
                table: "Party");

            migrationBuilder.DropColumn(
                name: "CompanyPostalCode",
                table: "Party");

            migrationBuilder.DropColumn(
                name: "CompanyProvince",
                table: "Party");

            migrationBuilder.DropColumn(
                name: "CompanyStreet",
                table: "Party");

            migrationBuilder.DropColumn(
                name: "ShippingCountry",
                table: "Party");

            migrationBuilder.DropColumn(
                name: "ShippingPostalCode",
                table: "Party");

            migrationBuilder.DropColumn(
                name: "ShippingProvince",
                table: "Party");

            migrationBuilder.DropColumn(
                name: "ShippingStreet",
                table: "Party");
        }
    }
}
