using System;
using System.Collections.Generic;

namespace Web.Models.ViewModels.Purchases
{
    public class PurchaseOrders
    {
        public PurchaseOrders()
        {
            PurchaseOrderListLines = new HashSet<PurchaseOrderListLine>();
        }

        public ICollection<PurchaseOrderListLine> PurchaseOrderListLines { get; set; }
    }

    public class PurchaseOrderListLine
    {
        public int Id { get; set; }
        public string No { get; set; }
        public DateTime Date { get; set; }
        public string Vendor { get; set; }
        public decimal Amount { get; set; }
        public bool Completed { get; set; }
        public bool Paid { get; set; }
        public bool HasInvoiced { get; set; }
    }
}