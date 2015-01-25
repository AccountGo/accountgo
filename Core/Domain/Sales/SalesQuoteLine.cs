using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Sales
{
    [Table("SalesQuoteLine")]
    public partial class SalesQuoteLine : BaseEntity
    {
        public int SalesQuoteHeaderId { get; set; }

        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public string ModifiedBy { get; set; }
        public DateTime ModifiedOn { get; set; }

        public SalesQuoteHeader SalesQuoteHeader { get; set; }
    }
}
