using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class Bills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccountId",
                table: "VendorPayment",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Memo",
                table: "VendorPayment",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "VendorPayment",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceNo",
                table: "VendorPayment",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Vendor",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidGeneralLedgerHeaderId",
                table: "SalesInvoiceHeader",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AccountId",
                table: "PurchaseInvoiceLine",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "PurchaseInvoiceLine",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                table: "PurchaseInvoiceLine",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "TaxId",
                table: "PurchaseInvoiceLine",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillFromJson",
                table: "PurchaseInvoiceHeader",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "PurchaseInvoiceHeader",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "PurchaseInvoiceHeader",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidGeneralLedgerHeaderId",
                table: "PurchaseInvoiceHeader",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendorPayment_AccountId",
                table: "VendorPayment",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceHeader_VoidGeneralLedgerHeaderId",
                table: "SalesInvoiceHeader",
                column: "VoidGeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLine_AccountId",
                table: "PurchaseInvoiceLine",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLine_TaxId",
                table: "PurchaseInvoiceLine",
                column: "TaxId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceHeader_VoidGeneralLedgerHeaderId",
                table: "PurchaseInvoiceHeader",
                column: "VoidGeneralLedgerHeaderId");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseInvoiceHeader_GeneralLedgerHeader_VoidGeneralLedgerHeaderId",
                table: "PurchaseInvoiceHeader",
                column: "VoidGeneralLedgerHeaderId",
                principalTable: "GeneralLedgerHeader",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseInvoiceLine_Account_AccountId",
                table: "PurchaseInvoiceLine",
                column: "AccountId",
                principalTable: "Account",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseInvoiceLine_Tax_TaxId",
                table: "PurchaseInvoiceLine",
                column: "TaxId",
                principalTable: "Tax",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceHeader_GeneralLedgerHeader_VoidGeneralLedgerHeaderId",
                table: "SalesInvoiceHeader",
                column: "VoidGeneralLedgerHeaderId",
                principalTable: "GeneralLedgerHeader",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_VendorPayment_Account_AccountId",
                table: "VendorPayment",
                column: "AccountId",
                principalTable: "Account",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseInvoiceHeader_GeneralLedgerHeader_VoidGeneralLedgerHeaderId",
                table: "PurchaseInvoiceHeader");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseInvoiceLine_Account_AccountId",
                table: "PurchaseInvoiceLine");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseInvoiceLine_Tax_TaxId",
                table: "PurchaseInvoiceLine");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceHeader_GeneralLedgerHeader_VoidGeneralLedgerHeaderId",
                table: "SalesInvoiceHeader");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorPayment_Account_AccountId",
                table: "VendorPayment");

            migrationBuilder.DropIndex(
                name: "IX_VendorPayment_AccountId",
                table: "VendorPayment");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceHeader_VoidGeneralLedgerHeaderId",
                table: "SalesInvoiceHeader");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoiceLine_AccountId",
                table: "PurchaseInvoiceLine");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoiceLine_TaxId",
                table: "PurchaseInvoiceLine");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoiceHeader_VoidGeneralLedgerHeaderId",
                table: "PurchaseInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "VendorPayment");

            migrationBuilder.DropColumn(
                name: "Memo",
                table: "VendorPayment");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "VendorPayment");

            migrationBuilder.DropColumn(
                name: "ReferenceNo",
                table: "VendorPayment");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Vendor");

            migrationBuilder.DropColumn(
                name: "VoidGeneralLedgerHeaderId",
                table: "SalesInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "PurchaseInvoiceLine");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "PurchaseInvoiceLine");

            migrationBuilder.DropColumn(
                name: "TaxAmount",
                table: "PurchaseInvoiceLine");

            migrationBuilder.DropColumn(
                name: "TaxId",
                table: "PurchaseInvoiceLine");

            migrationBuilder.DropColumn(
                name: "BillFromJson",
                table: "PurchaseInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "PurchaseInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "PurchaseInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "VoidGeneralLedgerHeaderId",
                table: "PurchaseInvoiceHeader");
        }
    }
}
