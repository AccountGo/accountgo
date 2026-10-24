using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class StockValue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "StockValue",
                table: "Item",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(@"
                UPDATE ""Item"" SET ""StockValue"" = ROUND(CASE WHEN NOT EXISTS (
                    SELECT 1 FROM ""InventoryControlJournal"" j
                    WHERE j.""ItemId"" = ""Item"".""Id"" AND j.""CompanyId"" = ""Item"".""CompanyId"" AND j.""TotalCost"" IS NULL
                ) THEN COALESCE((
                    SELECT SUM(CASE WHEN COALESCE(j.""INQty"", 0) > 0 THEN j.""TotalCost"" ELSE -j.""TotalCost"" END)
                    FROM ""InventoryControlJournal"" j
                    WHERE j.""ItemId"" = ""Item"".""Id"" AND j.""CompanyId"" = ""Item"".""CompanyId""
                ), ""QuantityOnHand"" * ""AverageCost"")
                ELSE ""QuantityOnHand"" * ""AverageCost"" END, 2);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StockValue",
                table: "Item");
        }
    }
}
