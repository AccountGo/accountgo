using Dto.Common;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dto.Sales
{
    public class Customer : BaseDto
    {
        [PublicId("cus")]
        public override int Id { get; set; }
        public string? No { get; set; }
        [Required]
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Website { get; set; }
        public string? Phone { get; set; }
        public string? Fax { get; set; }
        public Address CompanyAddress { get; set; } = new();
        public Address ShippingAddress { get; set; } = new();

        [PublicId("acct")]
        public int? AccountsReceivableId { get; set; }
        [PublicId("acct")]
        public int? SalesAccountId { get; set; }
        [PublicId("acct")]
        public int? PrepaymentAccountId { get; set; }
        [PublicId("acct")]
        public int? SalesDiscountAccountId { get; set; }
        [PublicId("txg")]
        public int? TaxGroupId { get; set; }
        [PublicId("term")]
        public int? PaymentTermId { get; set; }
        public bool Archived { get; set; }
        public decimal Balance { get; set; }
        public string? Contact { get; set; }
        public string? TaxGroup { get; set; }
        public Contact? PrimaryContact { get; set; }
        public IEnumerable<SalesInvoice>? Invoices { get; set; }

        public Customer()
        {
            PrimaryContact = new Contact();
        }
    }
}
