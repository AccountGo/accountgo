//-----------------------------------------------------------------------
// <copyright file="Items.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:50:13 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Web.Mvc;

namespace Web.Models.ViewModels.Items
{
    public class Items
    {
        public Items()
        {
            ItemsList = new HashSet<ItemListLine>();
        }

        public ICollection<ItemListLine> ItemsList { get; set; }

        public class ItemListLine
        {
            public int ItemId { get; set; }
            public string No { get; set; }
            public string Description { get; set; }
            public decimal QtyOnHand { get; set; }
        }

        public class AddItem
        {
            public AddItem()
            {
                ItemTypes = new HashSet<SelectListItem>();
            }

            public string Code { get; set; }
            public string Description { get; set; }
            public int? ItemType { get; set; }

            public ICollection<SelectListItem> ItemTypes { get; set; }
        }
    }
}
