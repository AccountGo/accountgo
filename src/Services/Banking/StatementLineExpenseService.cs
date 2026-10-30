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
using Core.Entitlements;
using Core.Tenancy;
using Dto.Banking;
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
            ("Equipment", "13302"),
            ("Other expenses", "50600"),
        };

        private readonly IRepository<BankStatementLine> _lines;
        private readonly IRepository<Bank> _banks;
        private readonly IRepository<Account> _accounts;
        private readonly IRepository<Vendor> _vendors;
        private readonly IRepository<PurchaseInvoiceHeader> _bills;
        private readonly IRepository<GeneralLedgerLine> _glLines;
        private readonly IPurchasingService _purchasing;
        private readonly IBankAccountService _bankAccounts;
        private readonly IEntitlementService _entitlements;
        private readonly ITenantContext _tenant;

        public StatementLineExpenseService(
            IRepository<BankStatementLine> lines,
            IRepository<Bank> banks,
            IRepository<Account> accounts,
            IRepository<Vendor> vendors,
            IRepository<PurchaseInvoiceHeader> bills,
            IRepository<GeneralLedgerLine> glLines,
            IPurchasingService purchasing,
            IBankAccountService bankAccounts,
            IEntitlementService entitlements,
            ITenantContext tenant)
        {
            _lines = lines;
            _banks = banks;
            _accounts = accounts;
            _vendors = vendors;
            _bills = bills;
            _glLines = glLines;
            _purchasing = purchasing;
            _bankAccounts = bankAccounts;
            _entitlements = entitlements;
            _tenant = tenant;
        }

        public StatementExpenseCategoriesDto ListExpenseCategories()
        {
            EnsureCompany();
            var companyId = _tenant.RequireCompanyId();
            // Same universe as Purchasing BillAccounts: leaf expense/asset, not cash or cash-child.
            var leaves = _accounts.Table
                .Where(a => a.CompanyId == companyId
                    && a.ArchivedAt == null
                    && !a.IsContraAccount
                    && (a.AccountClassId == (int)AccountClasses.Expense
                        || a.AccountClassId == (int)AccountClasses.Assets)
                    && !a.IsCash
                    && a.AccountCode != "10120")
                .ToList()
                .Where(a => !_accounts.Table.Any(c => c.ParentAccountId == a.Id && c.CompanyId == companyId))
                .Where(a =>
                {
                    if (!a.ParentAccountId.HasValue) return true;
                    var parent = _accounts.Table.FirstOrDefault(p => p.Id == a.ParentAccountId.Value);
                    return parent == null || !parent.IsCash;
                })
                .ToList();

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
            EnsureExpenseAccount(request.ExpenseAccountId);

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
                        Amount = amount,
                        AccountId = request.ExpenseAccountId,
                        TaxId = request.TaxId,
                        Description = description
                    }
                }
            };

            // Statement-recorded expenses do not consume the monthly Bills quota (#436 decision 4).
            using (_entitlements.Bypass())
            {
                _purchasing.SaveBill(bill);
                _purchasing.ApproveBill(bill.Id, string.IsNullOrWhiteSpace(actor) ? null : actor.Trim(), DateTime.UtcNow);
            }

            var payment = PayFromBank(bill.Id, bank, amount, line, request.PaymentMethod, request.Memo);
            LinkLineToBankCredit(line, bank, payment);

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

        private void EnsureExpenseAccount(int accountId)
        {
            if (accountId <= 0)
                throw new InvalidOperationException("Pick an expense category.");
            var companyId = _tenant.RequireCompanyId();
            var account = _accounts.Table.FirstOrDefault(a => a.Id == accountId && a.CompanyId == companyId)
                ?? throw new TenantEntityNotFoundException(typeof(Account).Name, accountId);
            if (account.IsCash)
                throw new InvalidOperationException("Pick an expense or asset category, not a cash account.");
            if (account.AccountClassId != (int)AccountClasses.Expense
                && account.AccountClassId != (int)AccountClasses.Assets)
                throw new InvalidOperationException("Pick an expense or asset category.");
        }

        private BankStatementLine RequireUnmatchedPaymentLine(int lineId)
        {
            var companyId = _tenant.RequireCompanyId();
            var line = _lines.Table.FirstOrDefault(l => l.Id == lineId && l.CompanyId == companyId)
                ?? throw new TenantEntityNotFoundException(typeof(BankStatementLine).Name, lineId);
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
            Candidates = Array.Empty<BankStatementMatchCandidateDto>()
        };

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
