// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    [DbContext(typeof(ApiDbContext))]
    [Migration("20261030210000_EntityTypeAndYearEndClose")]
    public partial class EntityTypeAndYearEndClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EntityType",
                table: "CompanySetting",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "FinancialYear",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosedBy",
                table: "FinancialYear",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClosingGeneralLedgerHeaderId",
                table: "FinancialYear",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ClosingGeneralLedgerHeaderId", table: "FinancialYear");
            migrationBuilder.DropColumn(name: "ClosedBy", table: "FinancialYear");
            migrationBuilder.DropColumn(name: "ClosedAt", table: "FinancialYear");
            migrationBuilder.DropColumn(name: "EntityType", table: "CompanySetting");
        }
    }
}
