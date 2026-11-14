using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Api.Data.Migrations.Postgres.ApiDb
{
    /// <inheritdoc />
    public partial class TaxFilings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaxFiling",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "date", nullable: false),
                    Frequency = table.Column<int>(type: "integer", nullable: false),
                    Basis = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    AmendsFilingId = table.Column<int>(type: "integer", nullable: true),
                    AmendmentReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FiledOn = table.Column<DateTime>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OutputTax = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    InputTax = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    NetTax = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxFiling", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxFiling_TaxFiling_AmendsFilingId",
                        column: x => x.AmendsFilingId,
                        principalTable: "TaxFiling",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaxFiling_AmendsFilingId",
                table: "TaxFiling",
                column: "AmendsFilingId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxFiling_CompanyId_PeriodStart_PeriodEnd_Sequence",
                table: "TaxFiling",
                columns: new[] { "CompanyId", "PeriodStart", "PeriodEnd", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaxFiling");
        }
    }
}
