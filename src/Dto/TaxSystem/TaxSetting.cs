using Dto.Common;
namespace Dto.TaxSystem
{
    public class TaxSettingWrite
    {
        public string? TaxName { get; set; }
        public string? TaxCode { get; set; }
        public decimal Rate { get; set; }
        public bool IsActive { get; set; } = true;
        [PublicId("acct")]
        public int? SalesAccountId { get; set; }
        [PublicId("acct")]
        public int? PurchaseAccountId { get; set; }
    }

    public class TaxSetting
    {
        [PublicId("tax")]
        public int Id { get; set; }
        public string? TaxCode { get; set; }
        public string? TaxName { get; set; }
        public decimal Rate { get; set; }
        public bool IsActive { get; set; }
        public bool IsSystem { get; set; }
        [PublicId("acct")]
        public int? SalesAccountId { get; set; }
        public string? SalesAccountCode { get; set; }
        public string? SalesAccountName { get; set; }
        [PublicId("acct")]
        public int? PurchaseAccountId { get; set; }
        public string? PurchaseAccountCode { get; set; }
        public string? PurchaseAccountName { get; set; }
        public bool InUse { get; set; }
    }
}
