using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    [Table("Party")]
    public partial class Party : BaseEntity
    {
        public virtual PartyTypes PartyType { get; set; }
        public virtual string Name { get; set; }
        public virtual string FirstName { get; set; }
        public virtual string LastName { get; set; }
        public virtual string MiddleName { get; set; }
        public virtual string Email { get; set; }
        public virtual string Website { get; set; }
        public virtual string Phone { get; set; }
        public virtual string Fax { get; set; }
        public virtual bool IsActive { get; set; }

        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public string ModifiedBy { get; set; }
        public DateTime ModifiedOn { get; set; }
    }
}
