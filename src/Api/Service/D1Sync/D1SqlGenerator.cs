using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Api.Service.D1Sync
{
    public static class D1SqlGenerator
    {
        /// <summary>
        /// Snapshots pending changes before SaveChanges. EF resets entry states to Unchanged/Detached
        /// after a save, so states must be recorded first. DELETE statements are generated immediately
        /// (key values are known); INSERT OR REPLACE statements are generated after the save so that
        /// store-generated keys are included.
        /// </summary>
        public static D1PendingChanges Capture(ChangeTracker changeTracker)
        {
            var pending = new D1PendingChanges();

            foreach (var entry in changeTracker.Entries())
            {
                switch (entry.State)
                {
                    case EntityState.Deleted:
                        try
                        {
                            pending.AddDelete(GenerateSql(entry, EntityState.Deleted));
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[D1Sync] Failed generating DELETE for {entry.Metadata.Name}: {ex.Message}");
                        }
                        break;
                    case EntityState.Added:
                    case EntityState.Modified:
                        pending.AddUpsert(entry);
                        break;
                }
            }

            return pending;
        }

        public static string? GenerateSql(EntityEntry entry, EntityState state)
        {
            if (state != EntityState.Added && state != EntityState.Modified && state != EntityState.Deleted)
                return null;

            var entityType = entry.Metadata;
            var tableName = entityType.GetTableName();
            if (string.IsNullOrEmpty(tableName))
                return null;

            if (state == EntityState.Deleted)
            {
                var pk = entityType.FindPrimaryKey();
                if (pk == null || pk.Properties.Count == 0)
                    return null;

                var whereClauses = new List<string>();
                foreach (var prop in pk.Properties)
                {
                    var colName = prop.GetColumnName() ?? prop.Name;
                    var val = FormatValue(prop, entry.Property(prop.Name).OriginalValue);
                    whereClauses.Add($"\"{colName}\" = {val}");
                }

                return $"DELETE FROM \"{tableName}\" WHERE {string.Join(" AND ", whereClauses)};";
            }

            // Added or Modified: generate INSERT OR REPLACE INTO
            var columns = new List<string>();
            var values = new List<string>();

            foreach (var prop in entityType.GetProperties())
            {
                var colName = prop.GetColumnName() ?? prop.Name;
                var propEntry = entry.Property(prop.Name);
                var val = FormatValue(prop, propEntry.CurrentValue);

                columns.Add($"\"{colName}\"");
                values.Add(val);
            }

            if (columns.Count == 0)
                return null;

            return $"INSERT OR REPLACE INTO \"{tableName}\" ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)});";
        }

        private static string FormatValue(IProperty prop, object? val)
        {
            if (val == null || val == DBNull.Value)
                return "NULL";

            // Use EF's own SQLite type mapping so the literal matches what EF writes locally
            // (enums as integers, full decimal precision, fractional seconds on dates).
            try
            {
                return prop.GetRelationalTypeMapping().GenerateSqlLiteral(val);
            }
            catch
            {
                return FormatValueFallback(val);
            }
        }

        private static string FormatValueFallback(object val)
        {
            return val switch
            {
                bool b => b ? "1" : "0",
                Enum e => Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                int i => i.ToString(CultureInfo.InvariantCulture),
                long l => l.ToString(CultureInfo.InvariantCulture),
                short s => s.ToString(CultureInfo.InvariantCulture),
                byte by => by.ToString(CultureInfo.InvariantCulture),
                decimal d => d.ToString(CultureInfo.InvariantCulture),
                double db => db.ToString("R", CultureInfo.InvariantCulture),
                float f => f.ToString("R", CultureInfo.InvariantCulture),
                DateTime dt => $"'{dt.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture)}'",
                DateTimeOffset dto => $"'{dto.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture)}'",
                string str => $"'{str.Replace("'", "''")}'",
                Guid g => $"'{g.ToString().ToUpperInvariant()}'",
                _ => $"'{val.ToString()?.Replace("'", "''") ?? ""}'"
            };
        }
    }

    /// <summary>
    /// Changes captured before SaveChanges, flushed to the D1 sync context only after the save succeeds.
    /// </summary>
    public sealed class D1PendingChanges
    {
        private readonly List<(string? Sql, EntityEntry? Entry, EntityState State)> _items = new();

        internal void AddDelete(string? sql) => _items.Add((sql, null, EntityState.Deleted));

        internal void AddUpsert(EntityEntry entry) => _items.Add((null, entry, entry.State));

        public bool IsEmpty => _items.Count == 0;

        public void FlushTo(ID1SyncContext syncContext)
        {
            foreach (var item in _items)
            {
                try
                {
                    var sql = item.Entry != null ? D1SqlGenerator.GenerateSql(item.Entry, item.State) : item.Sql;
                    if (!string.IsNullOrEmpty(sql))
                    {
                        syncContext.AddStatement(sql);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[D1Sync] Failed generating sync SQL for {item.Entry?.Metadata.Name ?? "deleted entity"}: {ex.Message}");
                }
            }
        }
    }
}
