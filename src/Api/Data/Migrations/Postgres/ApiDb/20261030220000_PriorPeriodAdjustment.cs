// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.Postgres.ApiDb
{
    /// <inheritdoc />
    [DbContext(typeof(Api.Data.Postgres.PostgresApiDbContext))]
    [Migration("20261030220000_PriorPeriodAdjustment")]
    public partial class PriorPeriodAdjustment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AdjustsFiscalYearId",
                table: "JournalEntryHeader",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryHeader_AdjustsFiscalYearId",
                table: "JournalEntryHeader",
                column: "AdjustsFiscalYearId");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryHeader_FinancialYear_AdjustsFiscalYearId",
                table: "JournalEntryHeader",
                column: "AdjustsFiscalYearId",
                principalTable: "FinancialYear",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntryHeader_FinancialYear_AdjustsFiscalYearId",
                table: "JournalEntryHeader");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryHeader_AdjustsFiscalYearId",
                table: "JournalEntryHeader");

            migrationBuilder.DropColumn(
                name: "AdjustsFiscalYearId",
                table: "JournalEntryHeader");
        }
    }
}
