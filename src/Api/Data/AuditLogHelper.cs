using Core.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Api.Data
{
    public class AuditLogHelper
    {
        /// <summary>
        /// Collects newly added entities for a single SaveChanges pass.
        /// Must not be static — concurrent requests would race on a shared dictionary.
        /// </summary>
        private readonly IDictionary<DateTime, EntityEntry> _addedEntities =
            new Dictionary<DateTime, EntityEntry>();

        public IReadOnlyDictionary<DateTime, EntityEntry> AddedEntities =>
            (IReadOnlyDictionary<DateTime, EntityEntry>)_addedEntities;

        public void ClearAddedEntities() => _addedEntities.Clear();

        public List<AuditLog> GetChangesForAuditLog(EntityEntry dbEntry, string username)
        {
            var result = new List<AuditLog>();

            // Get the Table() attribute, if one exists
            Type entryType = dbEntry.Entity.GetType();

            TableAttribute? tableAttr = entryType.GetCustomAttributes(typeof(TableAttribute), false).SingleOrDefault() as TableAttribute;

            if (tableAttr == null)
            {
                tableAttr = entryType.BaseType!.GetCustomAttributes(typeof(TableAttribute), false).SingleOrDefault() as TableAttribute;
            }

            // Get table name (if it has a Table attribute, use that, otherwise get the pluralized name)
            string tableName = tableAttr != null ? tableAttr.Name : dbEntry.Entity.GetType().Name;

            if (tableName.IndexOf("_") > 0)
                tableName = tableName.Substring(0, tableName.IndexOf("_"));

            bool isSchemaExt = tableAttr?.Schema == "ext";

            AuditableEntity? auditableEntity = null;
            if (!isSchemaExt)
            {
                auditableEntity = FillAuditableEntityAndAttributes(dbEntry);
            }

            try
            {
                // Get primary key value (If you have more than one key column, this will need to be adjusted)
                string keyName = dbEntry.Entity.GetType().GetProperties().Single(p => p.GetCustomAttributes(typeof(KeyAttribute), false).Count() > 0).Name;

                DateTime changeTime = DateTime.UtcNow;

                if (dbEntry.State == EntityState.Added)
                {
                    //For Inserts, just add the whole record
                    if (isSchemaExt || IsEntityAuditable(auditableEntity))
                    {
                        string dbEntryObject = ObjectFieldsValues(dbEntry);

                        var auditLog = CreateAuditLog(username,
                            changeTime,
                            AuditEventTypes.Added,
                            tableName,
                            null,
                            null,
                            null,
                            dbEntryObject);

                        result.Add(auditLog);

                        _addedEntities.Add(changeTime, dbEntry);
                    }
                }
                else if (dbEntry.State == EntityState.Deleted)
                {
                    if (isSchemaExt || IsEntityAuditable(auditableEntity))
                    {
                        string dbEntryObject = ObjectFieldsValues(dbEntry);

                        var auditLog = CreateAuditLog(username,
                            changeTime,
                            AuditEventTypes.Deleted,
                            tableName,
                            dbEntry.Property(keyName).CurrentValue!.ToString(),
                            null,
                            null,
                            dbEntryObject);

                        result.Add(auditLog);
                    }
                }
                else if (dbEntry.State == EntityState.Modified)
                {
                    var properties = dbEntry.Metadata.GetProperties().GetEnumerator();
                    while (properties.MoveNext())
                    {
                        string propertyName = properties.Current.Name;
                        if (isSchemaExt || IsAttributeAuditable(auditableEntity, tableName, propertyName))
                        {
                            var property = dbEntry.Property(propertyName);
                            var keyValue = dbEntry.Property(keyName).CurrentValue!.ToString();

                            if (!Equals(property.CurrentValue, property.OriginalValue))
                            {
                                var auditLog = CreateAuditLog(username,
                                    changeTime,
                                    AuditEventTypes.Modified,
                                    tableName,
                                    keyValue,
                                    propertyName,
                                    property.OriginalValue == null ? null : property.OriginalValue.ToString(),
                                    property.CurrentValue == null ? null : property.CurrentValue.ToString());

                                result.Add(auditLog);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            return result;
        }

        #region Private Methods
        private static AuditLog CreateAuditLog(string username, DateTime changeTime, AuditEventTypes type, string tableName, string? recordId, string? fieldname, string? originalvalue, string? newvalue)
        {
            var auditLog = new AuditLog()
            {
                UserName = username,
                AuditEventDateUTC = changeTime,
                AuditEventType = (int)type,
                TableName = tableName,
                RecordId = recordId,
                FieldName = fieldname,
                OriginalValue = originalvalue,
                NewValue = newvalue
            };

            return auditLog;
        }

        private static bool IsEntityAuditable(AuditableEntity? auditableEntity)
        {
            return auditableEntity != null;
        }

        private static bool IsAttributeAuditable(AuditableEntity? auditableEntity, string tablename, string columnname)
        {
            if (auditableEntity == null)
                return false;

            return auditableEntity.AuditableAttributes
                .Any(attr => attr.AttributeName == columnname && attr.AuditableEntity.EntityName == tablename);
        }

        private static string ObjectFieldsValues(EntityEntry dbEntry)
        {
            string record = string.Empty;

            var properties = dbEntry.Metadata.GetProperties().GetEnumerator();

            while (properties.MoveNext())
            {
                string propertyName = properties.Current.Name;
                var property = dbEntry.Property(propertyName);
                record += string.Format("|{0}:{1}", propertyName, property.CurrentValue == null ? string.Empty : property.CurrentValue.ToString());
            }

            return record;
        }

        private static AuditableEntity? FillAuditableEntityAndAttributes(EntityEntry dbEntry)
        {
            Type entryType = dbEntry.Entity.GetType();
            TableAttribute? tableAttr = entryType.GetCustomAttributes(typeof(TableAttribute), false).SingleOrDefault() as TableAttribute;
            string tableName = tableAttr != null ? tableAttr.Name : dbEntry.Entity.GetType().Name;

            return ((ApiDbContext)dbEntry.Context).AuditableEntities
                .Include(a => a.AuditableAttributes)
                .FirstOrDefault(e => e.EntityName == tableName);
        }
        #endregion
    }
}
