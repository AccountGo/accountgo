using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
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
                    Id = table.Column<int>(type: ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite" ? "INTEGER" : "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceCompanyId = table.Column<int>(type: ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite" ? "INTEGER" : "int", nullable: false),
                    SourceJournalId = table.Column<int>(type: ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite" ? "INTEGER" : "int", nullable: true),
                    MirrorCompanyId = table.Column<int>(type: ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite" ? "INTEGER" : "int", nullable: false),
                    MirrorJournalId = table.Column<int>(type: ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite" ? "INTEGER" : "int", nullable: true),
                    WorkBankAccountId = table.Column<int>(type: ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite" ? "INTEGER" : "int", nullable: false),
                    StatementLineId = table.Column<int>(type: ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite" ? "INTEGER" : "int", nullable: true),
                    OwnerUserId = table.Column<string>(type: ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite" ? "TEXT" : "nvarchar(450)", maxLength: 450, nullable: false),
                    Kind = table.Column<string>(type: ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite" ? "TEXT" : "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MirrorAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SourceCurrency = table.Column<string>(type: ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite" ? "TEXT" : "nvarchar(3)", maxLength: 3, nullable: false),
                    MirrorCurrency = table.Column<string>(type: ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite" ? "TEXT" : "nvarchar(3)", maxLength: 3, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    State = table.Column<string>(type: ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite" ? "TEXT" : "nvarchar(32)", maxLength: 32, nullable: false),
                    IdempotencyKey = table.Column<string>(type: ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite" ? "TEXT" : "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite" ? "TEXT" : "datetime2", nullable: false)
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
                filter: ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite" ? "\"MirrorJournalId\" IS NOT NULL" : "[MirrorJournalId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LinkedEntryPairs_SourceCompanyId_IdempotencyKey",
                table: "LinkedEntryPairs",
                columns: new[] { "SourceCompanyId", "IdempotencyKey" },
                unique: true,
                filter: ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite" ? "\"IdempotencyKey\" IS NOT NULL" : "[IdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LinkedEntryPairs_SourceCompanyId_SourceJournalId",
                table: "LinkedEntryPairs",
                columns: new[] { "SourceCompanyId", "SourceJournalId" },
                unique: true,
                filter: ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite" ? "\"SourceJournalId\" IS NOT NULL" : "[SourceJournalId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LinkedEntryPairs_StatementLineId",
                table: "LinkedEntryPairs",
                column: "StatementLineId",
                unique: true,
                filter: ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite" ? "\"StatementLineId\" IS NOT NULL" : "[StatementLineId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LinkedEntryPairs");
        }
    }
}
