using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class UpdateToTaxSystem_Purchasing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseInvoiceLine_Measurement_MeasurementId",
                table: "PurchaseInvoiceLine");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLine_Measurement_MeasurementId",
                table: "PurchaseOrderLine");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrderLine_MeasurementId",
                table: "PurchaseOrderLine");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoiceLine_MeasurementId",
                table: "PurchaseInvoiceLine");

            migrationBuilder.DropColumn(
                name: "MeasurementId",
                table: "PurchaseOrderLine");

            migrationBuilder.DropColumn(
                name: "MeasurementId",
                table: "PurchaseInvoiceLine");

            migrationBuilder.AddColumn<int>(
                name: "TaxGroupId",
                table: "PurchaseOrderLine",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TaxGroupId",
                table: "PurchaseInvoiceLine",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaxGroupId",
                table: "PurchaseOrderLine");

            migrationBuilder.DropColumn(
                name: "TaxGroupId",
                table: "PurchaseInvoiceLine");

            migrationBuilder.AddColumn<int>(
                name: "MeasurementId",
                table: "PurchaseOrderLine",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MeasurementId",
                table: "PurchaseInvoiceLine",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLine_MeasurementId",
                table: "PurchaseOrderLine",
                column: "MeasurementId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLine_MeasurementId",
                table: "PurchaseInvoiceLine",
                column: "MeasurementId");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseInvoiceLine_Measurement_MeasurementId",
                table: "PurchaseInvoiceLine",
                column: "MeasurementId",
                principalTable: "Measurement",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLine_Measurement_MeasurementId",
                table: "PurchaseOrderLine",
                column: "MeasurementId",
                principalTable: "Measurement",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
