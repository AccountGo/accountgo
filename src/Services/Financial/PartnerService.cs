#nullable enable
using System;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;

namespace Services.Financial;

public sealed class PartnerService
{
    private readonly IRepository<Partner> _partners;
    private readonly IRepository<PartnerShare> _shares;
    private readonly IRepository<Account> _accounts;
    private readonly IRepository<FinancialYear> _years;
    private readonly IRepository<CompanySetting> _settings;
    private readonly IRepository<GeneralLedgerLine> _lines;
    private readonly IRepository<GeneralLedgerHeader> _headers;
    private readonly IFinancialService _financial;
    private readonly IBooksLockGuard _lockGuard;

    public PartnerService(IRepository<Partner> partners, IRepository<PartnerShare> shares, IRepository<Account> accounts,
        IRepository<FinancialYear> years, IRepository<CompanySetting> settings, IRepository<GeneralLedgerLine> lines,
        IRepository<GeneralLedgerHeader> headers, IFinancialService financial, IBooksLockGuard lockGuard)
    { _partners = partners; _shares = shares; _accounts = accounts; _years = years; _settings = settings; _lines = lines; _headers = headers; _financial = financial; _lockGuard = lockGuard; }

    public CompanySetting Setting => _settings.Table.First();
    public IQueryable<Partner> Partners => _partners.Table;

    public void ValidateName(string name, int? excludingPartnerId = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100) throw new ArgumentException("Partner name is required and must be 100 characters or fewer.");
        if (_partners.Table.Where(p => p.ArchivedAt == null && p.Id != excludingPartnerId).ToList().Any(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Partner names must be unique.");
    }

    public void ValidateShares(int yearId, (int PartnerId, decimal Percent)[] shares)
    {
        if (shares.Length < 2 || shares.Any(x => x.Percent <= 0m) || shares.Sum(x => x.Percent) != 100.0000m
            || shares.Select(x => x.PartnerId).Distinct().Count() != shares.Length
            || shares.Any(x => !_partners.Table.Any(p => p.Id == x.PartnerId && p.ArchivedAt == null)))
            throw new ArgumentException("Profit-sharing ratios must include at least two active partners and total exactly 100.0000%.");
        var year = _years.Table.FirstOrDefault(y => y.Id == yearId) ?? throw new ArgumentException("Fiscal year was not found.");
        if (year.ClosedAt != null) throw new InvalidOperationException("Shares for a closed fiscal year can't change.");
    }

    public void Setup(DateTime date, (string Name, decimal Percent, decimal Capital, decimal Drawings)[] rows, string actor)
    {
        var setting = Setting;
        if (setting.EntityType != EntityType.Partnership) throw new InvalidOperationException("Partners are only available for partnerships.");
        if (setting.PartnerAccountsFrom != null) throw new InvalidOperationException("Partner accounts have already been set up.");
        var year = _years.Table.FirstOrDefault(y => y.StartDate.Date <= date.Date && y.EndDate.Date >= date.Date && y.ClosedAt == null)
            ?? throw new ArgumentException("The setup date must be inside an open fiscal year.");
        if (setting.BooksLockedThrough.HasValue && date.Date <= setting.BooksLockedThrough.Value.Date)
            throw new InvalidOperationException("The setup date is within the locked period.");
        if (rows.Length < 2 || rows.Any(x => string.IsNullOrWhiteSpace(x.Name) || x.Name.Trim().Length > 100 || x.Percent <= 0m)
            || rows.Sum(x => x.Percent) != 100.0000m)
            throw new ArgumentException("Profit-sharing ratios must include at least two partners and total exactly 100.0000%.");
        if (rows.Select(x => x.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Length
            || _partners.Table.Where(p => p.ArchivedAt == null).ToList().Any(p => rows.Any(r => string.Equals(p.Name, r.Name.Trim(), StringComparison.OrdinalIgnoreCase))))
            throw new ArgumentException("Partner names must be unique.");
        var capitalAccount = _accounts.Table.FirstOrDefault(a => a.AccountCode == "30100") ?? throw new InvalidOperationException("Account 30100 was not found.");
        var drawingsAccount = _accounts.Table.FirstOrDefault(a => a.AccountCode == "30800") ?? throw new InvalidOperationException("Account 30800 was not found.");
        var capitalBalance = Balance(capitalAccount.Id, date);
        var drawingsBalance = Balance(drawingsAccount.Id, date);
        var capitalSum = rows.Sum(r => r.Capital);
        var drawingsSum = rows.Sum(r => r.Drawings);
        if (capitalSum != capitalBalance || drawingsSum != drawingsBalance)
            throw new ArgumentException($"Partner capital must add up to {capitalBalance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} and drawings to {drawingsBalance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}.");
        using var bypass = _lockGuard.Bypass();
        var created = rows.Select(row =>
        {
            var cleanName = row.Name.Trim();
            var codes = CreateAccounts(cleanName);
            var partner = new Partner { CompanyId = setting.CompanyId, Name = cleanName, CapitalAccountId = _accounts.Table.Single(a => a.AccountCode == codes.CapitalCode).Id,
                DrawingsAccountId = _accounts.Table.Single(a => a.AccountCode == codes.DrawingsCode).Id, CreatedAt = DateTime.UtcNow, CreatedBy = actor, RowVersion = Guid.NewGuid().ToByteArray() };
            _partners.Insert(partner);
            return (Partner: partner, Row: row);
        }).ToArray();
        _shares.Insert(created.Select(x => new PartnerShare { CompanyId = setting.CompanyId, FinancialYearId = year.Id, PartnerId = x.Partner.Id, Percent = x.Row.Percent }));
        var journalLines = new System.Collections.Generic.List<JournalEntryLine>();
        if (capitalBalance != 0m)
        {
            foreach (var item in created.Where(x => x.Row.Capital != 0m)) journalLines.Add(new JournalEntryLine { AccountId = item.Partner.CapitalAccountId, DrCr = item.Row.Capital > 0m ? DrOrCrSide.Cr : DrOrCrSide.Dr, Amount = Math.Abs(item.Row.Capital) });
            journalLines.Add(new JournalEntryLine { AccountId = capitalAccount.Id, DrCr = capitalBalance > 0m ? DrOrCrSide.Dr : DrOrCrSide.Cr, Amount = Math.Abs(capitalBalance) });
        }
        if (drawingsBalance != 0m)
        {
            journalLines.Add(new JournalEntryLine { AccountId = drawingsAccount.Id, DrCr = DrOrCrSide.Cr, Amount = drawingsBalance });
            foreach (var item in created.Where(x => x.Row.Drawings != 0m)) journalLines.Add(new JournalEntryLine { AccountId = item.Partner.DrawingsAccountId, DrCr = DrOrCrSide.Dr, Amount = item.Row.Drawings });
        }
        if (journalLines.Count > 0)
        {
            var journal = new JournalEntryHeader { CompanyId = setting.CompanyId, Date = date.Date, Memo = "Partner accounts set up", Posted = false, VoucherType = JournalVoucherTypes.AdjustmentEntries };
            foreach (var line in journalLines) journal.JournalEntryLines.Add(line);
            _financial.AddJournalEntry(journal);
            _financial.UpdateJournalEntry(journal, posted: true);
        }
        setting.PartnerAccountsFrom = date.Date;
        _settings.Update(setting);
    }

    public (string CapitalCode, string DrawingsCode) CreateAccounts(string name)
    {
        ValidateName(name);
        var capitalCode = NextCode("301", 1, 49);
        var drawingsCode = NextCode("308", 51, 99);
        if (capitalCode == null || drawingsCode == null) throw new InvalidOperationException("This workspace already has the maximum of 49 partners.");
        _accounts.Insert(new Account { CompanyId = Setting.CompanyId, AccountClassId = 3, AccountCode = capitalCode, AccountName = "Capital – " + name, DrOrCrSide = DrOrCrSide.Cr, Description = "Partner capital" });
        _accounts.Insert(new Account { CompanyId = Setting.CompanyId, AccountClassId = 3, AccountCode = drawingsCode, AccountName = "Drawings – " + name, DrOrCrSide = DrOrCrSide.Dr, IsContraAccount = true, Description = "Partner drawings" });
        return (capitalCode, drawingsCode);
    }

    private string? NextCode(string prefix, int from, int to)
    {
        var used = _accounts.Table.Select(a => a.AccountCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = from; i <= to; i++) { var code = prefix + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture); if (!used.Contains(code)) return code; }
        return null;
    }

    public decimal Balance(int accountId, DateTime through)
    {
        var data = _lines.GetAllIncluding(l => l.GeneralLedgerHeader).Where(l => l.AccountId == accountId && l.GeneralLedgerHeader.Date <= through);
        var dr = data.Where(l => l.DrCr == DrOrCrSide.Dr).Sum(l => l.Amount);
        var cr = data.Where(l => l.DrCr == DrOrCrSide.Cr).Sum(l => l.Amount);
        var account = _accounts.Table.First(a => a.Id == accountId);
        return account.DrOrCrSide == DrOrCrSide.Dr ? dr - cr : cr - dr;
    }
}
