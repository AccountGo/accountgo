using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Web.Models.ViewModels.Sales
{
    public partial class SalesInvoiceModel : EntityModelBase
    {
        public SalesInvoiceModel()
        { }

        public int CustomerId { get; set; }

        #region Nested Classes
        public partial class SalesInvoiceItemModel : EntityModelBase
        {
            public SalesInvoiceItemModel()
            { }

            public int ItemId { get; set; }
            public decimal Quantity { get; set; }

            public string DiscountInclTax { get; set; }
            public string DiscountExclTax { get; set; }
            public decimal DiscountInclTaxValue { get; set; }
            public decimal DiscountExclTaxValue { get; set; }

            public string SubTotalInclTax { get; set; }
            public string SubTotalExclTax { get; set; }
            public decimal SubTotalInclTaxValue { get; set; }
            public decimal SubTotalExclTaxValue { get; set; }

            public IList<TaxRate> TaxRates { get; set; }
        }

        public partial class TaxRate : EntityModelBase
        {
            public string Rate { get; set; }
            public string Value { get; set; }
        }
        #endregion
    }
}