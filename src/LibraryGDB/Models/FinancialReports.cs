namespace LibraryGDB.Models;

public class TrialBalance
{
    public int AccountId { get; set; }
    public string? AccountCode { get; set; }
    public string? AccountName { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}

public class BalanceSheet
{
    public int AccountId { get; set; }
    public int AccountClassId { get; set; }
    public string? AccountCode { get; set; }
    public string? AccountName { get; set; }
    public decimal Amount { get; set; }
}

public class IncomeStatement
{
    public int AccountId { get; set; }
    public bool IsExpense { get; set; }
    public string? AccountCode { get; set; }
    public string? AccountName { get; set; }
    public decimal Amount { get; set; }
}

public partial class MasterGeneralLedger
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public int CurrencyId { get; set; }
    public string? DocumentType { get; set; }
    public int TransactionNo { get; set; }
    public string? AccountCode { get; set; }
    public string? AccountName { get; set; }
    public DateTime Date { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}
