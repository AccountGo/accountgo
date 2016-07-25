using System;

namespace Dto.Purchasing
{
    public class PurchaseOrder : BaseDto
    {
        public int VendorId { get; set; }
        public string VendorName { get; set; }
        public DateTime OrderDate { get; set; }
        public decimal Amount { get; set; }
    }

    public class PurchaseOrderLine : BaseDto
    {
        public decimal Amount { get; set; }
    }
}
