using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Api.Data.Migrations
{
    public partial class IntialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "AccountClass",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(nullable: true),
                    NormalBalance = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountClass", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Address",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    No = table.Column<string>(nullable: true),
                    Street = table.Column<string>(nullable: true),
                    City = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Address", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Company",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(nullable: true),
                    ShortName = table.Column<string>(nullable: true),
                    CompanyCode = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Company", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinancialYear",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    FiscalYearCode = table.Column<string>(maxLength: 10, nullable: false),
                    FiscalYearName = table.Column<string>(maxLength: 100, nullable: false),
                    StartDate = table.Column<DateTime>(nullable: false),
                    EndDate = table.Column<DateTime>(nullable: false),
                    IsActive = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialYear", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GeneralLedgerHeader",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Date = table.Column<DateTime>(nullable: false),
                    DocumentType = table.Column<int>(nullable: false),
                    Description = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneralLedgerHeader", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItemTaxGroup",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(nullable: true),
                    IsFullyExempt = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemTaxGroup", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Log",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    TimeStamp = table.Column<DateTime>(nullable: false),
                    Level = table.Column<string>(nullable: true),
                    Logger = table.Column<string>(nullable: true),
                    Message = table.Column<string>(nullable: true),
                    Username = table.Column<string>(nullable: true),
                    CallSite = table.Column<string>(nullable: true),
                    Thread = table.Column<string>(nullable: true),
                    Exception = table.Column<string>(nullable: true),
                    StackTrace = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Measurement",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Code = table.Column<string>(nullable: true),
                    Description = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Measurement", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Party",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    PartyType = table.Column<int>(nullable: false),
                    Name = table.Column<string>(nullable: true),
                    Email = table.Column<string>(nullable: true),
                    Website = table.Column<string>(nullable: true),
                    Phone = table.Column<string>(nullable: true),
                    Fax = table.Column<string>(nullable: true),
                    IsActive = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Party", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentTerm",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Description = table.Column<string>(nullable: true),
                    PaymentType = table.Column<int>(nullable: false),
                    DueAfterDays = table.Column<int>(nullable: true),
                    IsActive = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentTerm", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SequenceNumber",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    SequenceNumberType = table.Column<int>(nullable: false),
                    Description = table.Column<string>(nullable: true),
                    Prefix = table.Column<string>(nullable: true),
                    NextNumber = table.Column<int>(nullable: false),
                    UsePrefix = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SequenceNumber", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxGroup",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Description = table.Column<string>(nullable: true),
                    TaxAppliedToShipping = table.Column<bool>(nullable: false),
                    IsActive = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxGroup", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditableEntity",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    EntityName = table.Column<string>(nullable: true),
                    EnableAudit = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditableEntity", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLog",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    UserName = table.Column<string>(nullable: true),
                    AuditEventDateUTC = table.Column<DateTime>(nullable: false),
                    AuditEventType = table.Column<int>(nullable: false),
                    TableName = table.Column<string>(nullable: true),
                    RecordId = table.Column<string>(nullable: true),
                    FieldName = table.Column<string>(nullable: true),
                    OriginalValue = table.Column<string>(nullable: true),
                    NewValue = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecurityGroup",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(nullable: true),
                    DisplayName = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityGroup", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecurityRole",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(nullable: true),
                    DisplayName = table.Column<string>(nullable: true),
                    SysAdmin = table.Column<bool>(nullable: false),
                    System = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityRole", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "User",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    UserName = table.Column<string>(nullable: true),
                    EmailAddress = table.Column<string>(nullable: true),
                    Firstname = table.Column<string>(nullable: true),
                    Lastname = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Account",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    AccountClassId = table.Column<int>(nullable: false),
                    ParentAccountId = table.Column<int>(nullable: true),
                    DrOrCrSide = table.Column<int>(nullable: false),
                    CompanyId = table.Column<int>(nullable: false),
                    AccountCode = table.Column<string>(maxLength: 50, nullable: false),
                    AccountName = table.Column<string>(maxLength: 200, nullable: false),
                    Description = table.Column<string>(maxLength: 200, nullable: true),
                    IsCash = table.Column<bool>(nullable: false),
                    IsContraAccount = table.Column<bool>(nullable: false),
                    RowVersion = table.Column<byte[]>(type: "timestamp", maxLength: 8, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Account", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Account_AccountClass_AccountClassId",
                        column: x => x.AccountClassId,
                        principalTable: "AccountClass",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Account_Company_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Company",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Account_Account_ParentAccountId",
                        column: x => x.ParentAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompanySetting",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    CompanyId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanySetting", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanySetting_Company_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Company",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Contact",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    ContactType = table.Column<int>(nullable: false),
                    FirstName = table.Column<string>(nullable: true),
                    LastName = table.Column<string>(nullable: true),
                    MiddleName = table.Column<string>(nullable: true),
                    PartyId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contact", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contact_Party_PartyId",
                        column: x => x.PartyId,
                        principalTable: "Party",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntryHeader",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    GeneralLedgerHeaderId = table.Column<int>(nullable: true),
                    PartyId = table.Column<int>(nullable: true),
                    VoucherType = table.Column<int>(nullable: true),
                    Date = table.Column<DateTime>(nullable: false),
                    Memo = table.Column<string>(nullable: true),
                    ReferenceNo = table.Column<string>(nullable: true),
                    Posted = table.Column<bool>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntryHeader", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalEntryHeader_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalEntryHeader_Party_PartyId",
                        column: x => x.PartyId,
                        principalTable: "Party",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditableAttribute",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    AuditableEntityId = table.Column<int>(nullable: false),
                    AttributeName = table.Column<string>(nullable: true),
                    EnableAudit = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditableAttribute", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditableAttribute_AuditableEntity_AuditableEntityId",
                        column: x => x.AuditableEntityId,
                        principalSchema: "dbo",
                        principalTable: "AuditableEntity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SecurityPermission",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(nullable: true),
                    DisplayName = table.Column<string>(nullable: true),
                    SecurityGroupId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityPermission", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecurityPermission_SecurityGroup_SecurityGroupId",
                        column: x => x.SecurityGroupId,
                        principalSchema: "dbo",
                        principalTable: "SecurityGroup",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SecurityUserRole",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(nullable: false),
                    SecurityRoleId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityUserRole", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecurityUserRole_SecurityRole_SecurityRoleId",
                        column: x => x.SecurityRoleId,
                        principalSchema: "dbo",
                        principalTable: "SecurityRole",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SecurityUserRole_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Bank",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Type = table.Column<int>(nullable: false),
                    Name = table.Column<string>(nullable: true),
                    AccountId = table.Column<int>(nullable: true),
                    BankName = table.Column<string>(nullable: true),
                    Number = table.Column<string>(nullable: true),
                    Address = table.Column<string>(nullable: true),
                    IsDefault = table.Column<bool>(nullable: false),
                    IsActive = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bank", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bank_Account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GeneralLedgerLine",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    GeneralLedgerHeaderId = table.Column<int>(nullable: false),
                    AccountId = table.Column<int>(nullable: false),
                    DrCr = table.Column<int>(nullable: false),
                    Amount = table.Column<decimal>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneralLedgerLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GeneralLedgerLine_Account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GeneralLedgerLine_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GeneralLedgerSetting",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    CompanyId = table.Column<int>(nullable: true),
                    PayableAccountId = table.Column<int>(nullable: true),
                    PurchaseDiscountAccountId = table.Column<int>(nullable: true),
                    GoodsReceiptNoteClearingAccountId = table.Column<int>(nullable: true),
                    SalesDiscountAccountId = table.Column<int>(nullable: true),
                    ShippingChargeAccountId = table.Column<int>(nullable: true),
                    PermanentAccountId = table.Column<int>(nullable: true),
                    IncomeSummaryAccountId = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneralLedgerSetting", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GeneralLedgerSetting_Company_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Company",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeneralLedgerSetting_Account_GoodsReceiptNoteClearingAccountId",
                        column: x => x.GoodsReceiptNoteClearingAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeneralLedgerSetting_Account_IncomeSummaryAccountId",
                        column: x => x.IncomeSummaryAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeneralLedgerSetting_Account_PayableAccountId",
                        column: x => x.PayableAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeneralLedgerSetting_Account_PermanentAccountId",
                        column: x => x.PermanentAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeneralLedgerSetting_Account_PurchaseDiscountAccountId",
                        column: x => x.PurchaseDiscountAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeneralLedgerSetting_Account_SalesDiscountAccountId",
                        column: x => x.SalesDiscountAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeneralLedgerSetting_Account_ShippingChargeAccountId",
                        column: x => x.ShippingChargeAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemCategory",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    ItemType = table.Column<int>(nullable: false),
                    MeasurementId = table.Column<int>(nullable: true),
                    SalesAccountId = table.Column<int>(nullable: true),
                    InventoryAccountId = table.Column<int>(nullable: true),
                    CostOfGoodsSoldAccountId = table.Column<int>(nullable: true),
                    AdjustmentAccountId = table.Column<int>(nullable: true),
                    AssemblyAccountId = table.Column<int>(nullable: true),
                    Name = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemCategory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemCategory_Account_AdjustmentAccountId",
                        column: x => x.AdjustmentAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemCategory_Account_AssemblyAccountId",
                        column: x => x.AssemblyAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemCategory_Account_CostOfGoodsSoldAccountId",
                        column: x => x.CostOfGoodsSoldAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemCategory_Account_InventoryAccountId",
                        column: x => x.InventoryAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemCategory_Measurement_MeasurementId",
                        column: x => x.MeasurementId,
                        principalTable: "Measurement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemCategory_Account_SalesAccountId",
                        column: x => x.SalesAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MainContraAccount",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    MainAccountId = table.Column<int>(nullable: false),
                    RelatedContraAccountId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MainContraAccount", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MainContraAccount_Account_MainAccountId",
                        column: x => x.MainAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MainContraAccount_Account_RelatedContraAccountId",
                        column: x => x.RelatedContraAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);  // changed
                });

            migrationBuilder.CreateTable(
                name: "Tax",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    SalesAccountId = table.Column<int>(nullable: true),
                    PurchasingAccountId = table.Column<int>(nullable: true),
                    TaxName = table.Column<string>(maxLength: 50, nullable: false),
                    TaxCode = table.Column<string>(maxLength: 16, nullable: false),
                    Rate = table.Column<decimal>(nullable: false),
                    IsActive = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tax", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tax_Account_PurchasingAccountId",
                        column: x => x.PurchasingAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tax_Account_SalesAccountId",
                        column: x => x.SalesAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Customer",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    No = table.Column<string>(nullable: true),
                    PartyId = table.Column<int>(nullable: true),
                    PrimaryContactId = table.Column<int>(nullable: true),
                    TaxGroupId = table.Column<int>(nullable: true),
                    AccountsReceivableAccountId = table.Column<int>(nullable: true),
                    SalesAccountId = table.Column<int>(nullable: true),
                    SalesDiscountAccountId = table.Column<int>(nullable: true),
                    PromptPaymentDiscountAccountId = table.Column<int>(nullable: true),
                    PaymentTermId = table.Column<int>(nullable: true),
                    CustomerAdvancesAccountId = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customer_Account_AccountsReceivableAccountId",
                        column: x => x.AccountsReceivableAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Customer_Account_CustomerAdvancesAccountId",
                        column: x => x.CustomerAdvancesAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Customer_Party_PartyId",
                        column: x => x.PartyId,
                        principalTable: "Party",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Customer_PaymentTerm_PaymentTermId",
                        column: x => x.PaymentTermId,
                        principalTable: "PaymentTerm",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Customer_Contact_PrimaryContactId",
                        column: x => x.PrimaryContactId,
                        principalTable: "Contact",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Customer_Account_PromptPaymentDiscountAccountId",
                        column: x => x.PromptPaymentDiscountAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Customer_Account_SalesAccountId",
                        column: x => x.SalesAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Customer_Account_SalesDiscountAccountId",
                        column: x => x.SalesDiscountAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Customer_TaxGroup_TaxGroupId",
                        column: x => x.TaxGroupId,
                        principalTable: "TaxGroup",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Vendor",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    No = table.Column<string>(nullable: true),
                    PartyId = table.Column<int>(nullable: true),
                    AccountsPayableAccountId = table.Column<int>(nullable: true),
                    PurchaseAccountId = table.Column<int>(nullable: true),
                    PurchaseDiscountAccountId = table.Column<int>(nullable: true),
                    PrimaryContactId = table.Column<int>(nullable: true),
                    PaymentTermId = table.Column<int>(nullable: true),
                    TaxGroupId = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendor", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vendor_Account_AccountsPayableAccountId",
                        column: x => x.AccountsPayableAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vendor_Party_PartyId",
                        column: x => x.PartyId,
                        principalTable: "Party",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vendor_PaymentTerm_PaymentTermId",
                        column: x => x.PaymentTermId,
                        principalTable: "PaymentTerm",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vendor_Contact_PrimaryContactId",
                        column: x => x.PrimaryContactId,
                        principalTable: "Contact",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vendor_Account_PurchaseAccountId",
                        column: x => x.PurchaseAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vendor_Account_PurchaseDiscountAccountId",
                        column: x => x.PurchaseDiscountAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vendor_TaxGroup_TaxGroupId",
                        column: x => x.TaxGroupId,
                        principalTable: "TaxGroup",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntryLine",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    JournalEntryHeaderId = table.Column<int>(nullable: false),
                    AccountId = table.Column<int>(nullable: false),
                    DrCr = table.Column<int>(nullable: false),
                    Amount = table.Column<decimal>(nullable: false),
                    Memo = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntryLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalEntryLine_Account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JournalEntryLine_JournalEntryHeader_JournalEntryHeaderId",
                        column: x => x.JournalEntryHeaderId,
                        principalTable: "JournalEntryHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SecurityRolePermission",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    SecurityRoleId = table.Column<int>(nullable: false),
                    SecurityPermissionId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityRolePermission", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecurityRolePermission_SecurityPermission_SecurityPermissionId",
                        column: x => x.SecurityPermissionId,
                        principalSchema: "dbo",
                        principalTable: "SecurityPermission",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SecurityRolePermission_SecurityRole_SecurityRoleId",
                        column: x => x.SecurityRoleId,
                        principalSchema: "dbo",
                        principalTable: "SecurityRole",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemTaxGroupTax",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    TaxId = table.Column<int>(nullable: false),
                    ItemTaxGroupId = table.Column<int>(nullable: false),
                    IsExempt = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemTaxGroupTax", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemTaxGroupTax_ItemTaxGroup_ItemTaxGroupId",
                        column: x => x.ItemTaxGroupId,
                        principalTable: "ItemTaxGroup",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemTaxGroupTax_Tax_TaxId",
                        column: x => x.TaxId,
                        principalTable: "Tax",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaxGroupTax",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    TaxId = table.Column<int>(nullable: false),
                    TaxGroupId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxGroupTax", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxGroupTax_TaxGroup_TaxGroupId",
                        column: x => x.TaxGroupId,
                        principalTable: "TaxGroup",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaxGroupTax_Tax_TaxId",
                        column: x => x.TaxId,
                        principalTable: "Tax",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerContact",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    ContactId = table.Column<int>(nullable: false),
                    CustomerId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerContact", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerContact_Contact_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contact",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerContact_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesDeliveryHeader",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    PaymentTermId = table.Column<int>(nullable: true),
                    CustomerId = table.Column<int>(nullable: true),
                    GeneralLedgerHeaderId = table.Column<int>(nullable: true),
                    No = table.Column<string>(nullable: true),
                    Date = table.Column<DateTime>(nullable: false),
                    Status = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesDeliveryHeader", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesDeliveryHeader_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesDeliveryHeader_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesDeliveryHeader_PaymentTerm_PaymentTermId",
                        column: x => x.PaymentTermId,
                        principalTable: "PaymentTerm",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoiceHeader",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    CustomerId = table.Column<int>(nullable: false),
                    GeneralLedgerHeaderId = table.Column<int>(nullable: true),
                    No = table.Column<string>(nullable: true),
                    Date = table.Column<DateTime>(nullable: false),
                    ShippingHandlingCharge = table.Column<decimal>(nullable: false),
                    PaymentTermId = table.Column<int>(nullable: true),
                    ReferenceNo = table.Column<string>(nullable: true),
                    Status = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceHeader", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceHeader_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceHeader_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderHeader",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    CustomerId = table.Column<int>(nullable: true),
                    PaymentTermId = table.Column<int>(nullable: true),
                    No = table.Column<string>(nullable: true),
                    ReferenceNo = table.Column<string>(nullable: true),
                    Status = table.Column<int>(nullable: true),
                    Date = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderHeader", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderHeader_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderHeader_PaymentTerm_PaymentTermId",
                        column: x => x.PaymentTermId,
                        principalTable: "PaymentTerm",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesQuoteHeader",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    CustomerId = table.Column<int>(nullable: false),
                    PaymentTermId = table.Column<int>(nullable: true),
                    ReferenceNo = table.Column<string>(nullable: true),
                    No = table.Column<string>(nullable: true),
                    Status = table.Column<int>(nullable: true),
                    Date = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesQuoteHeader", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesQuoteHeader_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesReceiptHeader",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    CustomerId = table.Column<int>(nullable: false),
                    GeneralLedgerHeaderId = table.Column<int>(nullable: true),
                    AccountToDebitId = table.Column<int>(nullable: true),
                    No = table.Column<string>(nullable: true),
                    Date = table.Column<DateTime>(nullable: false),
                    Amount = table.Column<decimal>(nullable: false),
                    Status = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesReceiptHeader", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesReceiptHeader_Account_AccountToDebitId",
                        column: x => x.AccountToDebitId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesReceiptHeader_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesReceiptHeader_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Item",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    ItemCategoryId = table.Column<int>(nullable: true),
                    SmallestMeasurementId = table.Column<int>(nullable: true),
                    SellMeasurementId = table.Column<int>(nullable: true),
                    PurchaseMeasurementId = table.Column<int>(nullable: true),
                    PreferredVendorId = table.Column<int>(nullable: true),
                    ItemTaxGroupId = table.Column<int>(nullable: true),
                    SalesAccountId = table.Column<int>(nullable: true),
                    InventoryAccountId = table.Column<int>(nullable: true),
                    CostOfGoodsSoldAccountId = table.Column<int>(nullable: true),
                    InventoryAdjustmentAccountId = table.Column<int>(nullable: true),
                    No = table.Column<string>(nullable: true),
                    Code = table.Column<string>(nullable: true),
                    Description = table.Column<string>(nullable: true),
                    PurchaseDescription = table.Column<string>(nullable: true),
                    SellDescription = table.Column<string>(nullable: true),
                    Cost = table.Column<decimal>(nullable: true),
                    Price = table.Column<decimal>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Item", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Item_Account_CostOfGoodsSoldAccountId",
                        column: x => x.CostOfGoodsSoldAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Item_Account_InventoryAccountId",
                        column: x => x.InventoryAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Item_Account_InventoryAdjustmentAccountId",
                        column: x => x.InventoryAdjustmentAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Item_ItemCategory_ItemCategoryId",
                        column: x => x.ItemCategoryId,
                        principalTable: "ItemCategory",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Item_ItemTaxGroup_ItemTaxGroupId",
                        column: x => x.ItemTaxGroupId,
                        principalTable: "ItemTaxGroup",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Item_Vendor_PreferredVendorId",
                        column: x => x.PreferredVendorId,
                        principalTable: "Vendor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Item_Measurement_PurchaseMeasurementId",
                        column: x => x.PurchaseMeasurementId,
                        principalTable: "Measurement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Item_Account_SalesAccountId",
                        column: x => x.SalesAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Item_Measurement_SellMeasurementId",
                        column: x => x.SellMeasurementId,
                        principalTable: "Measurement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Item_Measurement_SmallestMeasurementId",
                        column: x => x.SmallestMeasurementId,
                        principalTable: "Measurement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoiceHeader",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    VendorId = table.Column<int>(nullable: true),
                    GeneralLedgerHeaderId = table.Column<int>(nullable: true),
                    Date = table.Column<DateTime>(nullable: false),
                    No = table.Column<string>(nullable: true),
                    VendorInvoiceNo = table.Column<string>(nullable: false),
                    Description = table.Column<string>(nullable: true),
                    PaymentTermId = table.Column<int>(nullable: true),
                    ReferenceNo = table.Column<string>(nullable: true),
                    Status = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoiceHeader", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceHeader_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceHeader_Vendor_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReceiptHeader",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    VendorId = table.Column<int>(nullable: false),
                    GeneralLedgerHeaderId = table.Column<int>(nullable: true),
                    Date = table.Column<DateTime>(nullable: false),
                    No = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReceiptHeader", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptHeader_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptHeader_Vendor_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VendorContact",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    ContactId = table.Column<int>(nullable: false),
                    VendorId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorContact", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendorContact_Contact_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contact",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VendorContact_Vendor_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerAllocation",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    CustomerId = table.Column<int>(nullable: false),
                    SalesInvoiceHeaderId = table.Column<int>(nullable: false),
                    SalesReceiptHeaderId = table.Column<int>(nullable: false),
                    Date = table.Column<DateTime>(nullable: false),
                    Amount = table.Column<decimal>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAllocation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerAllocation_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerAllocation_SalesInvoiceHeader_SalesInvoiceHeaderId",
                        column: x => x.SalesInvoiceHeaderId,
                        principalTable: "SalesInvoiceHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);  // changed
                    table.ForeignKey(
                        name: "FK_CustomerAllocation_SalesReceiptHeader_SalesReceiptHeaderId",
                        column: x => x.SalesReceiptHeaderId,
                        principalTable: "SalesReceiptHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);  // changed
                });

            migrationBuilder.CreateTable(
                name: "InventoryControlJournal",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    ItemId = table.Column<int>(nullable: false),
                    MeasurementId = table.Column<int>(nullable: false),
                    DocumentType = table.Column<int>(nullable: false),
                    INQty = table.Column<decimal>(nullable: true),
                    OUTQty = table.Column<decimal>(nullable: true),
                    Date = table.Column<DateTime>(nullable: false),
                    TotalCost = table.Column<decimal>(nullable: true),
                    TotalAmount = table.Column<decimal>(nullable: true),
                    IsReverse = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryControlJournal", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryControlJournal_Item_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InventoryControlJournal_Measurement_MeasurementId",
                        column: x => x.MeasurementId,
                        principalTable: "Measurement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderLine",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    SalesOrderHeaderId = table.Column<int>(nullable: false),
                    ItemId = table.Column<int>(nullable: false),
                    MeasurementId = table.Column<int>(nullable: false),
                    Quantity = table.Column<decimal>(nullable: false),
                    Discount = table.Column<decimal>(nullable: false),
                    Amount = table.Column<decimal>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderLine_Item_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesOrderLine_Measurement_MeasurementId",
                        column: x => x.MeasurementId,
                        principalTable: "Measurement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesOrderLine_SalesOrderHeader_SalesOrderHeaderId",
                        column: x => x.SalesOrderHeaderId,
                        principalTable: "SalesOrderHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesQuoteLine",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    SalesQuoteHeaderId = table.Column<int>(nullable: false),
                    ItemId = table.Column<int>(nullable: false),
                    MeasurementId = table.Column<int>(nullable: false),
                    Quantity = table.Column<decimal>(nullable: false),
                    Discount = table.Column<decimal>(nullable: false),
                    Amount = table.Column<decimal>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesQuoteLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesQuoteLine_Item_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesQuoteLine_Measurement_MeasurementId",
                        column: x => x.MeasurementId,
                        principalTable: "Measurement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesQuoteLine_SalesQuoteHeader_SalesQuoteHeaderId",
                        column: x => x.SalesQuoteHeaderId,
                        principalTable: "SalesQuoteHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderHeader",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    VendorId = table.Column<int>(nullable: true),
                    No = table.Column<string>(nullable: true),
                    Date = table.Column<DateTime>(nullable: false),
                    Description = table.Column<string>(nullable: true),
                    PaymentTermId = table.Column<int>(nullable: true),
                    ReferenceNo = table.Column<string>(nullable: true),
                    Status = table.Column<int>(nullable: true),
                    PurchaseInvoiceHeaderId = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderHeader", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderHeader_PurchaseInvoiceHeader_PurchaseInvoiceHeaderId",
                        column: x => x.PurchaseInvoiceHeaderId,
                        principalTable: "PurchaseInvoiceHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderHeader_Vendor_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VendorPayment",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    VendorId = table.Column<int>(nullable: false),
                    PurchaseInvoiceHeaderId = table.Column<int>(nullable: true),
                    GeneralLedgerHeaderId = table.Column<int>(nullable: true),
                    No = table.Column<string>(nullable: true),
                    Date = table.Column<DateTime>(nullable: false),
                    Amount = table.Column<decimal>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorPayment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendorPayment_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VendorPayment_PurchaseInvoiceHeader_PurchaseInvoiceHeaderId",
                        column: x => x.PurchaseInvoiceHeaderId,
                        principalTable: "PurchaseInvoiceHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VendorPayment_Vendor_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoiceLine",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    SalesInvoiceHeaderId = table.Column<int>(nullable: false),
                    SalesOrderLineId = table.Column<int>(nullable: true),
                    ItemId = table.Column<int>(nullable: false),
                    MeasurementId = table.Column<int>(nullable: false),
                    InventoryControlJournalId = table.Column<int>(nullable: true),
                    Quantity = table.Column<decimal>(nullable: false),
                    Discount = table.Column<decimal>(nullable: false),
                    Amount = table.Column<decimal>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceLine_InventoryControlJournal_InventoryControlJournalId",
                        column: x => x.InventoryControlJournalId,
                        principalTable: "InventoryControlJournal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceLine_Item_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceLine_Measurement_MeasurementId",
                        column: x => x.MeasurementId,
                        principalTable: "Measurement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceLine_SalesInvoiceHeader_SalesInvoiceHeaderId",
                        column: x => x.SalesInvoiceHeaderId,
                        principalTable: "SalesInvoiceHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceLine_SalesOrderLine_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalTable: "SalesOrderLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLine",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    PurchaseOrderHeaderId = table.Column<int>(nullable: false),
                    ItemId = table.Column<int>(nullable: false),
                    MeasurementId = table.Column<int>(nullable: false),
                    Quantity = table.Column<decimal>(nullable: false),
                    Cost = table.Column<decimal>(nullable: false),
                    Discount = table.Column<decimal>(nullable: false),
                    Amount = table.Column<decimal>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLine_Item_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLine_Measurement_MeasurementId",
                        column: x => x.MeasurementId,
                        principalTable: "Measurement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLine_PurchaseOrderHeader_PurchaseOrderHeaderId",
                        column: x => x.PurchaseOrderHeaderId,
                        principalTable: "PurchaseOrderHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesDeliveryLine",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    SalesDeliveryHeaderId = table.Column<int>(nullable: false),
                    SalesInvoiceLineId = table.Column<int>(nullable: true),
                    ItemId = table.Column<int>(nullable: true),
                    MeasurementId = table.Column<int>(nullable: true),
                    Quantity = table.Column<decimal>(nullable: false),
                    Price = table.Column<decimal>(nullable: false),
                    Discount = table.Column<decimal>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesDeliveryLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesDeliveryLine_Item_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesDeliveryLine_Measurement_MeasurementId",
                        column: x => x.MeasurementId,
                        principalTable: "Measurement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesDeliveryLine_SalesDeliveryHeader_SalesDeliveryHeaderId",
                        column: x => x.SalesDeliveryHeaderId,
                        principalTable: "SalesDeliveryHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesDeliveryLine_SalesInvoiceLine_SalesInvoiceLineId",
                        column: x => x.SalesInvoiceLineId,
                        principalTable: "SalesInvoiceLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesReceiptLine",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    SalesReceiptHeaderId = table.Column<int>(nullable: false),
                    SalesInvoiceLineId = table.Column<int>(nullable: true),
                    ItemId = table.Column<int>(nullable: true),
                    AccountToCreditId = table.Column<int>(nullable: true),
                    MeasurementId = table.Column<int>(nullable: true),
                    Quantity = table.Column<decimal>(nullable: true),
                    Discount = table.Column<decimal>(nullable: true),
                    Amount = table.Column<decimal>(nullable: true),
                    AmountPaid = table.Column<decimal>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesReceiptLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesReceiptLine_Account_AccountToCreditId",
                        column: x => x.AccountToCreditId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesReceiptLine_SalesInvoiceLine_SalesInvoiceLineId",
                        column: x => x.SalesInvoiceLineId,
                        principalTable: "SalesInvoiceLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesReceiptLine_SalesReceiptHeader_SalesReceiptHeaderId",
                        column: x => x.SalesReceiptHeaderId,
                        principalTable: "SalesReceiptHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoiceLine",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    PurchaseInvoiceHeaderId = table.Column<int>(nullable: false),
                    ItemId = table.Column<int>(nullable: false),
                    MeasurementId = table.Column<int>(nullable: false),
                    InventoryControlJournalId = table.Column<int>(nullable: true),
                    PurchaseOrderLineId = table.Column<int>(nullable: true),
                    Quantity = table.Column<decimal>(nullable: false),
                    ReceivedQuantity = table.Column<decimal>(nullable: true),
                    Cost = table.Column<decimal>(nullable: true),
                    Discount = table.Column<decimal>(nullable: true),
                    Amount = table.Column<decimal>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoiceLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLine_InventoryControlJournal_InventoryControlJournalId",
                        column: x => x.InventoryControlJournalId,
                        principalTable: "InventoryControlJournal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLine_Item_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLine_Measurement_MeasurementId",
                        column: x => x.MeasurementId,
                        principalTable: "Measurement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLine_PurchaseInvoiceHeader_PurchaseInvoiceHeaderId",
                        column: x => x.PurchaseInvoiceHeaderId,
                        principalTable: "PurchaseInvoiceHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLine_PurchaseOrderLine_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "PurchaseOrderLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReceiptLine",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    PurchaseReceiptHeaderId = table.Column<int>(nullable: false),
                    ItemId = table.Column<int>(nullable: false),
                    InventoryControlJournalId = table.Column<int>(nullable: true),
                    PurchaseInvoiceLineId = table.Column<int>(nullable: true),
                    MeasurementId = table.Column<int>(nullable: false),
                    Quantity = table.Column<decimal>(nullable: false),
                    ReceivedQuantity = table.Column<decimal>(nullable: false),
                    Cost = table.Column<decimal>(nullable: false),
                    Discount = table.Column<decimal>(nullable: false),
                    Amount = table.Column<decimal>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReceiptLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptLine_InventoryControlJournal_InventoryControlJournalId",
                        column: x => x.InventoryControlJournalId,
                        principalTable: "InventoryControlJournal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptLine_Item_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptLine_Measurement_MeasurementId",
                        column: x => x.MeasurementId,
                        principalTable: "Measurement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptLine_PurchaseInvoiceLine_PurchaseInvoiceLineId",
                        column: x => x.PurchaseInvoiceLineId,
                        principalTable: "PurchaseInvoiceLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptLine_PurchaseReceiptHeader_PurchaseReceiptHeaderId",
                        column: x => x.PurchaseReceiptHeaderId,
                        principalTable: "PurchaseReceiptHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Account_AccountClassId",
                table: "Account",
                column: "AccountClassId");

            migrationBuilder.CreateIndex(
                name: "IX_Account_CompanyId",
                table: "Account",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Account_ParentAccountId",
                table: "Account",
                column: "ParentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Bank_AccountId",
                table: "Bank",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanySetting_CompanyId",
                table: "CompanySetting",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Contact_PartyId",
                table: "Contact",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_AccountsReceivableAccountId",
                table: "Customer",
                column: "AccountsReceivableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_CustomerAdvancesAccountId",
                table: "Customer",
                column: "CustomerAdvancesAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_PartyId",
                table: "Customer",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_PaymentTermId",
                table: "Customer",
                column: "PaymentTermId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_PrimaryContactId",
                table: "Customer",
                column: "PrimaryContactId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_PromptPaymentDiscountAccountId",
                table: "Customer",
                column: "PromptPaymentDiscountAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_SalesAccountId",
                table: "Customer",
                column: "SalesAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_SalesDiscountAccountId",
                table: "Customer",
                column: "SalesDiscountAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_TaxGroupId",
                table: "Customer",
                column: "TaxGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAllocation_CustomerId",
                table: "CustomerAllocation",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAllocation_SalesInvoiceHeaderId",
                table: "CustomerAllocation",
                column: "SalesInvoiceHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAllocation_SalesReceiptHeaderId",
                table: "CustomerAllocation",
                column: "SalesReceiptHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerContact_ContactId",
                table: "CustomerContact",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerContact_CustomerId",
                table: "CustomerContact",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerLine_AccountId",
                table: "GeneralLedgerLine",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerLine_GeneralLedgerHeaderId",
                table: "GeneralLedgerLine",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerSetting_CompanyId",
                table: "GeneralLedgerSetting",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerSetting_GoodsReceiptNoteClearingAccountId",
                table: "GeneralLedgerSetting",
                column: "GoodsReceiptNoteClearingAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerSetting_IncomeSummaryAccountId",
                table: "GeneralLedgerSetting",
                column: "IncomeSummaryAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerSetting_PayableAccountId",
                table: "GeneralLedgerSetting",
                column: "PayableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerSetting_PermanentAccountId",
                table: "GeneralLedgerSetting",
                column: "PermanentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerSetting_PurchaseDiscountAccountId",
                table: "GeneralLedgerSetting",
                column: "PurchaseDiscountAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerSetting_SalesDiscountAccountId",
                table: "GeneralLedgerSetting",
                column: "SalesDiscountAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerSetting_ShippingChargeAccountId",
                table: "GeneralLedgerSetting",
                column: "ShippingChargeAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryControlJournal_ItemId",
                table: "InventoryControlJournal",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryControlJournal_MeasurementId",
                table: "InventoryControlJournal",
                column: "MeasurementId");

            migrationBuilder.CreateIndex(
                name: "IX_Item_CostOfGoodsSoldAccountId",
                table: "Item",
                column: "CostOfGoodsSoldAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Item_InventoryAccountId",
                table: "Item",
                column: "InventoryAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Item_InventoryAdjustmentAccountId",
                table: "Item",
                column: "InventoryAdjustmentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Item_ItemCategoryId",
                table: "Item",
                column: "ItemCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Item_ItemTaxGroupId",
                table: "Item",
                column: "ItemTaxGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Item_PreferredVendorId",
                table: "Item",
                column: "PreferredVendorId");

            migrationBuilder.CreateIndex(
                name: "IX_Item_PurchaseMeasurementId",
                table: "Item",
                column: "PurchaseMeasurementId");

            migrationBuilder.CreateIndex(
                name: "IX_Item_SalesAccountId",
                table: "Item",
                column: "SalesAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Item_SellMeasurementId",
                table: "Item",
                column: "SellMeasurementId");

            migrationBuilder.CreateIndex(
                name: "IX_Item_SmallestMeasurementId",
                table: "Item",
                column: "SmallestMeasurementId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCategory_AdjustmentAccountId",
                table: "ItemCategory",
                column: "AdjustmentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCategory_AssemblyAccountId",
                table: "ItemCategory",
                column: "AssemblyAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCategory_CostOfGoodsSoldAccountId",
                table: "ItemCategory",
                column: "CostOfGoodsSoldAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCategory_InventoryAccountId",
                table: "ItemCategory",
                column: "InventoryAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCategory_MeasurementId",
                table: "ItemCategory",
                column: "MeasurementId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemCategory_SalesAccountId",
                table: "ItemCategory",
                column: "SalesAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemTaxGroupTax_ItemTaxGroupId",
                table: "ItemTaxGroupTax",
                column: "ItemTaxGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemTaxGroupTax_TaxId",
                table: "ItemTaxGroupTax",
                column: "TaxId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryHeader_GeneralLedgerHeaderId",
                table: "JournalEntryHeader",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryHeader_PartyId",
                table: "JournalEntryHeader",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLine_AccountId",
                table: "JournalEntryLine",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLine_JournalEntryHeaderId",
                table: "JournalEntryLine",
                column: "JournalEntryHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_MainContraAccount_MainAccountId",
                table: "MainContraAccount",
                column: "MainAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MainContraAccount_RelatedContraAccountId",
                table: "MainContraAccount",
                column: "RelatedContraAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceHeader_GeneralLedgerHeaderId",
                table: "PurchaseInvoiceHeader",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceHeader_VendorId",
                table: "PurchaseInvoiceHeader",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLine_InventoryControlJournalId",
                table: "PurchaseInvoiceLine",
                column: "InventoryControlJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLine_ItemId",
                table: "PurchaseInvoiceLine",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLine_MeasurementId",
                table: "PurchaseInvoiceLine",
                column: "MeasurementId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLine_PurchaseInvoiceHeaderId",
                table: "PurchaseInvoiceLine",
                column: "PurchaseInvoiceHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLine_PurchaseOrderLineId",
                table: "PurchaseInvoiceLine",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderHeader_PurchaseInvoiceHeaderId",
                table: "PurchaseOrderHeader",
                column: "PurchaseInvoiceHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderHeader_VendorId",
                table: "PurchaseOrderHeader",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLine_ItemId",
                table: "PurchaseOrderLine",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLine_MeasurementId",
                table: "PurchaseOrderLine",
                column: "MeasurementId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLine_PurchaseOrderHeaderId",
                table: "PurchaseOrderLine",
                column: "PurchaseOrderHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptHeader_GeneralLedgerHeaderId",
                table: "PurchaseReceiptHeader",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptHeader_VendorId",
                table: "PurchaseReceiptHeader",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptLine_InventoryControlJournalId",
                table: "PurchaseReceiptLine",
                column: "InventoryControlJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptLine_ItemId",
                table: "PurchaseReceiptLine",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptLine_MeasurementId",
                table: "PurchaseReceiptLine",
                column: "MeasurementId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptLine_PurchaseInvoiceLineId",
                table: "PurchaseReceiptLine",
                column: "PurchaseInvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptLine_PurchaseReceiptHeaderId",
                table: "PurchaseReceiptLine",
                column: "PurchaseReceiptHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesDeliveryHeader_CustomerId",
                table: "SalesDeliveryHeader",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesDeliveryHeader_GeneralLedgerHeaderId",
                table: "SalesDeliveryHeader",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesDeliveryHeader_PaymentTermId",
                table: "SalesDeliveryHeader",
                column: "PaymentTermId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesDeliveryLine_ItemId",
                table: "SalesDeliveryLine",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesDeliveryLine_MeasurementId",
                table: "SalesDeliveryLine",
                column: "MeasurementId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesDeliveryLine_SalesDeliveryHeaderId",
                table: "SalesDeliveryLine",
                column: "SalesDeliveryHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesDeliveryLine_SalesInvoiceLineId",
                table: "SalesDeliveryLine",
                column: "SalesInvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceHeader_CustomerId",
                table: "SalesInvoiceHeader",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceHeader_GeneralLedgerHeaderId",
                table: "SalesInvoiceHeader",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLine_InventoryControlJournalId",
                table: "SalesInvoiceLine",
                column: "InventoryControlJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLine_ItemId",
                table: "SalesInvoiceLine",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLine_MeasurementId",
                table: "SalesInvoiceLine",
                column: "MeasurementId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLine_SalesInvoiceHeaderId",
                table: "SalesInvoiceLine",
                column: "SalesInvoiceHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLine_SalesOrderLineId",
                table: "SalesInvoiceLine",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderHeader_CustomerId",
                table: "SalesOrderHeader",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderHeader_PaymentTermId",
                table: "SalesOrderHeader",
                column: "PaymentTermId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLine_ItemId",
                table: "SalesOrderLine",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLine_MeasurementId",
                table: "SalesOrderLine",
                column: "MeasurementId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLine_SalesOrderHeaderId",
                table: "SalesOrderLine",
                column: "SalesOrderHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteHeader_CustomerId",
                table: "SalesQuoteHeader",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteLine_ItemId",
                table: "SalesQuoteLine",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteLine_MeasurementId",
                table: "SalesQuoteLine",
                column: "MeasurementId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteLine_SalesQuoteHeaderId",
                table: "SalesQuoteLine",
                column: "SalesQuoteHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReceiptHeader_AccountToDebitId",
                table: "SalesReceiptHeader",
                column: "AccountToDebitId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReceiptHeader_CustomerId",
                table: "SalesReceiptHeader",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReceiptHeader_GeneralLedgerHeaderId",
                table: "SalesReceiptHeader",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReceiptLine_AccountToCreditId",
                table: "SalesReceiptLine",
                column: "AccountToCreditId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReceiptLine_SalesInvoiceLineId",
                table: "SalesReceiptLine",
                column: "SalesInvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReceiptLine_SalesReceiptHeaderId",
                table: "SalesReceiptLine",
                column: "SalesReceiptHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_Tax_PurchasingAccountId",
                table: "Tax",
                column: "PurchasingAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Tax_SalesAccountId",
                table: "Tax",
                column: "SalesAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxGroupTax_TaxGroupId",
                table: "TaxGroupTax",
                column: "TaxGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxGroupTax_TaxId",
                table: "TaxGroupTax",
                column: "TaxId");

            migrationBuilder.CreateIndex(
                name: "IX_Vendor_AccountsPayableAccountId",
                table: "Vendor",
                column: "AccountsPayableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Vendor_PartyId",
                table: "Vendor",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_Vendor_PaymentTermId",
                table: "Vendor",
                column: "PaymentTermId");

            migrationBuilder.CreateIndex(
                name: "IX_Vendor_PrimaryContactId",
                table: "Vendor",
                column: "PrimaryContactId");

            migrationBuilder.CreateIndex(
                name: "IX_Vendor_PurchaseAccountId",
                table: "Vendor",
                column: "PurchaseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Vendor_PurchaseDiscountAccountId",
                table: "Vendor",
                column: "PurchaseDiscountAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Vendor_TaxGroupId",
                table: "Vendor",
                column: "TaxGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorContact_ContactId",
                table: "VendorContact",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorContact_VendorId",
                table: "VendorContact",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorPayment_GeneralLedgerHeaderId",
                table: "VendorPayment",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorPayment_PurchaseInvoiceHeaderId",
                table: "VendorPayment",
                column: "PurchaseInvoiceHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorPayment_VendorId",
                table: "VendorPayment",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditableAttribute_AuditableEntityId",
                schema: "dbo",
                table: "AuditableAttribute",
                column: "AuditableEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityPermission_SecurityGroupId",
                schema: "dbo",
                table: "SecurityPermission",
                column: "SecurityGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityRolePermission_SecurityPermissionId",
                schema: "dbo",
                table: "SecurityRolePermission",
                column: "SecurityPermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityRolePermission_SecurityRoleId",
                schema: "dbo",
                table: "SecurityRolePermission",
                column: "SecurityRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityUserRole_SecurityRoleId",
                schema: "dbo",
                table: "SecurityUserRole",
                column: "SecurityRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityUserRole_UserId",
                schema: "dbo",
                table: "SecurityUserRole",
                column: "UserId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Address");

            migrationBuilder.DropTable(
                name: "Bank");

            migrationBuilder.DropTable(
                name: "CompanySetting");

            migrationBuilder.DropTable(
                name: "CustomerAllocation");

            migrationBuilder.DropTable(
                name: "CustomerContact");

            migrationBuilder.DropTable(
                name: "FinancialYear");

            migrationBuilder.DropTable(
                name: "GeneralLedgerLine");

            migrationBuilder.DropTable(
                name: "GeneralLedgerSetting");

            migrationBuilder.DropTable(
                name: "ItemTaxGroupTax");

            migrationBuilder.DropTable(
                name: "JournalEntryLine");

            migrationBuilder.DropTable(
                name: "Log");

            migrationBuilder.DropTable(
                name: "MainContraAccount");

            migrationBuilder.DropTable(
                name: "PurchaseReceiptLine");

            migrationBuilder.DropTable(
                name: "SalesDeliveryLine");

            migrationBuilder.DropTable(
                name: "SalesQuoteLine");

            migrationBuilder.DropTable(
                name: "SalesReceiptLine");

            migrationBuilder.DropTable(
                name: "SequenceNumber");

            migrationBuilder.DropTable(
                name: "TaxGroupTax");

            migrationBuilder.DropTable(
                name: "VendorContact");

            migrationBuilder.DropTable(
                name: "VendorPayment");

            migrationBuilder.DropTable(
                name: "AuditableAttribute",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AuditLog",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "SecurityRolePermission",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "SecurityUserRole",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "JournalEntryHeader");

            migrationBuilder.DropTable(
                name: "PurchaseInvoiceLine");

            migrationBuilder.DropTable(
                name: "PurchaseReceiptHeader");

            migrationBuilder.DropTable(
                name: "SalesDeliveryHeader");

            migrationBuilder.DropTable(
                name: "SalesQuoteHeader");

            migrationBuilder.DropTable(
                name: "SalesInvoiceLine");

            migrationBuilder.DropTable(
                name: "SalesReceiptHeader");

            migrationBuilder.DropTable(
                name: "Tax");

            migrationBuilder.DropTable(
                name: "AuditableEntity",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "SecurityPermission",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "SecurityRole",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "User",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLine");

            migrationBuilder.DropTable(
                name: "InventoryControlJournal");

            migrationBuilder.DropTable(
                name: "SalesInvoiceHeader");

            migrationBuilder.DropTable(
                name: "SalesOrderLine");

            migrationBuilder.DropTable(
                name: "SecurityGroup",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "PurchaseOrderHeader");

            migrationBuilder.DropTable(
                name: "Item");

            migrationBuilder.DropTable(
                name: "SalesOrderHeader");

            migrationBuilder.DropTable(
                name: "PurchaseInvoiceHeader");

            migrationBuilder.DropTable(
                name: "ItemCategory");

            migrationBuilder.DropTable(
                name: "ItemTaxGroup");

            migrationBuilder.DropTable(
                name: "Customer");

            migrationBuilder.DropTable(
                name: "GeneralLedgerHeader");

            migrationBuilder.DropTable(
                name: "Vendor");

            migrationBuilder.DropTable(
                name: "Measurement");

            migrationBuilder.DropTable(
                name: "Account");

            migrationBuilder.DropTable(
                name: "PaymentTerm");

            migrationBuilder.DropTable(
                name: "Contact");

            migrationBuilder.DropTable(
                name: "TaxGroup");

            migrationBuilder.DropTable(
                name: "AccountClass");

            migrationBuilder.DropTable(
                name: "Company");

            migrationBuilder.DropTable(
                name: "Party");
        }
    }
}
