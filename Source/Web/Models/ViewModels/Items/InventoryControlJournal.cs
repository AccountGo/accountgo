//-----------------------------------------------------------------------
// <copyright file="InventoryControlJournal.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Web.Models.ViewModels.Items
{
    public class InventoryControlJournal
    {
        public decimal? In { get; set; }
        public decimal? Out { get; set; }
        public string Item { get; set; }
        public string Measurement { get; set; }
        public DateTime Date { get; set; }
    }
}
