using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class YearEndCloseEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyCloseState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    AccountingRevision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyCloseState", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "YearEndCloseEvidence",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    YearEndCloseRunId = table.Column<int>(type: "int", nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    ContentDigest = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearEndCloseEvidence", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "YearEndCloseRun",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    FinancialYearId = table.Column<int>(type: "int", nullable: false),
                    AttemptNo = table.Column<int>(type: "int", nullable: false),
                    PreviewHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PolicyVersion = table.Column<int>(type: "int", nullable: false),
                    SourceRevision = table.Column<long>(type: "bigint", nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BooksLockedThroughBefore = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BooksLockedThroughAfter = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosingGeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: true),
                    WarningExceptionReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RecordedAuthority = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearEndCloseRun", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "YearEndCommandReceipt",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    RequestDigest = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    YearEndCloseRunId = table.Column<int>(type: "int", nullable: true),
                    CommittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearEndCommandReceipt", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "YearEndIssuanceDeclaration",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    FinancialYearId = table.Column<int>(type: "int", nullable: false),
                    YearEndCloseRunId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DeclaredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExternalDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExternalReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearEndIssuanceDeclaration", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "YearEndLifecycleEvent",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    FinancialYearId = table.Column<int>(type: "int", nullable: false),
                    YearEndCloseRunId = table.Column<int>(type: "int", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReversalGeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: true),
                    BooksLockedThroughBefore = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BooksLockedThroughAfter = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearEndLifecycleEvent", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyCloseState_CompanyId",
                table: "CompanyCloseState",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YearEndCloseEvidence_YearEndCloseRunId",
                table: "YearEndCloseEvidence",
                column: "YearEndCloseRunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YearEndCloseRun_CompanyId_FinancialYearId_AttemptNo",
                table: "YearEndCloseRun",
                columns: new[] { "CompanyId", "FinancialYearId", "AttemptNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YearEndCommandReceipt_CompanyId_Operation_IdempotencyKey",
                table: "YearEndCommandReceipt",
                columns: new[] { "CompanyId", "Operation", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_YearEndIssuanceDeclaration_CompanyId_FinancialYearId_Id",
                table: "YearEndIssuanceDeclaration",
                columns: new[] { "CompanyId", "FinancialYearId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_YearEndLifecycleEvent_CompanyId_FinancialYearId_Id",
                table: "YearEndLifecycleEvent",
                columns: new[] { "CompanyId", "FinancialYearId", "Id" });

            // Append-only evidence. CompanyCloseState stays mutable because the revision counter advances.
            foreach (var table in new[]
            {
                "YearEndCloseEvidence",
                "YearEndCloseRun",
                "YearEndLifecycleEvent",
                "YearEndCommandReceipt",
                "YearEndIssuanceDeclaration"
            })
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER [TR_{table}_Immutable_Update] ON [{table}]
                    INSTEAD OF UPDATE AS
                    BEGIN
                        THROW 51000, 'Year-end close evidence is append-only.', 1;
                    END
                    """);
                migrationBuilder.Sql($"""
                    CREATE TRIGGER [TR_{table}_Immutable_Delete] ON [{table}]
                    INSTEAD OF DELETE AS
                    BEGIN
                        THROW 51000, 'Year-end close evidence is append-only.', 1;
                    END
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[]
            {
                "YearEndCloseEvidence",
                "YearEndCloseRun",
                "YearEndLifecycleEvent",
                "YearEndCommandReceipt",
                "YearEndIssuanceDeclaration"
            })
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS [TR_{table}_Immutable_Update]");
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS [TR_{table}_Immutable_Delete]");
            }

            migrationBuilder.DropTable(
                name: "CompanyCloseState");

            migrationBuilder.DropTable(
                name: "YearEndCloseEvidence");

            migrationBuilder.DropTable(
                name: "YearEndCloseRun");

            migrationBuilder.DropTable(
                name: "YearEndCommandReceipt");

            migrationBuilder.DropTable(
                name: "YearEndIssuanceDeclaration");

            migrationBuilder.DropTable(
                name: "YearEndLifecycleEvent");
        }
    }
}
