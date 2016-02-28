namespace Model.Financial
{
    public class Account : BaseModel
    {
        public AccountClass AccountClass { get; set; }
        public int? ParentAccountId { get; set; }
        public int CompanyId { get; set; }
        public string AccountCode { get; set; }
        public string AccountName { get; set; }
        public string Description { get; set; }
        public bool IsCash { get; set; }
        public bool IsContraAccount { get; set; }
    }
}
