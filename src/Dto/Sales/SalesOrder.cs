using System;
using System.Collections.Generic;

namespace Dto.Sales
{
    public class SalesOrder : BaseDto
    {
        public int CustomerId { get; set; }
        public string CustomerNo { get; set; }
        public string CustomerName { get; set; }
        public DateTime OrderDate { get; set; }
        public decimal Amount { get; set; }
        public IList<SalesOrderLine> SalesOrderLines { get; set; }
    }

    public class SalesOrderLine : BaseDto
    {
        public int ItemId { get; set; }
        public string ItemNo { get; set; }
        public string ItemDescription { get; set; }
        public int MeasurementId { get; set; }
        public string MeasurementDescription { get; set; }
        public decimal Quantity { get; set; }
        public decimal Discount { get; set; }
        public decimal Amount { get; set; }
    }
}
