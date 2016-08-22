using System;

namespace Dto.Sales
{
    public class SalesReceipt : BaseDto
    {
        public string ReceiptNo { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public decimal Amount { get; set; }
        public decimal RemainingAmountToAllocate { get; set; }
        public int AccountToDebitId { get; set; }
        public string AccountToDebit { get; set; }
        public int GeneralLedgerHederId { get; set; }
        public DateTime ReceiptDate { get; set; }
        public string ReferenceNo { get; set; }
    }

    public class SalesReceiptLine : BaseDto
    {
        public int AccountToCreditId { get; set; }
        public string AccountToCredit { get; set; }
        public decimal Amount { get; set; }
    }
}
