using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class AddDonationInvoiceTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DonationInvoiceHeader",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DonorId = table.Column<int>(type: "int", nullable: false),
                    No = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReferenceNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Purpose = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsTaxReceiptIssued = table.Column<bool>(type: "bit", nullable: false),
                    TaxReceiptNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Posted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DonationInvoiceHeader", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DonationInvoiceHeader_Customer_DonorId",
                        column: x => x.DonorId,
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DonationInvoiceLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DonationInvoiceHeaderId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    MeasurementId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DonationInvoiceLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DonationInvoiceLine_DonationInvoiceHeader_DonationInvoiceHeaderId",
                        column: x => x.DonationInvoiceHeaderId,
                        principalTable: "DonationInvoiceHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DonationInvoiceLine_Item_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DonationInvoiceLine_Measurement_MeasurementId",
                        column: x => x.MeasurementId,
                        principalTable: "Measurement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DonationInvoiceHeader_DonorId",
                table: "DonationInvoiceHeader",
                column: "DonorId");

            migrationBuilder.CreateIndex(
                name: "IX_DonationInvoiceLine_DonationInvoiceHeaderId",
                table: "DonationInvoiceLine",
                column: "DonationInvoiceHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_DonationInvoiceLine_ItemId",
                table: "DonationInvoiceLine",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_DonationInvoiceLine_MeasurementId",
                table: "DonationInvoiceLine",
                column: "MeasurementId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DonationInvoiceLine");

            migrationBuilder.DropTable(
                name: "DonationInvoiceHeader");
        }
    }
}
