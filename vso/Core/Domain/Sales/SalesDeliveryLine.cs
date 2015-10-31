using Core.Domain.Items;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Sales
{
    [Table("SalesDeliveryLine")]
    public partial class SalesDeliveryLine : BaseEntity
    {
        public int SalesDeliveryHeaderId { get; set; }
        public int? ItemId { get; set; }
        public int? MeasurementId { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Discount { get; set; }

        public virtual SalesDeliveryHeader SalesDeliveryHeader { get; set; }
        public virtual Item Item { get; set; }
        public virtual Measurement Measurement { get; set; }

        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public string ModifiedBy { get; set; }
        public DateTime ModifiedOn { get; set; }

        public decimal GetPriceAfterTax()
        {
            decimal priceAfterTax = 0;
            return priceAfterTax;
        }
    }
}
