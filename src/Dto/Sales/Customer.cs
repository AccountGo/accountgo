using Dto.Common;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dto.Sales
{
    public class Customer : BaseDto
    {
        public string No { get; set; }
        [Required]
        public string Name { get; set; }
        public string Email { get; set; }
        public string Website { get; set; }
        public string Phone { get; set; }
        public string Fax { get; set; }

        public int? AccountsReceivableId { get; set; }
        public int? SalesAccountId { get; set; }
        public int? PrepaymentAccountId { get; set; }
        public int? SalesDiscountAccountId { get; set; }
        public int? TaxGroupId { get; set; }
        public int? PaymentTermId { get; set; }
        public decimal Balance { get; set; }        
        public string Contact { get; set; }
        public string TaxGroup { get; set; }
        public Contact PrimaryContact { get; set; }
        public IEnumerable<SalesInvoice> Invoices { get; set; }

        public Customer() {
            PrimaryContact = new Contact();
        }
    }
}
