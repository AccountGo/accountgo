//-----------------------------------------------------------------------
// <copyright file="ItemCategory.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Financials;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Items
{
    [Table("ItemCategory")]
    public partial class ItemCategory : BaseEntity
    {
        public ItemCategory()
        {
            Items = new HashSet<Item>();
        }        
        
        public ItemTypes ItemType { get; set; }
        public int? MeasurementId { get; set; }        
        public int? SalesAccountId { get; set; }
        public int? InventoryAccountId { get; set; }
        public int? CostOfGoodsSoldAccountId { get; set; }
        public int? AdjustmentAccountId { get; set; }
        public int? AssemblyAccountId { get; set; }
        public string Name { get; set; }
        public virtual Measurement Measurement { get; set; }
        public virtual Account SalesAccount { get; set; }
        public virtual Account InventoryAccount { get; set; }
        public virtual Account CostOfGoodsSoldAccount { get; set; }
        public virtual Account AdjustmentAccount { get; set; }
        public virtual Account AssemblyAccount { get; set; }

        public virtual ICollection<Item> Items { get; set; }
    }
}
