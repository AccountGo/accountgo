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
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Upgrade_Phase2Schema_PreservesExistingRowsAndBackfillsCurrency(bool applyD1Migration)
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
                        ALTER TABLE CompanyInvitation DROP COLUMN ResendCount;
                        ALTER TABLE CompanyInvitation DROP COLUMN ResendWindowStart;";
                    command.ExecuteNonQuery();
                    if (applyD1Migration)
                    {
                        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
                        command.CommandText = File.ReadAllText(Path.Combine(root, "src/Api/d1/migrations/0002_workspace_lifecycle.sql"));
                        command.ExecuteNonQuery();
                    }
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
                    command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('GeneralLedgerHeader') WHERE name IN ('SourceDocumentId','ReversedGeneralLedgerHeaderId')";
                    Assert.Equal(2L, command.ExecuteScalar());
                    command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('CustomerAllocation') WHERE name = 'GeneralLedgerHeaderId'";
                    Assert.Equal(1L, command.ExecuteScalar());
                }
            }
        }
    }
}
