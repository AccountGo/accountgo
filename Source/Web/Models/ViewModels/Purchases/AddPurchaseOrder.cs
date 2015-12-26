//-----------------------------------------------------------------------
// <copyright file="AddPurchaseOrder.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Web.Mvc;

namespace Web.Models.ViewModels.Purchases
{
    public class AddPurchaseOrder
    {
        public AddPurchaseOrder()
        {
            PurchaseOrderLines = new List<AddPurchaseOrderLine>();
            Vendors = new HashSet<SelectListItem>();
            Items = new HashSet<SelectListItem>();
            UnitOfMeasurements = new HashSet<SelectListItem>();
            Date = DateTime.Now;
        }

        public DateTime Date { get; set; }
        public int VendorId { get; set; }

        public IList<AddPurchaseOrderLine> PurchaseOrderLines { get; set; }

        public ICollection<SelectListItem> Vendors { get; set; }
        public ICollection<SelectListItem> Items { get; set; }
        public ICollection<SelectListItem> UnitOfMeasurements { get; set; }

        #region Fields Add Line Item
        public int ItemId { get; set; }
        public int UnitOfMeasurementId { get; set; }
        public decimal Cost { get; set; }
        public decimal Quantity { get; set; }
        #endregion
    }

    public partial class AddPurchaseOrderLine
    {
        public int ItemId { get; set; }
        public string Description { get; set; }
        public int UnitOfMeasurementId { get; set; }
        public decimal Quantity { get; set; }
        public decimal? Cost { get; set; }
        public decimal TotalLineCost { get; set; }
    }
}
