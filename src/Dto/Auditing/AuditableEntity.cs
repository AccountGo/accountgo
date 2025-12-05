using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dto.Auditing
{   
    // DTO for AuditableEntity entity
    public class AuditableEntity : BaseDto
    {
        // Name of the auditable entity
        [Required]
        public string? EntityName { get; set; }

        // Indicates whether auditing is enabled for this entity
        public bool EnableAudit { get; set; }

        // Collection of auditable attributes associated with this entity
        public List<AuditableAttribute> AuditableAttributes {get; set; } = new List<AuditableAttribute>();
    }
}