using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;
using Core.Domain.Items;
using Microsoft.EntityFrameworkCore;

namespace Core.Domain.Donations
{
    [Table("DonationInvoiceLine")]
    public partial class DonationInvoiceLine : BaseEntity
    {
        public int DonationInvoiceHeaderId { get; set; }
        public int ItemId { get; set; }
        public int MeasurementId { get; set; }
        [Precision(18, 2)]
        public decimal Quantity { get; set; }
        [Precision(18, 2)]
        public decimal Amount { get; set; }
        public string Notes { get; set; }

        public virtual DonationInvoiceHeader DonationInvoiceHeader { get; set; }
        public virtual Item Item { get; set; }
        public virtual Measurement Measurement { get; set; }
    }
}
