using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    [Table("Address")]
    public partial class Address : BaseEntity
    {
        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public string ModifiedBy { get; set; }
        public DateTime ModifiedOn { get; set; }
    }
}
