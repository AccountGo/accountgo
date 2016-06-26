using Microsoft.AspNet.Mvc.Rendering;
using System;
using System.Collections.Generic;

namespace WebApp.Models.Sales
{
    public class SalesOrder
    {
        public DateTime OrderDate { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }

        public ICollection<SalesOrderLineItem> LineItems { get; set; }

        #region Lookup 
        public ICollection<SelectListItem> Customers { get; set; }
        public ICollection<SelectListItem> Items { get; set; }
        #endregion

        public SalesOrder()
        {
            LineItems = new HashSet<SalesOrderLineItem>();
            OrderDate = DateTime.Now;
        }
    }

    public class SalesOrderLineItem
    {
        public int ItemId { get; set; }
        public float Quantity { get; set; }
    }
}
