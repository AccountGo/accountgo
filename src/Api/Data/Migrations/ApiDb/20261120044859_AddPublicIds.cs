using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class AddPublicIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "YearEndCloseRun",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "WriteOffRecovery",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "VendorPayment",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "Vendor",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "TaxGroup",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "TaxFiling",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "Tax",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "SupplierReturnDispatchLine",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "SupplierReturnDispatch",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "SupplierCreditNoteLine",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "SupplierCreditNote",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "SupplierAdvance",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "SettlementApplication",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                schema: "dbo",
                table: "SecurityRole",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "SalesReceiptLine",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "SalesReceiptHeader",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "SalesQuoteLine",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "SalesQuoteHeader",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "SalesOrderLine",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "SalesOrderHeader",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "SalesInvoiceLine",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "SalesInvoiceHeader",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "SalesInvoiceEmail",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "SalesDeliveryHeader",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "RecurringSchedules",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "RecurringScheduleLines",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "RecurringOccurrences",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "ReceivableWriteOff",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "PurchaseReceiptLine",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "PurchaseReceiptHeader",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "PurchaseOrderLine",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "PurchaseOrderHeader",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "PurchaseInvoiceLine",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "PurchaseInvoiceHeader",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "PaymentTerm",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "PartyRefundAllocation",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "PartyRefund",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "Party",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "Partners",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "Measurement",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "LinkedEntryPairs",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "JournalEntryLine",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "JournalEntryHeader",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "ItemTaxGroup",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "ItemCategory",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "Item",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "InventoryControlJournal",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "InventoryAdjustment",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "GeneralLedgerLine",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "GeneralLedgerHeader",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "FinancialYear",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "Donors",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "DonationInvoiceLine",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "DonationInvoiceHeader",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "CustomerReturnReceiptLine",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "CustomerReturnReceipt",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "CustomerCreditNoteLine",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "CustomerCreditNote",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "CustomerAllocation",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "Customer",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "Contact",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "CompanyMember",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "CompanyInvitation",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "Company",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "BankTransaction",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "BankStatementLine",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "BankStatementImport",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "BankStatementDescriptionRule",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "BankReconciliationItem",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "BankReconciliation",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "Bank",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                schema: "dbo",
                table: "AuditLog",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "Account",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "Company",
                keyColumn: "Id",
                keyValue: 1,
                column: "PublicId",
                value: "ws_01j00000000000000000000001");

            migrationBuilder.UpdateData(
                table: "Donors",
                keyColumn: "Id",
                keyValue: 1,
                column: "PublicId",
                value: "dnr_01j00000000000000000000001");

            migrationBuilder.UpdateData(
                table: "Donors",
                keyColumn: "Id",
                keyValue: 2,
                column: "PublicId",
                value: "dnr_01j00000000000000000000002");


            foreach (var sql in Api.Data.PublicIdBackfillSql.ForApi(migrationBuilder.ActiveProvider))
            {
                migrationBuilder.Sql(sql);
            }

            migrationBuilder.CreateIndex(
                name: "IX_YearEndCloseRun_PublicId",
                table: "YearEndCloseRun",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_WriteOffRecovery_PublicId",
                table: "WriteOffRecovery",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_VendorPayment_PublicId",
                table: "VendorPayment",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_Vendor_PublicId",
                table: "Vendor",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_TaxGroup_PublicId",
                table: "TaxGroup",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_TaxFiling_PublicId",
                table: "TaxFiling",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_Tax_PublicId",
                table: "Tax",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnDispatchLine_PublicId",
                table: "SupplierReturnDispatchLine",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnDispatch_PublicId",
                table: "SupplierReturnDispatch",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNoteLine_PublicId",
                table: "SupplierCreditNoteLine",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNote_PublicId",
                table: "SupplierCreditNote",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierAdvance_PublicId",
                table: "SupplierAdvance",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementApplication_PublicId",
                table: "SettlementApplication",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityRole_PublicId",
                schema: "dbo",
                table: "SecurityRole",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReceiptLine_PublicId",
                table: "SalesReceiptLine",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReceiptHeader_PublicId",
                table: "SalesReceiptHeader",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteLine_PublicId",
                table: "SalesQuoteLine",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteHeader_PublicId",
                table: "SalesQuoteHeader",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLine_PublicId",
                table: "SalesOrderLine",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderHeader_PublicId",
                table: "SalesOrderHeader",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLine_PublicId",
                table: "SalesInvoiceLine",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceHeader_PublicId",
                table: "SalesInvoiceHeader",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceEmail_PublicId",
                table: "SalesInvoiceEmail",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_SalesDeliveryHeader_PublicId",
                table: "SalesDeliveryHeader",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSchedules_PublicId",
                table: "RecurringSchedules",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringScheduleLines_PublicId",
                table: "RecurringScheduleLines",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOccurrences_PublicId",
                table: "RecurringOccurrences",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableWriteOff_PublicId",
                table: "ReceivableWriteOff",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptLine_PublicId",
                table: "PurchaseReceiptLine",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptHeader_PublicId",
                table: "PurchaseReceiptHeader",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLine_PublicId",
                table: "PurchaseOrderLine",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderHeader_PublicId",
                table: "PurchaseOrderHeader",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLine_PublicId",
                table: "PurchaseInvoiceLine",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceHeader_PublicId",
                table: "PurchaseInvoiceHeader",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTerm_PublicId",
                table: "PaymentTerm",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_PartyRefundAllocation_PublicId",
                table: "PartyRefundAllocation",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_PartyRefund_PublicId",
                table: "PartyRefund",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_Party_PublicId",
                table: "Party",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_Partners_PublicId",
                table: "Partners",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_Measurement_PublicId",
                table: "Measurement",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_LinkedEntryPairs_PublicId",
                table: "LinkedEntryPairs",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLine_PublicId",
                table: "JournalEntryLine",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryHeader_PublicId",
                table: "JournalEntryHeader",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_ItemTaxGroup_PublicId",
                table: "ItemTaxGroup",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCategory_PublicId",
                table: "ItemCategory",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_Item_PublicId",
                table: "Item",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryControlJournal_PublicId",
                table: "InventoryControlJournal",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustment_PublicId",
                table: "InventoryAdjustment",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerLine_PublicId",
                table: "GeneralLedgerLine",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerHeader_PublicId",
                table: "GeneralLedgerHeader",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialYear_PublicId",
                table: "FinancialYear",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_Donors_PublicId",
                table: "Donors",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_DonationInvoiceLine_PublicId",
                table: "DonationInvoiceLine",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_DonationInvoiceHeader_PublicId",
                table: "DonationInvoiceHeader",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceiptLine_PublicId",
                table: "CustomerReturnReceiptLine",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceipt_PublicId",
                table: "CustomerReturnReceipt",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditNoteLine_PublicId",
                table: "CustomerCreditNoteLine",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditNote_PublicId",
                table: "CustomerCreditNote",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAllocation_PublicId",
                table: "CustomerAllocation",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_PublicId",
                table: "Customer",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_Contact_PublicId",
                table: "Contact",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyMember_PublicId",
                table: "CompanyMember",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyInvitation_PublicId",
                table: "CompanyInvitation",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_Company_PublicId",
                table: "Company",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransaction_PublicId",
                table: "BankTransaction",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLine_PublicId",
                table: "BankStatementLine",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImport_PublicId",
                table: "BankStatementImport",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementDescriptionRule_PublicId",
                table: "BankStatementDescriptionRule",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationItem_PublicId",
                table: "BankReconciliationItem",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliation_PublicId",
                table: "BankReconciliation",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_Bank_PublicId",
                table: "Bank",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_PublicId",
                schema: "dbo",
                table: "AuditLog",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_Account_PublicId",
                table: "Account",
                column: "PublicId",
                unique: true,
                filter: "[PublicId] <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_YearEndCloseRun_PublicId",
                table: "YearEndCloseRun");

            migrationBuilder.DropIndex(
                name: "IX_WriteOffRecovery_PublicId",
                table: "WriteOffRecovery");

            migrationBuilder.DropIndex(
                name: "IX_VendorPayment_PublicId",
                table: "VendorPayment");

            migrationBuilder.DropIndex(
                name: "IX_Vendor_PublicId",
                table: "Vendor");

            migrationBuilder.DropIndex(
                name: "IX_TaxGroup_PublicId",
                table: "TaxGroup");

            migrationBuilder.DropIndex(
                name: "IX_TaxFiling_PublicId",
                table: "TaxFiling");

            migrationBuilder.DropIndex(
                name: "IX_Tax_PublicId",
                table: "Tax");

            migrationBuilder.DropIndex(
                name: "IX_SupplierReturnDispatchLine_PublicId",
                table: "SupplierReturnDispatchLine");

            migrationBuilder.DropIndex(
                name: "IX_SupplierReturnDispatch_PublicId",
                table: "SupplierReturnDispatch");

            migrationBuilder.DropIndex(
                name: "IX_SupplierCreditNoteLine_PublicId",
                table: "SupplierCreditNoteLine");

            migrationBuilder.DropIndex(
                name: "IX_SupplierCreditNote_PublicId",
                table: "SupplierCreditNote");

            migrationBuilder.DropIndex(
                name: "IX_SupplierAdvance_PublicId",
                table: "SupplierAdvance");

            migrationBuilder.DropIndex(
                name: "IX_SettlementApplication_PublicId",
                table: "SettlementApplication");

            migrationBuilder.DropIndex(
                name: "IX_SecurityRole_PublicId",
                schema: "dbo",
                table: "SecurityRole");

            migrationBuilder.DropIndex(
                name: "IX_SalesReceiptLine_PublicId",
                table: "SalesReceiptLine");

            migrationBuilder.DropIndex(
                name: "IX_SalesReceiptHeader_PublicId",
                table: "SalesReceiptHeader");

            migrationBuilder.DropIndex(
                name: "IX_SalesQuoteLine_PublicId",
                table: "SalesQuoteLine");

            migrationBuilder.DropIndex(
                name: "IX_SalesQuoteHeader_PublicId",
                table: "SalesQuoteHeader");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrderLine_PublicId",
                table: "SalesOrderLine");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrderHeader_PublicId",
                table: "SalesOrderHeader");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceLine_PublicId",
                table: "SalesInvoiceLine");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceHeader_PublicId",
                table: "SalesInvoiceHeader");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceEmail_PublicId",
                table: "SalesInvoiceEmail");

            migrationBuilder.DropIndex(
                name: "IX_SalesDeliveryHeader_PublicId",
                table: "SalesDeliveryHeader");

            migrationBuilder.DropIndex(
                name: "IX_RecurringSchedules_PublicId",
                table: "RecurringSchedules");

            migrationBuilder.DropIndex(
                name: "IX_RecurringScheduleLines_PublicId",
                table: "RecurringScheduleLines");

            migrationBuilder.DropIndex(
                name: "IX_RecurringOccurrences_PublicId",
                table: "RecurringOccurrences");

            migrationBuilder.DropIndex(
                name: "IX_ReceivableWriteOff_PublicId",
                table: "ReceivableWriteOff");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReceiptLine_PublicId",
                table: "PurchaseReceiptLine");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReceiptHeader_PublicId",
                table: "PurchaseReceiptHeader");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrderLine_PublicId",
                table: "PurchaseOrderLine");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrderHeader_PublicId",
                table: "PurchaseOrderHeader");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoiceLine_PublicId",
                table: "PurchaseInvoiceLine");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoiceHeader_PublicId",
                table: "PurchaseInvoiceHeader");

            migrationBuilder.DropIndex(
                name: "IX_PaymentTerm_PublicId",
                table: "PaymentTerm");

            migrationBuilder.DropIndex(
                name: "IX_PartyRefundAllocation_PublicId",
                table: "PartyRefundAllocation");

            migrationBuilder.DropIndex(
                name: "IX_PartyRefund_PublicId",
                table: "PartyRefund");

            migrationBuilder.DropIndex(
                name: "IX_Party_PublicId",
                table: "Party");

            migrationBuilder.DropIndex(
                name: "IX_Partners_PublicId",
                table: "Partners");

            migrationBuilder.DropIndex(
                name: "IX_Measurement_PublicId",
                table: "Measurement");

            migrationBuilder.DropIndex(
                name: "IX_LinkedEntryPairs_PublicId",
                table: "LinkedEntryPairs");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryLine_PublicId",
                table: "JournalEntryLine");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryHeader_PublicId",
                table: "JournalEntryHeader");

            migrationBuilder.DropIndex(
                name: "IX_ItemTaxGroup_PublicId",
                table: "ItemTaxGroup");

            migrationBuilder.DropIndex(
                name: "IX_ItemCategory_PublicId",
                table: "ItemCategory");

            migrationBuilder.DropIndex(
                name: "IX_Item_PublicId",
                table: "Item");

            migrationBuilder.DropIndex(
                name: "IX_InventoryControlJournal_PublicId",
                table: "InventoryControlJournal");

            migrationBuilder.DropIndex(
                name: "IX_InventoryAdjustment_PublicId",
                table: "InventoryAdjustment");

            migrationBuilder.DropIndex(
                name: "IX_GeneralLedgerLine_PublicId",
                table: "GeneralLedgerLine");

            migrationBuilder.DropIndex(
                name: "IX_GeneralLedgerHeader_PublicId",
                table: "GeneralLedgerHeader");

            migrationBuilder.DropIndex(
                name: "IX_FinancialYear_PublicId",
                table: "FinancialYear");

            migrationBuilder.DropIndex(
                name: "IX_Donors_PublicId",
                table: "Donors");

            migrationBuilder.DropIndex(
                name: "IX_DonationInvoiceLine_PublicId",
                table: "DonationInvoiceLine");

            migrationBuilder.DropIndex(
                name: "IX_DonationInvoiceHeader_PublicId",
                table: "DonationInvoiceHeader");

            migrationBuilder.DropIndex(
                name: "IX_CustomerReturnReceiptLine_PublicId",
                table: "CustomerReturnReceiptLine");

            migrationBuilder.DropIndex(
                name: "IX_CustomerReturnReceipt_PublicId",
                table: "CustomerReturnReceipt");

            migrationBuilder.DropIndex(
                name: "IX_CustomerCreditNoteLine_PublicId",
                table: "CustomerCreditNoteLine");

            migrationBuilder.DropIndex(
                name: "IX_CustomerCreditNote_PublicId",
                table: "CustomerCreditNote");

            migrationBuilder.DropIndex(
                name: "IX_CustomerAllocation_PublicId",
                table: "CustomerAllocation");

            migrationBuilder.DropIndex(
                name: "IX_Customer_PublicId",
                table: "Customer");

            migrationBuilder.DropIndex(
                name: "IX_Contact_PublicId",
                table: "Contact");

            migrationBuilder.DropIndex(
                name: "IX_CompanyMember_PublicId",
                table: "CompanyMember");

            migrationBuilder.DropIndex(
                name: "IX_CompanyInvitation_PublicId",
                table: "CompanyInvitation");

            migrationBuilder.DropIndex(
                name: "IX_Company_PublicId",
                table: "Company");

            migrationBuilder.DropIndex(
                name: "IX_BankTransaction_PublicId",
                table: "BankTransaction");

            migrationBuilder.DropIndex(
                name: "IX_BankStatementLine_PublicId",
                table: "BankStatementLine");

            migrationBuilder.DropIndex(
                name: "IX_BankStatementImport_PublicId",
                table: "BankStatementImport");

            migrationBuilder.DropIndex(
                name: "IX_BankStatementDescriptionRule_PublicId",
                table: "BankStatementDescriptionRule");

            migrationBuilder.DropIndex(
                name: "IX_BankReconciliationItem_PublicId",
                table: "BankReconciliationItem");

            migrationBuilder.DropIndex(
                name: "IX_BankReconciliation_PublicId",
                table: "BankReconciliation");

            migrationBuilder.DropIndex(
                name: "IX_Bank_PublicId",
                table: "Bank");

            migrationBuilder.DropIndex(
                name: "IX_AuditLog_PublicId",
                schema: "dbo",
                table: "AuditLog");

            migrationBuilder.DropIndex(
                name: "IX_Account_PublicId",
                table: "Account");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "YearEndCloseRun");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "WriteOffRecovery");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "VendorPayment");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Vendor");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "TaxGroup");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "TaxFiling");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Tax");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "SupplierReturnDispatchLine");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "SupplierReturnDispatch");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "SupplierCreditNoteLine");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "SupplierCreditNote");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "SupplierAdvance");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "SettlementApplication");

            migrationBuilder.DropColumn(
                name: "PublicId",
                schema: "dbo",
                table: "SecurityRole");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "SalesReceiptLine");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "SalesReceiptHeader");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "SalesQuoteLine");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "SalesQuoteHeader");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "SalesOrderHeader");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "SalesInvoiceLine");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "SalesInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "SalesInvoiceEmail");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "SalesDeliveryHeader");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "RecurringSchedules");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "RecurringScheduleLines");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "RecurringOccurrences");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "ReceivableWriteOff");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "PurchaseReceiptLine");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "PurchaseReceiptHeader");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "PurchaseOrderLine");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "PurchaseOrderHeader");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "PurchaseInvoiceLine");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "PurchaseInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "PaymentTerm");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "PartyRefundAllocation");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "PartyRefund");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Party");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Measurement");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "LinkedEntryPairs");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "JournalEntryLine");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "JournalEntryHeader");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "ItemTaxGroup");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "ItemCategory");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Item");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "InventoryControlJournal");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "InventoryAdjustment");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "GeneralLedgerLine");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "GeneralLedgerHeader");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "FinancialYear");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Donors");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "DonationInvoiceLine");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "DonationInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "CustomerReturnReceiptLine");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "CustomerReturnReceipt");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "CustomerCreditNoteLine");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "CustomerCreditNote");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "CustomerAllocation");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Contact");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "CompanyMember");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "CompanyInvitation");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Company");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "BankTransaction");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "BankStatementLine");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "BankStatementImport");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "BankStatementDescriptionRule");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "BankReconciliationItem");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "BankReconciliation");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Bank");

            migrationBuilder.DropColumn(
                name: "PublicId",
                schema: "dbo",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "Account");
        }
    }
}
