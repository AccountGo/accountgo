using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using Api.Data;
using Api.Data.Schema;
using Core.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace AccountGo.ServicesTests;

public class MultiTenancyPhase1Tests
{
    [Fact]
    public void All_Tenant_Entities_Implement_ICompanyOwned_And_Have_CompanyId()
    {
        var coreAssembly = typeof(ICompanyOwned).Assembly;
        var types = coreAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(BaseEntity).IsAssignableFrom(t))
            .ToList();

        var globalEntityNames = new HashSet<string>
        {
            "Company",
            "AccountClass",
            "SecurityRole",
            "SecurityRolePermission",
            "SecurityPermission",
            "SecurityGroup",
            "SystemModule",
            "User",
            "AuditableEntity",
            "AuditableAttribute",
            "SecurityUserRole",
            "CompanyMember",
            "CompanyMemberSecurityRole",
            "CompanyInvitation"
        };

        foreach (var type in types)
        {
            if (globalEntityNames.Contains(type.Name))
            {
                Assert.False(typeof(ICompanyOwned).IsAssignableFrom(type),
                    $"Global entity {type.Name} should NOT implement ICompanyOwned.");
            }
            else
            {
                Assert.True(typeof(ICompanyOwned).IsAssignableFrom(type),
                    $"Tenant entity {type.Name} must implement ICompanyOwned.");

                var companyIdProp = type.GetProperty("CompanyId");
                Assert.NotNull(companyIdProp);
                Assert.Equal(typeof(int), companyIdProp.PropertyType);
            }
        }
    }

    [Fact]
    public void SqliteSchemaUpgrader_Upgrades_AccountGoDb_And_Ensures_CompanyId_On_All_Tenant_Tables()
    {
        var dbPath = Path.Combine(AppContext.BaseDirectory, "../../../../../src/Api/accountgo.db");
        if (!File.Exists(dbPath))
        {
            dbPath = Path.GetFullPath("src/Api/accountgo.db");
        }

        Assert.True(File.Exists(dbPath), $"Target database not found at {dbPath}");

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        // Run upgrader
        SqliteSchemaUpgrader.Upgrade(connection);

        // Verify every tenant table has CompanyId
        foreach (var table in SqliteSchemaUpgrader.TenantTables)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='CompanyId';";
            var count = Convert.ToInt32(cmd.ExecuteScalar());
            Assert.True(count == 1, $"Table {table} must have CompanyId column, found {count}");
        }

        // Verify Company table has CreatedAt and CreatedByUserId
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Company') WHERE name='CreatedAt';";
            Assert.Equal(1, Convert.ToInt32(cmd.ExecuteScalar()));
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Company') WHERE name='CreatedByUserId';";
            Assert.Equal(1, Convert.ToInt32(cmd.ExecuteScalar()));
        }

        // Verify Tenancy tables exist
        var tenancyTables = new[] { "CompanyMember", "CompanyMemberSecurityRole", "CompanyInvitation" };
        foreach (var table in tenancyTables)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';";
            Assert.Equal(1, Convert.ToInt32(cmd.ExecuteScalar()));
        }

        // Verify composite unique indexes exist
        var expectedIndexes = new[]
        {
            "IX_Account_CompanyId_AccountCode",
            "IX_SequenceNumber_CompanyId_SequenceNumberType",
            "IX_Customer_CompanyId_No",
            "IX_Vendor_CompanyId_No",
            "IX_Item_CompanyId_No",
            "IX_CompanyMember_CompanyId_UserId",
            "IX_CompanyMemberSecurityRole_CompanyMemberId_SecurityRoleId",
            "IX_CompanyInvitation_TokenHash"
        };
        foreach (var idx in expectedIndexes)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='{idx}';";
            Assert.True(Convert.ToInt32(cmd.ExecuteScalar()) >= 1, $"Index {idx} must exist in sqlite_master");
        }
    }

    [Fact]
    public void Backfill_Assigns_Correct_Roles_Per_Decisions_D1_And_D2()
    {
        var dbPath = Path.Combine(AppContext.BaseDirectory, "../../../../../src/Api/accountgo.db");
        if (!File.Exists(dbPath))
        {
            dbPath = Path.GetFullPath("src/Api/accountgo.db");
        }

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        SqliteSchemaUpgrader.Upgrade(connection);

        // Verify marvinperez@gmail.com is Owner (Role = 1)
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT cm.Role, cm.Status
                FROM CompanyMember cm
                JOIN AspNetUsers u ON u.Id = cm.UserId
                WHERE cm.CompanyId = 1 AND LOWER(u.Email) = 'marvinperez@gmail.com';";
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read(), "marvinperez@gmail.com should be a member of Company 1");
            Assert.Equal(1, reader.GetInt32(0)); // Owner
            Assert.Equal(1, reader.GetInt32(1)); // Active
        }

        // Verify hello@marvinperez.com is Admin (Role = 2)
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT cm.Role, cm.Status
                FROM CompanyMember cm
                JOIN AspNetUsers u ON u.Id = cm.UserId
                WHERE cm.CompanyId = 1 AND LOWER(u.Email) = 'hello@marvinperez.com';";
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read(), "hello@marvinperez.com should be a member of Company 1");
            Assert.Equal(2, reader.GetInt32(0)); // Admin
            Assert.Equal(1, reader.GetInt32(1)); // Active
        }

        // Verify admin@accountgo.ph is Member (Role = 3) with Auditor / Viewer role
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT cm.Role, cm.Status, sr.Name
                FROM CompanyMember cm
                JOIN AspNetUsers u ON u.Id = cm.UserId
                LEFT JOIN CompanyMemberSecurityRole cmsr ON cmsr.CompanyMemberId = cm.Id
                LEFT JOIN SecurityRole sr ON sr.Id = cmsr.SecurityRoleId
                WHERE cm.CompanyId = 1 AND LOWER(u.Email) = 'admin@accountgo.ph';";
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read(), "admin@accountgo.ph should be a member of Company 1");
            Assert.Equal(3, reader.GetInt32(0)); // Member
            Assert.Equal(1, reader.GetInt32(1)); // Active
            Assert.Equal("Auditor / Viewer", reader.GetString(2));
        }

        // Verify only marvinperez@gmail.com retains SystemAdministrators
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT u.UserName
                FROM SecurityUserRole ur
                JOIN SecurityRole sr ON sr.Id = ur.SecurityRoleId
                JOIN User u ON u.Id = ur.UserId
                WHERE sr.Name = 'SystemAdministrators';";
            using var reader = cmd.ExecuteReader();
            var sysAdmins = new List<string>();
            while (reader.Read())
            {
                sysAdmins.Add(reader.GetString(0));
            }
            Assert.Single(sysAdmins);
            Assert.Equal("marvinperez@gmail.com", sysAdmins[0], ignoreCase: true);
        }
    }

    [Fact]
    public void Fresh_Database_Schema_Matches_Upgraded_Database_Schema()
    {
        // 1. Upgraded DB (accountgo.db copy)
        var tempUpgradedDb = Path.GetTempFileName();
        var tempFreshDb = Path.GetTempFileName();

        try
        {
            var sourceDbPath = Path.Combine(AppContext.BaseDirectory, "../../../../../src/Api/accountgo.db");
            if (!File.Exists(sourceDbPath))
            {
                sourceDbPath = Path.GetFullPath("src/Api/accountgo.db");
            }
            File.Copy(sourceDbPath, tempUpgradedDb, overwrite: true);

            using (var connUpgraded = new SqliteConnection($"Data Source={tempUpgradedDb}"))
            {
                connUpgraded.Open();
                SqliteSchemaUpgrader.Upgrade(connUpgraded);
            }

            // 2. Fresh DB created via EF Core and then upgraded
            var options = new DbContextOptionsBuilder<ApiDbContext>()
                .UseSqlite($"Data Source={tempFreshDb}")
                .Options;

            using (var freshContext = new ApiDbContext(options))
            {
                var creator = (RelationalDatabaseCreator)freshContext.GetService<IRelationalDatabaseCreator>();
                creator.Create();
                creator.CreateTables();

                var connFresh = freshContext.Database.GetDbConnection();
                SqliteSchemaUpgrader.Upgrade(connFresh);
            }

            // 3. Compare tables in both databases
            var upgradedTables = GetTableNames(tempUpgradedDb);
            var freshTables = GetTableNames(tempFreshDb);

            // Fresh database contains the business tables of ApiDbContext
            // Verify every ApiDbContext table in fresh DB also exists in upgraded DB with identical columns
            foreach (var table in freshTables)
            {
                if (table.StartsWith("sqlite_") || table.StartsWith("__")) continue;

                Assert.Contains(table, upgradedTables);

                var freshColumns = GetColumns(tempFreshDb, table);
                var upgradedColumns = GetColumns(tempUpgradedDb, table);

                foreach (var col in freshColumns)
                {
                    Assert.True(upgradedColumns.ContainsKey(col.Key),
                        $"Upgraded table {table} is missing column {col.Key} present in fresh database.");
                }

                // Verify CompanyId presence if tenant table
                if (SqliteSchemaUpgrader.TenantTables.Contains(table))
                {
                    Assert.True(freshColumns.ContainsKey("companyid"),
                        $"Fresh table {table} must have CompanyId column.");
                    Assert.True(upgradedColumns.ContainsKey("companyid"),
                        $"Upgraded table {table} must have CompanyId column.");
                }
            }
        }
        finally
        {
            if (File.Exists(tempUpgradedDb)) File.Delete(tempUpgradedDb);
            if (File.Exists(tempFreshDb)) File.Delete(tempFreshDb);
        }
    }

    private static List<string> GetTableNames(string dbPath)
    {
        var tables = new List<string>();
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }
        return tables;
    }

    private static Dictionary<string, string> GetColumns(string dbPath, string tableName)
    {
        var cols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info('{tableName}');";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(1);
            var type = reader.GetString(2);
            cols[name] = type;
        }
        return cols;
    }
}
