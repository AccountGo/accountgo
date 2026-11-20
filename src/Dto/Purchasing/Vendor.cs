using Dto.Common;
using System.ComponentModel.DataAnnotations;
using System;

namespace Dto.Purchasing
{
    public class Vendor : BaseDto
    {
        [PublicId("ven")]
        public override int Id { get; set; }
        public string? No { get; set; }
        [Required]
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Website { get; set; }
        public string? Phone { get; set; }
        [PublicId("acct")]
        public int? AccountsPayableAccountId { get; set; }
        [PublicId("acct")]
        public int? PurchaseAccountId { get; set; }
        [PublicId("acct")]
        public int? PurchaseDiscountAccountId { get; set; }
        [PublicId("term")]
        public int? PaymentTermId { get; set; }
        public int? PaymentMethod { get; set; }
        [PublicId("txg")]
        public int? TaxGroupId { get; set; }
        public decimal? DiscountPercentage { get; set; }
        public string? Street1 { get; set; }
        public string? Street2 { get; set; }
        public string? City { get; set; }
        public string? Province { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public decimal Balance { get; set; }
        public string? Contact { get; set; }
        public string? TaxGroup { get; set; }
        public Contact PrimaryContact { get; set; }

        public Vendor()
        {
            PrimaryContact = new Contact();
        }
    }
}
