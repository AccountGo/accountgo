using System.Collections.Generic;

namespace AccountGoWeb.Models.Sales
{
    public class Allocate
    {
        [System.ComponentModel.DataAnnotations.Required]
        public int? CustomerId { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        public int? ReceiptId { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        public System.DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public decimal RemainingAmountToAllocate { get; set; }
        public decimal SumAllocatedAmount { get { return ComputeSumToAllocateAmount(); } }
        public IList<AllocationLine> AllocationLines { get; set; }

        public Allocate()
        {
            AllocationLines = new List<AllocationLine>();
        }

        private decimal ComputeSumToAllocateAmount()
        {
            decimal sum = 0;

            foreach (var line in AllocationLines) {
                sum += line.AmountToAllocate.GetValueOrDefault();
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
        [System.ComponentModel.DataAnnotations.Required]
        public int? InvoiceId { get; set; }
        public decimal? Amount { get; set; }
        public decimal? AllocatedAmount { get; set; }
        public decimal? AmountToAllocate { get; set; }
    }    
}
