namespace BlazorGDB.Client.Models;

public class AccountViewModel
{
    public int AccountClassId { get; set; }
    public int? ParentAccountId { get; set; }
    public int CompanyId { get; set; }
    public string? AccountCode { get; set; }
    public string? AccountName { get; set; }
    public string? Description { get; set; }
    public bool IsCash { get; set; }
    public bool IsContraAccount { get; set; }
    public decimal Balance { get; set; }
    public decimal DebitBalance { get; set; }
    public decimal CreditBalance { get; set; }
    public decimal TotalBalance { get; set; }
    public decimal TotalDebitBalance { get; set; }
    public decimal TotalCreditBalance { get; set; }
    public List<AccountViewModel> ChildAccounts { get; set; } = new List<AccountViewModel>();

}
