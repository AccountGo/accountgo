//-----------------------------------------------------------------------
// <copyright file="EditItem.cs" company="AccountGo">
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

namespace Web.Models.ViewModels.Items
{
    public class EditItem
    {
        public EditItem()
        {
            UnitOfMeasurements = new HashSet<SelectListItem>();
            ItemCategories = new HashSet<SelectListItem>();
            Accounts = new HashSet<SelectListItem>();
            Taxes = new HashSet<SelectListItem>();
            Vendors = new HashSet<SelectListItem>();
            Inventories = new HashSet<SelectListItem>();
            ItemTaxGroups = new HashSet<SelectListItem>();
        }

        public int Id { get; set; }
        public int? ItemCategoryId { get; set; }
        public int? SmallestMeasurementId { get; set; }
        public int? InventoryAccountId { get; set; }
        public int? ItemTaxGroupId { get; set; }
        public int? SellMeasurementId { get; set; }
        public int? PreferredVendorId { get; set; }
        public int? PurchaseMeasurementId { get; set; }
        public int? SellAccountId { get; set; }
        public int? InventoryAdjustmentAccountId { get; set; }
        public int? CostOfGoodsSoldAccountId { get; set; }
        public string No { get; set; }
        public string Code { get; set; }
        public string Description { get; set; }
        public string PurchaseDescription { get; set; }
        public string SellDescription { get; set; }
        public decimal? Cost { get; set; }
        public decimal? Price { get; set; }

        public ICollection<SelectListItem> UnitOfMeasurements { get; set; }
        public ICollection<SelectListItem> ItemCategories { get; set; }
        public ICollection<SelectListItem> Accounts { get; set; }
        public ICollection<SelectListItem> Taxes { get; set; }
        public ICollection<SelectListItem> ItemTaxGroups { get; set; }
        public ICollection<SelectListItem> Vendors { get; set; }
        public ICollection<SelectListItem> Inventories { get; set; }

        public void PrepareEditItemViewModel(Core.Domain.Items.Item item)
        {
            Id = item.Id;
            ItemCategoryId = item.ItemCategoryId;
            SmallestMeasurementId = item.SmallestMeasurementId;
            InventoryAccountId = item.InventoryAccountId;
            ItemTaxGroupId = item.ItemTaxGroupId;
            SellMeasurementId = item.SellMeasurementId;
            PreferredVendorId = item.PreferredVendorId;
            PurchaseMeasurementId = item.PurchaseMeasurementId;
            SellAccountId = item.SalesAccountId;
            InventoryAdjustmentAccountId = item.InventoryAdjustmentAccountId;
            CostOfGoodsSoldAccountId = item.CostOfGoodsSoldAccountId;
            No = item.No;
            Code = item.Code;
            Description = item.Description;
            PurchaseDescription = item.PurchaseDescription;
            SellDescription = item.SellDescription;
            Cost = item.Cost;
            Price = item.Price;
        }
    }
}
