using System;
using System.IO;
using Api.Data;
using Api.Data.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AccountGo.ServicesTests
{
    public class WorkspaceLifecycleSchemaTests
    {
        [Fact]
        public void Upgrade_Phase2Schema_PreservesExistingRowsAndBackfillsCurrency()
        {
            using (var connection = new SqliteConnection("Data Source=:memory:"))
            {
                connection.Open();
                using (var db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options))
                    db.Database.EnsureCreated();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        INSERT INTO CompanySetting (CompanyId, Currency) VALUES (1, 'PHP');
                        ALTER TABLE Company DROP COLUMN DeletedAt;
                        ALTER TABLE CompanySetting DROP COLUMN Currency;
                        ALTER TABLE CompanySetting DROP COLUMN ReportingFramework;
                        ALTER TABLE CompanySetting DROP COLUMN TaxRegime;
                        ALTER TABLE CompanySetting DROP COLUMN FiscalYearPattern;
                        ALTER TABLE CompanySetting DROP COLUMN FiscalYearStartMonth;
                        ALTER TABLE CompanySetting DROP COLUMN BooksStartDate;
                        ALTER TABLE CompanySetting DROP COLUMN IndustryTemplate;
                        ALTER TABLE CompanySetting DROP COLUMN InventoryCostingMethod;
                        ALTER TABLE CompanySetting DROP COLUMN CoaTemplateId;
                        ALTER TABLE CompanySetting DROP COLUMN OnboardingChecklistDismissedAt;
                        ALTER TABLE CompanySetting DROP COLUMN OnboardingChecklistJson;
                        ALTER TABLE CompanyInvitation DROP COLUMN ResendCount;
                        ALTER TABLE CompanyInvitation DROP COLUMN ResendWindowStart;";
                    command.ExecuteNonQuery();
                    SqliteSchemaUpgrader.Upgrade(connection);
                    SqliteSchemaUpgrader.Upgrade(connection);
                    command.CommandText = "SELECT Currency FROM CompanySetting WHERE CompanyId=1";
                    Assert.Equal("PHP", command.ExecuteScalar());
                    command.CommandText = "SELECT Name FROM Company WHERE Id=1 AND DeletedAt IS NULL";
                    Assert.Equal("Financial Solutions Inc.", command.ExecuteScalar());
                    command.CommandText = "SELECT COUNT(*) FROM Company";
                    Assert.Equal(1L, command.ExecuteScalar());
                    command.CommandText = "SELECT COUNT(*) FROM Donors";
                    Assert.Equal(2L, command.ExecuteScalar());
                    command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('CompanyInvitation') WHERE name IN ('ResendCount','ResendWindowStart')";
                    Assert.Equal(2L, command.ExecuteScalar());
                    command.CommandText = "SELECT AccountingBasis FROM CompanySetting WHERE CompanyId=1";
                    Assert.Equal(1L, command.ExecuteScalar());
                    command.CommandText = "SELECT ReportingFramework FROM CompanySetting WHERE CompanyId=1";
                    Assert.Equal(1L, command.ExecuteScalar());
                    command.CommandText = "SELECT TaxRegime FROM CompanySetting WHERE CompanyId=1";
                    Assert.Equal(0L, command.ExecuteScalar());
                    command.CommandText = "SELECT FiscalYearPattern FROM CompanySetting WHERE CompanyId=1";
                    Assert.Equal(1L, command.ExecuteScalar());
                    command.CommandText = "SELECT IndustryTemplate FROM CompanySetting WHERE CompanyId=1";
                    Assert.Equal(1L, command.ExecuteScalar());
                    command.CommandText = "SELECT InventoryCostingMethod FROM CompanySetting WHERE CompanyId=1";
                    Assert.Equal(0L, command.ExecuteScalar());
                    command.CommandText = "SELECT CoaTemplateId FROM CompanySetting WHERE CompanyId=1";
                    Assert.Equal("legacy-default", command.ExecuteScalar());
                    command.CommandText = @"
                        SELECT COALESCE(
                            (SELECT date(f.StartDate) FROM FinancialYear f
                             WHERE f.CompanyId = CompanySetting.CompanyId
                             ORDER BY f.IsActive DESC, f.StartDate LIMIT 1),
                            (SELECT date(c.CreatedAt) FROM Company c WHERE c.Id = CompanySetting.CompanyId),
                            '1970-01-01') = date(BooksStartDate)
                        FROM CompanySetting WHERE CompanyId=1";
                    Assert.Equal(1L, command.ExecuteScalar());
                    command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('GeneralLedgerHeader') WHERE name IN ('SourceDocumentId','ReversedGeneralLedgerHeaderId')";
                    Assert.Equal(2L, command.ExecuteScalar());
                    command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('CustomerAllocation') WHERE name = 'GeneralLedgerHeaderId'";
                    Assert.Equal(1L, command.ExecuteScalar());
                }
            }
        }
    }
}
