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
                            pending.AddDelete(GenerateSql(entry, EntityState.Deleted), IsSharedRowDependent(entry));
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[D1Sync] Failed generating DELETE for {entry.Metadata.Name}: {ex.Message}");
                        }
                        break;
                    case EntityState.Added:
                    case EntityState.Modified:
                        pending.AddUpsert(entry, IsSharedRowDependent(entry));
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

            // Column names must be resolved for the table: an owned type's properties map to prefixed
            // columns (e.g. BusinessAddress_City) and its key to the owner's key column.
            var table = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
            string Column(IProperty prop) => $"\"{prop.GetColumnName(table) ?? prop.Name}\"";

            if (IsSharedRowDependent(entry))
                return GenerateSharedRowSql(entry, state, table, Column);

            if (state == EntityState.Deleted)
            {
                var pk = entityType.FindPrimaryKey();
                if (pk == null || pk.Properties.Count == 0)
                    return null;

                var whereClauses = new List<string>();
                foreach (var prop in pk.Properties)
                {
                    var val = FormatValue(prop, entry.Property(prop.Name).OriginalValue);
                    whereClauses.Add($"{Column(prop)} = {val}");
                }

                return $"DELETE FROM \"{tableName}\" WHERE {string.Join(" AND ", whereClauses)};";
            }

            // Added or Modified: upsert on the primary key. Not INSERT OR REPLACE: REPLACE deletes the
            // existing row first, and D1 enforces foreign keys, so ON DELETE CASCADE would wipe the
            // row's children (e.g. a user's tokens, an invoice's lines, a company's members).
            var keyProps = entityType.FindPrimaryKey()?.Properties;
            if (keyProps == null || keyProps.Count == 0)
                return null;

            var columns = new List<string>();
            var values = new List<string>();
            var updates = new List<string>();

            foreach (var prop in entityType.GetProperties())
            {
                var colName = Column(prop);
                columns.Add(colName);
                values.Add(FormatValue(prop, entry.Property(prop.Name).CurrentValue));
                if (!keyProps.Contains(prop))
                    updates.Add($"{colName} = excluded.{colName}");
            }

            var keyColumns = string.Join(", ", keyProps.Select(Column));
            var onConflict = updates.Count > 0
                ? $"ON CONFLICT ({keyColumns}) DO UPDATE SET {string.Join(", ", updates)}"
                : $"ON CONFLICT ({keyColumns}) DO NOTHING";

            return $"INSERT INTO \"{tableName}\" ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)}) {onConflict};";
        }

        /// <summary>
        /// True for an entity stored in its principal's row (an owned type such as CompanySetting.BusinessAddress).
        /// EF tracks it as its own entry, but in the table it is only some of the principal row's columns.
        /// </summary>
        internal static bool IsSharedRowDependent(EntityEntry entry)
        {
            var entityType = entry.Metadata;
            var tableName = entityType.GetTableName();
            return !string.IsNullOrEmpty(tableName)
                && entityType.FindRowInternalForeignKeys(StoreObjectIdentifier.Table(tableName, entityType.GetSchema())).Any();
        }

        // A shared-row dependent never inserts or deletes the row, which belongs to its principal: saving it
        // updates its columns, removing it clears them.
        private static string? GenerateSharedRowSql(EntityEntry entry, EntityState state, StoreObjectIdentifier table, Func<IProperty, string> column)
        {
            var keyProps = entry.Metadata.FindPrimaryKey()?.Properties;
            if (keyProps == null || keyProps.Count == 0)
                return null;

            var assignments = entry.Metadata.GetProperties()
                .Where(p => !keyProps.Contains(p))
                .Select(p => $"{column(p)} = {(state == EntityState.Deleted ? "NULL" : FormatValue(p, entry.Property(p.Name).CurrentValue))}")
                .ToList();
            if (assignments.Count == 0)
                return null;

            var where = keyProps.Select(p => $"{column(p)} = {FormatValue(p, state == EntityState.Deleted ? entry.Property(p.Name).OriginalValue : entry.Property(p.Name).CurrentValue)}");
            return $"UPDATE \"{table.Name}\" SET {string.Join(", ", assignments)} WHERE {string.Join(" AND ", where)};";
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
        private readonly List<(string? Sql, EntityEntry? Entry, EntityState State, int Order)> _items = new();

        // Rows first, in tracker order. Then shared-row dependents, which only touch columns of rows that must
        // already exist: cleared before set, so replacing an owned instance (delete + add) ends with the new values.
        private const int RowOrder = 0, ClearDependentOrder = 1, SetDependentOrder = 2;

        internal void AddDelete(string? sql, bool sharedRowDependent = false) =>
            _items.Add((sql, null, EntityState.Deleted, sharedRowDependent ? ClearDependentOrder : RowOrder));

        internal void AddUpsert(EntityEntry entry, bool sharedRowDependent = false) =>
            _items.Add((null, entry, entry.State, sharedRowDependent ? SetDependentOrder : RowOrder));

        public bool IsEmpty => _items.Count == 0;

        public void FlushTo(ID1SyncContext syncContext)
        {
            foreach (var item in _items.OrderBy(i => i.Order))
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
