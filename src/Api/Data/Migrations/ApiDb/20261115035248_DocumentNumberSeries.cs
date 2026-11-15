using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class DocumentNumberSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Format",
                table: "SequenceNumber",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentNumberSeries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    SequenceNumberType = table.Column<int>(type: "int", nullable: false),
                    FinancialYearId = table.Column<int>(type: "int", nullable: false),
                    NextNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentNumberSeries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentNumberSeries_FinancialYear_FinancialYearId",
                        column: x => x.FinancialYearId,
                        principalTable: "FinancialYear",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentNumberSeries_CompanyId_SequenceNumberType_FinancialYearId",
                table: "DocumentNumberSeries",
                columns: new[] { "CompanyId", "SequenceNumberType", "FinancialYearId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentNumberSeries_FinancialYearId",
                table: "DocumentNumberSeries",
                column: "FinancialYearId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentNumberSeries");

            migrationBuilder.DropColumn(
                name: "Format",
                table: "SequenceNumber");
        }
    }
}
