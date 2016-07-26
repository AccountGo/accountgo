using System.Collections.Generic;

namespace AccountGoWeb.Models.Sales
{
    public class Allocate
    {
        public int CustomerId { get; set; }        
        public int ReceiptId { get; set; }
        public System.DateTime Date { get; set; }
        public double Amount { get; set; }
        public IList<AllocationLine> AllocationLines { get; set; }

        public Allocate()
        {
            AllocationLines = new List<AllocationLine>();
        }
    }

    public class AllocationLine
    {
        public int InvoiceId { get; set; }
        public double Amount { get; set; }
        public double AllocatedAmount { get; set; }
    }    
}
