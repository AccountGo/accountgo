using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations.ApiDb
{
    /// <inheritdoc />
    public partial class RetireLegacyVat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE customer
SET TaxGroupId = gst.Id
FROM Customer AS customer
INNER JOIN TaxGroup AS legacy ON customer.TaxGroupId = legacy.Id
CROSS APPLY (
    SELECT TOP (1) Id
    FROM TaxGroup
    WHERE Description = 'GST/HST' AND IsActive = 1
    ORDER BY Id
) AS gst
WHERE legacy.Description = 'VAT';

UPDATE vendor
SET TaxGroupId = gst.Id
FROM Vendor AS vendor
INNER JOIN TaxGroup AS legacy ON vendor.TaxGroupId = legacy.Id
CROSS APPLY (
    SELECT TOP (1) Id
    FROM TaxGroup
    WHERE Description = 'GST/HST' AND IsActive = 1
    ORDER BY Id
) AS gst
WHERE legacy.Description = 'VAT';

UPDATE TaxGroup SET IsActive = 0 WHERE Description = 'VAT';
UPDATE Tax SET IsActive = 0 WHERE TaxCode = 'VAT';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
    UPDATE TaxGroup SET IsActive = 1 WHERE Description = 'VAT';
    UPDATE Tax SET IsActive = 1 WHERE TaxCode = 'VAT';");
        }
    }
}
