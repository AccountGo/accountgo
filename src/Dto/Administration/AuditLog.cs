using System;

namespace Dto.Administration
{
    public class AuditLog : BaseDto
    {
        public string UserName { get; set; }
        public DateTime AuditEventDateUTC { get; set; }
        public int AuditEventType { get; set; }
        public string TableName { get; set; }
        public string RecordId { get; set; }
        public string FieldName { get; set; }
        public string OriginalValue { get; set; }
        public string NewValue { get; set; }
    }
}
