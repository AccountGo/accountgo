using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Api.Data.Migrations.Postgres.ApiDb
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    YearEndCloseRunId = table.Column<int>(type: "integer", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    ContentDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearEndCloseEvidence", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "YearEndCloseRun",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    FinancialYearId = table.Column<int>(type: "integer", nullable: false),
                    AttemptNo = table.Column<int>(type: "integer", nullable: false),
                    PreviewHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: false),
                    SourceRevision = table.Column<long>(type: "bigint", nullable: false),
                    Actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    BooksLockedThroughBefore = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    BooksLockedThroughAfter = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ClosingGeneralLedgerHeaderId = table.Column<int>(type: "integer", nullable: true),
                    WarningExceptionReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RecordedAuthority = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearEndCloseRun", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "YearEndCommandReceipt",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Operation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RequestDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    YearEndCloseRunId = table.Column<int>(type: "integer", nullable: true),
                    CommittedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearEndCommandReceipt", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "YearEndIssuanceDeclaration",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    FinancialYearId = table.Column<int>(type: "integer", nullable: false),
                    YearEndCloseRunId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DeclaredAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExternalDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExternalReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YearEndIssuanceDeclaration", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "YearEndLifecycleEvent",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    FinancialYearId = table.Column<int>(type: "integer", nullable: false),
                    YearEndCloseRunId = table.Column<int>(type: "integer", nullable: true),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReversalGeneralLedgerHeaderId = table.Column<int>(type: "integer", nullable: true),
                    BooksLockedThroughBefore = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    BooksLockedThroughAfter = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
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

            migrationBuilder.Sql("""
                CREATE FUNCTION year_end_evidence_immutable() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'Year-end close evidence is append-only.';
                END;
                $$ LANGUAGE plpgsql
                """);
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
                    CREATE TRIGGER "TR_{table}_Immutable" BEFORE UPDATE OR DELETE ON "{table}"
                    FOR EACH ROW EXECUTE FUNCTION year_end_evidence_immutable()
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
                migrationBuilder.Sql($"""DROP TRIGGER IF EXISTS "TR_{table}_Immutable" ON "{table}" """);
            }
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS year_end_evidence_immutable()");

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
