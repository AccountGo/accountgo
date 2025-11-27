namespace AccountGoWeb.Models.Financial
{
    public class AccountViewModel
    {
        public int Id { get; set; }
        public int? ParentAccountId { get; set; }
        public string? AccountCode { get; set; }
        public string? AccountName { get; set; }
        public decimal TotalBalance { get; set; }
        public decimal TotalDebitBalance { get; set; }
        public decimal TotalCreditBalance { get; set; }
        public IList<AccountViewModel>? ChildAccounts { get; set; }

    }
}
