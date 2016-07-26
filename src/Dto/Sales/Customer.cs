using Dto.Common;
using System.Collections.Generic;

namespace Dto.Sales
{
    public class Customer : BaseDto
    {
        public string No { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Website { get; set; }
        public string Phone { get; set; }
        public string Fax { get; set; }
        public decimal Balance { get; set; }

        public Contact PrimaryContact { get; set; }
        public IEnumerable<SalesInvoice> Invoices { get; set; }
    }
}
