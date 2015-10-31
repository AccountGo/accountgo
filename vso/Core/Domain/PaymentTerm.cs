using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    [Table("PaymentTerm")]
    public partial class PaymentTerm : BaseEntity
    {
        public string Description { get; set; }
        public PaymentTypes PaymentType { get; set; }
        public int? DueAfterDays { get; set; }
        public bool IsActive { get; set; }

        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public string ModifiedBy { get; set; }
        public DateTime ModifiedOn { get; set; }
    }
}
