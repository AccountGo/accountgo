namespace Web.Models.ViewModels.Administration
{
    public class PaymentTermModel : EntityModelBase
    {
        public string Description { get; set; }
        public int PaymentType { get; set; }
        public int? DueAfterDays { get; set; }
        public bool IsActive { get; set; }
    }
}