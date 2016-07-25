using System;

namespace Dto.Inventory
{
    public class InventoryControlJournal : BaseDto
    {
        public decimal? In { get; set; }
        public decimal? Out { get; set; }
        public string Item { get; set; }
        public string Measurement { get; set; }
        public DateTime Date { get; set; }
    }
}
