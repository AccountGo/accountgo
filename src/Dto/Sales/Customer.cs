using Dto.Common;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dto.Sales
{
    public class Customer : BaseDto
    {
        public string? No { get; set; }
        [Required]
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Website { get; set; }
        public string? Phone { get; set; }
        public string? Fax { get; set; }

        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? City { get; set; }
        public string? Province { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public string? ShippingAddressLine1 { get; set; }
        public string? ShippingAddressLine2 { get; set; }
        public string? ShippingCity { get; set; }
        public string? ShippingProvince { get; set; }
        public string? ShippingPostalCode { get; set; }
        public string? ShippingCountry { get; set; }

        public int? AccountsReceivableId { get; set; }
        public int? SalesAccountId { get; set; }
        public int? PrepaymentAccountId { get; set; }
        public int? SalesDiscountAccountId { get; set; }
        [Range(0, 100, ErrorMessage = "Discount must be between 0 and 100 percent.")]
        public decimal? DiscountPercentage { get; set; }
        public int? TaxGroupId { get; set; }
        public int? PaymentTermId { get; set; }
        public decimal Balance { get; set; }        
        public string? Contact { get; set; }
        public string? TaxGroup { get; set; }
        public Contact? PrimaryContact { get; set; }
        public IEnumerable<SalesInvoice>? Invoices { get; set; }

        public Customer() {
            PrimaryContact = new Contact();
        }
    }
}
