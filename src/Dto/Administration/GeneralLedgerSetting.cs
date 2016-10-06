using System.ComponentModel.DataAnnotations;

namespace Dto.Administration
{
  public class GeneralLedgerSetting : BaseDto
  {
    [Display(Name = "Company")]
    public int? CompanyId { get; set; }
    [Display(Name = "Payable Account")]
    public int? PayableAccountId { get; set; }
    [Display(Name = "Purchase Discount Account")]
    public int? PurchaseDiscountAccountId { get; set; }
    [Display(Name = "Goods Receipt Note Clearing Account")]
    public int? GoodsReceiptNoteClearingAccountId { get; set; }
    [Display(Name = "Sales Discount Account")]
    public int? SalesDiscountAccountId { get; set; }
    [Display(Name = "Shipping Charge Account")]
    public int? ShippingChargeAccountId { get; set; }
    [Display(Name = "Permanent Account")]
    public int? PermanentAccountId { get; set; }
    [Display(Name = "Income Summary Account")]
    public int? IncomeSummaryAccountId { get; set; }
  }
}
