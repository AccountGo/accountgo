using System;
using System.Collections.Generic;
using System.Linq;
using Api.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.TaxSystem;

namespace Api.Service
{
    /// <summary>Requested policy changes; null means "leave unchanged".</summary>
    public class AccountingPolicyChange
    {
        public ReportingFramework? ReportingFramework { get; set; }
        public AccountingBasis? AccountingBasis { get; set; }
        public FiscalYearPattern? FiscalYearPattern { get; set; }
        public int? FiscalYearStartMonth { get; set; }
        public DateTime? BooksStartDate { get; set; }
        public TaxRegime? TaxRegime { get; set; }
        public IndustryTemplate? IndustryTemplate { get; set; }
        public EntityType? EntityType { get; set; }
        public InventoryCostingMethod? InventoryCostingMethod { get; set; }
    }

    public class AccountingPolicyChangeResult
    {
        public bool Frozen { get; set; }
        public bool ClosedYearsConflict { get; set; }
        public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
        public bool Ok => !Frozen && !ClosedYearsConflict && Errors.Count == 0;
    }

    /// <summary>Change-control helpers for company accounting policy.</summary>
    public class CompanyAccountingPolicyService
    {
        private readonly ApiDbContext _db;

        public CompanyAccountingPolicyService(ApiDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>True once any general ledger journal exists for the company (queried inside the resolved tenant scope); policy fields freeze from then on.</summary>
        public bool IsLedgerFrozen(int companyId)
        {
            return _db.GeneralLedgerHeaders.Any(h => h.CompanyId == companyId);
        }

        /// <summary>
        /// Applies a policy change inside the current tenant scope. Framework, basis, fiscal year and books start
        /// are rejected once the ledger is frozen; tax regime, industry and inventory method stay editable.
        /// While the books are empty, a fiscal year or books start change re-derives the active fiscal year.
        /// </summary>
        public AccountingPolicyChangeResult Apply(CompanySetting setting, AccountingPolicyChange change, DateTime today)
        {
            var frozen = IsLedgerFrozen(setting.CompanyId);
            var touchesFrozenField = change.ReportingFramework.HasValue || change.AccountingBasis.HasValue
                || change.FiscalYearPattern.HasValue || change.FiscalYearStartMonth.HasValue || change.BooksStartDate.HasValue;
            if (frozen && touchesFrozenField)
                return new AccountingPolicyChangeResult { Frozen = true };

            if (change.EntityType.HasValue && change.EntityType.Value != setting.EntityType)
            {
                var hasClosedYears = _db.FinancialYears.Any(fy => fy.CompanyId == setting.CompanyId && fy.ClosedAt != null);
                if (hasClosedYears)
                    return new AccountingPolicyChangeResult { ClosedYearsConflict = true };
            }

            var options = new CompanyProvisionOptions
            {
                ReportingFramework = change.ReportingFramework ?? setting.ReportingFramework,
                FiscalYearPattern = change.FiscalYearPattern ?? setting.FiscalYearPattern,
                FiscalYearStartMonth = change.FiscalYearStartMonth ?? setting.FiscalYearStartMonth,
                BooksStartDate = (change.BooksStartDate ?? setting.BooksStartDate).Date,
                TaxRegime = change.TaxRegime ?? setting.TaxRegime,
                IndustryTemplate = change.IndustryTemplate ?? setting.IndustryTemplate,
                EntityType = change.EntityType ?? setting.EntityType,
                InventoryCostingMethod = change.InventoryCostingMethod ?? setting.InventoryCostingMethod,
            };
            // Switching to or from Cash Basis implies the matching basis unless the caller says otherwise.
            options.AccountingBasis = change.AccountingBasis
                ?? (change.ReportingFramework.HasValue
                    ? (options.ReportingFramework == ReportingFramework.CashBasis ? AccountingBasis.Cash : AccountingBasis.Accrual)
                    : setting.AccountingBasis);

            var errors = CompanyAccountingPolicyRules.Validate(options, today);
            if (errors.Count > 0)
                return new AccountingPolicyChangeResult { Errors = errors };

            var taxRegimeChanged = options.TaxRegime != setting.TaxRegime;
            var entityTypeChanged = options.EntityType != setting.EntityType;
            var fiscalYearChanged = options.FiscalYearPattern != setting.FiscalYearPattern
                || options.FiscalYearStartMonth != setting.FiscalYearStartMonth
                || options.BooksStartDate != setting.BooksStartDate.Date;

            setting.ReportingFramework = options.ReportingFramework;
            setting.AccountingBasis = options.AccountingBasis;
            setting.FiscalYearPattern = options.FiscalYearPattern;
            setting.FiscalYearStartMonth = options.FiscalYearPattern == FiscalYearPattern.Custom ? options.FiscalYearStartMonth : null;
            setting.BooksStartDate = options.BooksStartDate.Value;
            setting.TaxRegime = options.TaxRegime;
            setting.IndustryTemplate = options.IndustryTemplate;
            setting.EntityType = options.EntityType;
            setting.InventoryCostingMethod = options.InventoryCostingMethod;

            if (entityTypeChanged)
                ApplyEntityType(setting);
            if (fiscalYearChanged)
                ReplaceActiveFiscalYear(setting);
            if (taxRegimeChanged)
                SyncSeededTax(setting.TaxRegime);

            return new AccountingPolicyChangeResult();
        }

        private void ApplyEntityType(CompanySetting setting)
        {
            var profile = EntityEquityMapping.GetProfile(setting.EntityType);
            var accounts = _db.Accounts.Where(a => a.CompanyId == setting.CompanyId).ToList();
            var accountByCode = accounts.ToDictionary(a => a.AccountCode, StringComparer.Ordinal);

            if (accountByCode.TryGetValue(profile.CloseTargetAccountCode, out var targetAccount))
            {
                var glSetting = _db.GeneralLedgerSettings.FirstOrDefault(s => s.CompanyId == setting.CompanyId);
                if (glSetting == null)
                {
                    glSetting = new GeneralLedgerSetting { CompanyId = setting.CompanyId };
                    _db.GeneralLedgerSettings.Add(glSetting);
                }
                glSetting.PermanentAccountId = targetAccount.Id;
            }

            foreach (var (code, displayName) in profile.DisplayNames)
            {
                if (accountByCode.TryGetValue(code, out var acc) && EntityEquityMapping.IsRenameable(code, acc.AccountName))
                {
                    acc.AccountName = displayName;
                }
            }
        }

        // Only rewrites the untouched seed (a single zero-rate placeholder); real tax setups are left alone.
        private void SyncSeededTax(TaxRegime regime)
        {
            var taxes = _db.Taxes.ToList();
            var groups = _db.TaxGroups.ToList();
            if (taxes.Count != 1 || groups.Count != 1)
                return;
            var tax = taxes[0];
            if (tax.Rate != 0 || (tax.TaxCode != "NONE" && tax.TaxCode != "VAT" && tax.TaxCode != "SALESTAX"))
                return;
            var taxed = regime != TaxRegime.None;
            var label = regime == TaxRegime.VatGst ? "VAT/GST" : "Sales tax";
            tax.TaxCode = !taxed ? "NONE" : regime == TaxRegime.VatGst ? "VAT" : "SALESTAX";
            tax.TaxName = taxed ? label + " (set rate)" : "No tax";
            tax.IsActive = !taxed;
            groups[0].Description = taxed ? label : "No tax";
        }

        // Safe only while the ledger is empty: nothing can reference the rewritten year.
        // Deactivate every other code so a pattern change never leaves overlapping active years.
        private void ReplaceActiveFiscalYear(CompanySetting setting)
        {
            var fy = FiscalYearCalculator.Containing(setting.BooksStartDate, setting.FiscalYearPattern, setting.FiscalYearStartMonth);
            foreach (var old in _db.FinancialYears.Where(f => f.FiscalYearCode != fy.Code).ToList())
                old.IsActive = false;
            var existing = _db.FinancialYears.FirstOrDefault(f => f.FiscalYearCode == fy.Code);
            if (existing != null)
            {
                existing.StartDate = fy.Start;
                existing.EndDate = fy.End;
                existing.IsActive = true;
            }
            else
            {
                _db.FinancialYears.Add(new FinancialYear
                {
                    CompanyId = setting.CompanyId, FiscalYearCode = fy.Code, FiscalYearName = fy.Code,
                    StartDate = fy.Start, EndDate = fy.End, IsActive = true
                });
            }
        }
    }
}
