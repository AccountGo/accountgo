using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class CreditsReturnsRefundsFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CreditedAmount",
                table: "SalesInvoiceHeader",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ReceivableAccountId",
                table: "SalesInvoiceHeader",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "SalesInvoiceHeader",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<decimal>(
                name: "WrittenOffAmount",
                table: "SalesInvoiceHeader",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AdvanceAppliedAmount",
                table: "PurchaseInvoiceHeader",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CreditedAmount",
                table: "PurchaseInvoiceHeader",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "PurchaseInvoiceHeader",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "BadDebtRecoveryAccountId",
                table: "GeneralLedgerSetting",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustomerCreditLiabilityAccountId",
                table: "GeneralLedgerSetting",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImpairmentExpenseAccountId",
                table: "GeneralLedgerSetting",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReceivableLossAllowanceAccountId",
                table: "GeneralLedgerSetting",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReturnValuationDifferenceAccountId",
                table: "GeneralLedgerSetting",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalesReturnsAllowancesAccountId",
                table: "GeneralLedgerSetting",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupplierAdvanceAccountId",
                table: "GeneralLedgerSetting",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupplierCreditAssetAccountId",
                table: "GeneralLedgerSetting",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupplierReturnClearingAccountId",
                table: "GeneralLedgerSetting",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CreditsReturnsRefundsEnabled",
                table: "CompanySetting",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CustomerCreditNote",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    SourceSalesInvoiceHeaderId = table.Column<int>(type: "int", nullable: true),
                    No = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    CurrencyPrecision = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ReturnGoodsWithoutRestock = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SourceReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    EvidenceReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SellerJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BillToJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CustomerCreditLiabilityAccountId = table.Column<int>(type: "int", nullable: true),
                    SalesReturnsAccountId = table.Column<int>(type: "int", nullable: true),
                    ReceivableAccountId = table.Column<int>(type: "int", nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    AppliedAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    RefundedAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    GeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: true),
                    VoidGeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: true),
                    SubmittedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PostedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReversedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerCreditNote", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerCreditNote_Account_CustomerCreditLiabilityAccountId",
                        column: x => x.CustomerCreditLiabilityAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerCreditNote_Account_ReceivableAccountId",
                        column: x => x.ReceivableAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerCreditNote_Account_SalesReturnsAccountId",
                        column: x => x.SalesReturnsAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerCreditNote_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerCreditNote_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerCreditNote_GeneralLedgerHeader_VoidGeneralLedgerHeaderId",
                        column: x => x.VoidGeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerCreditNote_SalesInvoiceHeader_SourceSalesInvoiceHeaderId",
                        column: x => x.SourceSalesInvoiceHeaderId,
                        principalTable: "SalesInvoiceHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PartyRefund",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    VendorId = table.Column<int>(type: "int", nullable: true),
                    No = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    CurrencyPrecision = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    BankId = table.Column<int>(type: "int", nullable: false),
                    BankAccountId = table.Column<int>(type: "int", nullable: false),
                    PaymentMethod = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExternalReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    EvidenceReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    GeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: true),
                    VoidGeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: true),
                    SubmittedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PostedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReversedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyRefund", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartyRefund_Bank_BankId",
                        column: x => x.BankId,
                        principalTable: "Bank",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartyRefund_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartyRefund_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartyRefund_GeneralLedgerHeader_VoidGeneralLedgerHeaderId",
                        column: x => x.VoidGeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartyRefund_Vendor_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceivableWriteOff",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    SalesInvoiceHeaderId = table.Column<int>(type: "int", nullable: false),
                    No = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    AllowanceAllocatedAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ImpairmentShortfallAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    RecoveredAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ReceivableAccountId = table.Column<int>(type: "int", nullable: true),
                    LossAllowanceAccountId = table.Column<int>(type: "int", nullable: true),
                    ImpairmentExpenseAccountId = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CollectionEvidence = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    GeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: true),
                    VoidGeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: true),
                    SubmittedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PolicySnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PostedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReversedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivableWriteOff", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceivableWriteOff_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivableWriteOff_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivableWriteOff_GeneralLedgerHeader_VoidGeneralLedgerHeaderId",
                        column: x => x.VoidGeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivableWriteOff_SalesInvoiceHeader_SalesInvoiceHeaderId",
                        column: x => x.SalesInvoiceHeaderId,
                        principalTable: "SalesInvoiceHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SettlementCommandReceipt",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    OperationKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResultEntityType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResultEntityId = table.Column<int>(type: "int", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementCommandReceipt", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SupplierAdvance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    VendorId = table.Column<int>(type: "int", nullable: false),
                    PurchaseOrderHeaderId = table.Column<int>(type: "int", nullable: true),
                    No = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    CurrencyPrecision = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    AppliedAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    RefundedAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    BankId = table.Column<int>(type: "int", nullable: false),
                    BankAccountId = table.Column<int>(type: "int", nullable: false),
                    SupplierAdvanceAccountId = table.Column<int>(type: "int", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsRefundable = table.Column<bool>(type: "bit", nullable: false),
                    ReferenceNo = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    EvidenceReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    GeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: true),
                    VoidGeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: true),
                    SubmittedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PostedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReversedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierAdvance", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierAdvance_Bank_BankId",
                        column: x => x.BankId,
                        principalTable: "Bank",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierAdvance_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierAdvance_GeneralLedgerHeader_VoidGeneralLedgerHeaderId",
                        column: x => x.VoidGeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierAdvance_PurchaseOrderHeader_PurchaseOrderHeaderId",
                        column: x => x.PurchaseOrderHeaderId,
                        principalTable: "PurchaseOrderHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierAdvance_Vendor_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierCreditNote",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    VendorId = table.Column<int>(type: "int", nullable: false),
                    SourcePurchaseInvoiceHeaderId = table.Column<int>(type: "int", nullable: true),
                    No = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    VendorCreditNo = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    CurrencyPrecision = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    IsStandalone = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EvidenceReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BillFromJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SupplierCreditAssetAccountId = table.Column<int>(type: "int", nullable: true),
                    PayableAccountId = table.Column<int>(type: "int", nullable: true),
                    ReturnClearingAccountId = table.Column<int>(type: "int", nullable: true),
                    ValuationDifferenceAccountId = table.Column<int>(type: "int", nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    AppliedAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    RefundedAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    GeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: true),
                    VoidGeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: true),
                    SubmittedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PostedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReversedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierCreditNote", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierCreditNote_Account_PayableAccountId",
                        column: x => x.PayableAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCreditNote_Account_ReturnClearingAccountId",
                        column: x => x.ReturnClearingAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCreditNote_Account_SupplierCreditAssetAccountId",
                        column: x => x.SupplierCreditAssetAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCreditNote_Account_ValuationDifferenceAccountId",
                        column: x => x.ValuationDifferenceAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCreditNote_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCreditNote_GeneralLedgerHeader_VoidGeneralLedgerHeaderId",
                        column: x => x.VoidGeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCreditNote_PurchaseInvoiceHeader_SourcePurchaseInvoiceHeaderId",
                        column: x => x.SourcePurchaseInvoiceHeaderId,
                        principalTable: "PurchaseInvoiceHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCreditNote_Vendor_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerCreditNoteLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    CustomerCreditNoteId = table.Column<int>(type: "int", nullable: false),
                    SourceSalesInvoiceLineId = table.Column<int>(type: "int", nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    TaxId = table.Column<int>(type: "int", nullable: true),
                    TaxAccountId = table.Column<int>(type: "int", nullable: true),
                    TaxRateSnapshot = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TaxInclusiveSnapshot = table.Column<bool>(type: "bit", nullable: false),
                    IsShippingLine = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerCreditNoteLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerCreditNoteLine_Account_TaxAccountId",
                        column: x => x.TaxAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerCreditNoteLine_CustomerCreditNote_CustomerCreditNoteId",
                        column: x => x.CustomerCreditNoteId,
                        principalTable: "CustomerCreditNote",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerCreditNoteLine_Item_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerCreditNoteLine_SalesInvoiceLine_SourceSalesInvoiceLineId",
                        column: x => x.SourceSalesInvoiceLineId,
                        principalTable: "SalesInvoiceLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerCreditNoteLine_Tax_TaxId",
                        column: x => x.TaxId,
                        principalTable: "Tax",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerReturnReceipt",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    SalesInvoiceHeaderId = table.Column<int>(type: "int", nullable: false),
                    CustomerCreditNoteId = table.Column<int>(type: "int", nullable: true),
                    No = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ReceiptDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    GeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: true),
                    VoidGeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: true),
                    PostedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReversedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReturnReceipt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerReturnReceipt_CustomerCreditNote_CustomerCreditNoteId",
                        column: x => x.CustomerCreditNoteId,
                        principalTable: "CustomerCreditNote",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnReceipt_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnReceipt_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnReceipt_GeneralLedgerHeader_VoidGeneralLedgerHeaderId",
                        column: x => x.VoidGeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnReceipt_SalesInvoiceHeader_SalesInvoiceHeaderId",
                        column: x => x.SalesInvoiceHeaderId,
                        principalTable: "SalesInvoiceHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WriteOffRecovery",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    ReceivableWriteOffId = table.Column<int>(type: "int", nullable: false),
                    No = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    BankId = table.Column<int>(type: "int", nullable: false),
                    BankAccountId = table.Column<int>(type: "int", nullable: false),
                    BadDebtRecoveryAccountId = table.Column<int>(type: "int", nullable: false),
                    ReferenceNo = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    EvidenceReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    GeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: true),
                    VoidGeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: true),
                    PostedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReversedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WriteOffRecovery", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WriteOffRecovery_Bank_BankId",
                        column: x => x.BankId,
                        principalTable: "Bank",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WriteOffRecovery_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WriteOffRecovery_GeneralLedgerHeader_VoidGeneralLedgerHeaderId",
                        column: x => x.VoidGeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WriteOffRecovery_ReceivableWriteOff_ReceivableWriteOffId",
                        column: x => x.ReceivableWriteOffId,
                        principalTable: "ReceivableWriteOff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PartyRefundAllocation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    PartyRefundId = table.Column<int>(type: "int", nullable: false),
                    SourceType = table.Column<int>(type: "int", nullable: false),
                    CustomerCreditNoteId = table.Column<int>(type: "int", nullable: true),
                    SupplierCreditNoteId = table.Column<int>(type: "int", nullable: true),
                    SupplierAdvanceId = table.Column<int>(type: "int", nullable: true),
                    SourceAccountId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyRefundAllocation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartyRefundAllocation_CustomerCreditNote_CustomerCreditNoteId",
                        column: x => x.CustomerCreditNoteId,
                        principalTable: "CustomerCreditNote",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartyRefundAllocation_PartyRefund_PartyRefundId",
                        column: x => x.PartyRefundId,
                        principalTable: "PartyRefund",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PartyRefundAllocation_SupplierAdvance_SupplierAdvanceId",
                        column: x => x.SupplierAdvanceId,
                        principalTable: "SupplierAdvance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartyRefundAllocation_SupplierCreditNote_SupplierCreditNoteId",
                        column: x => x.SupplierCreditNoteId,
                        principalTable: "SupplierCreditNote",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SettlementApplication",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    SourceType = table.Column<int>(type: "int", nullable: false),
                    CustomerCreditNoteId = table.Column<int>(type: "int", nullable: true),
                    SupplierCreditNoteId = table.Column<int>(type: "int", nullable: true),
                    SupplierAdvanceId = table.Column<int>(type: "int", nullable: true),
                    DestinationType = table.Column<int>(type: "int", nullable: false),
                    SalesInvoiceHeaderId = table.Column<int>(type: "int", nullable: true),
                    PurchaseInvoiceHeaderId = table.Column<int>(type: "int", nullable: true),
                    CurrencyCode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SourceAccountId = table.Column<int>(type: "int", nullable: false),
                    DestinationAccountId = table.Column<int>(type: "int", nullable: false),
                    GeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsReversed = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementApplication", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SettlementApplication_CustomerCreditNote_CustomerCreditNoteId",
                        column: x => x.CustomerCreditNoteId,
                        principalTable: "CustomerCreditNote",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SettlementApplication_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SettlementApplication_PurchaseInvoiceHeader_PurchaseInvoiceHeaderId",
                        column: x => x.PurchaseInvoiceHeaderId,
                        principalTable: "PurchaseInvoiceHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SettlementApplication_SalesInvoiceHeader_SalesInvoiceHeaderId",
                        column: x => x.SalesInvoiceHeaderId,
                        principalTable: "SalesInvoiceHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SettlementApplication_SupplierAdvance_SupplierAdvanceId",
                        column: x => x.SupplierAdvanceId,
                        principalTable: "SupplierAdvance",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SettlementApplication_SupplierCreditNote_SupplierCreditNoteId",
                        column: x => x.SupplierCreditNoteId,
                        principalTable: "SupplierCreditNote",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierReturnDispatch",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    VendorId = table.Column<int>(type: "int", nullable: false),
                    PurchaseReceiptHeaderId = table.Column<int>(type: "int", nullable: true),
                    PurchaseInvoiceHeaderId = table.Column<int>(type: "int", nullable: true),
                    SupplierCreditNoteId = table.Column<int>(type: "int", nullable: true),
                    No = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DispatchDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ControlTransferDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HasControlTransferred = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EvidenceReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReturnClearingAccountId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    GeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: true),
                    VoidGeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: true),
                    PostedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReversedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierReturnDispatch", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierReturnDispatch_Account_ReturnClearingAccountId",
                        column: x => x.ReturnClearingAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnDispatch_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnDispatch_GeneralLedgerHeader_VoidGeneralLedgerHeaderId",
                        column: x => x.VoidGeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnDispatch_PurchaseInvoiceHeader_PurchaseInvoiceHeaderId",
                        column: x => x.PurchaseInvoiceHeaderId,
                        principalTable: "PurchaseInvoiceHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnDispatch_PurchaseReceiptHeader_PurchaseReceiptHeaderId",
                        column: x => x.PurchaseReceiptHeaderId,
                        principalTable: "PurchaseReceiptHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnDispatch_SupplierCreditNote_SupplierCreditNoteId",
                        column: x => x.SupplierCreditNoteId,
                        principalTable: "SupplierCreditNote",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnDispatch_Vendor_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerReturnReceiptLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    CustomerReturnReceiptId = table.Column<int>(type: "int", nullable: false),
                    SalesInvoiceLineId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Condition = table.Column<int>(type: "int", nullable: false),
                    OriginalUnitCost = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    RecoverableUnitCost = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TotalRestoredCost = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    InventoryControlJournalId = table.Column<int>(type: "int", nullable: true),
                    InventoryAccountId = table.Column<int>(type: "int", nullable: true),
                    CogsAccountId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReturnReceiptLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerReturnReceiptLine_Account_CogsAccountId",
                        column: x => x.CogsAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnReceiptLine_Account_InventoryAccountId",
                        column: x => x.InventoryAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnReceiptLine_CustomerReturnReceipt_CustomerReturnReceiptId",
                        column: x => x.CustomerReturnReceiptId,
                        principalTable: "CustomerReturnReceipt",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerReturnReceiptLine_InventoryControlJournal_InventoryControlJournalId",
                        column: x => x.InventoryControlJournalId,
                        principalTable: "InventoryControlJournal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnReceiptLine_Item_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnReceiptLine_SalesInvoiceLine_SalesInvoiceLineId",
                        column: x => x.SalesInvoiceLineId,
                        principalTable: "SalesInvoiceLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SettlementApplicationReversal",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    SettlementApplicationId = table.Column<int>(type: "int", nullable: false),
                    ReversalDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ReversedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ReversedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementApplicationReversal", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SettlementApplicationReversal_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SettlementApplicationReversal_SettlementApplication_SettlementApplicationId",
                        column: x => x.SettlementApplicationId,
                        principalTable: "SettlementApplication",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierCreditNoteLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    SupplierCreditNoteId = table.Column<int>(type: "int", nullable: false),
                    SourcePurchaseInvoiceLineId = table.Column<int>(type: "int", nullable: true),
                    MatchedReturnDispatchId = table.Column<int>(type: "int", nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    ExpenseOrAssetAccountId = table.Column<int>(type: "int", nullable: true),
                    TaxId = table.Column<int>(type: "int", nullable: true),
                    TaxAccountId = table.Column<int>(type: "int", nullable: true),
                    TaxRateSnapshot = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TaxRecoverableSnapshot = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierCreditNoteLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierCreditNoteLine_Account_ExpenseOrAssetAccountId",
                        column: x => x.ExpenseOrAssetAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCreditNoteLine_Account_TaxAccountId",
                        column: x => x.TaxAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCreditNoteLine_Item_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCreditNoteLine_PurchaseInvoiceLine_SourcePurchaseInvoiceLineId",
                        column: x => x.SourcePurchaseInvoiceLineId,
                        principalTable: "PurchaseInvoiceLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCreditNoteLine_SupplierCreditNote_SupplierCreditNoteId",
                        column: x => x.SupplierCreditNoteId,
                        principalTable: "SupplierCreditNote",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SupplierCreditNoteLine_SupplierReturnDispatch_MatchedReturnDispatchId",
                        column: x => x.MatchedReturnDispatchId,
                        principalTable: "SupplierReturnDispatch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierCreditNoteLine_Tax_TaxId",
                        column: x => x.TaxId,
                        principalTable: "Tax",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierReturnDispatchLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    SupplierReturnDispatchId = table.Column<int>(type: "int", nullable: false),
                    PurchaseReceiptLineId = table.Column<int>(type: "int", nullable: true),
                    PurchaseInvoiceLineId = table.Column<int>(type: "int", nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CarryingUnitCost = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TotalCarryingCost = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    InventoryControlJournalId = table.Column<int>(type: "int", nullable: true),
                    InventoryAccountId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierReturnDispatchLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierReturnDispatchLine_Account_InventoryAccountId",
                        column: x => x.InventoryAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnDispatchLine_InventoryControlJournal_InventoryControlJournalId",
                        column: x => x.InventoryControlJournalId,
                        principalTable: "InventoryControlJournal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnDispatchLine_Item_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnDispatchLine_PurchaseInvoiceLine_PurchaseInvoiceLineId",
                        column: x => x.PurchaseInvoiceLineId,
                        principalTable: "PurchaseInvoiceLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnDispatchLine_PurchaseReceiptLine_PurchaseReceiptLineId",
                        column: x => x.PurchaseReceiptLineId,
                        principalTable: "PurchaseReceiptLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierReturnDispatchLine_SupplierReturnDispatch_SupplierReturnDispatchId",
                        column: x => x.SupplierReturnDispatchId,
                        principalTable: "SupplierReturnDispatch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceHeader_ReceivableAccountId",
                table: "SalesInvoiceHeader",
                column: "ReceivableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerSetting_BadDebtRecoveryAccountId",
                table: "GeneralLedgerSetting",
                column: "BadDebtRecoveryAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerSetting_CustomerCreditLiabilityAccountId",
                table: "GeneralLedgerSetting",
                column: "CustomerCreditLiabilityAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerSetting_ImpairmentExpenseAccountId",
                table: "GeneralLedgerSetting",
                column: "ImpairmentExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerSetting_ReceivableLossAllowanceAccountId",
                table: "GeneralLedgerSetting",
                column: "ReceivableLossAllowanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerSetting_ReturnValuationDifferenceAccountId",
                table: "GeneralLedgerSetting",
                column: "ReturnValuationDifferenceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerSetting_SalesReturnsAllowancesAccountId",
                table: "GeneralLedgerSetting",
                column: "SalesReturnsAllowancesAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerSetting_SupplierAdvanceAccountId",
                table: "GeneralLedgerSetting",
                column: "SupplierAdvanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerSetting_SupplierCreditAssetAccountId",
                table: "GeneralLedgerSetting",
                column: "SupplierCreditAssetAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerSetting_SupplierReturnClearingAccountId",
                table: "GeneralLedgerSetting",
                column: "SupplierReturnClearingAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditNote_CompanyId_No",
                table: "CustomerCreditNote",
                columns: new[] { "CompanyId", "No" },
                unique: true,
                filter: "[No] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditNote_CustomerCreditLiabilityAccountId",
                table: "CustomerCreditNote",
                column: "CustomerCreditLiabilityAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditNote_CustomerId",
                table: "CustomerCreditNote",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditNote_GeneralLedgerHeaderId",
                table: "CustomerCreditNote",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditNote_ReceivableAccountId",
                table: "CustomerCreditNote",
                column: "ReceivableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditNote_SalesReturnsAccountId",
                table: "CustomerCreditNote",
                column: "SalesReturnsAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditNote_SourceSalesInvoiceHeaderId",
                table: "CustomerCreditNote",
                column: "SourceSalesInvoiceHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditNote_VoidGeneralLedgerHeaderId",
                table: "CustomerCreditNote",
                column: "VoidGeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditNoteLine_CustomerCreditNoteId",
                table: "CustomerCreditNoteLine",
                column: "CustomerCreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditNoteLine_ItemId",
                table: "CustomerCreditNoteLine",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditNoteLine_SourceSalesInvoiceLineId",
                table: "CustomerCreditNoteLine",
                column: "SourceSalesInvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditNoteLine_TaxAccountId",
                table: "CustomerCreditNoteLine",
                column: "TaxAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCreditNoteLine_TaxId",
                table: "CustomerCreditNoteLine",
                column: "TaxId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceipt_CompanyId_No",
                table: "CustomerReturnReceipt",
                columns: new[] { "CompanyId", "No" },
                unique: true,
                filter: "[No] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceipt_CustomerCreditNoteId",
                table: "CustomerReturnReceipt",
                column: "CustomerCreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceipt_CustomerId",
                table: "CustomerReturnReceipt",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceipt_GeneralLedgerHeaderId",
                table: "CustomerReturnReceipt",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceipt_SalesInvoiceHeaderId",
                table: "CustomerReturnReceipt",
                column: "SalesInvoiceHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceipt_VoidGeneralLedgerHeaderId",
                table: "CustomerReturnReceipt",
                column: "VoidGeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceiptLine_CogsAccountId",
                table: "CustomerReturnReceiptLine",
                column: "CogsAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceiptLine_CustomerReturnReceiptId",
                table: "CustomerReturnReceiptLine",
                column: "CustomerReturnReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceiptLine_InventoryAccountId",
                table: "CustomerReturnReceiptLine",
                column: "InventoryAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceiptLine_InventoryControlJournalId",
                table: "CustomerReturnReceiptLine",
                column: "InventoryControlJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceiptLine_ItemId",
                table: "CustomerReturnReceiptLine",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnReceiptLine_SalesInvoiceLineId",
                table: "CustomerReturnReceiptLine",
                column: "SalesInvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyRefund_BankId",
                table: "PartyRefund",
                column: "BankId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyRefund_CompanyId_No",
                table: "PartyRefund",
                columns: new[] { "CompanyId", "No" },
                unique: true,
                filter: "[No] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PartyRefund_CustomerId",
                table: "PartyRefund",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyRefund_GeneralLedgerHeaderId",
                table: "PartyRefund",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyRefund_VendorId",
                table: "PartyRefund",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyRefund_VoidGeneralLedgerHeaderId",
                table: "PartyRefund",
                column: "VoidGeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyRefundAllocation_CustomerCreditNoteId",
                table: "PartyRefundAllocation",
                column: "CustomerCreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyRefundAllocation_PartyRefundId",
                table: "PartyRefundAllocation",
                column: "PartyRefundId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyRefundAllocation_SupplierAdvanceId",
                table: "PartyRefundAllocation",
                column: "SupplierAdvanceId");

            migrationBuilder.CreateIndex(
                name: "IX_PartyRefundAllocation_SupplierCreditNoteId",
                table: "PartyRefundAllocation",
                column: "SupplierCreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableWriteOff_CompanyId_No",
                table: "ReceivableWriteOff",
                columns: new[] { "CompanyId", "No" },
                unique: true,
                filter: "[No] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableWriteOff_CustomerId",
                table: "ReceivableWriteOff",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableWriteOff_GeneralLedgerHeaderId",
                table: "ReceivableWriteOff",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableWriteOff_SalesInvoiceHeaderId",
                table: "ReceivableWriteOff",
                column: "SalesInvoiceHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivableWriteOff_VoidGeneralLedgerHeaderId",
                table: "ReceivableWriteOff",
                column: "VoidGeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementApplication_CompanyId_PurchaseInvoiceHeaderId",
                table: "SettlementApplication",
                columns: new[] { "CompanyId", "PurchaseInvoiceHeaderId" });

            migrationBuilder.CreateIndex(
                name: "IX_SettlementApplication_CompanyId_SalesInvoiceHeaderId",
                table: "SettlementApplication",
                columns: new[] { "CompanyId", "SalesInvoiceHeaderId" });

            migrationBuilder.CreateIndex(
                name: "IX_SettlementApplication_CustomerCreditNoteId",
                table: "SettlementApplication",
                column: "CustomerCreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementApplication_GeneralLedgerHeaderId",
                table: "SettlementApplication",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementApplication_PurchaseInvoiceHeaderId",
                table: "SettlementApplication",
                column: "PurchaseInvoiceHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementApplication_SalesInvoiceHeaderId",
                table: "SettlementApplication",
                column: "SalesInvoiceHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementApplication_SupplierAdvanceId",
                table: "SettlementApplication",
                column: "SupplierAdvanceId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementApplication_SupplierCreditNoteId",
                table: "SettlementApplication",
                column: "SupplierCreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementApplicationReversal_GeneralLedgerHeaderId",
                table: "SettlementApplicationReversal",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementApplicationReversal_SettlementApplicationId",
                table: "SettlementApplicationReversal",
                column: "SettlementApplicationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SettlementCommandReceipt_Company_Op_Key",
                table: "SettlementCommandReceipt",
                columns: new[] { "CompanyId", "OperationKey", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierAdvance_BankId",
                table: "SupplierAdvance",
                column: "BankId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierAdvance_CompanyId_No",
                table: "SupplierAdvance",
                columns: new[] { "CompanyId", "No" },
                unique: true,
                filter: "[No] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierAdvance_GeneralLedgerHeaderId",
                table: "SupplierAdvance",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierAdvance_PurchaseOrderHeaderId",
                table: "SupplierAdvance",
                column: "PurchaseOrderHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierAdvance_VendorId",
                table: "SupplierAdvance",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierAdvance_VoidGeneralLedgerHeaderId",
                table: "SupplierAdvance",
                column: "VoidGeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNote_CompanyId_No",
                table: "SupplierCreditNote",
                columns: new[] { "CompanyId", "No" },
                unique: true,
                filter: "[No] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNote_GeneralLedgerHeaderId",
                table: "SupplierCreditNote",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNote_PayableAccountId",
                table: "SupplierCreditNote",
                column: "PayableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNote_ReturnClearingAccountId",
                table: "SupplierCreditNote",
                column: "ReturnClearingAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNote_SourcePurchaseInvoiceHeaderId",
                table: "SupplierCreditNote",
                column: "SourcePurchaseInvoiceHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNote_SupplierCreditAssetAccountId",
                table: "SupplierCreditNote",
                column: "SupplierCreditAssetAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNote_ValuationDifferenceAccountId",
                table: "SupplierCreditNote",
                column: "ValuationDifferenceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNote_VendorId",
                table: "SupplierCreditNote",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNote_VoidGeneralLedgerHeaderId",
                table: "SupplierCreditNote",
                column: "VoidGeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNoteLine_ExpenseOrAssetAccountId",
                table: "SupplierCreditNoteLine",
                column: "ExpenseOrAssetAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNoteLine_ItemId",
                table: "SupplierCreditNoteLine",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNoteLine_MatchedReturnDispatchId",
                table: "SupplierCreditNoteLine",
                column: "MatchedReturnDispatchId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNoteLine_SourcePurchaseInvoiceLineId",
                table: "SupplierCreditNoteLine",
                column: "SourcePurchaseInvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNoteLine_SupplierCreditNoteId",
                table: "SupplierCreditNoteLine",
                column: "SupplierCreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNoteLine_TaxAccountId",
                table: "SupplierCreditNoteLine",
                column: "TaxAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierCreditNoteLine_TaxId",
                table: "SupplierCreditNoteLine",
                column: "TaxId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnDispatch_CompanyId_No",
                table: "SupplierReturnDispatch",
                columns: new[] { "CompanyId", "No" },
                unique: true,
                filter: "[No] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnDispatch_GeneralLedgerHeaderId",
                table: "SupplierReturnDispatch",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnDispatch_PurchaseInvoiceHeaderId",
                table: "SupplierReturnDispatch",
                column: "PurchaseInvoiceHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnDispatch_PurchaseReceiptHeaderId",
                table: "SupplierReturnDispatch",
                column: "PurchaseReceiptHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnDispatch_ReturnClearingAccountId",
                table: "SupplierReturnDispatch",
                column: "ReturnClearingAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnDispatch_SupplierCreditNoteId",
                table: "SupplierReturnDispatch",
                column: "SupplierCreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnDispatch_VendorId",
                table: "SupplierReturnDispatch",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnDispatch_VoidGeneralLedgerHeaderId",
                table: "SupplierReturnDispatch",
                column: "VoidGeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnDispatchLine_InventoryAccountId",
                table: "SupplierReturnDispatchLine",
                column: "InventoryAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnDispatchLine_InventoryControlJournalId",
                table: "SupplierReturnDispatchLine",
                column: "InventoryControlJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnDispatchLine_ItemId",
                table: "SupplierReturnDispatchLine",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnDispatchLine_PurchaseInvoiceLineId",
                table: "SupplierReturnDispatchLine",
                column: "PurchaseInvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnDispatchLine_PurchaseReceiptLineId",
                table: "SupplierReturnDispatchLine",
                column: "PurchaseReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierReturnDispatchLine_SupplierReturnDispatchId",
                table: "SupplierReturnDispatchLine",
                column: "SupplierReturnDispatchId");

            migrationBuilder.CreateIndex(
                name: "IX_WriteOffRecovery_BankId",
                table: "WriteOffRecovery",
                column: "BankId");

            migrationBuilder.CreateIndex(
                name: "IX_WriteOffRecovery_CompanyId_No",
                table: "WriteOffRecovery",
                columns: new[] { "CompanyId", "No" },
                unique: true,
                filter: "[No] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WriteOffRecovery_GeneralLedgerHeaderId",
                table: "WriteOffRecovery",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_WriteOffRecovery_ReceivableWriteOffId",
                table: "WriteOffRecovery",
                column: "ReceivableWriteOffId");

            migrationBuilder.CreateIndex(
                name: "IX_WriteOffRecovery_VoidGeneralLedgerHeaderId",
                table: "WriteOffRecovery",
                column: "VoidGeneralLedgerHeaderId");

            migrationBuilder.AddForeignKey(
                name: "FK_GeneralLedgerSetting_Account_BadDebtRecoveryAccountId",
                table: "GeneralLedgerSetting",
                column: "BadDebtRecoveryAccountId",
                principalTable: "Account",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GeneralLedgerSetting_Account_CustomerCreditLiabilityAccountId",
                table: "GeneralLedgerSetting",
                column: "CustomerCreditLiabilityAccountId",
                principalTable: "Account",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GeneralLedgerSetting_Account_ImpairmentExpenseAccountId",
                table: "GeneralLedgerSetting",
                column: "ImpairmentExpenseAccountId",
                principalTable: "Account",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GeneralLedgerSetting_Account_ReceivableLossAllowanceAccountId",
                table: "GeneralLedgerSetting",
                column: "ReceivableLossAllowanceAccountId",
                principalTable: "Account",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GeneralLedgerSetting_Account_ReturnValuationDifferenceAccountId",
                table: "GeneralLedgerSetting",
                column: "ReturnValuationDifferenceAccountId",
                principalTable: "Account",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GeneralLedgerSetting_Account_SalesReturnsAllowancesAccountId",
                table: "GeneralLedgerSetting",
                column: "SalesReturnsAllowancesAccountId",
                principalTable: "Account",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GeneralLedgerSetting_Account_SupplierAdvanceAccountId",
                table: "GeneralLedgerSetting",
                column: "SupplierAdvanceAccountId",
                principalTable: "Account",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GeneralLedgerSetting_Account_SupplierCreditAssetAccountId",
                table: "GeneralLedgerSetting",
                column: "SupplierCreditAssetAccountId",
                principalTable: "Account",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GeneralLedgerSetting_Account_SupplierReturnClearingAccountId",
                table: "GeneralLedgerSetting",
                column: "SupplierReturnClearingAccountId",
                principalTable: "Account",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceHeader_Account_ReceivableAccountId",
                table: "SalesInvoiceHeader",
                column: "ReceivableAccountId",
                principalTable: "Account",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GeneralLedgerSetting_Account_BadDebtRecoveryAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropForeignKey(
                name: "FK_GeneralLedgerSetting_Account_CustomerCreditLiabilityAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropForeignKey(
                name: "FK_GeneralLedgerSetting_Account_ImpairmentExpenseAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropForeignKey(
                name: "FK_GeneralLedgerSetting_Account_ReceivableLossAllowanceAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropForeignKey(
                name: "FK_GeneralLedgerSetting_Account_ReturnValuationDifferenceAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropForeignKey(
                name: "FK_GeneralLedgerSetting_Account_SalesReturnsAllowancesAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropForeignKey(
                name: "FK_GeneralLedgerSetting_Account_SupplierAdvanceAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropForeignKey(
                name: "FK_GeneralLedgerSetting_Account_SupplierCreditAssetAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropForeignKey(
                name: "FK_GeneralLedgerSetting_Account_SupplierReturnClearingAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceHeader_Account_ReceivableAccountId",
                table: "SalesInvoiceHeader");

            migrationBuilder.DropTable(
                name: "CustomerCreditNoteLine");

            migrationBuilder.DropTable(
                name: "CustomerReturnReceiptLine");

            migrationBuilder.DropTable(
                name: "PartyRefundAllocation");

            migrationBuilder.DropTable(
                name: "SettlementApplicationReversal");

            migrationBuilder.DropTable(
                name: "SettlementCommandReceipt");

            migrationBuilder.DropTable(
                name: "SupplierCreditNoteLine");

            migrationBuilder.DropTable(
                name: "SupplierReturnDispatchLine");

            migrationBuilder.DropTable(
                name: "WriteOffRecovery");

            migrationBuilder.DropTable(
                name: "CustomerReturnReceipt");

            migrationBuilder.DropTable(
                name: "PartyRefund");

            migrationBuilder.DropTable(
                name: "SettlementApplication");

            migrationBuilder.DropTable(
                name: "SupplierReturnDispatch");

            migrationBuilder.DropTable(
                name: "ReceivableWriteOff");

            migrationBuilder.DropTable(
                name: "CustomerCreditNote");

            migrationBuilder.DropTable(
                name: "SupplierAdvance");

            migrationBuilder.DropTable(
                name: "SupplierCreditNote");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceHeader_ReceivableAccountId",
                table: "SalesInvoiceHeader");

            migrationBuilder.DropIndex(
                name: "IX_GeneralLedgerSetting_BadDebtRecoveryAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropIndex(
                name: "IX_GeneralLedgerSetting_CustomerCreditLiabilityAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropIndex(
                name: "IX_GeneralLedgerSetting_ImpairmentExpenseAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropIndex(
                name: "IX_GeneralLedgerSetting_ReceivableLossAllowanceAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropIndex(
                name: "IX_GeneralLedgerSetting_ReturnValuationDifferenceAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropIndex(
                name: "IX_GeneralLedgerSetting_SalesReturnsAllowancesAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropIndex(
                name: "IX_GeneralLedgerSetting_SupplierAdvanceAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropIndex(
                name: "IX_GeneralLedgerSetting_SupplierCreditAssetAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropIndex(
                name: "IX_GeneralLedgerSetting_SupplierReturnClearingAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropColumn(
                name: "CreditedAmount",
                table: "SalesInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "ReceivableAccountId",
                table: "SalesInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "SalesInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "WrittenOffAmount",
                table: "SalesInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "AdvanceAppliedAmount",
                table: "PurchaseInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "CreditedAmount",
                table: "PurchaseInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "PurchaseInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "BadDebtRecoveryAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropColumn(
                name: "CustomerCreditLiabilityAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropColumn(
                name: "ImpairmentExpenseAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropColumn(
                name: "ReceivableLossAllowanceAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropColumn(
                name: "ReturnValuationDifferenceAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropColumn(
                name: "SalesReturnsAllowancesAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropColumn(
                name: "SupplierAdvanceAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropColumn(
                name: "SupplierCreditAssetAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropColumn(
                name: "SupplierReturnClearingAccountId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropColumn(
                name: "CreditsReturnsRefundsEnabled",
                table: "CompanySetting");
        }
    }
}
