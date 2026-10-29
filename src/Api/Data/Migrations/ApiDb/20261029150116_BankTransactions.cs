using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class BankTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankTransaction",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    BankId = table.Column<int>(type: "int", nullable: false),
                    ToBankId = table.Column<int>(type: "int", nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ContraAccountId = table.Column<int>(type: "int", nullable: true),
                    PartyId = table.Column<int>(type: "int", nullable: true),
                    No = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReferenceNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Memo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    GeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: true),
                    VoidGeneralLedgerHeaderId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransaction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankTransaction_Account_ContraAccountId",
                        column: x => x.ContraAccountId,
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransaction_Bank_BankId",
                        column: x => x.BankId,
                        principalTable: "Bank",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransaction_Bank_ToBankId",
                        column: x => x.ToBankId,
                        principalTable: "Bank",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransaction_GeneralLedgerHeader_GeneralLedgerHeaderId",
                        column: x => x.GeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransaction_GeneralLedgerHeader_VoidGeneralLedgerHeaderId",
                        column: x => x.VoidGeneralLedgerHeaderId,
                        principalTable: "GeneralLedgerHeader",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankTransaction_Party_PartyId",
                        column: x => x.PartyId,
                        principalTable: "Party",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankTransaction_BankId",
                table: "BankTransaction",
                column: "BankId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransaction_Company_Bank_Date",
                table: "BankTransaction",
                columns: new[] { "CompanyId", "BankId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_BankTransaction_CompanyId",
                table: "BankTransaction",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransaction_ContraAccountId",
                table: "BankTransaction",
                column: "ContraAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransaction_GeneralLedgerHeaderId",
                table: "BankTransaction",
                column: "GeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransaction_PartyId",
                table: "BankTransaction",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransaction_ToBankId",
                table: "BankTransaction",
                column: "ToBankId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransaction_VoidGeneralLedgerHeaderId",
                table: "BankTransaction",
                column: "VoidGeneralLedgerHeaderId");

            migrationBuilder.CreateIndex(
                name: "UX_BankTransaction_Company_No",
                table: "BankTransaction",
                columns: new[] { "CompanyId", "No" },
                unique: true);

            // Backfill: SourceDocumentId = bank id on existing OB headers; one OpeningBalance BankTransaction per bank.
            migrationBuilder.Sql(@"
UPDATE [GeneralLedgerHeader]
SET [SourceDocumentId] = (
    SELECT TOP (1) b.[Id] FROM [Bank] AS b
    WHERE b.[OpeningBalanceGeneralLedgerHeaderId] = [GeneralLedgerHeader].[Id]
      AND b.[CompanyId] = [GeneralLedgerHeader].[CompanyId])
WHERE [DocumentType] = 17
  AND [Description] = N'Opening balance'
  AND [SourceDocumentId] IS NULL
  AND EXISTS (
    SELECT 1 FROM [Bank] AS b
    WHERE b.[OpeningBalanceGeneralLedgerHeaderId] = [GeneralLedgerHeader].[Id]
      AND b.[CompanyId] = [GeneralLedgerHeader].[CompanyId]);

INSERT INTO [BankTransaction] (
    [CompanyId], [Kind], [BankId], [ToBankId], [Date], [Amount],
    [ContraAccountId], [PartyId], [No], [ReferenceNo], [Memo],
    [GeneralLedgerHeaderId], [VoidGeneralLedgerHeaderId])
SELECT b.[CompanyId], 7, b.[Id], NULL,
       COALESCE(b.[OpeningBalanceDate], SYSUTCDATETIME()),
       ABS(COALESCE(b.[OpeningBalance], 0)),
       NULL, NULL, CONCAT(N'OB-', b.[Id]), NULL, N'Opening balance',
       b.[OpeningBalanceGeneralLedgerHeaderId], NULL
FROM [Bank] AS b
WHERE b.[OpeningBalanceGeneralLedgerHeaderId] IS NOT NULL
  AND COALESCE(b.[OpeningBalance], 0) <> 0
  AND NOT EXISTS (
    SELECT 1 FROM [BankTransaction] AS t
    WHERE t.[BankId] = b.[Id] AND t.[Kind] = 7 AND t.[VoidGeneralLedgerHeaderId] IS NULL);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankTransaction");
        }
    }
}
