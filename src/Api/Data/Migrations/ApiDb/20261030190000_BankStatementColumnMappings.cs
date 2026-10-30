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
    [Migration("20261030190000_BankStatementColumnMappings")]
    public partial class BankStatementColumnMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankStatementColumnMapping",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    BankId = table.Column<int>(type: "int", nullable: false),
                    HeaderSignature = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ColumnMapJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "BankStatementColumnMapping");
        }
    }
}
