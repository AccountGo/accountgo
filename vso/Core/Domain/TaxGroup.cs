using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain
{
    [Table("TaxGroup")]
    public partial class TaxGroup : BaseEntity
    {
        public TaxGroup()
        {
            TaxGroupTax = new HashSet<TaxGroupTax>();
        }
        public string Description { get; set; }
        public bool TaxAppliedToShipping { get; set; }
        public bool IsActive { get; set; }

        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public string ModifiedBy { get; set; }
        public DateTime ModifiedOn { get; set; }

        public virtual ICollection<TaxGroupTax> TaxGroupTax { get; set; }
    }

    [Table("TaxGroupTax")]
    public partial class TaxGroupTax :  BaseEntity
    {
        public int TaxId { get; set; }
        public int TaxGroupId { get; set; }

        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public string ModifiedBy { get; set; }
        public DateTime ModifiedOn { get; set; }

        public virtual Tax Tax { get; set; }
        public virtual TaxGroup TaxGroup { get; set; }
    }
}
