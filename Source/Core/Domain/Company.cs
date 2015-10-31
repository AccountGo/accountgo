using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    [Table("Company")]
    public partial class Company : BaseEntity
    {
        public string Name { get; set; }
        public string ShortName { get; set; }

        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public string ModifiedBy { get; set; }
        public DateTime ModifiedOn { get; set; }
    }
}
