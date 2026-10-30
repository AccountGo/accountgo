using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace Api.Data.Postgres
{
    /// <summary>
    /// One-time copy of a SQLite database (a D1 export loaded with sqlite3) into an empty, migrated Postgres database:
    /// <c>dotnet Api.dll --copy-from-sqlite /path/to/d1.db</c> with <c>DatabaseProvider=Postgres</c>.
    /// Runs in one transaction, refuses a target that already has rows, and resets identity sequences afterwards.
    /// </summary>
    public static class SqliteToPostgresCopy
    {
        // Bookkeeping tables of the D1/SQLite path; EF owns __EFMigrationsHistory on the Postgres side.
        private static readonly HashSet<string> SkippedTables = new(StringComparer.OrdinalIgnoreCase)
        {
            "d1_migrations", "__SchemaVersion", "__EFMigrationsHistory",
        };

        private sealed record TargetColumn(string Name, string DataType);

        public static int Run(IServiceProvider services, string sqlitePath)
        {
            using var scope = services.CreateScope();
            var apiDb = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var identityDb = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            if (!apiDb.Database.IsNpgsql())
            {
                Console.Error.WriteLine("✗ --copy-from-sqlite needs DatabaseProvider=Postgres.");
                return 2;
            }

            Console.WriteLine("Applying Postgres migrations...");
            apiDb.Database.Migrate();
            identityDb.Database.Migrate();

            var sourceBuilder = new SqliteConnectionStringBuilder { DataSource = sqlitePath, Mode = SqliteOpenMode.ReadOnly };
            using var source = new SqliteConnection(sourceBuilder.ConnectionString);
            source.Open();

            using var target = new NpgsqlConnection(apiDb.Database.GetConnectionString());
            target.Open();

            var sourceTables = ListSourceTables(source);
            var targetTables = ListTargetTables(target);

            var missing = sourceTables.Where(t => !targetTables.ContainsKey(t)).ToList();
            var nonEmptyMissing = missing.Where(t => CountSource(source, t) > 0).ToList();
            if (nonEmptyMissing.Count > 0)
            {
                Console.Error.WriteLine($"✗ Source tables with rows have no Postgres table: {string.Join(", ", nonEmptyMissing)}");
                return 3;
            }
            foreach (var table in missing)
                Console.WriteLine($"  skip {table}: no Postgres table (empty in source)");

            var tables = sourceTables.Where(targetTables.ContainsKey).ToList();
            // Migrations insert the model's HasData rows (e.g. Company 1); the source has those rows too.
            var seededTables = SeededTables(apiDb).Concat(SeededTables(identityDb)).ToHashSet(StringComparer.Ordinal);
            var occupied = tables.Where(t => !seededTables.Contains(t) && CountTarget(target, t) > 0).ToList();
            if (occupied.Count > 0)
            {
                Console.Error.WriteLine($"✗ Target tables already have rows: {string.Join(", ", occupied)}. Copy into an empty database.");
                return 4;
            }

            var foreignKeys = ListForeignKeys(target);
            SetForeignKeysDeferrable(target, foreignKeys, deferrable: true);
            try
            {
                using var tx = target.BeginTransaction();
                using (var defer = new NpgsqlCommand("SET CONSTRAINTS ALL DEFERRED", target, tx))
                    defer.ExecuteNonQuery();

                foreach (var table in tables.Where(seededTables.Contains))
                {
                    using var clear = new NpgsqlCommand($"DELETE FROM {Quote(table)};", target, tx);
                    clear.ExecuteNonQuery();
                }

                foreach (var table in tables)
                {
                    var copied = CopyTable(source, target, table, targetTables[table]);
                    Console.WriteLine($"  {table}: {copied} rows");
                }

                ResetIdentitySequences(target, tx, tables);
                tx.Commit();
            }
            finally
            {
                SetForeignKeysDeferrable(target, foreignKeys, deferrable: false);
            }

            var mismatches = tables
                .Select(t => (Table: t, Source: CountSource(source, t), Target: CountTarget(target, t)))
                .Where(c => c.Source != c.Target)
                .ToList();
            foreach (var m in mismatches)
                Console.Error.WriteLine($"✗ {m.Table}: source {m.Source} rows, Postgres {m.Target}");
            if (mismatches.Count > 0)
                return 5;

            Console.WriteLine($"✓ Copied {tables.Count} tables; row counts match.");
            return 0;
        }

        private static IEnumerable<string> SeededTables(DbContext context) =>
            // HasData lives on the design-time model; the runtime model drops it.
            context.GetService<IDesignTimeModel>().Model.GetEntityTypes()
                .Where(e => e.GetSeedData().Any())
                .Select(e => e.GetTableName())
                .Where(t => t != null)
                .Select(t => t!);

        private static List<string> ListSourceTables(SqliteConnection source)
        {
            var tables = new List<string>();
            using var cmd = source.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '_cf_%' ORDER BY name;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                if (!SkippedTables.Contains(name))
                    tables.Add(name);
            }
            return tables;
        }

        private static Dictionary<string, Dictionary<string, TargetColumn>> ListTargetTables(NpgsqlConnection target)
        {
            var tables = new Dictionary<string, Dictionary<string, TargetColumn>>(StringComparer.Ordinal);
            using var cmd = new NpgsqlCommand(
                "SELECT table_name, column_name, data_type FROM information_schema.columns WHERE table_schema = 'public';", target);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var table = reader.GetString(0);
                if (SkippedTables.Contains(table))
                    continue;
                if (!tables.TryGetValue(table, out var columns))
                    tables[table] = columns = new Dictionary<string, TargetColumn>(StringComparer.Ordinal);
                columns[reader.GetString(1)] = new TargetColumn(reader.GetString(1), reader.GetString(2));
            }
            return tables;
        }

        private static long CountSource(SqliteConnection source, string table)
        {
            using var cmd = source.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {Quote(table)};";
            return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private static long CountTarget(NpgsqlConnection target, string table)
        {
            using var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {Quote(table)};", target);
            return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private static List<(string Table, string Constraint)> ListForeignKeys(NpgsqlConnection target)
        {
            var keys = new List<(string, string)>();
            using var cmd = new NpgsqlCommand(@"
                SELECT c.conrelid::regclass::text, c.conname
                FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace
                WHERE c.contype = 'f' AND n.nspname = 'public';", target);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                keys.Add((reader.GetString(0), reader.GetString(1)));
            return keys;
        }

        // Owner privileges are enough for this, unlike session_replication_role, which hosted Postgres may not grant.
        private static void SetForeignKeysDeferrable(NpgsqlConnection target, List<(string Table, string Constraint)> keys, bool deferrable)
        {
            if (keys.Count == 0)
                return;
            var mode = deferrable ? "DEFERRABLE INITIALLY IMMEDIATE" : "NOT DEFERRABLE";
            var sql = string.Join("\n", keys.Select(k => $"ALTER TABLE {k.Table} ALTER CONSTRAINT {Quote(k.Constraint)} {mode};"));
            using var cmd = new NpgsqlCommand(sql, target);
            cmd.ExecuteNonQuery();
        }

        private static long CopyTable(SqliteConnection source, NpgsqlConnection target, string table,
            Dictionary<string, TargetColumn> targetColumns)
        {
            using var read = source.CreateCommand();
            read.CommandText = $"SELECT * FROM {Quote(table)};";
            using var reader = read.ExecuteReader();

            var columns = new List<TargetColumn>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                if (!targetColumns.TryGetValue(name, out var column))
                    throw new InvalidOperationException($"{table}.{name} exists in SQLite but not in Postgres.");
                columns.Add(column);
            }

            var columnList = string.Join(", ", columns.Select(c => Quote(c.Name)));
            long rows = 0;
            using (var writer = target.BeginBinaryImport($"COPY {Quote(table)} ({columnList}) FROM STDIN (FORMAT BINARY)"))
            {
                while (reader.Read())
                {
                    writer.StartRow();
                    for (int i = 0; i < columns.Count; i++)
                    {
                        var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        if (value == null)
                        {
                            writer.WriteNull();
                            continue;
                        }
                        try
                        {
                            WriteValue(writer, value, columns[i].DataType);
                        }
                        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
                        {
                            throw new InvalidOperationException(
                                $"{table}.{columns[i].Name}: cannot convert '{value}' to {columns[i].DataType}.", ex);
                        }
                    }
                    rows++;
                }
                writer.Complete();
            }
            return rows;
        }

        internal static void WriteValue(NpgsqlBinaryImporter writer, object value, string dataType)
        {
            switch (dataType)
            {
                case "boolean":
                    writer.Write(ToBoolean(value), NpgsqlDbType.Boolean);
                    break;
                case "integer":
                    writer.Write(Convert.ToInt32(value, CultureInfo.InvariantCulture), NpgsqlDbType.Integer);
                    break;
                case "bigint":
                    writer.Write(Convert.ToInt64(value, CultureInfo.InvariantCulture), NpgsqlDbType.Bigint);
                    break;
                case "smallint":
                    writer.Write(Convert.ToInt16(value, CultureInfo.InvariantCulture), NpgsqlDbType.Smallint);
                    break;
                case "numeric":
                    writer.Write(ToDecimal(value), NpgsqlDbType.Numeric);
                    break;
                case "double precision":
                    writer.Write(Convert.ToDouble(value, CultureInfo.InvariantCulture), NpgsqlDbType.Double);
                    break;
                case "real":
                    writer.Write(Convert.ToSingle(value, CultureInfo.InvariantCulture), NpgsqlDbType.Real);
                    break;
                case "timestamp without time zone":
                    writer.Write(ToDateTime(value), NpgsqlDbType.Timestamp);
                    break;
                case "date":
                    writer.Write(ToDateTime(value).Date, NpgsqlDbType.Date);
                    break;
                case "timestamp with time zone":
                    writer.Write(ToDateTimeOffset(value).UtcDateTime, NpgsqlDbType.TimestampTz);
                    break;
                case "uuid":
                    writer.Write(value is byte[] bytes ? new Guid(bytes) : Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!), NpgsqlDbType.Uuid);
                    break;
                case "bytea":
                    writer.Write(value is byte[] blob ? blob : Convert.FromBase64String(Convert.ToString(value, CultureInfo.InvariantCulture)!), NpgsqlDbType.Bytea);
                    break;
                case "text":
                case "character varying":
                    writer.Write(Convert.ToString(value, CultureInfo.InvariantCulture)!, NpgsqlDbType.Text);
                    break;
                default:
                    throw new InvalidCastException($"Unhandled Postgres type '{dataType}'.");
            }
        }

        internal static bool ToBoolean(object value) => value switch
        {
            long l => l != 0,
            int i => i != 0,
            bool b => b,
            string s => s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase),
            _ => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
        };

        internal static decimal ToDecimal(object value) => value switch
        {
            string s => decimal.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture),
            double d => (decimal)d,
            _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
        };

        // EF's SQLite provider writes "yyyy-MM-dd HH:mm:ss.FFFFFFF"; restores and raw SQL also left ISO 8601 ("o").
        internal static DateTime ToDateTime(object value)
        {
            if (value is DateTime dt)
                return dt;
            var text = Convert.ToString(value, CultureInfo.InvariantCulture)!;
            // A value with an offset is a UTC instant; the container (TZ=UTC) read it as UTC too.
            var styles = HasOffset(text) ? DateTimeStyles.AdjustToUniversal : DateTimeStyles.None;
            return DateTime.SpecifyKind(DateTime.Parse(text, CultureInfo.InvariantCulture, styles), DateTimeKind.Unspecified);
        }

        internal static DateTimeOffset ToDateTimeOffset(object value)
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture)!;
            return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        }

        private static bool HasOffset(string text) =>
            text.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
            || (text.Length > 6 && (text[^6] == '+' || text[^6] == '-') && text[^3] == ':');

        private static void ResetIdentitySequences(NpgsqlConnection target, NpgsqlTransaction tx, IEnumerable<string> tables)
        {
            foreach (var table in tables)
            {
                using var find = new NpgsqlCommand(@"
                    SELECT a.attname FROM pg_attribute a
                    WHERE a.attrelid = @table::regclass AND a.attidentity <> '' AND NOT a.attisdropped;", target, tx);
                find.Parameters.AddWithValue("table", Quote(table));
                var identityColumns = new List<string>();
                using (var reader = find.ExecuteReader())
                {
                    while (reader.Read())
                        identityColumns.Add(reader.GetString(0));
                }

                foreach (var column in identityColumns)
                {
                    using var reset = new NpgsqlCommand($@"
                        SELECT setval(pg_get_serial_sequence(@table, @column),
                                      COALESCE((SELECT MAX({Quote(column)}) FROM {Quote(table)}), 0) + 1, false);", target, tx);
                    reset.Parameters.AddWithValue("table", Quote(table));
                    reset.Parameters.AddWithValue("column", column);
                    reset.ExecuteNonQuery();
                }
            }
        }

        private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }
}
