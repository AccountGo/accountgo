using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class AddRecurringSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Timezone",
                table: "CompanySetting",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.CreateTable(
                name: "RecurringSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    PartyId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Cadence = table.Column<int>(type: "int", nullable: false),
                    Interval = table.Column<int>(type: "int", nullable: false),
                    DayOfMonth = table.Column<int>(type: "int", nullable: true),
                    StartDate = table.Column<DateTime>(type: "date", nullable: false),
                    EndDate = table.Column<DateTime>(type: "date", nullable: true),
                    MaxOccurrences = table.Column<int>(type: "int", nullable: true),
                    NextRunDate = table.Column<DateTime>(type: "date", nullable: true),
                    OccurrenceCount = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    RecognitionMode = table.Column<int>(type: "int", nullable: false),
                    ServicePeriodMode = table.Column<int>(type: "int", nullable: false),
                    PaymentTermId = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PaymentInstructions = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    VendorInvoiceNumberPattern = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AutomationEnabledBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AutomationEnabledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "int", nullable: false),
                    NeedsAttention = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringSchedules_Company_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Company",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecurringOccurrences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    RecurringScheduleId = table.Column<int>(type: "int", nullable: false),
                    ScheduledRunDate = table.Column<DateTime>(type: "date", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "date", nullable: false),
                    DocumentDate = table.Column<DateTime>(type: "date", nullable: false),
                    DueDate = table.Column<DateTime>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LeasedUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeasedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SalesInvoiceId = table.Column<int>(type: "int", nullable: true),
                    PurchaseInvoiceId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GuardFailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActionBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SkipReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SeriesAdvanced = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringOccurrences_PurchaseInvoiceHeader_PurchaseInvoiceId",
                        column: x => x.PurchaseInvoiceId,
                        principalTable: "PurchaseInvoiceHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringOccurrences_RecurringSchedules_RecurringScheduleId",
                        column: x => x.RecurringScheduleId,
                        principalTable: "RecurringSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringOccurrences_SalesInvoiceHeader_SalesInvoiceId",
                        column: x => x.SalesInvoiceId,
                        principalTable: "SalesInvoiceHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecurringScheduleLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    RecurringScheduleId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false, defaultValue: 1m),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Discount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, defaultValue: 0m),
                    DiscountType = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    TaxId = table.Column<int>(type: "int", nullable: true),
                    AccountId = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringScheduleLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringScheduleLines_Account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringScheduleLines_Item_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringScheduleLines_RecurringSchedules_RecurringScheduleId",
                        column: x => x.RecurringScheduleId,
                        principalTable: "RecurringSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecurringScheduleLines_Tax_TaxId",
                        column: x => x.TaxId,
                        principalTable: "Tax",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOccurrence_Company_Status_DocumentDate",
                table: "RecurringOccurrences",
                columns: new[] { "CompanyId", "Status", "DocumentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOccurrence_Company_Status_Lease",
                table: "RecurringOccurrences",
                columns: new[] { "CompanyId", "Status", "LeasedUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOccurrences_IdempotencyKey",
                table: "RecurringOccurrences",
                column: "IdempotencyKey");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOccurrences_PurchaseInvoiceId",
                table: "RecurringOccurrences",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOccurrences_RecurringScheduleId",
                table: "RecurringOccurrences",
                column: "RecurringScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOccurrences_SalesInvoiceId",
                table: "RecurringOccurrences",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "UX_RecurringOccurrence_Company_Schedule_RunDate",
                table: "RecurringOccurrences",
                columns: new[] { "CompanyId", "RecurringScheduleId", "ScheduledRunDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringScheduleLines_AccountId",
                table: "RecurringScheduleLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringScheduleLines_CompanyId",
                table: "RecurringScheduleLines",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringScheduleLines_ItemId",
                table: "RecurringScheduleLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringScheduleLines_RecurringScheduleId",
                table: "RecurringScheduleLines",
                column: "RecurringScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringScheduleLines_TaxId",
                table: "RecurringScheduleLines",
                column: "TaxId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSchedule_Company_Kind_Party",
                table: "RecurringSchedules",
                columns: new[] { "CompanyId", "Kind", "PartyId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSchedule_Company_NextRunDate",
                table: "RecurringSchedules",
                columns: new[] { "CompanyId", "NextRunDate" },
                filter: "[Status] = 1 AND [NextRunDate] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSchedule_Company_Status",
                table: "RecurringSchedules",
                columns: new[] { "CompanyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSchedules_CompanyId",
                table: "RecurringSchedules",
                column: "CompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecurringOccurrences");

            migrationBuilder.DropTable(
                name: "RecurringScheduleLines");

            migrationBuilder.DropTable(
                name: "RecurringSchedules");

            migrationBuilder.DropColumn(
                name: "Timezone",
                table: "CompanySetting");
        }
    }
}
