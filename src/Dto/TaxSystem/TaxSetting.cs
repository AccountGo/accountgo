namespace Dto.TaxSystem
{
    public class TaxSettingWrite
    {
        public string? TaxName { get; set; }
        public string? TaxCode { get; set; }
        public decimal Rate { get; set; }
        public bool IsActive { get; set; } = true;
        public int? SalesAccountId { get; set; }
        public int? PurchaseAccountId { get; set; }
    }

    public class TaxSetting
    {
        public int Id { get; set; }
        public string? TaxCode { get; set; }
        public string? TaxName { get; set; }
        public decimal Rate { get; set; }
        public bool IsActive { get; set; }
        public bool IsSystem { get; set; }
        public int? SalesAccountId { get; set; }
        public string? SalesAccountCode { get; set; }
        public string? SalesAccountName { get; set; }
        public int? PurchaseAccountId { get; set; }
        public string? PurchaseAccountCode { get; set; }
        public string? PurchaseAccountName { get; set; }
        public bool InUse { get; set; }
    }
}
