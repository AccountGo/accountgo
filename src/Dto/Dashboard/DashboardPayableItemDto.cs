using System;
using Dto.Common;

namespace Dto.Dashboard
{
    public class DashboardPayableItemDto
    {
        [PublicId("bill")]
        public int PurchaseInvoiceId { get; set; }
        public string VendorName { get; set; } = string.Empty;
        public string InvoiceNo { get; set; } = string.Empty;
        public decimal RemainingAmount { get; set; }
        public DateTime? InvoiceDate { get; set; }
    }
}
