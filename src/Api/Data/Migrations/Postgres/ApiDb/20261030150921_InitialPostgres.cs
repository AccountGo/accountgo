using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Api.Data.Migrations.Postgres.ApiDb
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountClass",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: true),
                    NormalBalance = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountClass", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Address",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    No = table.Column<string>(type: "text", nullable: true),
                    Street = table.Column<string>(type: "text", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Address", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditableEntity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EntityName = table.Column<string>(type: "text", nullable: true),
                    EnableAudit = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditableEntity", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    UserName = table.Column<string>(type: "text", nullable: true),
                    AuditEventDateUTC = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    AuditEventType = table.Column<int>(type: "integer", nullable: false),
                    TableName = table.Column<string>(type: "text", nullable: true),
                    RecordId = table.Column<string>(type: "text", nullable: true),
                    FieldName = table.Column<string>(type: "text", nullable: true),
                    OriginalValue = table.Column<string>(type: "text", nullable: true),
                    NewValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BooksLockChange",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PreviousLockedThrough = table.Column<DateTime>(type: "date", nullable: true),
                    NewLockedThrough = table.Column<DateTime>(type: "date", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ChangedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BooksLockChange", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Company",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ShortName = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: true),
                    CompanyCode = table.Column<string>(type: "text", nullable: true),
                    CRA = table.Column<string>(type: "text", nullable: true),
                    Plan = table.Column<string>(type: "text", nullable: true),
                    Tier = table.Column<string>(type: "text", nullable: true),
                    ActiveModulesJson = table.Column<string>(type: "text", nullable: true),
                    DisabledModulesJson = table.Column<string>(type: "text", nullable: true),
                    SubscriptionStatus = table.Column<string>(type: "text", nullable: true),
                    SubscriptionValidUntil = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    BillingProvider = table.Column<string>(type: "text", nullable: true),
                    BillingCustomerId = table.Column<string>(type: "text", nullable: true),
                    BillingSubscriptionId = table.Column<string>(type: "text", nullable: true),
                    CurrentPeriodEnd = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CancelAtPeriodEnd = table.Column<bool>(type: "boolean", nullable: false),
                    BillingManagedManually = table.Column<bool>(type: "boolean", nullable: false),
                    PaidAccessEndedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Company", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Donors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    DonorType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CompanyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Province = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    PostalCode = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Telephone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Donors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EntitlementUsage",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Resource = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<int>(type: "integer", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntitlementUsage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinancialYear",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    FiscalYearCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    FiscalYearName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ClosedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ClosingGeneralLedgerHeaderId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialYear", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GeneralLedgerHeader",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    SourceDocumentId = table.Column<int>(type: "integer", nullable: true),
                    ReversedGeneralLedgerHeaderId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneralLedgerHeader", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItemTaxGroup",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    IsFullyExempt = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemTaxGroup", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Log",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    TimeStamp = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Level = table.Column<string>(type: "text", nullable: true),
                    Logger = table.Column<string>(type: "text", nullable: true),
                    Message = table.Column<string>(type: "text", nullable: true),
                    Username = table.Column<string>(type: "text", nullable: true),
                    CallSite = table.Column<string>(type: "text", nullable: true),
                    Thread = table.Column<string>(type: "text", nullable: true),
                    Exception = table.Column<string>(type: "text", nullable: true),
                    StackTrace = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Measurement",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Measurement", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Party",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PartyType = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    Website = table.Column<string>(type: "text", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Fax = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CompanyStreet = table.Column<string>(type: "text", nullable: true),
                    CompanyProvince = table.Column<string>(type: "text", nullable: true),
                    CompanyPostalCode = table.Column<string>(type: "text", nullable: true),
                    CompanyCountry = table.Column<string>(type: "text", nullable: true),
                    ShippingStreet = table.Column<string>(type: "text", nullable: true),
                    ShippingProvince = table.Column<string>(type: "text", nullable: true),
                    ShippingPostalCode = table.Column<string>(type: "text", nullable: true),
                    ShippingCountry = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Party", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentTerm",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    PaymentType = table.Column<int>(type: "integer", nullable: false),
                    DueAfterDays = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentTerm", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecurityGroup",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityGroup", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecurityRole",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    SysAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    System = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityRole", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SequenceNumber",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    SequenceNumberType = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Prefix = table.Column<string>(type: "text", nullable: true),
                    NextNumber = table.Column<int>(type: "integer", nullable: false),
                    UsePrefix = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SequenceNumber", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxGroup",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    TaxAppliedToShipping = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxGroup", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "User",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserName = table.Column<string>(type: "text", nullable: true),
                    EmailAddress = table.Column<string>(type: "text", nullable: true),
                    Firstname = table.Column<string>(type: "text", nullable: true),
                    Lastname = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditableAttribute",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AuditableEntityId = table.Column<int>(type: "integer", nullable: false),
                    AttributeName = table.Column<string>(type: "text", nullable: true),
                    EnableAudit = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditableAttribute", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditableAttribute_AuditableEntity_AuditableEntityId",
                        column: x => x.AuditableEntityId,
                        principalTable: "AuditableEntity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Account",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountClassId = table.Column<int>(type: "integer", nullable: false),
                    ParentAccountId = table.Column<int>(type: "integer", nullable: true),
                    DrOrCrSide = table.Column<int>(type: "integer", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    AccountCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AccountName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsCash = table.Column<bool>(type: "boolean", nullable: false),
                    IsContraAccount = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, nullable: true)
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
                        name: "FK_Account_Account_ParentAccountId",
                        column: x => x.ParentAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Account_Company_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Company",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompanyInvitation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    SecurityRoleIdsJson = table.Column<string>(type: "text", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    InvitedByUserId = table.Column<string>(type: "text", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    AcceptedByUserId = table.Column<string>(type: "text", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ResendWindowStart = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ResendCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyInvitation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyInvitation_Company_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Company",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompanyMember",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    InvitedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyMember", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyMember_Company_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Company",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompanySetting",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "PHP"),
                    AccountingBasis = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    ReportingFramework = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    TaxRegime = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    FiscalYearPattern = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    FiscalYearStartMonth = table.Column<int>(type: "integer", nullable: true),
                    BooksStartDate = table.Column<DateTime>(type: "date", nullable: false, defaultValue: new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)),
                    BooksLockedThrough = table.Column<DateTime>(type: "date", nullable: true),
                    IndustryTemplate = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    EntityType = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    InventoryCostingMethod = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CoaTemplateId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "legacy-default"),
                    OnboardingChecklistDismissedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    OnboardingChecklistJson = table.Column<string>(type: "text", nullable: true),
                    LegalName = table.Column<string>(type: "text", nullable: true),
                    BusinessEmail = table.Column<string>(type: "text", nullable: true),
                    BusinessPhone = table.Column<string>(type: "text", nullable: true),
                    BusinessAddress_Line1 = table.Column<string>(type: "text", nullable: true),
                    BusinessAddress_Line2 = table.Column<string>(type: "text", nullable: true),
                    BusinessAddress_City = table.Column<string>(type: "text", nullable: true),
                    BusinessAddress_Region = table.Column<string>(type: "text", nullable: true),
                    BusinessAddress_PostalCode = table.Column<string>(type: "text", nullable: true),
                    BusinessAddress_CountryCode = table.Column<string>(type: "text", nullable: true),
                    SalesInvoiceSettingsJson = table.Column<string>(type: "text", nullable: true),
                    MfaPolicy = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    MfaGracePeriodEndsAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RequireMfaEveryLogin = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    ContactType = table.Column<int>(type: "integer", nullable: false),
                    FirstName = table.Column<string>(type: "text", nullable: true),
                    LastName = table.Column<string>(type: "text", nullable: true),
                    MiddleName = table.Column<string>(type: "text", nullable: true),
                    PartyId = table.Column<int>(type: "integer", nullable: false)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    GeneralLedgerHeaderId = table.Column<int>(type: "integer", nullable: true),
                    PartyId = table.Column<int>(type: "integer", nullable: true),
                    VoucherType = table.Column<int>(type: "integer", nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Memo = table.Column<string>(type: "text", nullable: true),
                    ReferenceNo = table.Column<string>(type: "text", nullable: true),
                    Posted = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntryHeader", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalEntryHeader_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_JournalEntryHeader_Party_PartyId",
                        column: x => x.PartyId,
                        principalTable: "Party",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SecurityPermission",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    SecurityGroupId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityPermission", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecurityPermission_SecurityGroup_SecurityGroupId",
                        column: x => x.SecurityGroupId,
                        principalTable: "SecurityGroup",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SecurityUserRole",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SecurityRoleId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityUserRole", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecurityUserRole_SecurityRole_SecurityRoleId",
                        column: x => x.SecurityRoleId,
                        principalTable: "SecurityRole",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SecurityUserRole_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Bank",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AccountId = table.Column<int>(type: "integer", nullable: true),
                    BankName = table.Column<string>(type: "text", nullable: true),
                    Number = table.Column<string>(type: "text", nullable: true),
                    Address = table.Column<string>(type: "text", nullable: true),
                    RoutingNumber = table.Column<string>(type: "text", nullable: true),
                    Iban = table.Column<string>(type: "text", nullable: true),
                    Swift = table.Column<string>(type: "text", nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefaultForReceipts = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefaultForPayments = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    OpeningBalanceDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    OpeningBalanceGeneralLedgerHeaderId = table.Column<int>(type: "integer", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bank", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bank_Account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DonationInvoiceHeader",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    DonorId = table.Column<int>(type: "integer", nullable: false),
                    No = table.Column<string>(type: "text", nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ReferenceNo = table.Column<string>(type: "text", nullable: true),
                    PaymentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    GlAccountId = table.Column<int>(type: "integer", nullable: true),
                    Purpose = table.Column<string>(type: "text", nullable: true),
                    IsTaxReceiptIssued = table.Column<bool>(type: "boolean", nullable: false),
                    TaxReceiptNo = table.Column<string>(type: "text", nullable: true),
                    Posted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DonationInvoiceHeader", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DonationInvoiceHeader_Account_GlAccountId",
                        column: x => x.GlAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DonationInvoiceHeader_Donors_DonorId",
                        column: x => x.DonorId,
                        principalTable: "Donors",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GeneralLedgerSetting",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PayableAccountId = table.Column<int>(type: "integer", nullable: true),
                    PurchaseDiscountAccountId = table.Column<int>(type: "integer", nullable: true),
                    GoodsReceiptNoteClearingAccountId = table.Column<int>(type: "integer", nullable: true),
                    SalesDiscountAccountId = table.Column<int>(type: "integer", nullable: true),
                    ShippingChargeAccountId = table.Column<int>(type: "integer", nullable: true),
                    PermanentAccountId = table.Column<int>(type: "integer", nullable: true),
                    IncomeSummaryAccountId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneralLedgerSetting", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GeneralLedgerSetting_Account_GoodsReceiptNoteClearingAccoun~",
                        column: x => x.GoodsReceiptNoteClearingAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GeneralLedgerSetting_Account_IncomeSummaryAccountId",
                        column: x => x.IncomeSummaryAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GeneralLedgerSetting_Account_PayableAccountId",
                        column: x => x.PayableAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GeneralLedgerSetting_Account_PermanentAccountId",
                        column: x => x.PermanentAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GeneralLedgerSetting_Account_PurchaseDiscountAccountId",
                        column: x => x.PurchaseDiscountAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GeneralLedgerSetting_Account_SalesDiscountAccountId",
                        column: x => x.SalesDiscountAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GeneralLedgerSetting_Account_ShippingChargeAccountId",
                        column: x => x.ShippingChargeAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GeneralLedgerSetting_Company_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Company",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemCategory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    ItemType = table.Column<int>(type: "integer", nullable: false),
                    MeasurementId = table.Column<int>(type: "integer", nullable: true),
                    SalesAccountId = table.Column<int>(type: "integer", nullable: true),
                    InventoryAccountId = table.Column<int>(type: "integer", nullable: true),
                    CostOfGoodsSoldAccountId = table.Column<int>(type: "integer", nullable: true),
                    AdjustmentAccountId = table.Column<int>(type: "integer", nullable: true),
                    AssemblyAccountId = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemCategory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemCategory_Account_AdjustmentAccountId",
                        column: x => x.AdjustmentAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ItemCategory_Account_AssemblyAccountId",
                        column: x => x.AssemblyAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ItemCategory_Account_CostOfGoodsSoldAccountId",
                        column: x => x.CostOfGoodsSoldAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ItemCategory_Account_InventoryAccountId",
                        column: x => x.InventoryAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ItemCategory_Account_SalesAccountId",
                        column: x => x.SalesAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ItemCategory_Measurement_MeasurementId",
                        column: x => x.MeasurementId,
                        principalTable: "Measurement",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MainContraAccount",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    MainAccountId = table.Column<int>(type: "integer", nullable: false),
                    RelatedContraAccountId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MainContraAccount", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MainContraAccount_Account_MainAccountId",
                        column: x => x.MainAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MainContraAccount_Account_RelatedContraAccountId",
                        column: x => x.RelatedContraAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tax",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    SalesAccountId = table.Column<int>(type: "integer", nullable: true),
                    PurchasingAccountId = table.Column<int>(type: "integer", nullable: true),
                    TaxName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TaxCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tax", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tax_Account_PurchasingAccountId",
                        column: x => x.PurchasingAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Tax_Account_SalesAccountId",
                        column: x => x.SalesAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CompanyMemberSecurityRole",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyMemberId = table.Column<int>(type: "integer", nullable: false),
                    SecurityRoleId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyMemberSecurityRole", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyMemberSecurityRole_CompanyMember_CompanyMemberId",
                        column: x => x.CompanyMemberId,
                        principalTable: "CompanyMember",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompanyMemberSecurityRole_SecurityRole_SecurityRoleId",
                        column: x => x.SecurityRoleId,
                        principalTable: "SecurityRole",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Customer",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    No = table.Column<string>(type: "text", nullable: true),
                    PartyId = table.Column<int>(type: "integer", nullable: true),
                    PrimaryContactId = table.Column<int>(type: "integer", nullable: true),
                    BillToContactId = table.Column<int>(type: "integer", nullable: true),
                    TaxGroupId = table.Column<int>(type: "integer", nullable: true),
                    AccountsReceivableAccountId = table.Column<int>(type: "integer", nullable: true),
                    SalesAccountId = table.Column<int>(type: "integer", nullable: true),
                    SalesDiscountAccountId = table.Column<int>(type: "integer", nullable: true),
                    PromptPaymentDiscountAccountId = table.Column<int>(type: "integer", nullable: true),
                    PaymentTermId = table.Column<int>(type: "integer", nullable: true),
                    CustomerAdvancesAccountId = table.Column<int>(type: "integer", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customer_Account_AccountsReceivableAccountId",
                        column: x => x.AccountsReceivableAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Customer_Account_CustomerAdvancesAccountId",
                        column: x => x.CustomerAdvancesAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Customer_Account_PromptPaymentDiscountAccountId",
                        column: x => x.PromptPaymentDiscountAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Customer_Account_SalesAccountId",
                        column: x => x.SalesAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Customer_Account_SalesDiscountAccountId",
                        column: x => x.SalesDiscountAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Customer_Contact_BillToContactId",
                        column: x => x.BillToContactId,
                        principalTable: "Contact",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Customer_Contact_PrimaryContactId",
                        column: x => x.PrimaryContactId,
                        principalTable: "Contact",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Customer_Party_PartyId",
                        column: x => x.PartyId,
                        principalTable: "Party",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Customer_PaymentTerm_PaymentTermId",
                        column: x => x.PaymentTermId,
                        principalTable: "PaymentTerm",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Customer_TaxGroup_TaxGroupId",
                        column: x => x.TaxGroupId,
                        principalTable: "TaxGroup",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Vendor",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    No = table.Column<string>(type: "text", nullable: true),
                    PartyId = table.Column<int>(type: "integer", nullable: true),
                    AccountsPayableAccountId = table.Column<int>(type: "integer", nullable: true),
                    PurchaseAccountId = table.Column<int>(type: "integer", nullable: true),
                    PurchaseDiscountAccountId = table.Column<int>(type: "integer", nullable: true),
                    PrimaryContactId = table.Column<int>(type: "integer", nullable: true),
                    BillToContactId = table.Column<int>(type: "integer", nullable: true),
                    PaymentTermId = table.Column<int>(type: "integer", nullable: true),
                    PaymentMethod = table.Column<int>(type: "integer", nullable: true),
                    TaxGroupId = table.Column<int>(type: "integer", nullable: true),
                    DiscountPercentage = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Street1 = table.Column<string>(type: "text", nullable: true),
                    Street2 = table.Column<string>(type: "text", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true),
                    Province = table.Column<string>(type: "text", nullable: true),
                    PostalCode = table.Column<string>(type: "text", nullable: true),
                    Country = table.Column<string>(type: "text", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendor", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vendor_Account_AccountsPayableAccountId",
                        column: x => x.AccountsPayableAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Vendor_Account_PurchaseAccountId",
                        column: x => x.PurchaseAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Vendor_Account_PurchaseDiscountAccountId",
                        column: x => x.PurchaseDiscountAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Vendor_Contact_BillToContactId",
                        column: x => x.BillToContactId,
                        principalTable: "Contact",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Vendor_Contact_PrimaryContactId",
                        column: x => x.PrimaryContactId,
                        principalTable: "Contact",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Vendor_Party_PartyId",
                        column: x => x.PartyId,
                        principalTable: "Party",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Vendor_PaymentTerm_PaymentTermId",
                        column: x => x.PaymentTermId,
                        principalTable: "PaymentTerm",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Vendor_TaxGroup_TaxGroupId",
                        column: x => x.TaxGroupId,
                        principalTable: "TaxGroup",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "JournalEntryLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    JournalEntryHeaderId = table.Column<int>(type: "integer", nullable: false),
                    AccountId = table.Column<int>(type: "integer", nullable: false),
                    DrCr = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Memo = table.Column<string>(type: "text", nullable: true)
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
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SecurityRoleId = table.Column<int>(type: "integer", nullable: false),
                    SecurityPermissionId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityRolePermission", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecurityRolePermission_SecurityPermission_SecurityPermissio~",
                        column: x => x.SecurityPermissionId,
                        principalTable: "SecurityPermission",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SecurityRolePermission_SecurityRole_SecurityRoleId",
                        column: x => x.SecurityRoleId,
                        principalTable: "SecurityRole",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BankReconciliation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    BankId = table.Column<int>(type: "integer", nullable: false),
                    StatementEndDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    StatementEndingBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    BeginningBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    FinishedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReviewedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    UndoneBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UndoneAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    UndoReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    FinishedMaxGeneralLedgerHeaderId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankReconciliation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankReconciliation_Bank_BankId",
                        column: x => x.BankId,
                        principalTable: "Bank",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementColumnMapping",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    BankId = table.Column<int>(type: "integer", nullable: false),
                    HeaderSignature = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ColumnMapJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementColumnMapping", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementColumnMapping_Bank_BankId",
                        column: x => x.BankId,
                        principalTable: "Bank",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementImport",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    BankId = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ImportedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ColumnMapJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementImport", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementImport_Bank_BankId",
                        column: x => x.BankId,
                        principalTable: "Bank",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankTransaction",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    BankId = table.Column<int>(type: "integer", nullable: false),
                    ToBankId = table.Column<int>(type: "integer", nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ContraAccountId = table.Column<int>(type: "integer", nullable: true),
                    PartyId = table.Column<int>(type: "integer", nullable: true),
                    No = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ReferenceNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Memo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    GeneralLedgerHeaderId = table.Column<int>(type: "integer", nullable: true),
                    VoidGeneralLedgerHeaderId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransaction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankTransaction_Account_ContraAccountId",
                        column: x => x.ContraAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransaction_Bank_BankId",
                        column: x => x.BankId,
                        principalTable: "Bank",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransaction_Bank_ToBankId",
                        column: x => x.ToBankId,
                        principalTable: "Bank",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransaction_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransaction_GeneralLedgerHeader_VoidGeneralLedgerHeader~",
                        column: x => x.VoidGeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransaction_Party_PartyId",
                        column: x => x.PartyId,
                        principalTable: "Party",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemTaxGroupTax",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    TaxId = table.Column<int>(type: "integer", nullable: false),
                    ItemTaxGroupId = table.Column<int>(type: "integer", nullable: false),
                    IsExempt = table.Column<bool>(type: "boolean", nullable: false)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    TaxId = table.Column<int>(type: "integer", nullable: false),
                    TaxGroupId = table.Column<int>(type: "integer", nullable: false)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    ContactId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PaymentTermId = table.Column<int>(type: "integer", nullable: true),
                    CustomerId = table.Column<int>(type: "integer", nullable: true),
                    GeneralLedgerHeaderId = table.Column<int>(type: "integer", nullable: true),
                    No = table.Column<string>(type: "text", nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesDeliveryHeader", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesDeliveryHeader_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customer",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SalesDeliveryHeader_GeneralLedgerHeader_GeneralLedgerHeader~",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SalesDeliveryHeader_PaymentTerm_PaymentTermId",
                        column: x => x.PaymentTermId,
                        principalTable: "PaymentTerm",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoiceHeader",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    GeneralLedgerHeaderId = table.Column<int>(type: "integer", nullable: true),
                    VoidGeneralLedgerHeaderId = table.Column<int>(type: "integer", nullable: true),
                    No = table.Column<string>(type: "text", nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ShippingHandlingCharge = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PaymentTermId = table.Column<int>(type: "integer", nullable: true),
                    DueDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReferenceNo = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    PaymentInstructions = table.Column<string>(type: "text", nullable: true),
                    SellerJson = table.Column<string>(type: "text", nullable: true),
                    BillToJson = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: true),
                    Origin = table.Column<int>(type: "integer", nullable: true)
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
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SalesInvoiceHeader_GeneralLedgerHeader_VoidGeneralLedgerHea~",
                        column: x => x.VoidGeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderHeader",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: true),
                    PaymentTermId = table.Column<int>(type: "integer", nullable: true),
                    No = table.Column<string>(type: "text", nullable: true),
                    ReferenceNo = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderHeader", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderHeader_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customer",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SalesOrderHeader_PaymentTerm_PaymentTermId",
                        column: x => x.PaymentTermId,
                        principalTable: "PaymentTerm",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SalesQuoteHeader",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    PaymentTermId = table.Column<int>(type: "integer", nullable: true),
                    ReferenceNo = table.Column<string>(type: "text", nullable: true),
                    No = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    GeneralLedgerHeaderId = table.Column<int>(type: "integer", nullable: true),
                    AccountToDebitId = table.Column<int>(type: "integer", nullable: true),
                    No = table.Column<string>(type: "text", nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: true),
                    PaymentMethod = table.Column<string>(type: "text", nullable: true),
                    ReferenceNo = table.Column<string>(type: "text", nullable: true),
                    Memo = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesReceiptHeader", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesReceiptHeader_Account_AccountToDebitId",
                        column: x => x.AccountToDebitId,
                        principalTable: "Account",
                        principalColumn: "Id");
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
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BankStatementDescriptionRule",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    DescriptionKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VendorId = table.Column<int>(type: "integer", nullable: true),
                    ExpenseAccountId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementDescriptionRule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementDescriptionRule_Account_ExpenseAccountId",
                        column: x => x.ExpenseAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankStatementDescriptionRule_Vendor_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Item",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    ItemCategoryId = table.Column<int>(type: "integer", nullable: true),
                    SmallestMeasurementId = table.Column<int>(type: "integer", nullable: true),
                    SellMeasurementId = table.Column<int>(type: "integer", nullable: true),
                    PurchaseMeasurementId = table.Column<int>(type: "integer", nullable: true),
                    PreferredVendorId = table.Column<int>(type: "integer", nullable: true),
                    ItemTaxGroupId = table.Column<int>(type: "integer", nullable: true),
                    SalesAccountId = table.Column<int>(type: "integer", nullable: true),
                    InventoryAccountId = table.Column<int>(type: "integer", nullable: true),
                    CostOfGoodsSoldAccountId = table.Column<int>(type: "integer", nullable: true),
                    InventoryAdjustmentAccountId = table.Column<int>(type: "integer", nullable: true),
                    No = table.Column<string>(type: "text", nullable: true),
                    Code = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    PurchaseDescription = table.Column<string>(type: "text", nullable: true),
                    SellDescription = table.Column<string>(type: "text", nullable: true),
                    Cost = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Price = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    QuantityOnHand = table.Column<decimal>(type: "numeric(18,4)", nullable: false, defaultValue: 0m),
                    AverageCost = table.Column<decimal>(type: "numeric(18,4)", nullable: false, defaultValue: 0m),
                    StockValue = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Item", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Item_Account_CostOfGoodsSoldAccountId",
                        column: x => x.CostOfGoodsSoldAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Item_Account_InventoryAccountId",
                        column: x => x.InventoryAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Item_Account_InventoryAdjustmentAccountId",
                        column: x => x.InventoryAdjustmentAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Item_Account_SalesAccountId",
                        column: x => x.SalesAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Item_ItemCategory_ItemCategoryId",
                        column: x => x.ItemCategoryId,
                        principalTable: "ItemCategory",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Item_ItemTaxGroup_ItemTaxGroupId",
                        column: x => x.ItemTaxGroupId,
                        principalTable: "ItemTaxGroup",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Item_Measurement_PurchaseMeasurementId",
                        column: x => x.PurchaseMeasurementId,
                        principalTable: "Measurement",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Item_Measurement_SellMeasurementId",
                        column: x => x.SellMeasurementId,
                        principalTable: "Measurement",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Item_Measurement_SmallestMeasurementId",
                        column: x => x.SmallestMeasurementId,
                        principalTable: "Measurement",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Item_Vendor_PreferredVendorId",
                        column: x => x.PreferredVendorId,
                        principalTable: "Vendor",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoiceHeader",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    VendorId = table.Column<int>(type: "integer", nullable: true),
                    GeneralLedgerHeaderId = table.Column<int>(type: "integer", nullable: true),
                    VoidGeneralLedgerHeaderId = table.Column<int>(type: "integer", nullable: true),
                    PayableAccountId = table.Column<int>(type: "integer", nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    No = table.Column<string>(type: "text", nullable: true),
                    VendorInvoiceNo = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    PaymentTermId = table.Column<int>(type: "integer", nullable: true),
                    DueDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReferenceNo = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    BillFromJson = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: true),
                    ApprovedBy = table.Column<string>(type: "text", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    VoidedBy = table.Column<string>(type: "text", nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    VoidReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoiceHeader", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceHeader_Account_PayableAccountId",
                        column: x => x.PayableAccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceHeader_GeneralLedgerHeader_GeneralLedgerHead~",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceHeader_GeneralLedgerHeader_VoidGeneralLedger~",
                        column: x => x.VoidGeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceHeader_Vendor_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendor",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReceiptHeader",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    VendorId = table.Column<int>(type: "integer", nullable: false),
                    GeneralLedgerHeaderId = table.Column<int>(type: "integer", nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    No = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReceiptHeader", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptHeader_GeneralLedgerHeader_GeneralLedgerHead~",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id");
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    ContactId = table.Column<int>(type: "integer", nullable: false),
                    VendorId = table.Column<int>(type: "integer", nullable: false)
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
                name: "GeneralLedgerLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    GeneralLedgerHeaderId = table.Column<int>(type: "integer", nullable: false),
                    AccountId = table.Column<int>(type: "integer", nullable: false),
                    DrCr = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    BankReconciliationId = table.Column<int>(type: "integer", nullable: true)
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
                        name: "FK_GeneralLedgerLine_BankReconciliation_BankReconciliationId",
                        column: x => x.BankReconciliationId,
                        principalTable: "BankReconciliation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeneralLedgerLine_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoiceEmail",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    SalesInvoiceHeaderId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ToEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    BccEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    SentByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    SentByEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceEmail", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceEmail_SalesInvoiceHeader_SalesInvoiceHeaderId",
                        column: x => x.SalesInvoiceHeaderId,
                        principalTable: "SalesInvoiceHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerAllocation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    SalesInvoiceHeaderId = table.Column<int>(type: "integer", nullable: false),
                    SalesReceiptHeaderId = table.Column<int>(type: "integer", nullable: false),
                    GeneralLedgerHeaderId = table.Column<int>(type: "integer", nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAllocation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerAllocation_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customer",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CustomerAllocation_SalesInvoiceHeader_SalesInvoiceHeaderId",
                        column: x => x.SalesInvoiceHeaderId,
                        principalTable: "SalesInvoiceHeader",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CustomerAllocation_SalesReceiptHeader_SalesReceiptHeaderId",
                        column: x => x.SalesReceiptHeaderId,
                        principalTable: "SalesReceiptHeader",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DonationInvoiceLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    DonationInvoiceHeaderId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    MeasurementId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DonationInvoiceLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DonationInvoiceLine_DonationInvoiceHeader_DonationInvoiceHe~",
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

            migrationBuilder.CreateTable(
                name: "InventoryControlJournal",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    MeasurementId = table.Column<int>(type: "integer", nullable: false),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    DocumentId = table.Column<int>(type: "integer", nullable: true),
                    INQty = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    OUTQty = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    TotalCost = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    UnitCost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Memo = table.Column<string>(type: "text", nullable: true),
                    IsReverse = table.Column<bool>(type: "boolean", nullable: false)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderHeaderId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    MeasurementId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    SalesQuoteHeaderId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    MeasurementId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    VendorId = table.Column<int>(type: "integer", nullable: true),
                    No = table.Column<string>(type: "text", nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    PaymentTermId = table.Column<int>(type: "integer", nullable: true),
                    ReferenceNo = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: true),
                    PurchaseInvoiceHeaderId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderHeader", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderHeader_PurchaseInvoiceHeader_PurchaseInvoiceHe~",
                        column: x => x.PurchaseInvoiceHeaderId,
                        principalTable: "PurchaseInvoiceHeader",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseOrderHeader_Vendor_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendor",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "VendorPayment",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    VendorId = table.Column<int>(type: "integer", nullable: false),
                    PurchaseInvoiceHeaderId = table.Column<int>(type: "integer", nullable: true),
                    GeneralLedgerHeaderId = table.Column<int>(type: "integer", nullable: true),
                    VoidGeneralLedgerHeaderId = table.Column<int>(type: "integer", nullable: true),
                    No = table.Column<string>(type: "text", nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AccountId = table.Column<int>(type: "integer", nullable: true),
                    PaymentMethod = table.Column<string>(type: "text", nullable: true),
                    ReferenceNo = table.Column<string>(type: "text", nullable: true),
                    Memo = table.Column<string>(type: "text", nullable: true),
                    VoidedBy = table.Column<string>(type: "text", nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    VoidReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorPayment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendorPayment_Account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VendorPayment_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VendorPayment_GeneralLedgerHeader_VoidGeneralLedgerHeaderId",
                        column: x => x.VoidGeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VendorPayment_PurchaseInvoiceHeader_PurchaseInvoiceHeaderId",
                        column: x => x.PurchaseInvoiceHeaderId,
                        principalTable: "PurchaseInvoiceHeader",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VendorPayment_Vendor_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BankReconciliationItem",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    BankReconciliationId = table.Column<int>(type: "integer", nullable: false),
                    GeneralLedgerLineId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DrCr = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankReconciliationItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankReconciliationItem_BankReconciliation_BankReconciliatio~",
                        column: x => x.BankReconciliationId,
                        principalTable: "BankReconciliation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BankReconciliationItem_GeneralLedgerLine_GeneralLedgerLineId",
                        column: x => x.GeneralLedgerLineId,
                        principalTable: "GeneralLedgerLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    BankId = table.Column<int>(type: "integer", nullable: false),
                    BankStatementImportId = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PostedDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Payee = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Memo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MatchedGeneralLedgerLineId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExcludedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExcludedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExclusionReason = table.Column<int>(type: "integer", nullable: true),
                    ExclusionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementLine_BankStatementImport_BankStatementImportId",
                        column: x => x.BankStatementImportId,
                        principalTable: "BankStatementImport",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankStatementLine_Bank_BankId",
                        column: x => x.BankId,
                        principalTable: "Bank",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankStatementLine_GeneralLedgerLine_MatchedGeneralLedgerLin~",
                        column: x => x.MatchedGeneralLedgerLineId,
                        principalTable: "GeneralLedgerLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryAdjustment",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    No = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    QuantityChange = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Memo = table.Column<string>(type: "text", nullable: true),
                    GeneralLedgerHeaderId = table.Column<int>(type: "integer", nullable: true),
                    InventoryControlJournalId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryAdjustment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryAdjustment_GeneralLedgerHeader_GeneralLedgerHeader~",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InventoryAdjustment_InventoryControlJournal_InventoryContro~",
                        column: x => x.InventoryControlJournalId,
                        principalTable: "InventoryControlJournal",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InventoryAdjustment_Item_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoiceLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    SalesInvoiceHeaderId = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderLineId = table.Column<int>(type: "integer", nullable: true),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    MeasurementId = table.Column<int>(type: "integer", nullable: false),
                    InventoryControlJournalId = table.Column<int>(type: "integer", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TaxId = table.Column<int>(type: "integer", nullable: true),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    SalesAccountId = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceLine_Account_SalesAccountId",
                        column: x => x.SalesAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceLine_InventoryControlJournal_InventoryControlJo~",
                        column: x => x.InventoryControlJournalId,
                        principalTable: "InventoryControlJournal",
                        principalColumn: "Id");
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
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SalesInvoiceLine_Tax_TaxId",
                        column: x => x.TaxId,
                        principalTable: "Tax",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PurchaseOrderHeaderId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    TaxGroupId = table.Column<int>(type: "integer", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    SalesDeliveryHeaderId = table.Column<int>(type: "integer", nullable: false),
                    SalesInvoiceLineId = table.Column<int>(type: "integer", nullable: true),
                    ItemId = table.Column<int>(type: "integer", nullable: true),
                    MeasurementId = table.Column<int>(type: "integer", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesDeliveryLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesDeliveryLine_Item_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Item",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SalesDeliveryLine_Measurement_MeasurementId",
                        column: x => x.MeasurementId,
                        principalTable: "Measurement",
                        principalColumn: "Id");
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
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SalesReceiptLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    SalesReceiptHeaderId = table.Column<int>(type: "integer", nullable: false),
                    SalesInvoiceLineId = table.Column<int>(type: "integer", nullable: true),
                    ItemId = table.Column<int>(type: "integer", nullable: true),
                    AccountToCreditId = table.Column<int>(type: "integer", nullable: true),
                    MeasurementId = table.Column<int>(type: "integer", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Discount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    AmountPaid = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesReceiptLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesReceiptLine_Account_AccountToCreditId",
                        column: x => x.AccountToCreditId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SalesReceiptLine_Item_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Item",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SalesReceiptLine_Measurement_MeasurementId",
                        column: x => x.MeasurementId,
                        principalTable: "Measurement",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SalesReceiptLine_SalesInvoiceLine_SalesInvoiceLineId",
                        column: x => x.SalesInvoiceLineId,
                        principalTable: "SalesInvoiceLine",
                        principalColumn: "Id");
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PurchaseInvoiceHeaderId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    TaxGroupId = table.Column<int>(type: "integer", nullable: true),
                    InventoryControlJournalId = table.Column<int>(type: "integer", nullable: true),
                    PurchaseOrderLineId = table.Column<int>(type: "integer", nullable: true),
                    AccountId = table.Column<int>(type: "integer", nullable: true),
                    TaxId = table.Column<int>(type: "integer", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Cost = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Discount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoiceLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLine_Account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLine_InventoryControlJournal_InventoryContro~",
                        column: x => x.InventoryControlJournalId,
                        principalTable: "InventoryControlJournal",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLine_Item_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLine_PurchaseInvoiceHeader_PurchaseInvoiceHe~",
                        column: x => x.PurchaseInvoiceHeaderId,
                        principalTable: "PurchaseInvoiceHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLine_PurchaseOrderLine_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "PurchaseOrderLine",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLine_Tax_TaxId",
                        column: x => x.TaxId,
                        principalTable: "Tax",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReceiptLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PurchaseReceiptHeaderId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    InventoryControlJournalId = table.Column<int>(type: "integer", nullable: true),
                    PurchaseInvoiceLineId = table.Column<int>(type: "integer", nullable: true),
                    MeasurementId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReceiptLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptLine_InventoryControlJournal_InventoryContro~",
                        column: x => x.InventoryControlJournalId,
                        principalTable: "InventoryControlJournal",
                        principalColumn: "Id");
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
                        name: "FK_PurchaseReceiptLine_PurchaseInvoiceLine_PurchaseInvoiceLine~",
                        column: x => x.PurchaseInvoiceLineId,
                        principalTable: "PurchaseInvoiceLine",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptLine_PurchaseReceiptHeader_PurchaseReceiptHe~",
                        column: x => x.PurchaseReceiptHeaderId,
                        principalTable: "PurchaseReceiptHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Company",
                columns: new[] { "Id", "ActiveModulesJson", "BillingCustomerId", "BillingManagedManually", "BillingProvider", "BillingSubscriptionId", "CRA", "CancelAtPeriodEnd", "CompanyCode", "CreatedAt", "CreatedByUserId", "CurrentPeriodEnd", "DeletedAt", "DisabledModulesJson", "Name", "PaidAccessEndedAt", "Plan", "ShortName", "Slug", "SubscriptionStatus", "SubscriptionValidUntil", "Tier" },
                values: new object[] { 1, null, null, false, null, null, "012345678", false, "100", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, null, "Financial Solutions Inc.", null, "accounting_free", "FSI", "ph-financials", "active", null, null });

            migrationBuilder.InsertData(
                table: "Donors",
                columns: new[] { "Id", "CompanyId", "CompanyName", "DonorType", "Email", "FirstName", "LastName", "PostalCode", "Province", "Street", "Telephone" },
                values: new object[,]
                {
                    { 1, 1, "", "Individual", "olivia.bennett@example.com", "Olivia", "Bennett", "K1A 0B1", "ON", "125 Maple Street", "613-555-0101" },
                    { 2, 1, "Cedar Grove Community Services", "Company", "giving@cedargrove.example.com", "", "", "V6B 1A1", "BC", "480 Cedar Avenue", "604-555-0102" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Account_AccountClassId",
                table: "Account",
                column: "AccountClassId");

            migrationBuilder.CreateIndex(
                name: "IX_Account_CompanyId_AccountCode",
                table: "Account",
                columns: new[] { "CompanyId", "AccountCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Account_ParentAccountId",
                table: "Account",
                column: "ParentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditableAttribute_AuditableEntityId",
                table: "AuditableAttribute",
                column: "AuditableEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_Bank_AccountId",
                table: "Bank",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "UX_Bank_Company_Account",
                table: "Bank",
                columns: new[] { "CompanyId", "AccountId" },
                unique: true,
                filter: "\"AccountId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Bank_Company_Name",
                table: "Bank",
                columns: new[] { "CompanyId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Bank_DefaultPayments",
                table: "Bank",
                column: "CompanyId",
                unique: true,
                filter: "\"IsDefaultForPayments\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "UX_Bank_DefaultReceipts",
                table: "Bank",
                column: "CompanyId",
                unique: true,
                filter: "\"IsDefaultForReceipts\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliation_BankId",
                table: "BankReconciliation",
                column: "BankId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliation_Company_Bank_EndDate",
                table: "BankReconciliation",
                columns: new[] { "CompanyId", "BankId", "StatementEndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliation_CompanyId",
                table: "BankReconciliation",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "UX_BankReconciliation_OneDraft",
                table: "BankReconciliation",
                columns: new[] { "CompanyId", "BankId" },
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationItem_BankReconciliationId",
                table: "BankReconciliationItem",
                column: "BankReconciliationId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationItem_CompanyId",
                table: "BankReconciliationItem",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliationItem_GeneralLedgerLineId",
                table: "BankReconciliationItem",
                column: "GeneralLedgerLineId");

            migrationBuilder.CreateIndex(
                name: "UX_BankReconciliationItem_Recon_Line",
                table: "BankReconciliationItem",
                columns: new[] { "BankReconciliationId", "GeneralLedgerLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementColumnMapping_BankId",
                table: "BankStatementColumnMapping",
                column: "BankId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementColumnMapping_CompanyId",
                table: "BankStatementColumnMapping",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "UX_BankStatementColumnMapping_Company_Bank_Header",
                table: "BankStatementColumnMapping",
                columns: new[] { "CompanyId", "BankId", "HeaderSignature" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementDescriptionRule_CompanyId",
                table: "BankStatementDescriptionRule",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementDescriptionRule_ExpenseAccountId",
                table: "BankStatementDescriptionRule",
                column: "ExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementDescriptionRule_VendorId",
                table: "BankStatementDescriptionRule",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "UX_BankStatementDescriptionRule_Company_DescriptionKey",
                table: "BankStatementDescriptionRule",
                columns: new[] { "CompanyId", "DescriptionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImport_BankId",
                table: "BankStatementImport",
                column: "BankId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImport_Company_Bank",
                table: "BankStatementImport",
                columns: new[] { "CompanyId", "BankId" });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImport_CompanyId",
                table: "BankStatementImport",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLine_BankId",
                table: "BankStatementLine",
                column: "BankId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLine_BankStatementImportId",
                table: "BankStatementLine",
                column: "BankStatementImportId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLine_Company_Bank_ExcludedAt",
                table: "BankStatementLine",
                columns: new[] { "CompanyId", "BankId", "ExcludedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLine_Company_Bank_PostedDate",
                table: "BankStatementLine",
                columns: new[] { "CompanyId", "BankId", "PostedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLine_CompanyId",
                table: "BankStatementLine",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLine_MatchedGeneralLedgerLineId",
                table: "BankStatementLine",
                column: "MatchedGeneralLedgerLineId");

            migrationBuilder.CreateIndex(
                name: "UX_BankStatementLine_Company_Bank_Source_External",
                table: "BankStatementLine",
                columns: new[] { "CompanyId", "BankId", "Source", "ExternalId" },
                unique: true,
                filter: "\"ExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransaction_BankId",
                table: "BankTransaction",
                column: "BankId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransaction_Company_Bank_Date",
                table: "BankTransaction",
                columns: new[] { "CompanyId", "BankId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_BankTransaction_CompanyId",
                table: "BankTransaction",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransaction_ContraAccountId",
                table: "BankTransaction",
                column: "ContraAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransaction_GeneralLedgerHeaderId",
                table: "BankTransaction",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransaction_PartyId",
                table: "BankTransaction",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransaction_ToBankId",
                table: "BankTransaction",
                column: "ToBankId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransaction_VoidGeneralLedgerHeaderId",
                table: "BankTransaction",
                column: "VoidGeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "UX_BankTransaction_Company_No",
                table: "BankTransaction",
                columns: new[] { "CompanyId", "No" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BooksLockChange_CompanyId",
                table: "BooksLockChange",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Company_Slug",
                table: "Company",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyInvitation_CompanyId_Email",
                table: "CompanyInvitation",
                columns: new[] { "CompanyId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyInvitation_TokenHash",
                table: "CompanyInvitation",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyMember_CompanyId_UserId",
                table: "CompanyMember",
                columns: new[] { "CompanyId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyMember_UserId",
                table: "CompanyMember",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyMemberSecurityRole_CompanyMemberId_SecurityRoleId",
                table: "CompanyMemberSecurityRole",
                columns: new[] { "CompanyMemberId", "SecurityRoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyMemberSecurityRole_SecurityRoleId",
                table: "CompanyMemberSecurityRole",
                column: "SecurityRoleId");

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
                name: "IX_Customer_BillToContactId",
                table: "Customer",
                column: "BillToContactId");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_CompanyId_No",
                table: "Customer",
                columns: new[] { "CompanyId", "No" },
                unique: true);

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
                name: "IX_DonationInvoiceHeader_DonorId",
                table: "DonationInvoiceHeader",
                column: "DonorId");

            migrationBuilder.CreateIndex(
                name: "IX_DonationInvoiceHeader_GlAccountId",
                table: "DonationInvoiceHeader",
                column: "GlAccountId");

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

            migrationBuilder.CreateIndex(
                name: "IX_EntitlementUsage_CompanyId_Resource_OccurredAt",
                table: "EntitlementUsage",
                columns: new[] { "CompanyId", "Resource", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerLine_AccountId",
                table: "GeneralLedgerLine",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerLine_BankReconciliationId",
                table: "GeneralLedgerLine",
                column: "BankReconciliationId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerLine_Company_BankReconciliationId",
                table: "GeneralLedgerLine",
                columns: new[] { "CompanyId", "BankReconciliationId" });

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
                name: "IX_InventoryAdjustment_GeneralLedgerHeaderId",
                table: "InventoryAdjustment",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustment_InventoryControlJournalId",
                table: "InventoryAdjustment",
                column: "InventoryControlJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustment_ItemId",
                table: "InventoryAdjustment",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryControlJournal_ItemId",
                table: "InventoryControlJournal",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryControlJournal_MeasurementId",
                table: "InventoryControlJournal",
                column: "MeasurementId");

            migrationBuilder.CreateIndex(
                name: "IX_Item_CompanyId_No",
                table: "Item",
                columns: new[] { "CompanyId", "No" },
                unique: true);

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
                column: "MainAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MainContraAccount_RelatedContraAccountId",
                table: "MainContraAccount",
                column: "RelatedContraAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceHeader_GeneralLedgerHeaderId",
                table: "PurchaseInvoiceHeader",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceHeader_PayableAccountId",
                table: "PurchaseInvoiceHeader",
                column: "PayableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceHeader_VendorId",
                table: "PurchaseInvoiceHeader",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceHeader_VoidGeneralLedgerHeaderId",
                table: "PurchaseInvoiceHeader",
                column: "VoidGeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLine_AccountId",
                table: "PurchaseInvoiceLine",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLine_InventoryControlJournalId",
                table: "PurchaseInvoiceLine",
                column: "InventoryControlJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLine_ItemId",
                table: "PurchaseInvoiceLine",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLine_PurchaseInvoiceHeaderId",
                table: "PurchaseInvoiceLine",
                column: "PurchaseInvoiceHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLine_PurchaseOrderLineId",
                table: "PurchaseInvoiceLine",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLine_TaxId",
                table: "PurchaseInvoiceLine",
                column: "TaxId");

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
                name: "IX_SalesInvoiceEmail_CompanyId_SentAt",
                table: "SalesInvoiceEmail",
                columns: new[] { "CompanyId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceEmail_SalesInvoiceHeaderId",
                table: "SalesInvoiceEmail",
                column: "SalesInvoiceHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceHeader_Company_Origin",
                table: "SalesInvoiceHeader",
                columns: new[] { "CompanyId", "Origin" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceHeader_CompanyId_No",
                table: "SalesInvoiceHeader",
                columns: new[] { "CompanyId", "No" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceHeader_CustomerId",
                table: "SalesInvoiceHeader",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceHeader_GeneralLedgerHeaderId",
                table: "SalesInvoiceHeader",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceHeader_VoidGeneralLedgerHeaderId",
                table: "SalesInvoiceHeader",
                column: "VoidGeneralLedgerHeaderId");

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
                name: "IX_SalesInvoiceLine_SalesAccountId",
                table: "SalesInvoiceLine",
                column: "SalesAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLine_SalesInvoiceHeaderId",
                table: "SalesInvoiceLine",
                column: "SalesInvoiceHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLine_SalesOrderLineId",
                table: "SalesInvoiceLine",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLine_TaxId",
                table: "SalesInvoiceLine",
                column: "TaxId");

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
                name: "IX_SalesReceiptLine_ItemId",
                table: "SalesReceiptLine",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReceiptLine_MeasurementId",
                table: "SalesReceiptLine",
                column: "MeasurementId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReceiptLine_SalesInvoiceLineId",
                table: "SalesReceiptLine",
                column: "SalesInvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReceiptLine_SalesReceiptHeaderId",
                table: "SalesReceiptLine",
                column: "SalesReceiptHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityPermission_SecurityGroupId",
                table: "SecurityPermission",
                column: "SecurityGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityRolePermission_SecurityPermissionId",
                table: "SecurityRolePermission",
                column: "SecurityPermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityRolePermission_SecurityRoleId",
                table: "SecurityRolePermission",
                column: "SecurityRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityUserRole_SecurityRoleId",
                table: "SecurityUserRole",
                column: "SecurityRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityUserRole_UserId",
                table: "SecurityUserRole",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SequenceNumber_CompanyId_SequenceNumberType",
                table: "SequenceNumber",
                columns: new[] { "CompanyId", "SequenceNumberType" },
                unique: true);

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
                name: "IX_Vendor_BillToContactId",
                table: "Vendor",
                column: "BillToContactId");

            migrationBuilder.CreateIndex(
                name: "IX_Vendor_CompanyId_No",
                table: "Vendor",
                columns: new[] { "CompanyId", "No" },
                unique: true);

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
                name: "IX_VendorPayment_AccountId",
                table: "VendorPayment",
                column: "AccountId");

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
                name: "IX_VendorPayment_VoidGeneralLedgerHeaderId",
                table: "VendorPayment",
                column: "VoidGeneralLedgerHeaderId");

            // Schema the SQLite/D1 path adds outside the EF model (D1 0032, SqliteSchemaUpgrader, BankAccountCleanup).
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_InventoryAdjustment_CompanyId"" ON ""InventoryAdjustment"" (""CompanyId"");");

            // D1 compares bank names with COLLATE NOCASE.
            migrationBuilder.DropIndex(name: "UX_Bank_Company_Name", table: "Bank");
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX ""UX_Bank_Company_Name"" ON ""Bank"" (""CompanyId"", lower(""Name""));");

            // Replaces the trg_Bank_AccountId_required_* triggers: the column stays nullable in the model (D11).
            migrationBuilder.Sql(@"ALTER TABLE ""Bank"" ADD CONSTRAINT ""CK_Bank_AccountId_required"" CHECK (""AccountId"" IS NOT NULL);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Address");

            migrationBuilder.DropTable(
                name: "AuditableAttribute");

            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "BankReconciliationItem");

            migrationBuilder.DropTable(
                name: "BankStatementColumnMapping");

            migrationBuilder.DropTable(
                name: "BankStatementDescriptionRule");

            migrationBuilder.DropTable(
                name: "BankStatementLine");

            migrationBuilder.DropTable(
                name: "BankTransaction");

            migrationBuilder.DropTable(
                name: "BooksLockChange");

            migrationBuilder.DropTable(
                name: "CompanyInvitation");

            migrationBuilder.DropTable(
                name: "CompanyMemberSecurityRole");

            migrationBuilder.DropTable(
                name: "CompanySetting");

            migrationBuilder.DropTable(
                name: "CustomerAllocation");

            migrationBuilder.DropTable(
                name: "CustomerContact");

            migrationBuilder.DropTable(
                name: "DonationInvoiceLine");

            migrationBuilder.DropTable(
                name: "EntitlementUsage");

            migrationBuilder.DropTable(
                name: "FinancialYear");

            migrationBuilder.DropTable(
                name: "GeneralLedgerSetting");

            migrationBuilder.DropTable(
                name: "InventoryAdjustment");

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
                name: "SalesInvoiceEmail");

            migrationBuilder.DropTable(
                name: "SalesQuoteLine");

            migrationBuilder.DropTable(
                name: "SalesReceiptLine");

            migrationBuilder.DropTable(
                name: "SecurityRolePermission");

            migrationBuilder.DropTable(
                name: "SecurityUserRole");

            migrationBuilder.DropTable(
                name: "SequenceNumber");

            migrationBuilder.DropTable(
                name: "TaxGroupTax");

            migrationBuilder.DropTable(
                name: "VendorContact");

            migrationBuilder.DropTable(
                name: "VendorPayment");

            migrationBuilder.DropTable(
                name: "AuditableEntity");

            migrationBuilder.DropTable(
                name: "BankStatementImport");

            migrationBuilder.DropTable(
                name: "GeneralLedgerLine");

            migrationBuilder.DropTable(
                name: "CompanyMember");

            migrationBuilder.DropTable(
                name: "DonationInvoiceHeader");

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
                name: "SecurityPermission");

            migrationBuilder.DropTable(
                name: "SecurityRole");

            migrationBuilder.DropTable(
                name: "User");

            migrationBuilder.DropTable(
                name: "BankReconciliation");

            migrationBuilder.DropTable(
                name: "Donors");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLine");

            migrationBuilder.DropTable(
                name: "InventoryControlJournal");

            migrationBuilder.DropTable(
                name: "SalesInvoiceHeader");

            migrationBuilder.DropTable(
                name: "SalesOrderLine");

            migrationBuilder.DropTable(
                name: "Tax");

            migrationBuilder.DropTable(
                name: "SecurityGroup");

            migrationBuilder.DropTable(
                name: "Bank");

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
                name: "Contact");

            migrationBuilder.DropTable(
                name: "PaymentTerm");

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
