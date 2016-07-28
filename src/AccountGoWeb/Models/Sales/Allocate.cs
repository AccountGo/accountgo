using System.Collections.Generic;

namespace AccountGoWeb.Models.Sales
{
    public class Allocate
    {
        public int CustomerId { get; set; }        
        public int ReceiptId { get; set; }
        public System.DateTime Date { get; set; }
        public double Amount { get; set; }
        public double RemainingAmountToAllocate { get; set; }
        public double SumAllocatedAmount { get { return ComputeSumToAllocateAmount(); } }
        public IList<AllocationLine> AllocationLines { get; set; }

        public Allocate()
        {
            AllocationLines = new List<AllocationLine>();
        }

        private double ComputeSumToAllocateAmount()
        {
            double sum = 0;

            foreach (var line in AllocationLines) {
                sum += line.AmountToAllocate;
            }

            return sum;
        }

        public bool IsValid()
        {
            if (RemainingAmountToAllocate < SumAllocatedAmount)
                return false;
            else
                return true;
        }
    }

    public class AllocationLine
    {
        public int InvoiceId { get; set; }
        public double Amount { get; set; }
        public double AllocatedAmount { get; set; }
        public double AmountToAllocate { get; set; }
    }    
}
