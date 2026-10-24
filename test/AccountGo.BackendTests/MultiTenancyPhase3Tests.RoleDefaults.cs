#nullable disable
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AccountGo.BackendTests
{
    // Role defaults reach production only through D1 migrations: start-up writes never leave the container.
    public partial class MultiTenancyPhase3Tests
    {
        [Fact]
        public void RoleDefaultsMigration_GivesBuiltInRolesTheDefaultsStartUpApplies()
        {
            using var source = new SqliteConnection("Data Source=" + _path);
            using var replica = new SqliteConnection("Data Source=:memory:");
            source.Open();
            replica.Open();
            source.BackupDatabase(replica);
            // D1 refuses compound SELECTs with more than a few terms (UNION ALL lists); local SQLite allows 500.
            SQLitePCL.raw.sqlite3_limit(replica.Handle, SQLitePCL.raw.SQLITE_LIMIT_COMPOUND_SELECT, 1);
            using var command = replica.CreateCommand();

            // The test database got its defaults at start-up; D1 is missing those added later (Products & Services).
            var expected = RolePermissions(command);
            command.CommandText = @"
                DELETE FROM ""SecurityRolePermission"" WHERE ""SecurityPermissionId"" IN
                    (SELECT ""Id"" FROM ""SecurityPermission"" WHERE (""Name"" LIKE 'inventory.%' AND ""Name"" != 'inventory.stock.adjust') OR ""Name"" = 'purchases.payments.create');
                DELETE FROM ""SecurityPermission"" WHERE (""Name"" LIKE 'inventory.%' AND ""Name"" != 'inventory.stock.adjust') OR ""Name"" = 'purchases.payments.create';";
            command.ExecuteNonQuery();
            Assert.NotEqual(expected, RolePermissions(command));

            // When a role default changes, add a migration like this one and run it here too.
            var migration = File.ReadAllText(Path.Combine(RepoRoot(), "src/Api/d1/migrations/0013_role_defaults.sql"));
            command.CommandText = migration;
            command.ExecuteNonQuery();
            Assert.Equal(expected, RolePermissions(command));

            command.CommandText = migration;
            command.ExecuteNonQuery();
            Assert.Equal(expected, RolePermissions(command));
        }

        [Fact]
        public void StockPermissionMigration_GrantsAdjustToRolesThatManageItems_AndIsIdempotent()
        {
            using (var source = new SqliteConnection("Data Source=" + _path))
            using (var replica = new SqliteConnection("Data Source=:memory:"))
            {
                source.Open();
                replica.Open();
                source.BackupDatabase(replica);
                SQLitePCL.raw.sqlite3_limit(replica.Handle, SQLitePCL.raw.SQLITE_LIMIT_COMPOUND_SELECT, 1);
                using var command = replica.CreateCommand();

                var expected = RolePermissions(command);
                Assert.Contains("Accountant / inventory.stock.adjust", expected);
                command.CommandText = @"
                    DELETE FROM ""SecurityRolePermission"" WHERE ""SecurityPermissionId"" IN
                        (SELECT ""Id"" FROM ""SecurityPermission"" WHERE ""Name"" = 'inventory.stock.adjust');
                    DELETE FROM ""SecurityPermission"" WHERE ""Name"" = 'inventory.stock.adjust';";
                command.ExecuteNonQuery();
                Assert.NotEqual(expected, RolePermissions(command));

                var migration = File.ReadAllText(Path.Combine(RepoRoot(), "src/Api/d1/migrations/0015_stock_permission.sql"));
                command.CommandText = migration;
                command.ExecuteNonQuery();
                Assert.Equal(expected, RolePermissions(command));

                command.CommandText = migration;
                command.ExecuteNonQuery();
                Assert.Equal(expected, RolePermissions(command));
            }
        }

        private static SortedSet<string> RolePermissions(SqliteCommand command)
        {
            command.CommandText = @"SELECT r.""Name"" || ' / ' || p.""Name"" FROM ""SecurityRolePermission"" rp
                JOIN ""SecurityRole"" r ON r.""Id"" = rp.""SecurityRoleId""
                JOIN ""SecurityPermission"" p ON p.""Id"" = rp.""SecurityPermissionId""";
            var pairs = new SortedSet<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                pairs.Add(reader.GetString(0));
            return pairs;
        }
    }
}
