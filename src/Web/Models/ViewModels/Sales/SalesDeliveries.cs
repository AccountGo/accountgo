//-----------------------------------------------------------------------
// <copyright file="SalesDeliveries.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace Web.Models.ViewModels.Sales
{
    public partial class SalesDeliveries
    {
        public SalesDeliveries()
        {
            SalesDeliveriesViewModel = new HashSet<SalesDeliveryViewModel>();
        }

        public virtual ICollection<SalesDeliveryViewModel> SalesDeliveriesViewModel { get; set; }
    }

    public partial class SalesDeliveryViewModel
    {
        public SalesDeliveryViewModel()
        {
            PaymentTerms = new HashSet<SelectListItem>();
            Customers = new HashSet<SelectListItem>();
            Items = new HashSet<SelectListItem>();
            Measurements = new HashSet<SelectListItem>();
            SalesDeliveryLines = new List<SalesDeliveryLineViewModel>();
            Taxes = new List<KeyValuePair<string, decimal>>();
            Date = DateTime.Now;
        }

        public int CustomerId { get; set; }
        public DateTime Date { get; set; }
        public int? PaymentTermId { get; set; }
        public decimal Amount { get; set; }
        public decimal TotalTaxAmount { get; set; }
        public decimal ShippingCharge { get; set; }

        public ICollection<SelectListItem> PaymentTerms { get; set; }
        public ICollection<SelectListItem> Customers { get; set; }
        public ICollection<SelectListItem> Items { get; set; }
        public ICollection<SelectListItem> Measurements { get; set; }

        public IList<SalesDeliveryLineViewModel> SalesDeliveryLines { get; set; }
        public IList<KeyValuePair<string, decimal>> Taxes { get; set; }

        #region Fields for new line
        /// <summary>
        /// Field for new line Item Id
        /// </summary>
        public int? ItemId { get; set; }
        /// <summary>
        /// Field for new line Measurement Id
        /// </summary>
        public int? MeasurementId { get; set; }
        /// <summary>
        /// Field for new line Quantity
        /// </summary>
        public decimal Quantity { get; set; }
        /// <summary>
        /// Field for new line Price
        /// </summary>
        public decimal Price { get; set; }
        /// <summary>
        /// Field for new line Discount
        /// </summary>
        public decimal Discount { get; set; }
        #endregion
    }

    public partial class SalesDeliveryLineViewModel
    {
        public int? ItemId { get; set; }
        public int? MeasurementId { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Discount { get; set; }
        public decimal LineTotalTaxAmount { get; set; }
    }
}
