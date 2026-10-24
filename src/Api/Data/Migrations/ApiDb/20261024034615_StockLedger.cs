using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class StockLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AverageCost",
                table: "Item",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityOnHand",
                table: "Item",
                type: "decimal(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "OUTQty",
                table: "InventoryControlJournal",
                type: "decimal(18,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "INQty",
                table: "InventoryControlJournal",
                type: "decimal(18,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DocumentId",
                table: "InventoryControlJournal",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Memo",
                table: "InventoryControlJournal",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "InventoryControlJournal",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InventoryAdjustment",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    No = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    QuantityChange = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Memo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: true),
                    InventoryControlJournalId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryAdjustment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryAdjustment_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InventoryAdjustment_InventoryControlJournal_InventoryControlJournalId",
                        column: x => x.InventoryControlJournalId,
                        principalTable: "InventoryControlJournal",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InventoryAdjustment_Item_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustment_GeneralLedgerHeaderId",
                table: "InventoryAdjustment",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustment_InventoryControlJournalId",
                table: "InventoryAdjustment",
                column: "InventoryControlJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustment_ItemId",
                table: "InventoryAdjustment",
                column: "ItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryAdjustment");

            migrationBuilder.DropColumn(
                name: "AverageCost",
                table: "Item");

            migrationBuilder.DropColumn(
                name: "QuantityOnHand",
                table: "Item");

            migrationBuilder.DropColumn(
                name: "DocumentId",
                table: "InventoryControlJournal");

            migrationBuilder.DropColumn(
                name: "Memo",
                table: "InventoryControlJournal");

            migrationBuilder.DropColumn(
                name: "UnitCost",
                table: "InventoryControlJournal");

            migrationBuilder.AlterColumn<decimal>(
                name: "OUTQty",
                table: "InventoryControlJournal",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "INQty",
                table: "InventoryControlJournal",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldNullable: true);
        }
    }
}
