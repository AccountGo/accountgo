using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Purchases
{
    [Table("PurchaseOrderHeader")]
    public class PurchaseOrderHeader : BaseEntity
    {
        public int? VendorId { get; set; }
        public string No { get; set; }
        public DateTime Date { get; set; }
        public string Description { get; set; }
        public int? PaymentTermId { get; set; }
        public string ReferenceNo { get; set; }
        public PurchaseOrderStatus? Status { get; set; }
        public virtual Vendor Vendor { get; set; }
        public virtual ICollection<PurchaseOrderLine> PurchaseOrderLines { get; set; }

        public PurchaseOrderHeader()
        {
            PurchaseOrderLines = new HashSet<PurchaseOrderLine>();
        }

        public bool IsCompleted()
        {
            //foreach (var line in PurchaseOrderLines)
            //{
            //    foreach (var receipt in PurchaseReceipts)
            //    {
            //        var totalReceivedQuatity = receipt.PurchaseReceiptLines.Where(l => l.PurchaseOrderLineId == line.Id).Sum(q => q.ReceivedQuantity);

            //        if (totalReceivedQuatity >= line.Quantity)
            //            return true;
            //    }
            //}

            return false;
        }

        public bool IsPaid()
        {
            bool paid = false;
            //decimal totalPaidAmount = Payments.Where(p => p.PurchaseOrderId == Id).Sum(a => a.Amount);
            //decimal totalPurchaseAmount = PurchaseOrderLines.Sum(d => d.Amount);
            //if (totalPaidAmount == totalPurchaseAmount)
            //    paid = true;
            return paid;
        }
    }
}
