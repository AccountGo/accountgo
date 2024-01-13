using System.Collections.Generic;

namespace AccountGoWeb.Models.Sales {
    public class SalesQuotations {
        [System.ComponentModel.DataAnnotations.Required]
        public int? CustomerId { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        public int? PaymentTermId { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        public int? ItemId { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        public int? Quantity { get; set; }
        public decimal Amount { get; set; }
        public System.DateTime Date { get; set; }
        public decimal Discount { get; set; }

    }
}