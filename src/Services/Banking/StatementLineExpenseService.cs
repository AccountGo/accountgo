// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Purchases;
using Core.Domain.TaxSystem;
using Core.Entitlements;
using Core.Tenancy;
using Dto.Banking;
using Services.Financial;
using Services.Purchasing;

namespace Services.Banking
{
    public sealed class StatementLineExpenseService : IStatementLineExpenseService
    {
        /// <summary>
        /// Short curated labels mapped to template expense account codes (name shown only).
        /// Missing codes are skipped; remaining postable expenses appear under More.
        /// </summary>
        private static readonly (string Name, string Code)[] CuratedExpenseCodes =
        {
            ("Software & subscriptions", "50115"),
            ("Travel", "50118"),
            ("Meals", "50117"),
            ("Office supplies", "50111"),
            ("Bank fees", "50121"),
            ("Professional fees", "50120"),
            ("Equipment (asset)", "13302"),
            ("Other expenses", "50600"),
        };

        private readonly IRepository<BankStatementLine> _lines;
        private readonly IRepository<Bank> _banks;
        private readonly IRepository<Vendor> _vendors;
        private readonly IRepository<PurchaseInvoiceHeader> _bills;
        private readonly IRepository<GeneralLedgerLine> _glLines;
        private readonly IRepository<Tax> _taxes;
        private readonly IRepository<BankStatementDescriptionRule> _rules;
        private readonly IPurchasingService _purchasing;
        private readonly IBankAccountService _bankAccounts;
        private readonly IDocumentLineAccountCatalog _lineAccounts;
        private readonly IEntitlementService _entitlements;
        private readonly ITenantContext _tenant;

        public StatementLineExpenseService(
            IRepository<BankStatementLine> lines,
            IRepository<Bank> banks,
            IRepository<Vendor> vendors,
            IRepository<PurchaseInvoiceHeader> bills,
            IRepository<GeneralLedgerLine> glLines,
            IRepository<Tax> taxes,
            IRepository<BankStatementDescriptionRule> rules,
            IPurchasingService purchasing,
            IBankAccountService bankAccounts,
            IDocumentLineAccountCatalog lineAccounts,
            IEntitlementService entitlements,
            ITenantContext tenant)
        {
            _lines = lines;
            _banks = banks;
            _vendors = vendors;
            _bills = bills;
            _glLines = glLines;
            _taxes = taxes;
            _rules = rules;
            _purchasing = purchasing;
            _bankAccounts = bankAccounts;
            _lineAccounts = lineAccounts;
            _entitlements = entitlements;
            _tenant = tenant;
        }

        public StatementExpenseCategoriesDto ListExpenseCategories()
        {
            EnsureCompany();
            var leaves = _lineAccounts.ListBillLineAccounts();

            var curated = new List<StatementExpenseCategoryDto>();
            var usedIds = new HashSet<int>();
            foreach (var (name, code) in CuratedExpenseCodes)
            {
                var account = leaves.FirstOrDefault(a => a.AccountCode == code)
                    ?? leaves.FirstOrDefault(a =>
                        a.AccountName.Contains(name, StringComparison.OrdinalIgnoreCase)
                        || name.Contains(a.AccountName, StringComparison.OrdinalIgnoreCase));
                if (account == null || !usedIds.Add(account.Id))
                    continue;
                curated.Add(new StatementExpenseCategoryDto
                {
                    AccountId = account.Id,
                    Name = name,
                    Curated = true
                });
            }

            var more = leaves
                .Where(a => !usedIds.Contains(a.Id) && a.AccountClassId == (int)AccountClasses.Expense)
                .OrderBy(a => a.AccountName)
                .Select(a => new StatementExpenseCategoryDto
                {
                    AccountId = a.Id,
                    Name = a.AccountName,
                    Curated = false
                })
                .ToList();

            return new StatementExpenseCategoriesDto { Curated = curated, More = more };
        }

        public StatementDescriptionRuleDto? FindDescriptionRule(string? description)
        {
            EnsureCompany();
            var key = NormalizeDescriptionKey(description);
            if (key == null)
                return null;

            var companyId = _tenant.RequireCompanyId();
            var rule = _rules.Table.FirstOrDefault(r => r.CompanyId == companyId && r.DescriptionKey == key);
            return rule == null ? null : ToRuleDto(rule);
        }

        public RecordExpenseFromStatementLineResultDto RecordExpense(
            int lineId,
            RecordExpenseFromStatementLineRequest request,
            string actor)
        {
            EnsureCompany();
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var line = RequireUnmatchedPaymentLine(lineId);
            var bank = RequireActiveBank(line.BankId);
            var amount = Math.Abs(line.Amount);
            var vendorId = ResolveVendorId(request);
            _lineAccounts.EnsureBillLineAccount(request.ExpenseAccountId);

            var taxId = request.TaxId is > 0 ? request.TaxId : null;
            var lineAmount = amount;
            var lineTaxAmount = 0m;
            var preserveTaxAmounts = false;
            if (taxId.HasValue)
            {
                var tax = RequireActiveTax(taxId.Value);
                (lineAmount, lineTaxAmount) = PurchaseInvoiceLine.SplitInclusive(amount, tax.Rate);
                preserveTaxAmounts = true;
            }

            var description = FirstNonBlank(request.Description, line.Payee, line.Memo) ?? "Expense";
            var bill = new PurchaseInvoiceHeader
            {
                VendorId = vendorId,
                Date = line.PostedDate.Date,
                VendorInvoiceNo = string.IsNullOrWhiteSpace(request.VendorInvoiceNo)
                    ? ""
                    : request.VendorInvoiceNo.Trim(),
                Description = description,
                ReferenceNo = line.Reference,
                PurchaseInvoiceLines =
                {
                    new PurchaseInvoiceLine
                    {
                        Quantity = 1m,
                        Amount = lineAmount,
                        AccountId = request.ExpenseAccountId,
                        TaxId = taxId,
                        TaxAmount = lineTaxAmount,
                        Description = description
                    }
                }
            };

            // Statement-recorded expenses do not consume the monthly Bills quota (#436 decision 4).
            using (_entitlements.Bypass())
            {
                _purchasing.SaveBill(bill, preserveTaxAmounts);
                _purchasing.ApproveBill(bill.Id, string.IsNullOrWhiteSpace(actor) ? null : actor.Trim(), DateTime.UtcNow);
            }

            var savedForPay = _bills.GetById(bill.Id) ?? bill;
            // Bill tax is on top of the line amount — net + tax must equal the bank gross before PayBill.
            if (savedForPay.Total() != amount)
                throw new InvalidOperationException(
                    $"The bill total ({savedForPay.Total()}) must equal the statement amount ({amount}).");

            var payment = PayFromBank(bill.Id, bank, amount, line, request.PaymentMethod, request.Memo);
            LinkLineToBankCredit(line, bank, payment);

            if (request.RememberRule)
                RememberRule(FirstNonBlank(line.Payee, line.Memo, description), vendorId, request.ExpenseAccountId);

            var savedBill = _bills.GetById(bill.Id) ?? bill;
            return new RecordExpenseFromStatementLineResultDto
            {
                Line = ToLineDto(line),
                Bill = new StatementLineDocumentLinkDto
                {
                    Kind = "bill",
                    Id = savedBill.Id,
                    No = savedBill.No
                },
                Payment = new StatementLineDocumentLinkDto
                {
                    Kind = "vendorPayment",
                    Id = payment.Id,
                    No = payment.No
                }
            };
        }

        public BulkRecordExpenseFromStatementLinesResultDto BulkRecordExpense(
            BulkRecordExpenseFromStatementLinesRequest request,
            string actor)
        {
            EnsureCompany();
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (request.LineIds == null || request.LineIds.Count == 0)
                throw new InvalidOperationException("Pick at least one statement line.");
            if (request.LineIds.Count > 50)
                throw new InvalidOperationException("Record at most 50 lines at a time.");

            // One vendor for the batch: create once if needed, then reuse the id.
            int? sharedVendorId = request.VendorId;
            if (sharedVendorId is null or <= 0 && request.NewVendor != null)
            {
                sharedVendorId = ResolveVendorId(new RecordExpenseFromStatementLineRequest
                {
                    NewVendor = request.NewVendor,
                    ExpenseAccountId = request.ExpenseAccountId
                });
            }

            var results = new List<RecordExpenseFromStatementLineResultDto>();
            foreach (var lineId in request.LineIds.Distinct())
            {
                results.Add(RecordExpense(lineId, new RecordExpenseFromStatementLineRequest
                {
                    VendorId = sharedVendorId,
                    ExpenseAccountId = request.ExpenseAccountId,
                    TaxId = request.TaxId,
                    PaymentMethod = request.PaymentMethod,
                    Memo = request.Memo,
                    RememberRule = false
                }, actor));
            }

            StatementDescriptionRuleDto? ruleDto = null;
            if (request.RememberRule && sharedVendorId is > 0)
            {
                var firstLine = _lines.GetById(request.LineIds[0]);
                var keySource = FirstNonBlank(firstLine?.Payee, firstLine?.Memo, request.NewVendor?.Name);
                var rule = RememberRule(keySource, sharedVendorId.Value, request.ExpenseAccountId);
                ruleDto = rule == null ? null : ToRuleDto(rule);
            }

            return new BulkRecordExpenseFromStatementLinesResultDto
            {
                Results = results,
                Rule = ruleDto
            };
        }

        public PayOpenBillFromStatementLineResultDto PayOpenBill(
            int lineId,
            PayOpenBillFromStatementLineRequest request,
            string actor)
        {
            EnsureCompany();
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var line = RequireUnmatchedPaymentLine(lineId);
            var bank = RequireActiveBank(line.BankId);
            var amount = Math.Abs(line.Amount);
            var companyId = _tenant.RequireCompanyId();

            var bill = _bills.Table.FirstOrDefault(b => b.Id == request.BillId && b.CompanyId == companyId)
                ?? throw new TenantEntityNotFoundException(typeof(PurchaseInvoiceHeader).Name, request.BillId);
            if (bill.IsDraft || bill.IsVoid)
                throw new InvalidOperationException("Pick an open bill to pay.");

            var payment = PayFromBank(bill.Id, bank, amount, line, request.PaymentMethod, request.Memo);
            LinkLineToBankCredit(line, bank, payment);

            return new PayOpenBillFromStatementLineResultDto
            {
                Line = ToLineDto(line),
                Bill = new StatementLineDocumentLinkDto
                {
                    Kind = "bill",
                    Id = bill.Id,
                    No = bill.No
                },
                Payment = new StatementLineDocumentLinkDto
                {
                    Kind = "vendorPayment",
                    Id = payment.Id,
                    No = payment.No
                }
            };
        }

        private VendorPayment PayFromBank(
            int billId,
            Bank bank,
            decimal amount,
            BankStatementLine line,
            string? paymentMethod,
            string? memo)
        {
            var glAccountId = _bankAccounts.ResolvePaymentAccountId(bank.Id, null);
            return _purchasing.PayBill(
                billId,
                glAccountId,
                amount,
                line.PostedDate.Date,
                paymentMethod,
                line.Reference,
                FirstNonBlank(memo, line.Memo, line.Payee));
        }

        private void LinkLineToBankCredit(BankStatementLine line, Bank bank, VendorPayment payment)
        {
            var headerId = payment.GeneralLedgerHeaderId ?? payment.GeneralLedgerHeader?.Id;
            if (!headerId.HasValue || !bank.AccountId.HasValue)
                throw new InvalidOperationException("The payment did not post a bank ledger line.");

            var bankAccountId = bank.AccountId.Value;
            var glLine = _glLines.Table.FirstOrDefault(l =>
                    l.GeneralLedgerHeaderId == headerId.Value
                    && l.AccountId == bankAccountId
                    && l.DrCr == DrOrCrSide.Cr)
                ?? throw new InvalidOperationException("Could not find the bank credit for the payment.");

            line.MatchedGeneralLedgerLineId = glLine.Id;
            _lines.Update(line);
        }

        private int ResolveVendorId(RecordExpenseFromStatementLineRequest request)
        {
            if (request.VendorId is > 0)
            {
                var companyId = _tenant.RequireCompanyId();
                if (!_vendors.Table.Any(v => v.Id == request.VendorId.Value && v.CompanyId == companyId))
                    throw new TenantEntityNotFoundException(typeof(Vendor).Name, request.VendorId.Value);
                return request.VendorId.Value;
            }

            var name = request.NewVendor?.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Pick a vendor or create one from the bank description.");
            if (name.Length > 256)
                throw new InvalidOperationException("A vendor name can be at most 256 characters.");

            var email = string.IsNullOrWhiteSpace(request.NewVendor?.Email)
                ? null
                : request.NewVendor!.Email!.Trim();
            var parts = name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var vendor = new Vendor
            {
                Party = new Party
                {
                    PartyType = PartyTypes.Vendor,
                    IsActive = true,
                    Name = name,
                    Email = email
                },
                PrimaryContact = new Contact
                {
                    ContactType = ContactTypes.Vendor,
                    FirstName = parts[0],
                    LastName = parts.Length > 1 ? parts[1] : "Contact",
                    Party = new Party
                    {
                        PartyType = PartyTypes.Contact,
                        Name = name,
                        Email = email
                    }
                }
            };
            _purchasing.AddVendor(vendor);
            return vendor.Id;
        }

        private Tax RequireActiveTax(int taxId)
        {
            var companyId = _tenant.RequireCompanyId();
            var tax = _taxes.Table.FirstOrDefault(t => t.Id == taxId && t.CompanyId == companyId)
                ?? throw new TenantEntityNotFoundException(typeof(Tax).Name, taxId);
            if (!tax.IsActive)
                throw new InvalidOperationException("That tax rate is inactive.");
            return tax;
        }

        private BankStatementLine RequireUnmatchedPaymentLine(int lineId)
        {
            var companyId = _tenant.RequireCompanyId();
            var line = _lines.Table.FirstOrDefault(l => l.Id == lineId && l.CompanyId == companyId)
                ?? throw new TenantEntityNotFoundException(typeof(BankStatementLine).Name, lineId);
            if (line.ExcludedAt.HasValue)
                throw new InvalidOperationException("That statement line is excluded. Include it first.");
            if (line.MatchedGeneralLedgerLineId.HasValue)
                throw new InvalidOperationException("That statement line is already matched.");
            if (line.Amount >= 0m)
                throw new InvalidOperationException("Record expense and pay bill apply to money-out lines only.");
            return line;
        }

        private Bank RequireActiveBank(int bankId)
        {
            var companyId = _tenant.RequireCompanyId();
            var bank = _banks.Table.FirstOrDefault(b => b.Id == bankId && b.CompanyId == companyId)
                ?? throw new TenantEntityNotFoundException(typeof(Bank).Name, bankId);
            if (bank.ArchivedAt != null)
                throw new InvalidOperationException("That bank account is archived.");
            if (!bank.AccountId.HasValue)
                throw new InvalidOperationException("That bank account has no posting account.");
            return bank;
        }

        private void EnsureCompany()
        {
            if (!_tenant.IsResolved)
                throw new InvalidOperationException("No active workspace.");
            _ = _tenant.RequireCompanyId();
        }

        private static BankStatementLineDto ToLineDto(BankStatementLine line) => new()
        {
            Id = line.Id,
            BankId = line.BankId,
            BankStatementImportId = line.BankStatementImportId,
            Source = (int)line.Source,
            ExternalId = line.ExternalId,
            PostedDate = line.PostedDate,
            Amount = line.Amount,
            Payee = line.Payee,
            Memo = line.Memo,
            Reference = line.Reference,
            MatchedGeneralLedgerLineId = line.MatchedGeneralLedgerLineId,
            IsMatched = line.MatchedGeneralLedgerLineId.HasValue,
            IsExcluded = line.ExcludedAt.HasValue,
            ExcludedAt = line.ExcludedAt,
            ExcludedBy = line.ExcludedBy,
            ExclusionReason = line.ExclusionReason.HasValue ? (int)line.ExclusionReason.Value : null,
            ExclusionNote = line.ExclusionNote,
            Candidates = Array.Empty<BankStatementMatchCandidateDto>()
        };

        private BankStatementDescriptionRule? RememberRule(string? description, int vendorId, int expenseAccountId)
        {
            var key = NormalizeDescriptionKey(description);
            if (key == null)
                return null;

            var companyId = _tenant.RequireCompanyId();
            var now = DateTime.UtcNow;
            var rule = _rules.Table.FirstOrDefault(r => r.CompanyId == companyId && r.DescriptionKey == key);
            if (rule == null)
            {
                rule = new BankStatementDescriptionRule
                {
                    CompanyId = companyId,
                    DescriptionKey = key,
                    VendorId = vendorId,
                    ExpenseAccountId = expenseAccountId,
                    CreatedAt = now,
                    LastUsedAt = now
                };
                _rules.Insert(rule);
            }
            else
            {
                rule.VendorId = vendorId;
                rule.ExpenseAccountId = expenseAccountId;
                rule.LastUsedAt = now;
                _rules.Update(rule);
            }
            return rule;
        }

        private StatementDescriptionRuleDto ToRuleDto(BankStatementDescriptionRule rule)
        {
            string? vendorName = null;
            if (rule.VendorId is > 0)
            {
                var vendor = _vendors.GetAllIncluding(v => v.Party!).FirstOrDefault(v => v.Id == rule.VendorId.Value);
                vendorName = vendor?.Party?.Name;
            }

            string? accountName = null;
            if (rule.ExpenseAccountId is > 0)
                accountName = _accounts.GetById(rule.ExpenseAccountId.Value)?.AccountName;

            return new StatementDescriptionRuleDto
            {
                Id = rule.Id,
                DescriptionKey = rule.DescriptionKey,
                VendorId = rule.VendorId,
                VendorName = vendorName,
                ExpenseAccountId = rule.ExpenseAccountId,
                ExpenseAccountName = accountName
            };
        }

        internal static string? NormalizeDescriptionKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            var normalized = string.Join(' ', value.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (normalized.Length == 0)
                return null;
            return normalized.Length <= 200 ? normalized : normalized[..200];
        }

        private static string? FirstNonBlank(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return null;
        }
    }
}
