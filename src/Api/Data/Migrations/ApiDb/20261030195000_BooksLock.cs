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
    [Migration("20261030195000_BooksLock")]
    public partial class BooksLock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BooksLockedThrough",
                table: "CompanySetting",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BooksLockChange",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    PreviousLockedThrough = table.Column<DateTime>(type: "date", nullable: true),
                    NewLockedThrough = table.Column<DateTime>(type: "date", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ChangedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BooksLockChange", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BooksLockChange_CompanyId",
                table: "BooksLockChange",
                column: "CompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "BooksLockChange");

            migrationBuilder.DropColumn(
                name: "BooksLockedThrough",
                table: "CompanySetting");
        }
    }
}
