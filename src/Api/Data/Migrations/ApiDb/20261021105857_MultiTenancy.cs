using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class MultiTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GeneralLedgerSetting_Company_CompanyId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropIndex(
                name: "IX_Account_CompanyId",
                table: "Account");

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "VendorPayment",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "VendorContact",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AlterColumn<string>(
                name: "No",
                table: "Vendor",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "Vendor",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "TaxGroupTax",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "TaxGroup",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "Tax",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "SequenceNumber",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "SalesReceiptLine",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "SalesReceiptHeader",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "SalesQuoteLine",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "SalesQuoteHeader",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "SalesOrderLine",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "SalesOrderHeader",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "SalesInvoiceLine",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "SalesInvoiceHeader",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "SalesDeliveryLine",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "SalesDeliveryHeader",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "PurchaseReceiptLine",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "PurchaseReceiptHeader",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "PurchaseOrderLine",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "PurchaseOrderHeader",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "PurchaseInvoiceLine",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "PurchaseInvoiceHeader",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "PaymentTerm",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "Party",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "Measurement",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "MainContraAccount",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "Log",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "JournalEntryLine",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "JournalEntryHeader",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "ItemTaxGroupTax",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "ItemTaxGroup",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "ItemCategory",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AlterColumn<string>(
                name: "No",
                table: "Item",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "Item",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "InventoryControlJournal",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AlterColumn<int>(
                name: "CompanyId",
                table: "GeneralLedgerSetting",
                type: "int",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "GeneralLedgerLine",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "GeneralLedgerHeader",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "FinancialYear",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "Donors",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "DonationInvoiceLine",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "DonationInvoiceHeader",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "CustomerContact",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "CustomerAllocation",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AlterColumn<string>(
                name: "No",
                table: "Customer",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "Customer",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "Contact",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AlterColumn<string>(
                name: "ShortName",
                table: "Company",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Company",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActiveModulesJson",
                table: "Company",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Company",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "Company",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Plan",
                table: "Company",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Company",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionStatus",
                table: "Company",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubscriptionValidUntil",
                table: "Company",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "Bank",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                schema: "dbo",
                table: "AuditLog",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "Address",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "CompanyInvitation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    SecurityRoleIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InvitedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcceptedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyInvitation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyInvitation_Company_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Company",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompanyMember",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InvitedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyMember", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyMember_Company_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Company",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompanyMemberSecurityRole",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyMemberId = table.Column<int>(type: "int", nullable: false),
                    SecurityRoleId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyMemberSecurityRole", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyMemberSecurityRole_CompanyMember_CompanyMemberId",
                        column: x => x.CompanyMemberId,
                        principalTable: "CompanyMember",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompanyMemberSecurityRole_SecurityRole_SecurityRoleId",
                        column: x => x.SecurityRoleId,
                        principalSchema: "dbo",
                        principalTable: "SecurityRole",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Company",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "ActiveModulesJson", "CreatedAt", "CreatedByUserId", "Plan", "Slug", "SubscriptionStatus", "SubscriptionValidUntil" },
                values: new object[] { null, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "accounting", null, "active", null });

            migrationBuilder.UpdateData(
                table: "Donors",
                keyColumn: "Id",
                keyValue: 1,
                column: "CompanyId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Donors",
                keyColumn: "Id",
                keyValue: 2,
                column: "CompanyId",
                value: 1);

            migrationBuilder.CreateIndex(
                name: "IX_Vendor_CompanyId_No",
                table: "Vendor",
                columns: new[] { "CompanyId", "No" },
                unique: true,
                filter: "[No] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SequenceNumber_CompanyId_SequenceNumberType",
                table: "SequenceNumber",
                columns: new[] { "CompanyId", "SequenceNumberType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Item_CompanyId_No",
                table: "Item",
                columns: new[] { "CompanyId", "No" },
                unique: true,
                filter: "[No] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_CompanyId_No",
                table: "Customer",
                columns: new[] { "CompanyId", "No" },
                unique: true,
                filter: "[No] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Account_CompanyId_AccountCode",
                table: "Account",
                columns: new[] { "CompanyId", "AccountCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyInvitation_CompanyId_Email",
                table: "CompanyInvitation",
                columns: new[] { "CompanyId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyInvitation_TokenHash",
                table: "CompanyInvitation",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyMember_CompanyId_UserId",
                table: "CompanyMember",
                columns: new[] { "CompanyId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyMember_UserId",
                table: "CompanyMember",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyMemberSecurityRole_CompanyMemberId_SecurityRoleId",
                table: "CompanyMemberSecurityRole",
                columns: new[] { "CompanyMemberId", "SecurityRoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyMemberSecurityRole_SecurityRoleId",
                table: "CompanyMemberSecurityRole",
                column: "SecurityRoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_GeneralLedgerSetting_Company_CompanyId",
                table: "GeneralLedgerSetting",
                column: "CompanyId",
                principalTable: "Company",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.Sql(@"
                -- Backfill Company 1 memberships (D1)
                INSERT INTO CompanyMember (CompanyId, UserId, Role, Status, JoinedAt)
                SELECT 1, Id, 1, 1, SYSUTCDATETIME()
                FROM AspNetUsers
                WHERE LOWER(Email) = 'marvinperez@gmail.com'
                  AND NOT EXISTS (
                      SELECT 1 FROM CompanyMember
                      WHERE CompanyId = 1 AND UserId = AspNetUsers.Id
                  );

                INSERT INTO CompanyMember (CompanyId, UserId, Role, Status, JoinedAt)
                SELECT 1, Id, 2, 1, SYSUTCDATETIME()
                FROM AspNetUsers
                WHERE LOWER(Email) = 'hello@marvinperez.com'
                  AND NOT EXISTS (
                      SELECT 1 FROM CompanyMember
                      WHERE CompanyId = 1 AND UserId = AspNetUsers.Id
                  );

                INSERT INTO CompanyMember (CompanyId, UserId, Role, Status, JoinedAt)
                SELECT 1, Id, 3, 1, SYSUTCDATETIME()
                FROM AspNetUsers
                WHERE LOWER(Email) = 'admin@accountgo.ph'
                  AND NOT EXISTS (
                      SELECT 1 FROM CompanyMember
                      WHERE CompanyId = 1 AND UserId = AspNetUsers.Id
                  );

                -- Attach Auditor / Viewer role to admin@accountgo.ph (D1)
                INSERT INTO CompanyMemberSecurityRole (CompanyMemberId, SecurityRoleId)
                SELECT cm.Id, sr.Id
                FROM CompanyMember cm
                JOIN AspNetUsers u ON u.Id = cm.UserId
                JOIN dbo.SecurityRole sr ON sr.Name = 'Auditor / Viewer'
                WHERE cm.CompanyId = 1 AND LOWER(u.Email) = 'admin@accountgo.ph'
                  AND NOT EXISTS (
                      SELECT 1 FROM CompanyMemberSecurityRole cmsr
                      WHERE cmsr.CompanyMemberId = cm.Id AND cmsr.SecurityRoleId = sr.Id
                  );

                -- Remove SystemAdministrators from non-platform staff (D2)
                DELETE FROM dbo.SecurityUserRole
                WHERE SecurityRoleId IN (SELECT Id FROM dbo.SecurityRole WHERE Name = 'SystemAdministrators')
                  AND UserId NOT IN (
                      SELECT Id FROM dbo.[User]
                      WHERE LOWER(UserName) = 'marvinperez@gmail.com' OR LOWER(EmailAddress) = 'marvinperez@gmail.com'
                  );
            ");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GeneralLedgerSetting_Company_CompanyId",
                table: "GeneralLedgerSetting");

            migrationBuilder.DropTable(
                name: "CompanyInvitation");

            migrationBuilder.DropTable(
                name: "CompanyMemberSecurityRole");

            migrationBuilder.DropTable(
                name: "CompanyMember");

            migrationBuilder.DropIndex(
                name: "IX_Vendor_CompanyId_No",
                table: "Vendor");

            migrationBuilder.DropIndex(
                name: "IX_SequenceNumber_CompanyId_SequenceNumberType",
                table: "SequenceNumber");

            migrationBuilder.DropIndex(
                name: "IX_Item_CompanyId_No",
                table: "Item");

            migrationBuilder.DropIndex(
                name: "IX_Customer_CompanyId_No",
                table: "Customer");

            migrationBuilder.DropIndex(
                name: "IX_Account_CompanyId_AccountCode",
                table: "Account");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "VendorPayment");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "VendorContact");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Vendor");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "TaxGroupTax");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "TaxGroup");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Tax");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "SequenceNumber");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "SalesReceiptLine");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "SalesReceiptHeader");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "SalesQuoteLine");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "SalesQuoteHeader");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "SalesOrderLine");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "SalesOrderHeader");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "SalesInvoiceLine");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "SalesInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "SalesDeliveryLine");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "SalesDeliveryHeader");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "PurchaseReceiptLine");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "PurchaseReceiptHeader");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "PurchaseOrderLine");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "PurchaseOrderHeader");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "PurchaseInvoiceLine");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "PurchaseInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "PaymentTerm");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Party");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Measurement");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "MainContraAccount");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Log");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "JournalEntryLine");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "JournalEntryHeader");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "ItemTaxGroupTax");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "ItemTaxGroup");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "ItemCategory");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Item");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "InventoryControlJournal");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "GeneralLedgerLine");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "GeneralLedgerHeader");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "FinancialYear");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Donors");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "DonationInvoiceLine");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "DonationInvoiceHeader");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "CustomerContact");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "CustomerAllocation");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Contact");

            migrationBuilder.DropColumn(
                name: "ActiveModulesJson",
                table: "Company");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Company");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Company");

            migrationBuilder.DropColumn(
                name: "Plan",
                table: "Company");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Company");

            migrationBuilder.DropColumn(
                name: "SubscriptionStatus",
                table: "Company");

            migrationBuilder.DropColumn(
                name: "SubscriptionValidUntil",
                table: "Company");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Bank");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                schema: "dbo",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Address");

            migrationBuilder.AlterColumn<string>(
                name: "No",
                table: "Vendor",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "No",
                table: "Item",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "CompanyId",
                table: "GeneralLedgerSetting",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "No",
                table: "Customer",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ShortName",
                table: "Company",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Company",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_Account_CompanyId",
                table: "Account",
                column: "CompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_GeneralLedgerSetting_Company_CompanyId",
                table: "GeneralLedgerSetting",
                column: "CompanyId",
                principalTable: "Company",
                principalColumn: "Id");
        }
    }
}
