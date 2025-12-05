using System.ComponentModel.DataAnnotations;

namespace Dto.Auditing
{   
    // DTO for AuditableAttribute entity
    public class AuditableAttribute : BaseDto
    {   
        // Foreign key to the associated AuditableEntity
        [Required]
        public int AuditableEntityId { get; set; }
        
        // Name of the auditable attribute
        [Required]
        public string? AttributeName { get; set; }

        // Indicates whether auditing is enabled for this attribute
        public bool EnableAudit { get; set; }
    }
}