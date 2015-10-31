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