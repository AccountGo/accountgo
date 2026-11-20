using Dto.Common;
﻿using System;

namespace Dto.Sales
{
    public class SalesReceipt : BaseDto
    {
        [PublicId("rcpt")]
        public override int Id { get; set; }
        public string? ReceiptNo { get; set; }
        [PublicId("cus")]
        public int CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public decimal Amount { get; set; }
        public decimal RemainingAmountToAllocate { get; set; }
        [PublicId("acct")]
        public int AccountToDebitId { get; set; }
        public string? AccountToDebit { get; set; }
        [PublicId("acct")]
        public int AccountToCreditId { get; set; }
        [PublicId("gl")]
        public int GeneralLedgerHederId { get; set; }
        public DateTime ReceiptDate { get; set; }
        public string? ReferenceNo { get; set; }
        public string? PaymentMethod { get; set; }
        public string? Memo { get; set; }
    }

    public class SalesReceiptLine : BaseDto
    {
        [PublicId("rcptl")]
        public override int Id { get; set; }
        [PublicId("acct")]
        public int AccountToCreditId { get; set; }
        public string? AccountToCredit { get; set; }
        public decimal Amount { get; set; }
    }
}
