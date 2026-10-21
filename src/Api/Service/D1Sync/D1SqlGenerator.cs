using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Api.Service.D1Sync
{
    public static class D1SqlGenerator
    {
        public static string? GenerateSql(EntityEntry entry)
        {
            if (entry.State != EntityState.Added && entry.State != EntityState.Modified && entry.State != EntityState.Deleted)
                return null;

            var entityType = entry.Metadata;
            var tableName = entityType.GetTableName();
            if (string.IsNullOrEmpty(tableName))
                return null;

            if (entry.State == EntityState.Deleted)
            {
                var pk = entityType.FindPrimaryKey();
                if (pk == null || pk.Properties.Count == 0)
                    return null;

                var whereClauses = new List<string>();
                foreach (var prop in pk.Properties)
                {
                    var colName = prop.GetColumnName() ?? prop.Name;
                    var val = FormatValue(entry.Property(prop.Name).OriginalValue);
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
                var val = FormatValue(propEntry.CurrentValue);

                columns.Add($"\"{colName}\"");
                values.Add(val);
            }

            if (columns.Count == 0)
                return null;

            return $"INSERT OR REPLACE INTO \"{tableName}\" ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)});";
        }

        private static string FormatValue(object? val)
        {
            if (val == null || val == DBNull.Value)
                return "NULL";

            return val switch
            {
                bool b => b ? "1" : "0",
                int i => i.ToString(CultureInfo.InvariantCulture),
                long l => l.ToString(CultureInfo.InvariantCulture),
                short s => s.ToString(CultureInfo.InvariantCulture),
                byte by => by.ToString(CultureInfo.InvariantCulture),
                decimal d => d.ToString("0.##", CultureInfo.InvariantCulture),
                double db => db.ToString(CultureInfo.InvariantCulture),
                float f => f.ToString(CultureInfo.InvariantCulture),
                DateTime dt => $"'{dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}'",
                DateTimeOffset dto => $"'{dto.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}'",
                string str => $"'{str.Replace("'", "''")}'",
                Guid g => $"'{g}'",
                _ => $"'{val.ToString()?.Replace("'", "''") ?? ""}'"
            };
        }
    }
}
