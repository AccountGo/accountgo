using Dto.Common;
using System.ComponentModel.DataAnnotations;
using System;

namespace Dto.Purchasing
{
    public class Vendor : BaseDto
    {
        public string No { get; set; }
        [Required]
        public string Name { get; set; }
        public string Email { get; set; }
        public string Website { get; set; }
        public string Phone { get; set; }
        public string Fax { get; set; }
        public int? AccountsPayableAccountId { get; set; }
        public int? PurchaseAccountId { get; set; }
        public int? PurchaseDiscountAccountId { get; set; }
        public int? PaymentTermId { get; set; }
        public int? TaxGroupId { get; set; }
        public decimal Balance { get; set; }
        public string Contact { get; set; }
        public string TaxGroup { get; set; }
        public Contact PrimaryContact { get; set; }

        public Vendor()
        {
            PrimaryContact = new Contact();
        }
    }
}
