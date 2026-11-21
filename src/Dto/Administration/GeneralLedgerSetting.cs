using Dto.Common;
﻿using System.ComponentModel.DataAnnotations;

namespace Dto.Administration
{
  public class GeneralLedgerSetting
  {
    [Display(Name = "Company")]
    [PublicId("ws")]
    public int? CompanyId { get; set; }
    [Display(Name = "Payable Account")]
    [PublicId("acct")]
    public int? PayableAccountId { get; set; }
    [Display(Name = "Purchase Discount Account")]
    [PublicId("acct")]
    public int? PurchaseDiscountAccountId { get; set; }
    [Display(Name = "Goods Receipt Note Clearing Account")]
    [PublicId("acct")]
    public int? GoodsReceiptNoteClearingAccountId { get; set; }
    [Display(Name = "Sales Discount Account")]
    [PublicId("acct")]
    public int? SalesDiscountAccountId { get; set; }
    [Display(Name = "Shipping Charge Account")]
    [PublicId("acct")]
    public int? ShippingChargeAccountId { get; set; }
    [Display(Name = "Permanent Account")]
    [PublicId("acct")]
    public int? PermanentAccountId { get; set; }
    [Display(Name = "Income Summary Account")]
    [PublicId("acct")]
    public int? IncomeSummaryAccountId { get; set; }
  }
}
