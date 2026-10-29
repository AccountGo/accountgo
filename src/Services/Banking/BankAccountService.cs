// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Security;
using Core.Entitlements;
using Core.Tenancy;
using Dto.Banking;

namespace Services.Banking
{
    public class BankAccountService : IBankAccountService
    {
        private readonly IRepository<Bank> _banks;
        private readonly IRepository<Account> _accounts;
        private readonly IRepository<GeneralLedgerLine> _glLines;
        private readonly IRepository<GeneralLedgerHeader> _glHeaders;
        private readonly IRepository<CompanySetting> _companySettings;
        private readonly IEntitlementService _entitlements;
        private readonly ITenantContext _tenant;

        public BankAccountService(
            IRepository<Bank> banks,
            IRepository<Account> accounts,
            IRepository<GeneralLedgerLine> glLines,
            IRepository<GeneralLedgerHeader> glHeaders,
            IRepository<CompanySetting> companySettings,
            IEntitlementService entitlements,
            ITenantContext tenant)
        {
            _banks = banks;
            _accounts = accounts;
            _glLines = glLines;
            _glHeaders = glHeaders;
            _companySettings = companySettings;
            _entitlements = entitlements;
            _tenant = tenant;
        }

        public BankAccountsListDto List(bool includeArchived, bool includeFullNumbers, bool includeCashHistoryCodes)
        {
            EnsureCompany();
            var query = _banks.Table.AsQueryable();
            if (!includeArchived)
                query = query.Where(b => b.ArchivedAt == null);

            var banks = query.OrderBy(b => b.Name).ToList();
            return new BankAccountsListDto
            {
                Accounts = banks.Select(b => ToDto(b, includeFullNumbers)).ToList(),
                UnassignedCash = GetCashHistory(includeCodes: includeCashHistoryCodes)
            };
        }

        public BankAccountDto Get(int id, bool includeFullNumbers)
        {
            return ToDto(RequireBank(id), includeFullNumbers);
        }

        public IReadOnlyList<BankAccountPickerItemDto> Picker(bool includeOtherCashAccounts)
        {
            EnsureCompany();
            var items = _banks.Table
                .Where(b => b.ArchivedAt == null)
                .OrderBy(b => b.Name)
                .ToList()
                .Select(b => new BankAccountPickerItemDto
                {
                    Id = b.Id,
                    Name = b.Name,
                    AccountNumberMasked = MaskAccountNumber(b.Number),
                    IsDefaultForReceipts = b.IsDefaultForReceipts,
                    IsDefaultForPayments = b.IsDefaultForPayments,
                    AccountId = b.AccountId
                })
                .ToList();

            if (includeOtherCashAccounts)
            {
                var bankedAccountIds = _banks.Table
                    .Where(b => b.AccountId != null)
                    .Select(b => b.AccountId!.Value)
                    .ToHashSet();
                foreach (var account in PostableCashAccounts().Where(a => !bankedAccountIds.Contains(a.Id)).OrderBy(a => a.AccountCode))
                {
                    items.Add(new BankAccountPickerItemDto
                    {
                        Id = 0,
                        Name = $"{account.AccountCode} {account.AccountName}",
                        AccountNumberMasked = null,
                        AccountId = account.Id,
                        Group = "Other cash accounts"
                    });
                }
            }

            return items;
        }

        public IReadOnlyList<UnassignedCashDto> GetCashHistory(bool includeCodes)
        {
            EnsureCompany();
            var bankedAccountIds = _banks.Table
                .Where(b => b.AccountId != null)
                .Select(b => b.AccountId!.Value)
                .ToHashSet();

            var result = new List<UnassignedCashDto>();
            foreach (var account in PostableCashAccounts().Where(a => !bankedAccountIds.Contains(a.Id)))
            {
                var lines = LinesForAccount(account.Id).ToList();
                if (lines.Count == 0)
                    continue;

                var headerIds = lines.Select(l => l.GeneralLedgerHeaderId).Distinct().ToList();
                var headers = _glHeaders.Table.Where(h => headerIds.Contains(h.Id)).ToDictionary(h => h.Id);
                var receiptCount = 0;
                var paymentCount = 0;
                var otherCount = 0;
                foreach (var line in lines)
                {
                    if (!headers.TryGetValue(line.GeneralLedgerHeaderId, out var header))
                    {
                        otherCount++;
                        continue;
                    }
                    switch (header.DocumentType)
                    {
                        case DocumentTypes.SalesReceipt:
                            receiptCount++;
                            break;
                        case DocumentTypes.PurchaseInvoicePayment:
                            paymentCount++;
                            break;
                        default:
                            otherCount++;
                            break;
                    }
                }

                result.Add(new UnassignedCashDto
                {
                    AccountId = account.Id,
                    AccountCode = includeCodes ? account.AccountCode : null,
                    AccountName = includeCodes ? account.AccountName : null,
                    Description = includeCodes
                        ? $"{account.AccountCode} {account.AccountName}"
                        : account.AccountName,
                    ReceiptCount = receiptCount,
                    PaymentCount = paymentCount,
                    OtherLineCount = otherCount,
                    Balance = BookBalance(account)
                });
            }

            return result.OrderBy(r => r.AccountCode ?? r.Description).ToList();
        }

        public IReadOnlyList<object> EligiblePostingAccounts()
        {
            EnsureCompany();
            var bankedAccountIds = _banks.Table
                .Where(b => b.AccountId != null)
                .Select(b => b.AccountId!.Value)
                .ToHashSet();

            return PostableCashAccounts()
                .Where(a => !bankedAccountIds.Contains(a.Id))
                .OrderBy(a => a.AccountCode)
                .Select(a => (object)new
                {
                    a.Id,
                    Code = a.AccountCode,
                    Name = a.AccountName,
                    Balance = BookBalance(a)
                })
                .ToList();
        }

        public BankAccountDto Create(CreateBankAccountRequest request, bool includeFullNumbers)
        {
            EnsureCompany();
            var name = RequireUniqueName(request.Name, exceptBankId: null);
            var type = ParseType(request.Type);

            if (request.OpeningBalance.HasValue || request.OpeningBalanceDate.HasValue)
                throw new InvalidOperationException("Opening balances arrive with the register (1D).");

            if (request.PostingAccountId.HasValue && !HasFinancialsModule())
                throw new InvalidOperationException("Linking a posting account requires the Financials module.");

            Account account;
            if (request.AdoptAccountId.HasValue)
            {
                account = EnsureEligiblePostingAccount(request.AdoptAccountId.Value, exceptBankId: null);
            }
            else if (request.PostingAccountId.HasValue)
            {
                account = EnsureEligiblePostingAccount(request.PostingAccountId.Value, exceptBankId: null);
            }
            else
            {
                account = ResolveAccountForCreate(name);
            }

            _entitlements.EnsureCanCreate(MeteredResource.BankAccounts);

            var isFirstActive = !_banks.Table.Any(b => b.ArchivedAt == null);
            var bank = new Bank
            {
                CompanyId = _tenant.CompanyId!.Value,
                Name = name,
                Type = type,
                BankName = request.BankName,
                Number = request.AccountNumber,
                RoutingNumber = request.RoutingNumber,
                Iban = request.Iban,
                Swift = request.Swift,
                AccountId = account.Id,
                OpeningBalance = null,
                OpeningBalanceDate = null,
                CurrencyCode = NormalizeCurrency(request.CurrencyCode),
                IsActive = true,
                ArchivedAt = null,
                IsDefaultForReceipts = isFirstActive || request.IsDefaultForReceipts,
                IsDefaultForPayments = isFirstActive || request.IsDefaultForPayments,
                IsDefault = isFirstActive || request.IsDefaultForReceipts
            };

            SyncAccountName(account, name);
            ApplyDefaults(bank);
            _banks.Insert(bank);
            return ToDto(bank, includeFullNumbers);
        }

        public BankAccountDto Update(int id, UpdateBankAccountRequest request, bool includeFullNumbers)
        {
            var bank = RequireBank(id);
            if (bank.ArchivedAt != null)
                throw new InvalidOperationException("Archived bank accounts cannot be edited. Unarchive first.");

            bank.Name = RequireUniqueName(request.Name, exceptBankId: id);
            bank.Type = ParseType(request.Type);
            bank.BankName = request.BankName;
            bank.Number = request.AccountNumber;
            bank.RoutingNumber = request.RoutingNumber;
            bank.Iban = request.Iban;
            bank.Swift = request.Swift;
            bank.CurrencyCode = NormalizeCurrency(request.CurrencyCode);

            if (request.PostingAccountId.HasValue && request.PostingAccountId != bank.AccountId)
            {
                if (!HasFinancialsModule())
                    throw new InvalidOperationException("Linking a posting account requires the Financials module.");
                if (bank.AccountId.HasValue && LinesForAccount(bank.AccountId.Value).Any())
                    throw new InvalidOperationException("The posting account can only be changed while the bank's current account has no ledger lines.");
                bank.AccountId = EnsureEligiblePostingAccount(request.PostingAccountId.Value, exceptBankId: id).Id;
            }

            if (!bank.AccountId.HasValue)
                throw new InvalidOperationException("Pick a postable account.");

            var account = _accounts.GetById(bank.AccountId.Value)
                ?? throw new InvalidOperationException("Pick a postable account.");
            SyncAccountName(account, bank.Name);

            bank.IsDefaultForReceipts = request.IsDefaultForReceipts;
            bank.IsDefaultForPayments = request.IsDefaultForPayments;
            bank.IsDefault = bank.IsDefaultForReceipts;
            ApplyDefaults(bank);

            _banks.Update(bank);
            return ToDto(bank, includeFullNumbers);
        }

        public BankAccountDto Archive(int id, bool includeFullNumbers, bool confirmNonZeroBalance = false)
        {
            var bank = RequireBank(id);
            if (bank.ArchivedAt != null)
                return ToDto(bank, includeFullNumbers);

            var activeCount = _banks.Table.Count(b => b.ArchivedAt == null);
            if (activeCount <= 1)
                throw new InvalidOperationException("Keep at least one active bank account while receipts or bill payments need a default.");

            EnsureArchiveBalanceAllowed(bank, confirmNonZeroBalance);

            bank.ArchivedAt = DateTime.UtcNow;
            bank.IsActive = false;
            bank.IsDefaultForReceipts = false;
            bank.IsDefaultForPayments = false;
            bank.IsDefault = false;

            if (bank.AccountId.HasValue)
            {
                var account = _accounts.GetById(bank.AccountId.Value);
                if (account != null)
                {
                    account.ArchivedAt = bank.ArchivedAt;
                    _accounts.Update(account);
                }
            }

            _banks.Update(bank);
            return ToDto(bank, includeFullNumbers);
        }

        public BankAccountDto Unarchive(int id, bool includeFullNumbers)
        {
            var bank = RequireBank(id);
            if (bank.ArchivedAt == null)
                return ToDto(bank, includeFullNumbers);

            RequireUniqueName(bank.Name, exceptBankId: id);
            _entitlements.EnsureCanCreate(MeteredResource.BankAccounts);

            bank.ArchivedAt = null;
            bank.IsActive = true;
            if (bank.AccountId.HasValue)
            {
                var account = _accounts.GetById(bank.AccountId.Value);
                if (account != null)
                {
                    account.ArchivedAt = null;
                    _accounts.Update(account);
                }
            }

            if (!_banks.Table.Any(b => b.Id != id && b.ArchivedAt == null && b.IsDefaultForReceipts))
            {
                bank.IsDefaultForReceipts = true;
                bank.IsDefault = true;
            }
            if (!_banks.Table.Any(b => b.Id != id && b.ArchivedAt == null && b.IsDefaultForPayments))
                bank.IsDefaultForPayments = true;

            ApplyDefaults(bank);
            _banks.Update(bank);
            return ToDto(bank, includeFullNumbers);
        }

        public BankAccountDto SetDefault(int id, string forKind, bool includeFullNumbers)
        {
            var bank = RequireBank(id);
            if (bank.ArchivedAt != null)
                throw new InvalidOperationException("Archived bank accounts cannot be set as default.");

            var kind = (forKind ?? string.Empty).Trim().ToLowerInvariant();
            if (kind is "receipts" or "receipt")
            {
                bank.IsDefaultForReceipts = true;
                bank.IsDefault = true;
            }
            else if (kind is "payments" or "payment")
            {
                bank.IsDefaultForPayments = true;
            }
            else
            {
                throw new ArgumentException("Set default for 'receipts' or 'payments'.", nameof(forKind));
            }

            ApplyDefaults(bank);
            _banks.Update(bank);
            return ToDto(bank, includeFullNumbers);
        }

        public bool DeleteOrArchive(int id)
        {
            var bank = RequireBank(id);
            var hasHistory = bank.AccountId.HasValue && LinesForAccount(bank.AccountId.Value).Any();
            if (hasHistory)
            {
                // Explicit delete counts as confirmation for a non-zero I&B balance.
                Archive(id, includeFullNumbers: false, confirmNonZeroBalance: true);
                return true;
            }

            // 1B: always leave the GL account in place. Takeover renames the account to the
            // bank's name, so "name match ⇒ dedicated" would delete cash history accounts.
            _banks.Delete(bank);
            return false;
        }

        public Account EnsureEligiblePostingAccount(int accountId, int? exceptBankId)
        {
            EnsureCompany();
            var account = _accounts.GetById(accountId);
            if (account == null || account.CompanyId != _tenant.CompanyId)
                throw new InvalidOperationException("Pick a postable account.");

            if (account.ArchivedAt.HasValue)
                throw new InvalidOperationException("Pick a postable account.");

            // Load children for CanPost without relying on navigation being populated.
            var hasChildren = _accounts.Table.Any(a => a.ParentAccountId == account.Id);
            if (hasChildren)
                throw new InvalidOperationException("Pick a postable account.");

            if (!IsCashOrCashChild(account))
                throw new InvalidOperationException("Pick a postable account.");

            if (!TryParseCode(account.AccountCode, out var code)
                || code < BankCashAccountCodeAllocator.CashCodeMin
                || code > BankCashAccountCodeAllocator.CashCodeMax)
                throw new InvalidOperationException("Pick a postable account.");

            var other = _banks.Table.FirstOrDefault(b =>
                b.AccountId == account.Id && (!exceptBankId.HasValue || b.Id != exceptBankId.Value));
            if (other != null)
                throw new InvalidOperationException($"That account already belongs to {other.Name}.");

            return account;
        }

        public void SyncBankNameFromAccount(Account account)
        {
            if (account == null)
                return;
            var bank = _banks.Table.FirstOrDefault(b => b.AccountId == account.Id);
            if (bank == null)
                return;

            var newName = (account.AccountName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(newName))
                throw new InvalidOperationException("Bank account name is required.");

            if (NameTaken(newName, exceptBankId: bank.Id))
                throw new InvalidOperationException($"A bank account named '{newName}' already exists.");

            bank.Name = newName;
            _banks.Update(bank);
        }

        public void EnsureAccountMayChangeWhileBackingBank(
            Account account,
            Account? proposedParent,
            string? proposedCode,
            bool becomingSummary,
            bool archiving)
        {
            var bank = _banks.Table.FirstOrDefault(b => b.AccountId == account.Id);
            if (bank == null)
                return;

            if (archiving)
                throw new InvalidOperationException(
                    $"Account is linked as GL account for Bank '{bank.Name}'. Archive the bank account in Banking first.");

            if (becomingSummary)
                throw new InvalidOperationException(
                    $"Account is linked as GL account for Bank '{bank.Name}'. A bank posting account cannot become a summary account.");

            var codeText = proposedCode ?? account.AccountCode;
            if (!TryParseCode(codeText, out var code)
                || code < BankCashAccountCodeAllocator.CashCodeMin
                || code > BankCashAccountCodeAllocator.CashCodeMax)
                throw new InvalidOperationException(
                    $"Account is linked as GL account for Bank '{bank.Name}'. Keep the code in 10100–10999.");

            var parent = proposedParent;
            if (parent == null && account.ParentAccountId.HasValue)
                parent = _accounts.GetById(account.ParentAccountId.Value);

            var isCash = account.IsCash || (parent != null && parent.IsCash);
            if (!isCash)
                throw new InvalidOperationException(
                    $"Account is linked as GL account for Bank '{bank.Name}'. Keep it under a cash account.");
        }

        public int ResolveDepositAccountId(int? bankAccountId)
        {
            EnsureCompany();
            if (bankAccountId.HasValue)
                return RequireActiveBank(bankAccountId.Value).AccountId!.Value;

            var receiptDefault = ActiveBanks()
                .OrderByDescending(b => b.IsDefaultForReceipts)
                .ThenBy(b => b.Name)
                .FirstOrDefault();
            if (receiptDefault != null)
                return receiptDefault.AccountId
                    ?? throw new InvalidOperationException("That bank account has no posting account.");

            return FallbackPostableCashAccountId("No cash or bank account is set up to receive payments.");
        }

        public int ResolvePaymentAccountId(int? bankAccountId, int? accountId)
        {
            EnsureCompany();
            if (bankAccountId.HasValue)
                return RequireActiveBank(bankAccountId.Value).AccountId!.Value;

            if (accountId.HasValue && accountId.Value > 0)
            {
                // Accounting only: Invoicing & Bills pays through bank accounts (same gate as PostingAccountId).
                if (!HasFinancialsModule())
                    throw new InvalidOperationException("Paying from a GL account requires the Financials module.");

                var account = _accounts.GetById(accountId.Value);
                if (account == null || account.CompanyId != _tenant.CompanyId)
                    throw new TenantEntityNotFoundException(nameof(Account), accountId.Value, nameof(accountId));
                if (account.ArchivedAt.HasValue || !IsPostableCashAccount(account))
                    throw new InvalidOperationException("Pay from a cash or bank account.");
                return account.Id;
            }

            var paymentDefault = ActiveBanks()
                .OrderByDescending(b => b.IsDefaultForPayments)
                .ThenBy(b => b.Name)
                .FirstOrDefault();
            if (paymentDefault != null)
                return paymentDefault.AccountId
                    ?? throw new InvalidOperationException("That bank account has no posting account.");

            return FallbackPostableCashAccountId("No cash or bank account is set up to pay from.");
        }

        public Bank RequireActiveBank(int bankId)
        {
            var bank = RequireBank(bankId);
            if (bank.ArchivedAt != null)
                throw new InvalidOperationException("That bank account is archived.");
            if (!bank.AccountId.HasValue)
                throw new InvalidOperationException("That bank account has no posting account.");
            return bank;
        }

        private IQueryable<Bank> ActiveBanks() =>
            _banks.Table.Where(b => b.ArchivedAt == null && b.AccountId != null);

        private int FallbackPostableCashAccountId(string missingMessage)
        {
            var account = PostableCashAccounts().OrderBy(a => a.AccountCode).FirstOrDefault();
            if (account == null)
                throw new InvalidOperationException(missingMessage);
            return account.Id;
        }

        private bool IsPostableCashAccount(Account account)
        {
            if (account.ArchivedAt.HasValue)
                return false;
            if (_accounts.Table.Any(a => a.ParentAccountId == account.Id))
                return false;
            if (!TryParseCode(account.AccountCode, out var code)
                || code < BankCashAccountCodeAllocator.CashCodeMin
                || code > BankCashAccountCodeAllocator.CashCodeMax)
                return false;
            return IsCashOrCashChild(account);
        }

        private Account ResolveAccountForCreate(string bankName)
        {
            var existingBanks = _banks.Table.Any();
            if (!existingBanks)
            {
                var history = GetCashHistory(includeCodes: true);
                if (history.Count >= 2)
                    throw new InvalidOperationException(
                        "Assign each unassigned cash account to a bank account before creating more accounts.");
                if (history.Count == 1)
                    return EnsureEligiblePostingAccount(history[0].AccountId, exceptBankId: null);

                var lowest = PostableCashAccounts()
                    .OrderBy(a => a.AccountCode)
                    .FirstOrDefault();
                if (lowest != null)
                    return EnsureEligiblePostingAccount(lowest.Id, exceptBankId: null);
            }

            return CreateDedicatedCashAccount(bankName);
        }

        private Account CreateDedicatedCashAccount(string bankName)
        {
            var settings = _companySettings.Table.FirstOrDefault();
            var templateId = settings?.CoaTemplateId ?? CoaTemplates.Legacy;
            var used = _accounts.Table.Select(a => a.AccountCode).ToList();
            var code = BankCashAccountCodeAllocator.Allocate(used, templateId);
            var parent = _accounts.Table.FirstOrDefault(a => a.IsCash && _accounts.Table.Any(c => c.ParentAccountId == a.Id))
                ?? _accounts.Table.FirstOrDefault(a => a.AccountCode == "10110")
                ?? throw new InvalidOperationException("Cash summary account (10110) is required before creating a bank account.");

            var account = new Account
            {
                CompanyId = _tenant.CompanyId!.Value,
                AccountCode = code,
                AccountName = bankName,
                AccountClassId = (int)AccountClasses.Assets,
                ParentAccountId = parent.Id,
                DrOrCrSide = DrOrCrSide.Dr,
                IsCash = false,
                IsContraAccount = false
            };
            _accounts.Insert(account);
            return account;
        }

        private void ApplyDefaults(Bank bank)
        {
            if (bank.IsDefaultForReceipts)
            {
                foreach (var other in _banks.Table.Where(b => b.Id != bank.Id && b.IsDefaultForReceipts).ToList())
                {
                    other.IsDefaultForReceipts = false;
                    other.IsDefault = false;
                    _banks.Update(other);
                }
                bank.IsDefault = true;
            }

            if (bank.IsDefaultForPayments)
            {
                foreach (var other in _banks.Table.Where(b => b.Id != bank.Id && b.IsDefaultForPayments).ToList())
                {
                    other.IsDefaultForPayments = false;
                    _banks.Update(other);
                }
            }
        }

        private void SyncAccountName(Account account, string bankName)
        {
            if (!string.Equals(account.AccountName, bankName, StringComparison.Ordinal))
            {
                account.AccountName = bankName;
                _accounts.Update(account);
            }
        }

        private BankAccountDto ToDto(Bank bank, bool includeFullNumbers)
        {
            Account? account = null;
            if (bank.AccountId.HasValue)
                account = _accounts.GetById(bank.AccountId.Value);

            var dto = new BankAccountDto
            {
                Id = bank.Id,
                Name = bank.Name,
                Type = (int)bank.Type,
                AccountNumberMasked = MaskAccountNumber(bank.Number),
                BankName = bank.BankName,
                AccountId = bank.AccountId,
                AccountCode = account?.AccountCode,
                AccountName = account?.AccountName,
                IsDefaultForReceipts = bank.IsDefaultForReceipts,
                IsDefaultForPayments = bank.IsDefaultForPayments,
                BookBalance = account == null ? 0m : BookBalance(account),
                ArchivedAt = bank.ArchivedAt,
                OpeningBalance = bank.OpeningBalance,
                OpeningBalanceDate = bank.OpeningBalanceDate,
                CurrencyCode = bank.CurrencyCode
            };

            if (includeFullNumbers)
            {
                dto.AccountNumber = bank.Number;
                dto.RoutingNumber = bank.RoutingNumber;
                dto.Iban = bank.Iban;
                dto.Swift = bank.Swift;
            }

            return dto;
        }

        private Bank RequireBank(int id)
        {
            EnsureCompany();
            var bank = _banks.GetById(id);
            if (bank == null || bank.CompanyId != _tenant.CompanyId)
                throw new TenantEntityNotFoundException(nameof(Bank), id);
            return bank;
        }

        private string RequireUniqueName(string? rawName, int? exceptBankId)
        {
            var name = (rawName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name))
                throw new InvalidOperationException("Bank account name is required.");

            if (NameTaken(name, exceptBankId))
                throw new InvalidOperationException($"A bank account named '{name}' already exists.");

            return name;
        }

        /// <summary>
        /// Case-insensitive name clash check that EF Core can translate (SQLite LOWER).
        /// Do not use <see cref="string.Equals(string, string, StringComparison)"/> in IQueryable.
        /// </summary>
        private bool NameTaken(string name, int? exceptBankId)
        {
            var nameKey = name.ToLower();
            return _banks.Table.Any(b =>
                (!exceptBankId.HasValue || b.Id != exceptBankId.Value)
                && b.Name.ToLower() == nameKey);
        }

        private void EnsureArchiveBalanceAllowed(Bank bank, bool confirmNonZeroBalance)
        {
            if (!bank.AccountId.HasValue)
                return;

            var account = _accounts.GetById(bank.AccountId.Value);
            if (account == null)
                return;

            var balance = BookBalance(account);
            if (balance == 0m)
                return;

            var amount = balance.ToString("0.00", CultureInfo.InvariantCulture);
            if (HasFinancialsModule())
                throw new InvalidOperationException($"Move the remaining {amount} to another account first.");

            if (!confirmNonZeroBalance)
                throw new BankArchiveConfirmationRequiredException(balance);
        }

        /// <summary>Book balance from GL lines (does not rely on <see cref="Account.GeneralLedgerLines"/> being loaded).</summary>
        private decimal BookBalance(Account account)
        {
            var lines = LinesForAccount(account.Id).ToList();
            var dr = lines.Where(l => l.DrCr == DrOrCrSide.Dr).Sum(l => l.Amount);
            var cr = lines.Where(l => l.DrCr == DrOrCrSide.Cr).Sum(l => l.Amount);
            var isDebitNormal = account.AccountClass?.NormalBalance == "Dr"
                || (account.AccountClass == null && account.DrOrCrSide == DrOrCrSide.Dr);
            return isDebitNormal ? dr - cr : cr - dr;
        }

        private bool HasFinancialsModule()
        {
            EnsureCompany();
            return _entitlements.Evaluate(
                _tenant.RequireCompanyId(),
                new EntitlementAction.Module(ModuleAccess.Read, SystemModules.Financials)).Allowed;
        }

        private IEnumerable<Account> PostableCashAccounts()
        {
            var accounts = _accounts.Table.Where(a => a.ArchivedAt == null).ToList();
            var children = accounts.Where(a => a.ParentAccountId.HasValue)
                .GroupBy(a => a.ParentAccountId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var account in accounts)
            {
                if (children.ContainsKey(account.Id))
                    continue;
                if (!TryParseCode(account.AccountCode, out var code)
                    || code < BankCashAccountCodeAllocator.CashCodeMin
                    || code > BankCashAccountCodeAllocator.CashCodeMax)
                    continue;
                if (!IsCashOrCashChild(account, accounts))
                    continue;
                yield return account;
            }
        }

        private bool IsCashOrCashChild(Account account, IList<Account>? all = null)
        {
            if (account.IsCash)
                return true;
            if (!account.ParentAccountId.HasValue)
                return false;
            var parent = all?.FirstOrDefault(a => a.Id == account.ParentAccountId.Value)
                ?? _accounts.GetById(account.ParentAccountId.Value);
            return parent != null && parent.IsCash;
        }

        private IQueryable<GeneralLedgerLine> LinesForAccount(int accountId) =>
            _glLines.Table.Where(l => l.AccountId == accountId);

        private void EnsureCompany()
        {
            if (!_tenant.IsResolved || !_tenant.CompanyId.HasValue)
                throw new InvalidOperationException("Workspace is required.");
        }

        private static BankTypes ParseType(int type)
        {
            if (!Enum.IsDefined(typeof(BankTypes), type))
                throw new ArgumentException("Invalid bank account type.");
            var value = (BankTypes)type;
            if (value is BankTypes.CreditCard or BankTypes.LineOfCredit)
                throw new ArgumentException("That bank account type is not available yet.");
            return value;
        }

        private static string? NormalizeCurrency(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return null;
            return code.Trim().ToUpperInvariant();
        }

        private static bool TryParseCode(string? code, out int value) =>
            int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out value);

        public static string? MaskAccountNumber(string? number)
        {
            if (string.IsNullOrWhiteSpace(number))
                return null;
            var digits = new string(number.Where(char.IsLetterOrDigit).ToArray());
            if (digits.Length == 0)
                return null;
            var tail = digits.Length <= 4 ? digits : digits[^4..];
            return "•••• " + tail;
        }
    }
}
