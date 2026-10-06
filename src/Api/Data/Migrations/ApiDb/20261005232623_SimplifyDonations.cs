using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class SimplifyDonations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DonationInvoiceHeader_Customer_DonorId",
                table: "DonationInvoiceHeader");

            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "DonationInvoiceHeader",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "GlAccountId",
                table: "DonationInvoiceHeader",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentType",
                table: "DonationInvoiceHeader",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DonationInvoiceHeader_GlAccountId",
                table: "DonationInvoiceHeader",
                column: "GlAccountId");

            migrationBuilder.Sql(@"
UPDATE donation
SET Amount = COALESCE(totals.TotalAmount, 0)
FROM DonationInvoiceHeader AS donation
OUTER APPLY (
    SELECT SUM(line.Quantity * line.Amount) AS TotalAmount
    FROM DonationInvoiceLine AS line
    WHERE line.DonationInvoiceHeaderId = donation.Id
) AS totals;

DECLARE @DonorMap TABLE (CustomerId int NOT NULL, DonorId int NOT NULL);
MERGE Donors AS target
USING (
    SELECT DISTINCT customer.Id AS CustomerId,
        LEFT(COALESCE(party.Name, ''), 200) AS CompanyName,
        LEFT(COALESCE(party.CompanyStreet, ''), 200) AS Street,
        LEFT(COALESCE(party.CompanyProvince, ''), 2) AS Province,
        LEFT(COALESCE(party.CompanyPostalCode, ''), 7) AS PostalCode,
        LEFT(COALESCE(party.Phone, ''), 30) AS Telephone,
        LEFT(COALESCE(party.Email, ''), 254) AS Email
    FROM Customer AS customer
    LEFT JOIN Party AS party ON party.Id = customer.PartyId
    WHERE EXISTS (SELECT 1 FROM DonationInvoiceHeader WHERE DonorId = customer.Id)
) AS source ON 1 = 0
WHEN NOT MATCHED THEN INSERT
    (DonorType, FirstName, LastName, CompanyName, Street, Province, PostalCode, Telephone, Email)
VALUES ('Company', '', '', source.CompanyName, source.Street, source.Province,
    source.PostalCode, source.Telephone, source.Email)
OUTPUT source.CustomerId, inserted.Id INTO @DonorMap;

UPDATE donation SET DonorId = mapping.DonorId
FROM DonationInvoiceHeader AS donation
INNER JOIN @DonorMap AS mapping ON donation.DonorId = mapping.CustomerId;

INSERT INTO Account
    (AccountCode, AccountName, AccountClassId, ParentAccountId, CompanyId, IsCash, IsContraAccount, DrOrCrSide)
SELECT '41000', 'Donations', revenue.AccountClassId, revenue.Id, revenue.CompanyId, 0, 0, revenue.DrOrCrSide
FROM Account AS revenue
WHERE revenue.AccountCode = '40000'
AND NOT EXISTS (SELECT 1 FROM Account WHERE AccountCode = '41000' AND CompanyId = revenue.CompanyId);

INSERT INTO Account
    (AccountCode, AccountName, AccountClassId, ParentAccountId, CompanyId, IsCash, IsContraAccount, DrOrCrSide)
SELECT seed.AccountCode, seed.AccountName, parent.AccountClassId, parent.Id, parent.CompanyId, 0, 0, parent.DrOrCrSide
FROM Account AS parent
CROSS JOIN (VALUES ('41001', 'General Purpose'), ('41200', 'Sunday School'),
    ('41250', 'Brothers In Christ')) AS seed(AccountCode, AccountName)
WHERE parent.AccountCode = '41000'
AND NOT EXISTS (SELECT 1 FROM Account WHERE AccountCode = seed.AccountCode AND CompanyId = parent.CompanyId);

UPDATE DonationInvoiceHeader
SET PaymentType = ReferenceNo
WHERE ReferenceNo IN ('Cash', 'Cheque', 'Credit Card', 'Debit Card', 'E-Transfer');
");

            migrationBuilder.AddForeignKey(
                name: "FK_DonationInvoiceHeader_Account_GlAccountId",
                table: "DonationInvoiceHeader",
                column: "GlAccountId",
                principalTable: "Account",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_DonationInvoiceHeader_Donors_DonorId",
                table: "DonationInvoiceHeader",
                column: "DonorId",
                principalTable: "Donors",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Donation donor links cannot safely be changed back to customer links. Restore a database backup to roll back this migration.");
        }
    }
}
