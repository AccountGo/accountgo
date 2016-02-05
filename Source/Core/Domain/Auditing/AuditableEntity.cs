using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Core.Domain.Auditing
{
    [Table("AuditableEntity", Schema = "dbo")]
    public class AuditableEntity : BaseEntity
    {
        //[NotMapped]
        //public int AuditableEntityId { get; set; }
        public string EntityName { get; set; }
        public bool EnableAudit { get; set; }
        //public override int Id
        //{
        //    get { return AuditableEntityId; }
        //    set { AuditableEntityId = value; }
        //}
        public virtual ICollection<AuditableAttribute> AuditableAttributes { get; set; }

        public AuditableEntity()
        {
            AuditableAttributes = new HashSet<AuditableAttribute>();
        }
    }
}
