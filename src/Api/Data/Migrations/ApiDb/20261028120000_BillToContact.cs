// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <summary>
    /// Bill To contact FK on Customer and Vendor. Mirrors D1 0029_bill_to_contact.sql and SqliteSchemaUpgrader.
    /// Hand-authored (same pattern as CompanyBilling) so SQLite design-time does not rewrite SQL Server types.
    /// </summary>
    [DbContext(typeof(ApiDbContext))]
    [Migration("20261028120000_BillToContact")]
    public class BillToContact : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BillToContactId",
                table: "Customer",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BillToContactId",
                table: "Vendor",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customer_BillToContactId",
                table: "Customer",
                column: "BillToContactId");

            migrationBuilder.CreateIndex(
                name: "IX_Vendor_BillToContactId",
                table: "Vendor",
                column: "BillToContactId");

            migrationBuilder.AddForeignKey(
                name: "FK_Customer_Contact_BillToContactId",
                table: "Customer",
                column: "BillToContactId",
                principalTable: "Contact",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Vendor_Contact_BillToContactId",
                table: "Vendor",
                column: "BillToContactId",
                principalTable: "Contact",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customer_Contact_BillToContactId",
                table: "Customer");

            migrationBuilder.DropForeignKey(
                name: "FK_Vendor_Contact_BillToContactId",
                table: "Vendor");

            migrationBuilder.DropIndex(
                name: "IX_Customer_BillToContactId",
                table: "Customer");

            migrationBuilder.DropIndex(
                name: "IX_Vendor_BillToContactId",
                table: "Vendor");

            migrationBuilder.DropColumn(
                name: "BillToContactId",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "BillToContactId",
                table: "Vendor");
        }
    }
}
