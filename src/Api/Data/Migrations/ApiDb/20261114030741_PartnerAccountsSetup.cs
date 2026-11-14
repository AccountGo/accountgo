using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class PartnerAccountsSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PartnerAccountsFrom",
                table: "CompanySetting",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Partners",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CapitalAccountId = table.Column<int>(type: "int", nullable: false),
                    DrawingsAccountId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Partners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PartnerShares",
                columns: table => new
                {
                    FinancialYearId = table.Column<int>(type: "int", nullable: false),
                    PartnerId = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    Percent = table.Column<decimal>(type: "decimal(7,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartnerShares", x => new { x.FinancialYearId, x.PartnerId });
                    table.ForeignKey(
                        name: "FK_PartnerShares_FinancialYear_FinancialYearId",
                        column: x => x.FinancialYearId,
                        principalTable: "FinancialYear",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PartnerShares_Partners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "Partners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Partners_CompanyId_CapitalAccountId",
                table: "Partners",
                columns: new[] { "CompanyId", "CapitalAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partners_CompanyId_DrawingsAccountId",
                table: "Partners",
                columns: new[] { "CompanyId", "DrawingsAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partners_CompanyId_Name",
                table: "Partners",
                columns: new[] { "CompanyId", "Name" },
                unique: true,
                filter: "ArchivedAt IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerShares_PartnerId",
                table: "PartnerShares",
                column: "PartnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PartnerShares");

            migrationBuilder.DropTable(
                name: "Partners");

            migrationBuilder.DropColumn(
                name: "PartnerAccountsFrom",
                table: "CompanySetting");
        }
    }
}
