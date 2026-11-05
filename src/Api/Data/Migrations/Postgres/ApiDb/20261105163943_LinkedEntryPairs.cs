using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Api.Data.Migrations.Postgres.ApiDb
{
    /// <inheritdoc />
    public partial class LinkedEntryPairs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LinkedEntryPairs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceCompanyId = table.Column<int>(type: "integer", nullable: false),
                    SourceJournalId = table.Column<int>(type: "integer", nullable: true),
                    MirrorCompanyId = table.Column<int>(type: "integer", nullable: false),
                    MirrorJournalId = table.Column<int>(type: "integer", nullable: true),
                    WorkBankAccountId = table.Column<int>(type: "integer", nullable: false),
                    StatementLineId = table.Column<int>(type: "integer", nullable: true),
                    OwnerUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    MirrorAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    SourceCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    MirrorCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkedEntryPairs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LinkedEntryPairs_MirrorCompanyId_MirrorJournalId",
                table: "LinkedEntryPairs",
                columns: new[] { "MirrorCompanyId", "MirrorJournalId" },
                unique: true,
                filter: "\"MirrorJournalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LinkedEntryPairs_SourceCompanyId_IdempotencyKey",
                table: "LinkedEntryPairs",
                columns: new[] { "SourceCompanyId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LinkedEntryPairs_SourceCompanyId_SourceJournalId",
                table: "LinkedEntryPairs",
                columns: new[] { "SourceCompanyId", "SourceJournalId" },
                unique: true,
                filter: "\"SourceJournalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LinkedEntryPairs_StatementLineId",
                table: "LinkedEntryPairs",
                column: "StatementLineId",
                unique: true,
                filter: "\"StatementLineId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LinkedEntryPairs");
        }
    }
}
