using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace Web.Models.ViewModels.Sales
{
    public class SalesInvoices
    {
        public SalesInvoices()
        {
            SalesInvoiceListLines = new HashSet<SalesInvoiceListLine>();
        }

        public virtual ICollection<SalesInvoiceListLine> SalesInvoiceListLines { get; set; }
    }

    public class SalesInvoiceListLine
    {
        public int Id { get; set; }
        public string No { get; set; }
        public string Customer { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public bool IsFullPaid { get; set; }
    }

    public class AddSalesInvoice
    {
        public AddSalesInvoice()
        {
            Date = DateTime.Now;
            AddSalesInvoiceLines = new List<AddSalesInvoiceLine>();
            
            //remove after testing
            Discount = 2;
            ShippingHandlingCharge = 4;
        }

        public int CustomerId { get; set; }
        public DateTime Date { get; set; }
        public int? SalesOrderId { get; set; }
        public decimal ShippingHandlingCharge { get; set; }

        public IList<AddSalesInvoiceLine> AddSalesInvoiceLines { get; set; }

        public IEnumerable<SelectListItem> Customers { get; set; }
        public IEnumerable<SelectListItem> Items { get; set; }
        public IEnumerable<SelectListItem> Measurements { get; set; }

        #region Fields for new invoice item
        public int ItemId { get; set; }
        public int MeasurementId { get; set; }
        public decimal Quantity { get; set; }
        public decimal Amount { get; set; }
        public decimal Discount { get; set; }
        public decimal Price { get; set; }
        #endregion
    }

    public class AddSalesInvoiceLine
    {
        public int ItemId { get; set; }
        public int MeasurementId { get; set; }
        public decimal Quantity { get; set; }
        public decimal Amount { get; set; }
        public decimal Discount { get; set; }
        public decimal Price { get; set; }
    }
}