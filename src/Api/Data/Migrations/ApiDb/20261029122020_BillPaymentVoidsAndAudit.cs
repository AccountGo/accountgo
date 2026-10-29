using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class BillPaymentVoidsAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VoidGeneralLedgerHeaderId",
                table: "VendorPayment",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "VendorPayment",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAt",
                table: "VendorPayment",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidedBy",
                table: "VendorPayment",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "PurchaseInvoiceHeader",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "PurchaseInvoiceHeader",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PayableAccountId",
                table: "PurchaseInvoiceHeader",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "PurchaseInvoiceHeader",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAt",
                table: "PurchaseInvoiceHeader",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidedBy",
                table: "PurchaseInvoiceHeader",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendorPayment_VoidGeneralLedgerHeaderId",
                table: "VendorPayment",
                column: "VoidGeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceHeader_PayableAccountId",
                table: "PurchaseInvoiceHeader",
                column: "PayableAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseInvoiceHeader_Account_PayableAccountId",
                table: "PurchaseInvoiceHeader",
                column: "PayableAccountId",
                principalTable: "Account",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_VendorPayment_GeneralLedgerHeader_VoidGeneralLedgerHeaderId",
                table: "VendorPayment",
                column: "VoidGeneralLedgerHeaderId",
                principalTable: "GeneralLedgerHeader",
                principalColumn: "Id");

            migrationBuilder.Sql(@"
                UPDATE b
                SET b.PayableAccountId = (
                    SELECT TOP 1 l.AccountId
                    FROM GeneralLedgerLine l
                    INNER JOIN Account a ON a.Id = l.AccountId
                    WHERE l.GeneralLedgerHeaderId = b.GeneralLedgerHeaderId
                      AND l.DrCr = 2
                      AND a.AccountClassId = 2
                    ORDER BY l.Amount DESC
                )
                FROM PurchaseInvoiceHeader b
                WHERE b.GeneralLedgerHeaderId IS NOT NULL
                  AND b.PayableAccountId IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseInvoiceHeader_Account_PayableAccountId",
                table: "PurchaseInvoiceHeader");

            migrationBuilder.DropForeignKey(
                name: "FK_VendorPayment_GeneralLedgerHeader_VoidGeneralLedgerHeaderId",
                table: "VendorPayment");

            migrationBuilder.DropIndex(
                name: "IX_VendorPayment_VoidGeneralLedgerHeaderId",
                table: "VendorPayment");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoiceHeader_PayableAccountId",
                table: "PurchaseInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "VoidGeneralLedgerHeaderId",
                table: "VendorPayment");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "VendorPayment");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "VendorPayment");

            migrationBuilder.DropColumn(
                name: "VoidedBy",
                table: "VendorPayment");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "PurchaseInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "PurchaseInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "PayableAccountId",
                table: "PurchaseInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "PurchaseInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "PurchaseInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "VoidedBy",
                table: "PurchaseInvoiceHeader");
        }
    }
}
