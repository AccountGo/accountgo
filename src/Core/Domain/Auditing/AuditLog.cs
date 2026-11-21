using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Auditing
{
    public enum AuditEventTypes
    {
        Added = 1,
        Deleted = 2,
        Modified = 3
    }

    [Table("AuditLog", Schema = "dbo")]
    [PublicIdPrefix("alog")]
    public class AuditLog : BaseEntity, ICompanyOwned, IHasPublicId
    {
        public string PublicId { get; set; } = string.Empty;
        ////[Key]
        //public int AuditLogId { get; set; }
        public int CompanyId { get; set; }
        public string? UserName { get; set; }
        public DateTime AuditEventDateUTC { get; set; }
        public int AuditEventType { get; set; }
        public string? TableName { get; set; }
        public string? RecordId { get; set; }
        public string? FieldName { get; set; }
        public string? OriginalValue { get; set; }
        public string? NewValue { get; set; }
        //public override int Id
        //{
        //    get { return AuditLogId; }
        //    set { AuditLogId = value; }
        //}
    }
}
